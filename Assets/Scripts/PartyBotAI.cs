using System.Collections;
using UnityEngine;

namespace RaidRogue
{
    public enum PartyBotRole
    {
        Tank,      // 가디언 탱커 봇 (파란색) - 북쪽(Z+) 이동 및 보스 시선 북쪽 주차, 단발 탱버 시 뎀감기 방어
        Healer,    // 오라클 힐러 봇 (초록색) - 남서쪽 위치, 최저 체력 아군 회복 & 위험 장판 80% 시전 시 '구출(Rescue)' 발동
        MeleeDPS,  // 블레이드 근딜 봇 (노란색) - 보스 측·후면 근접 공격 & 내가 탱커일 때 2연타 탱크버스터 줄 자동 가로채기 + 생존기 방어!
        RangedDPS  // 아크메이지 캐스터/원딜 봇 (주황색) - 남동쪽 외곽 무빙 및 마법 투사체 발사
    }

    /// <summary>
    /// 4인 협동 레이드용 3명의 AI 파티원 봇 (탱커 / 힐러 / 근딜 / 캐스터) 컨트롤러 (Step 2 ~ Step 8 통합):
    /// - 모든 거리 및 이동 계산은 XZ 평면(Y축 무시)을 기준으로 수행됩니다.
    /// - Step 8 역할 교대 AI:
    ///   ① 내가 선택한 직업을 제외한 나머지 3개의 빈 역할군으로 자동 편성됩니다.
    ///   ② 내가 '탱커(가디언)'를 선택했을 때는 나에게 보스 어그로 1순위가 고정되며,
    ///      2페이즈 2연타 탱크버스터 줄(Tether)이 나에게 연결되면 '근딜 AI 봇(MeleeDPS)'이 자동으로 줄 사이로 달려들어
    ///      줄을 가로채고 생존기를 켜서 나를 지켜줍니다!
    ///   ③ 보호막(Shield) 게이지 및 플레이어 힐러의 '구출(Rescue)' 끌려감 + 3초 무적/이속 버프를 지원합니다.
    /// </summary>
    public partial class PartyBotAI : MonoBehaviour
    {
        [Header("Combat Body & Melee Ranges (world units)")]
        [Min(0f)] public float combatRadius = 0.55f;
        public MeleeRangeSettings meleeRanges = new MeleeRangeSettings();
        public float MeleeAttackRange => meleeRanges.Get(role == PartyBotRole.Tank ? MeleeSkill.TankPrimary : MeleeSkill.BladeFlank);
        public bool CanMeleeHit(Transform target) => !isDead && CombatRange.LivingEnemy(target) && CombatRange.InRange(transform, target, MeleeAttackRange);
        [Header("Role & References")]
        public PartyBotRole role = PartyBotRole.Tank;
        public BossController targetBoss;
        public PlayerController humanPlayer;
        public PartyBotAI tankBotReference;
        public Renderer bodyRenderer;
        public DualVisualController dualVisual;

        [Header("Movement Settings (XZ Plane)")]
        public float moveSpeed = 7.8f;
        public float rotationSpeed = 540f;

        [Header("Health & Shield Settings (Step 8)")]
        public float maxHp = 1600f;
        public float currentHp = 1600f;
        public float currentShield = 0f;
        private readonly CombatShieldLedger _metricsShields = new CombatShieldLedger();
        public bool isDead;
        public CommonPotion Potion { get; private set; }
        public PartyLife Life { get; private set; }
        public BattleResurrection Resurrection { get; private set; }

        [Header("Tank Settings (North Z+ Parking & Defensive)")]
        public float tankNorthParkingDistance = 4.8f;
        public float tankAttackInterval = 1.8f;
        public int tankAttackDamage = 55;
        public bool isTankDefensiveActive;
        private float _tankDefensiveTimer;

        [Header("Melee DPS Bot Settings (Rear/Flank & Tether Intercept for Tank Player)")]
        public float meleeCombatDistance = 3.1f;
        public float meleeAttackInterval = 1.45f;
        public int meleeAttackDamage = 95;

        [Header("Healer Settings (Southwest, Green Beam Heal & Rescue)")]
        public Vector3 healerDesiredOffsetXZ = new Vector3(-6.8f, 0f, -6.5f);
        public float healCheckInterval = 1.3f;
        public float healAmount = 280f;
        public float healBeamDuration = 0.55f;

        [Tooltip("구출(Rescue) 쿨타임 (30초)")]
        public float rescueCooldown = 30.0f;
        [Tooltip("보스 원형/도넛 장판 시전 진행도 80% 이상일 때 구출 발동")]
        public float rescueTriggerCastProgress = 0.80f;

        [Header("Ranged Caster DPS Settings (Southeast Strafe & Projectile)")]
        public Vector3 rangedBaseOffsetXZ = new Vector3(6.6f, 0f, -6.0f);
        public float rangedStrafeAmplitude = 2.4f;
        public float rangedStrafeSpeed = 1.6f;
        public float rangedFireInterval = 1.25f;
        public float projectileSpeed = 18f;
        public int projectileDamage = 65;

        public float HpRatio => maxHp > 0f ? Mathf.Clamp01(currentHp / maxHp) : 0f;
        public float ShieldRatio => maxHp > 0f ? Mathf.Clamp01(currentShield / maxHp) : 0f;
        public bool NeedsHealing => !isDead && currentHp < maxHp - 1f;
        public float RescueCooldownRemaining => _rescueCooldownTimer;
        public bool IsRescueReady => _rescueCooldownTimer <= 0f;
        public bool IsInvulnerable => _invulnerableTimer > 0f;
        public bool IsInterceptingTetherForPlayer => _isInterceptingTetherForPlayer;
        public string botDisplayName => string.IsNullOrEmpty(_customDisplayName) ? GetRoleDisplayNameKor() : _customDisplayName;
        public Transform CurrentActionTarget { get; private set; }

        private string _customDisplayName;
        private float _actionTimer;
        private float _strafePhase;
        private float _rescueCooldownTimer;
        private float _berserkerAttackSpeedTimer;
        private float _invulnerableTimer;
        private float _rescueSpeedBoostTimer;
        private bool _isInterceptingTetherForPlayer;
        private AoETelegraph _lastRescuedTelegraph;

        // 플레이어 힐러의 '구출(Rescue)'에 의해 끌려가는 상태
        private bool _isBeingRescuedByPlayer;
        public bool IsBeingRescued => _isBeingRescuedByPlayer;
        private Vector3 _rescueByPlayerTargetXZ;
        private float _rescueByPlayerSpeed = 30f;

        // 넉백(밀쳐내기) 상태
        private bool _isBeingKnockedBack;
        private Vector3 _knockbackTargetXZ;
        private float _knockbackSpeed = 24f;

        private LineRenderer _effectBeam;
        private Coroutine _beamCoroutine;
        private Color _defaultBodyColor;

        // 머리 위 3D 체력바
        private Transform _hpBarRoot;
        private Transform _hpFillBar;
        private Renderer _hpFillRenderer;
        private TextMesh _nameLabelText;

        private void Start()
        {
            Potion = CommonPotion.Ensure(gameObject);
            Life = PartyLife.Ensure(gameObject);
            Resurrection = GetComponent<BattleResurrection>();
            if (targetBoss == null)
            {
                targetBoss = FindFirstObjectByType<BossController>();
            }

            if (humanPlayer == null)
            {
                humanPlayer = FindFirstObjectByType<PlayerController>();
            }

            if (dualVisual == null)
            {
                dualVisual = GetComponent<DualVisualController>();
            }

            if (bodyRenderer == null)
            {
                if (dualVisual != null && dualVisual.primary3DRenderer != null)
                {
                    bodyRenderer = dualVisual.primary3DRenderer;
                }
                else
                {
                    bodyRenderer = GetComponentInChildren<Renderer>();
                }
            }

            CreateBeamLineRenderer();
            CreateOverheadHpBar();
            ApplyRoleStatsAndColors(role);
        }

        /// <summary>
        /// Step 8: RoleSelectionManager에서 플레이어 직업 변경 시 나머지 3개 역할군으로 이 봇을 즉시 재설정합니다.
        /// </summary>
        public void ConfigureBotRoleDynamic(PartyBotRole newRole, string customDisplayName, Vector3 spawnPos, bool reposition = true)
        {
            _customDisplayName = customDisplayName;
            ConfigureBotRoleDynamic(newRole, spawnPos, reposition);
        }

        public void ConfigureBotRoleDynamic(PartyBotRole newRole, Vector3 spawnPos, bool reposition = true)
        {
            _guardianCounterReady = _guardianDefenseReady = 0;
            Life?.ClearDeadForStagePlacement();
            Potion?.EndRevivalProtection();
            role = newRole;
            isDead = false;
            CombatMetricsManager.SetDead(transform, false);
            _metricsShields.Clear();
            _isBeingKnockedBack = false;
            _isBeingRescuedByPlayer = false;
            _isInterceptingTetherForPlayer = false;
            isTankDefensiveActive = false;
            _tankDefensiveTimer = 0f;
            _invulnerableTimer = 0f;
            _rescueSpeedBoostTimer = 0f;
            _rescueCooldownTimer = 0f;
            _lastRescuedTelegraph = null;
            currentShield = 0f;

            ApplyRoleStatsAndColors(newRole);

            if (reposition)
            {
                transform.position = new Vector3(spawnPos.x, 1.0f, spawnPos.z);
                if (targetBoss != null)
                {
                    Vector3 toBossXZ = new Vector3(
                        targetBoss.transform.position.x - spawnPos.x,
                        0f,
                        targetBoss.transform.position.z - spawnPos.z
                    );
                    if (toBossXZ.sqrMagnitude > 0.001f)
                    {
                        transform.rotation = Quaternion.LookRotation(toBossXZ.normalized, Vector3.up);
                    }
                }
            }

            RefreshTankReference();
            UpdateOverheadHpBar();
        }

        public void ActivateBotDefensiveBuff(float duration)
        {
            ActivateTankDefensive(duration);
        }

        private void ApplyRoleStatsAndColors(PartyBotRole targetRole)
        {
            GetComponent<CharacterPartyStats>()?.ResetHealthFactor();
            _defaultBodyColor = GetRoleColor(targetRole);

            switch (targetRole)
            {
                case PartyBotRole.Tank:
                    maxHp = 2400f;
                    currentHp = maxHp;
                    break;
                case PartyBotRole.Healer:
                    maxHp = 1200f;
                    currentHp = maxHp;
                    break;
                case PartyBotRole.MeleeDPS:
                    maxHp = 1200f;
                    currentHp = maxHp;
                    break;
                case PartyBotRole.RangedDPS:
                default:
                    maxHp = 1100f;
                    currentHp = maxHp;
                    break;
            }

            if (dualVisual == null)
            {
                dualVisual = GetComponent<DualVisualController>();
            }
            if (dualVisual != null)
            {
                dualVisual.unitKind = targetRole switch
                {
                    PartyBotRole.Tank => VisualUnitKind.BotTank,
                    PartyBotRole.Healer => VisualUnitKind.BotHealer,
                    PartyBotRole.MeleeDPS => VisualUnitKind.PlayerMelee,
                    _ => VisualUnitKind.BotRangedDPS
                };
                dualVisual.base2DScale = targetRole == PartyBotRole.Tank ? 1.50f : 1.40f;
                dualVisual.SetPrimaryColor(_defaultBodyColor);
                dualVisual.Refresh2DSpritesAndColors();
            }

            ApplyBodyColor(_defaultBodyColor);

            if (_hpFillRenderer != null)
            {
                SetUnlitColor(_hpFillRenderer, GetRoleBrightColor(targetRole));
            }
        }

        public static Color GetRoleColor(PartyBotRole r)
        {
            return r switch
            {
                PartyBotRole.Tank => new Color(0.18f, 0.52f, 1.0f, 1f),      // 파랑
                PartyBotRole.Healer => new Color(0.18f, 0.88f, 0.38f, 1f),   // 초록
                PartyBotRole.MeleeDPS => new Color(1.0f, 0.85f, 0.12f, 1f),  // 노랑
                PartyBotRole.RangedDPS => new Color(1.0f, 0.50f, 0.12f, 1f), // 주황
                _ => Color.white
            };
        }

        public static Color GetRoleBrightColor(PartyBotRole r)
        {
            return r switch
            {
                PartyBotRole.Tank => new Color(0.28f, 0.68f, 1.0f, 1f),
                PartyBotRole.Healer => new Color(0.25f, 0.95f, 0.45f, 1f),
                PartyBotRole.MeleeDPS => new Color(1.0f, 0.90f, 0.22f, 1f),
                PartyBotRole.RangedDPS => new Color(1.0f, 0.58f, 0.18f, 1f),
                _ => Color.white
            };
        }

        public string GetRoleDisplayNameKor()
        {
            return role switch
            {
                PartyBotRole.Tank => "가디언 (탱커 봇)",
                PartyBotRole.Healer => "오라클 (힐러 봇)",
                PartyBotRole.MeleeDPS => "블레이드 (근딜 봇)",
                PartyBotRole.RangedDPS => "아크메이지 (캐스터 봇)",
                _ => "AI 봇"
            };
        }

        private void RefreshTankReference()
        {
            tankBotReference = null;
            PartyBotAI[] bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            foreach (PartyBotAI bot in bots)
            {
                if (bot != null && bot.role == PartyBotRole.Tank)
                {
                    tankBotReference = bot;
                    break;
                }
            }
        }

        private void Update()
        {
            if (RaidCombatAuthority.Replica || Time.timeScale <= 0.001f || targetBoss == null || StageManager.CombatSuspended ||
                targetBoss.HardTransformationActive)
            {
                return;
            }

            if (_rescueCooldownTimer > 0f)
            {
                _rescueCooldownTimer = Mathf.Max(0f, _rescueCooldownTimer - Time.deltaTime);
            }

            if (_berserkerAttackSpeedTimer > 0f)
            {
                _berserkerAttackSpeedTimer = Mathf.Max(0f, _berserkerAttackSpeedTimer - Time.deltaTime);
            }

            if (_invulnerableTimer > 0f)
            {
                _invulnerableTimer = Mathf.Max(0f, _invulnerableTimer - Time.deltaTime);
            }

            if (_rescueSpeedBoostTimer > 0f)
            {
                _rescueSpeedBoostTimer = Mathf.Max(0f, _rescueSpeedBoostTimer - Time.deltaTime);
            }

            if (isDead)
            {
                UpdateOverheadHpBar();
                return;
            }

            if (_tankDefensiveTimer > 0f)
            {
                _tankDefensiveTimer -= Time.deltaTime;
                if (_tankDefensiveTimer <= 0f)
                {
                    _tankDefensiveTimer = 0f;
                    isTankDefensiveActive = false;
                    ApplyBodyColor(_defaultBodyColor);
                }
            }

            if (targetBoss.IsBossDefeatedCollapse)
            {
                UpdateOverheadHpBar();
                return;
            }

            // 플레이어 힐러의 [4] 구출(Rescue)에 의해 끌려가는 중일 때 최우선 이동
            if (_isBeingRescuedByPlayer)
            {
                UpdateRescuedByPlayerMovementXZ();
                UpdateOverheadHpBar();
                return;
            }

            // 외곽 넉백(밀쳐내기) 중일 때는 넉백 이동 우선 처리
            if (_isBeingKnockedBack)
            {
                UpdateKnockbackMovementXZ();
                UpdateOverheadHpBar();
                return;
            }

            if (CharacterTrainingSession.Active) { UpdateOverheadHpBar(); return; }
            if(targetBoss.MarionetteControlled(transform)) { UpdateOverheadHpBar(); return; }
            if(targetBoss.GuardianLured(transform)) { UpdateOverheadHpBar(); return; }
            if (targetBoss.GuardianClashHolding(transform) || targetBoss.CampanellaLockHolding(transform))
            {
                if (role == PartyBotRole.Tank && RaidCombatAuthority.ServerTime >= _guardianDefenseReady &&
                    (targetBoss.GuardianClashPulseImminent(transform) || targetBoss.CampanellaLockPulseImminent(transform)))
                { ActivateTankDefensive(4.5f, "힘겨루기 맥동 대비 · 방어기"); _guardianDefenseReady = RaidCombatAuthority.ServerTime + 15; }
                UpdateOverheadHpBar(); return;
            }
            UpdateGuardianActions();
            switch (role)
            {
                case PartyBotRole.Tank:
                    UpdateTankAI();
                    break;

                case PartyBotRole.Healer:
                    UpdateHealerAI();
                    break;

                case PartyBotRole.MeleeDPS:
                    UpdateMeleeDPSAI();
                    break;

                case PartyBotRole.RangedDPS:
                    UpdateRangedDPSAI();
                    break;
            }

            UpdateOverheadHpBar();
        }

        /// <summary>
        /// 스테이지 클리어 후 카드 선택 시 봇 전원 체력 100% 회복 및 초기 위치로 재배치
        /// </summary>
        public void ResetBotForNewStage(Vector3 spawnPos)
        {
            Life?.ClearDeadForStagePlacement();
            Potion?.EndRevivalProtection();
            isDead = false;
            CombatMetricsManager.SetDead(transform, false);
            _metricsShields.Clear();
            _isBeingKnockedBack = false;
            _isBeingRescuedByPlayer = false;
            _isInterceptingTetherForPlayer = false;
            isTankDefensiveActive = false;
            _tankDefensiveTimer = 0f;
            _invulnerableTimer = 0f;
            _rescueCooldownTimer = 0f;
            _lastRescuedTelegraph = null;
            currentHp = maxHp;
            currentShield = 0f;

            transform.position = new Vector3(spawnPos.x, 1.0f, spawnPos.z);
            transform.rotation = Quaternion.identity;
            ApplyBodyColor(_defaultBodyColor);

            if (role == PartyBotRole.Tank && targetBoss != null &&
                (humanPlayer == null || humanPlayer.currentRole != PlayerRole.Guardian_Tank))
            {
                targetBoss.SetAggroTarget(transform);
            }
        }

        /// <summary>
        /// Step 8: 플레이어가 힐러(오라클)일 때 [4]번 '구출(Rescue)'로 이 봇을 안전지대로 끌어당기며 3초간 무적/이속 버프 부여!
        /// </summary>
        public void StartPulledByPlayerRescue(Vector3 safeDestinationXZ, float invulnDuration = 3.0f)
        {
            if (isDead || currentHp <= 0 || StageManager.CombatSuspended || targetBoss?.GuardianClashHolding(transform)==true || targetBoss?.CampanellaLockHolding(transform)==true || targetBoss?.MarionetteControlled(transform)==true) return;
            targetBoss?.OnGuardianLureRescue(transform,safeDestinationXZ);
            _isBeingKnockedBack = false;
            _isBeingRescuedByPlayer = true;
            _rescueByPlayerTargetXZ = new Vector3(safeDestinationXZ.x, 1.0f, safeDestinationXZ.z);
            _invulnerableTimer = Mathf.Max(_invulnerableTimer, invulnDuration);
            _rescueSpeedBoostTimer = Mathf.Max(_rescueSpeedBoostTimer, invulnDuration);

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.4f,
                $"구출(Rescue)됨! ({invulnDuration:F0}초 무적 & 이속 증가!)",
                new Color(0.3f, 0.98f, 1f, 1f),
                true
            );
        }

        private void UpdateRescuedByPlayerMovementXZ()
        {
            Vector3 current = transform.position;
            Vector3 target = new Vector3(_rescueByPlayerTargetXZ.x, 1.0f, _rescueByPlayerTargetXZ.z);
            transform.position = Vector3.MoveTowards(current, target, _rescueByPlayerSpeed * Time.deltaTime);

            Vector3 diffXZ = new Vector3(target.x - transform.position.x, 0f, target.z - transform.position.z);
            if (diffXZ.sqrMagnitude < 0.05f)
            {
                transform.position = target;
                _isBeingRescuedByPlayer = false;
            }
        }

        /// <summary>
        /// Stage 3 복합 기믹: 보스 중심에서 외곽으로 강하게 밀쳐내는 넉백(Knockback)
        /// </summary>
        public void StartKnockbackXZ(Vector3 originXZ, float knockbackDistance, float duration = 0.45f, bool ignoreImmunity = false)
        {
            if(RaidCombatAuthority.Replica||!CombatDamageRules.CanKnockback(GetComponent<CombatStatusEffects>()?.KnockbackImmunity??0,ignoreImmunity))return;
            if (isDead || currentHp <= 0 || StageManager.CombatSuspended) return;
            if (isDead || _isBeingRescuedByPlayer)
            {
                return;
            }

            Vector3 dirXZ = new Vector3(
                transform.position.x - originXZ.x,
                0f,
                transform.position.z - originXZ.z
            );

            if (dirXZ.sqrMagnitude < 0.001f)
            {
                dirXZ = role switch
                {
                    PartyBotRole.Tank => Vector3.forward,
                    PartyBotRole.Healer => new Vector3(-0.7f, 0f, -0.7f).normalized,
                    PartyBotRole.MeleeDPS => new Vector3(-0.7f, 0f, 0.7f).normalized,
                    PartyBotRole.RangedDPS => new Vector3(0.7f, 0f, -0.7f).normalized,
                    _ => Vector3.forward
                };
            }
            else
            {
                dirXZ.Normalize();
            }

            Vector3 targetPos = transform.position + dirXZ * knockbackDistance;
            Vector2 flat = new Vector2(targetPos.x, targetPos.z);
            if (flat.magnitude > 16.5f)
            {
                flat = flat.normalized * 16.5f;
            }

            _knockbackTargetXZ = new Vector3(flat.x, 1.0f, flat.y);
            float dist = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(_knockbackTargetXZ.x, _knockbackTargetXZ.z)
            );
            _knockbackSpeed = Mathf.Max(14f, dist / Mathf.Max(0.15f, duration));
            _isBeingKnockedBack = true;
        }

        private void UpdateKnockbackMovementXZ()
        {
            Vector3 current = transform.position;
            Vector3 target = new Vector3(_knockbackTargetXZ.x, 1.0f, _knockbackTargetXZ.z);
            transform.position = Vector3.MoveTowards(current, target, _knockbackSpeed * Time.deltaTime);

            Vector3 diffXZ = new Vector3(target.x - transform.position.x, 0f, target.z - transform.position.z);
            if (diffXZ.sqrMagnitude < 0.04f)
            {
                transform.position = target;
                _isBeingKnockedBack = false;
            }
        }

        public void ResetRescueCooldownImmediate()
        {
            _rescueCooldownTimer = 0f;
            _lastRescuedTelegraph = null;
        }

        public void ApplyBerserkerAttackSpeedBoost(float duration)
        {
            _berserkerAttackSpeedTimer = Mathf.Max(_berserkerAttackSpeedTimer, duration);
            // The card owner emits one party-wide notice for this pulse.
        }

        private float GetAttackSpeedMultiplier()
        {
            return _berserkerAttackSpeedTimer > 0f ? 1.6f : 1.0f;
        }

        private float GetMoveSpeedMultiplier(float baseMult = 1.0f)
        {
            if (targetBoss != null && targetBoss.GuardianNormal) baseMult = 1;
            float rescueBonus = _rescueSpeedBoostTimer > 0f ? 1.4f : 1.0f;
            return baseMult * rescueBonus;
        }

        /// <summary>
        /// 파티 협동 기믹(4인 쉐어 / 4인 산개) 목표 위치 계산
        /// </summary>
        private bool TryGetPartyMechanicDestinationXZ(out Vector3 mechanicDestXZ)
        {
            mechanicDestXZ = transform.position;
            if (targetBoss != null && targetBoss.GuardianNormal) return targetBoss.GuardianDestination(transform, (int)role+1, out mechanicDestXZ);
            if (targetBoss != null && targetBoss.FloraEncounter) return targetBoss.FloraDestination(transform, (int)role+1, out mechanicDestXZ);
            if (targetBoss != null && targetBoss.CampanellaEncounter) return targetBoss.CampanellaDestination(transform, (int)role+1, out mechanicDestXZ);
            if (targetBoss != null && targetBoss.MarionetteEncounter) return targetBoss.MarionetteDestination(transform,(int)role+1,out mechanicDestXZ);
            if (targetBoss != null && targetBoss.JustitiaEncounter) return targetBoss.JustitiaDestination(transform,(int)role+1,out mechanicDestXZ);
            if (targetBoss != null && targetBoss.JanusEncounter) return targetBoss.JanusDestination(transform,(int)role+1,out mechanicDestXZ);
            if (RaidNetworkSession.Multiplayer && targetBoss != null) return PartyAiTactics.PartyMechanic(transform, (int)role + 1, targetBoss, out mechanicDestXZ);
            if (targetBoss == null)
            {
                return false;
            }

            Vector3 bossPos = targetBoss.transform.position;

            // ① [4인 쉐어 (Stack)] 발동 중 -> 내 캐릭터(플레이어) 위치로 봇 3명이 즉시 달려와 뭉침!
            if (targetBoss.IsStackMechanicActive && targetBoss.StackTarget != null)
            {
                Vector3 playerPos = targetBoss.StackTarget.position;
                Vector3 stackOffset = role switch
                {
                    PartyBotRole.Tank => new Vector3(0f, 0f, 0.85f),
                    PartyBotRole.Healer => new Vector3(-0.80f, 0f, -0.55f),
                    PartyBotRole.MeleeDPS => new Vector3(-0.75f, 0f, 0.65f),
                    PartyBotRole.RangedDPS => new Vector3(0.80f, 0f, -0.55f),
                    _ => Vector3.zero
                };

                mechanicDestXZ = new Vector3(
                    playerPos.x + stackOffset.x,
                    1.0f,
                    playerPos.z + stackOffset.z
                );
                return true;
            }

            // ② [4인 산개 (Spread)] 발동 중 -> 서로 원이 겹치지 않도록 흩어짐!
            if (targetBoss.IsSpreadMechanicActive)
            {
                bool isDonutCombo = targetBoss.ActiveTelegraph != null &&
                                    targetBoss.ActiveTelegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12;

                // 현재 활성화된 봇 3명 중 내 순번(0, 1, 2)을 구해 항상 북/서/동 3방향으로 겹치지 않게 배정
                int botSlotIndex = GetBotSpreadSlotIndex();

                if (isDonutCombo)
                {
                    Vector3 donutSpreadOffset = botSlotIndex switch
                    {
                        0 => new Vector3(0f, 0f, 2.85f),      // 북쪽
                        1 => new Vector3(-2.85f, 0f, 0f),     // 서쪽
                        _ => new Vector3(2.85f, 0f, 0f)       // 동쪽 (남쪽은 플레이어 자리)
                    };
                    mechanicDestXZ = new Vector3(bossPos.x + donutSpreadOffset.x, 1.0f, bossPos.z + donutSpreadOffset.z);
                }
                else
                {
                    Vector3 spreadOffset = botSlotIndex switch
                    {
                        0 => new Vector3(0f, 0f, 8.2f),
                        1 => new Vector3(-8.5f, 0f, -7.2f),
                        _ => new Vector3(8.5f, 0f, -7.2f)
                    };

                    mechanicDestXZ = new Vector3(bossPos.x + spreadOffset.x, 1.0f, bossPos.z + spreadOffset.z);

                    if (humanPlayer != null)
                    {
                        Vector2 botXZ = new Vector2(mechanicDestXZ.x, mechanicDestXZ.z);
                        Vector2 playerXZ = new Vector2(humanPlayer.transform.position.x, humanPlayer.transform.position.z);
                        float minSafeDist = targetBoss.CurrentSpreadRadius * 2.25f;
                        Vector2 diff = botXZ - playerXZ;
                        if (diff.magnitude < minSafeDist)
                        {
                            Vector2 awayDir = diff.sqrMagnitude > 0.01f ? diff.normalized : new Vector2(0f, 1f);
                            Vector2 pushed = playerXZ + awayDir * (minSafeDist + 0.8f);
                            if (pushed.magnitude > 16.5f)
                            {
                                pushed = pushed.normalized * 16.5f;
                            }
                            mechanicDestXZ = new Vector3(pushed.x, 1.0f, pushed.y);
                        }
                    }
                }

                return true;
            }

            return false;
        }

        private int GetBotSpreadSlotIndex()
        {
            return role switch
            {
                PartyBotRole.Tank => 0,
                PartyBotRole.Healer => 1,
                PartyBotRole.RangedDPS => 2,
                PartyBotRole.MeleeDPS => (humanPlayer != null && humanPlayer.currentRole == PlayerRole.Guardian_Tank) ? 0
                    : (humanPlayer != null && humanPlayer.currentRole == PlayerRole.Oracle_Healer ? 1 : 2),
                _ => 0
            };
        }

        // =====================================================================
        // 1. 탱커 봇 AI (내가 탱커가 아닐 때 활성화됨): 북쪽(Z+) 주차 및 어그로 1순위 고정
        // =====================================================================
        private void UpdateTankAI()
        {
            _isInterceptingTetherForPlayer = false;
            // A world-space parking point must not move north each time the boss follows us.
            Vector3 desiredPos = PartyAiTactics.TankPosition(transform, targetBoss, MeleeAttackRange, tankNorthParkingDistance);
            if (TryGetMeleeMechanicDestination(out Vector3 mechanicPos)) desiredPos = mechanicPos;
            desiredPos = CombatRange.ClampToArena(desiredPos, PartyArenaRadius);

            var floraCore = targetBoss.FloraHard || targetBoss.CampanellaHard ?
                BossAddSpawner.Instance?.GetPriorityAddForBot(PartyBotRole.Tank) : null;
            MoveTowardsXZ(desiredPos, floraCore!=null?floraCore.transform.position:BossLookPointXZ(),
                GetMoveSpeedMultiplier(targetBoss.IsStackMechanicActive || targetBoss.IsSpreadMechanicActive ? 1.25f : 1.0f));

            // Stage setup and taunts own aggro; do not overwrite a valid new target every frame.

            BossAddMinion priorityAdd = BossAddSpawner.Instance != null
                ? BossAddSpawner.Instance.GetPriorityAddForBot(PartyBotRole.Tank)
                : null;

            // [Step 9] 힐러를 추격하는 [암살자] 쫄이 소환되면 탱커 봇이 자동으로 도발하여 힐러를 보호!
            if (priorityAdd != null && priorityAdd.addType == AddMinionType.Assassin_HealerHunter && priorityAdd.aggroTarget != transform)
            {
                priorityAdd.TauntBy(transform, botDisplayName);
            }

            // Hold the boss's parking spot. Taunted adds can approach us; never hit distant stationary adds.
            if (priorityAdd != null && !CanMeleeHit(priorityAdd.transform)) priorityAdd = null;

            CurrentActionTarget = priorityAdd != null ? priorityAdd.transform : targetBoss.transform;

            _actionTimer = Mathf.Min(tankAttackInterval, _actionTimer + Time.deltaTime * GetAttackSpeedMultiplier());
            if (_actionTimer >= tankAttackInterval && CanMeleeHit(CurrentActionTarget))
            {
                _actionTimer = 0f;
                if (priorityAdd != null)
                {
                    priorityAdd.ReceiveBotAttack(tankAttackDamage, transform);
                    ShowBeamEffect(
                        transform.position + Vector3.up * 0.2f,
                        priorityAdd.transform.position + Vector3.up * 1.1f,
                        new Color(0.25f, 0.65f, 1f, 0.9f),
                        0.14f,
                        0.12f
                    );
                }
                else
                {
                    targetBoss.ReceiveBotAttackDamage(tankAttackDamage, false, transform);
                    ShowBeamEffect(
                        transform.position + Vector3.up * 0.2f,
                        targetBoss.transform.position + Vector3.up * 1.2f,
                        new Color(0.25f, 0.65f, 1f, 0.9f),
                        0.14f,
                        0.12f
                    );
                }
            }
        }

        // =====================================================================
        // 2. 근딜 봇 AI (내가 탱커/힐러/캐스터일 때 활성화됨):
        //    - 평상시 보스 후면/측면을 잡으며 근접 참격
        //    - [핵심!] 내가 '탱커'를 선택해 2연타 탱크버스터 줄(Tether)이 나에게 연결되면,
        //      근딜 AI 봇이 자동으로 줄 사이로 달려들어 줄을 가로채고 생존기를 켜서 나를 지켜줌!
        // =====================================================================
        private void UpdateMeleeDPSAI()
        {
            Vector3 bossPos = targetBoss.transform.position;

            // [Step 8 핵심] 보스의 2연타 탱크버스터 줄(Tether)이 탱커 플레이어에게 연결된 경우 -> 근딜 봇이 즉시 가로채기 돌진!
            if (targetBoss.IsTetherActive && CombatRange.LivingPartyMember(targetBoss.TetherPrimaryTarget) && targetBoss.TetherPrimaryTarget != transform)
            {
                _isInterceptingTetherForPlayer = true;
                Vector3 interceptPoint = Vector3.Lerp(bossPos, targetBoss.TetherPrimaryTarget.position, 0.52f);
                interceptPoint.y = 1.0f;

                MoveTowardsXZ(interceptPoint, BossLookPointXZ(), GetMoveSpeedMultiplier(1.65f));

                if (!isTankDefensiveActive && targetBoss.IsTetherIntercepted)
                {
                    ActivateTankDefensive(4.5f, "MELEE BOT 생존기 ON! (탱커 줄 가로채기 방어!)");
                }
                return;
            }

            _isInterceptingTetherForPlayer = false;

            BossAddMinion priorityAdd = BossAddSpawner.Instance != null
                ? BossAddSpawner.Instance.GetPriorityAddForBot(PartyBotRole.MeleeDPS)
                : null;
            CurrentActionTarget = priorityAdd != null ? priorityAdd.transform : targetBoss.transform;

            Vector3 desiredPos;
            Vector3 lookPoint = priorityAdd != null ? priorityAdd.transform.position : BossLookPointXZ();
            if (TryGetMeleeMechanicDestination(out Vector3 mechanicPos))
            {
                desiredPos = mechanicPos;
            }
            else if (priorityAdd != null)
            {
                // [Step 9] 소환된 쫄(동력 공급체 우선)이 살아있으면 쫄에게 접근하여 집중 타격!
                Vector3 toAdd = priorityAdd.transform.position - bossPos;
                desiredPos = CombatRange.AttackPosition(transform, priorityAdd.transform, -toAdd, MeleeAttackRange);
            }
            else
            {
                // Recompute the rear position from the moving boss and its explicit combat radius.
                Vector3 rearDir = -targetBoss.transform.forward;
                rearDir.y = 0f;
                if (rearDir.sqrMagnitude < 0.01f) rearDir = Vector3.back;
                desiredPos = CombatRange.AttackPosition(transform, targetBoss.transform, rearDir, MeleeAttackRange);
            }

            desiredPos = CombatRange.ClampToArena(desiredPos, PartyArenaRadius);
            MoveTowardsXZ(desiredPos, lookPoint, GetMoveSpeedMultiplier(1.15f));

            _actionTimer = Mathf.Min(meleeAttackInterval, _actionTimer + Time.deltaTime * GetAttackSpeedMultiplier());
            if (_actionTimer >= meleeAttackInterval && CanMeleeHit(CurrentActionTarget))
            {
                _actionTimer = 0f;
                if (priorityAdd != null)
                {
                    priorityAdd.ReceiveBotAttack(meleeAttackDamage, transform);
                    ShowBeamEffect(
                        transform.position + Vector3.up * 0.25f,
                        priorityAdd.transform.position + Vector3.up * 1.15f,
                        new Color(1.0f, 0.88f, 0.20f, 0.95f),
                        0.18f,
                        0.13f
                    );
                }
                else
                {
                    targetBoss.ReceiveBotAttackDamage(meleeAttackDamage, false, transform);
                    ShowBeamEffect(
                        transform.position + Vector3.up * 0.25f,
                        targetBoss.transform.position + Vector3.up * 1.25f,
                        new Color(1.0f, 0.88f, 0.20f, 0.95f),
                        0.18f,
                        0.13f
                    );
                }
            }
        }

        public void ActivateTankDefensive(float duration, string customPopupText = null)
        {
            if (isDead)
            {
                return;
            }

            if (StageManager.CombatSuspended) return;
            Potion?.EndRevivalProtection();
            isTankDefensiveActive = true;
            _tankDefensiveTimer = duration;
            ApplyBodyColor(new Color(0.35f, 0.95f, 1f, 1f));

            string msg = string.IsNullOrEmpty(customPopupText)
                ? $"{role.ToString().ToUpper()} 뎀감기 ON! (피해 75% 감소)"
                : customPopupText;

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.3f,
                msg,
                new Color(0.3f, 0.95f, 1f, 1f),
                true
            );
        }

        private float PartyArenaRadius => Mathf.Max(0, (humanPlayer != null ? humanPlayer.arenaBoundaryRadius : 18f) - combatRadius - 0.5f);

        private bool TryGetMeleeMechanicDestination(out Vector3 destination)
        {
            if (TryGetPartyMechanicDestinationXZ(out destination)) return true;
            return PartyAiTactics.HazardDestination(transform, targetBoss, MeleeAttackRange, out destination);
        }

        public void KillTankByUninterceptedTether()
        {
            if (isDead || StageManager.CombatSuspended || IsInvulnerable || (Potion != null && Potion.HasRevivalProtection)) return;
            currentHp = 0;
            PartyLife.Ensure(gameObject).Die();
        }

        public void OnPartyDeath()
        {
            isDead = true;
            currentHp = currentShield = 0;
            ClearInterruptedActions();
            ApplyBodyColor(new Color(0.3f, 0.3f, 0.34f, 0.5f));
        }

        private void ClearInterruptedActions()
        {
            _isBeingKnockedBack = _isBeingRescuedByPlayer = _isInterceptingTetherForPlayer = false;
            isTankDefensiveActive = false;
            _tankDefensiveTimer = _invulnerableTimer = _rescueSpeedBoostTimer = _berserkerAttackSpeedTimer = _actionTimer = 0;
            CurrentActionTarget = null;
            _metricsShields.Clear();
            StopAllCoroutines(); _beamCoroutine = null;
            if (_effectBeam != null) _effectBeam.enabled = false;
            Resurrection?.Cancel();
        }

        public void RestorePartyLife(float fraction, bool resetCooldowns = false)
        {
            ClearInterruptedActions();
            isDead = false;
            currentShield = 0;
            currentHp = maxHp * Mathf.Clamp01(fraction);
            ApplyBodyColor(_defaultBodyColor);
            if (resetCooldowns) { _actionTimer = ActionInterval; _rescueCooldownTimer = 0; _lastRescuedTelegraph = null; }
        }

        public void ReviveBot() => IrisRescue.TryUse(transform, transform, StageManager.Instance?.AttemptId, PartyLife.Ensure(gameObject).DeathGeneration);

        private void UpdateHealerAI()
        {
            _isInterceptingTetherForPlayer = false;
            Vector3 bossPos = targetBoss.transform.position;
            Vector3 safePos = new Vector3(bossPos.x + healerDesiredOffsetXZ.x, 1f, bossPos.z + healerDesiredOffsetXZ.z);
            bool mechanic = TryGetPartyMechanicDestinationXZ(out Vector3 mechanicPos);
            if (mechanic) safePos = mechanicPos;
            var hazard = targetBoss.ActiveTelegraph;
            if (!mechanic && hazard != null && TryGetMeleeMechanicDestination(out mechanicPos))
            {
                mechanic = true;
                safePos = mechanicPos;
            }
            bool emergency = false;
            foreach (var member in PartyLife.OrderedMembers())
            {
                if (!CombatRange.LivingPartyMember(member)) continue;
                var human = member.GetComponent<PlayerController>();
                var bot = member.GetComponent<PartyBotAI>();
                if ((human != null ? human.HpRatio : bot.HpRatio) <= 0.25f) { emergency = true; break; }
            }
            if (mechanic || emergency) Resurrection?.Cancel();
            if (!targetBoss.GuardianNormal && !targetBoss.FloraEncounter && !targetBoss.CampanellaEncounter) CheckAndTriggerHealerRescue(safePos);
            bool holdingOrApproaching = !mechanic && !emergency && TryHandleResurrection(hazard);
            if (!holdingOrApproaching)
                MoveTowardsXZ(safePos, BossLookPointXZ(), GetMoveSpeedMultiplier(mechanic ? 1.3f : 1f));
            if (Resurrection != null && Resurrection.IsCasting) return;

            // Healing keeps ticking while approaching a corpse, or when resurrection cannot be used.
            _actionTimer += Time.deltaTime * GetAttackSpeedMultiplier();
            if (_actionTimer >= healCheckInterval)
            {
                _actionTimer = 0f;
                TryHealLowestPartyMember();
            }
        }

        private bool TryHandleResurrection(AoETelegraph hazard)
        {
            if (Resurrection == null || StageManager.Instance == null || StageManager.Instance.IrisCoins <= 0) return false;
            if (Resurrection.IsCasting) return true;
            if (Resurrection.Cooldown > 0) return false;
            Transform corpse = Resurrection.ChooseBotTarget();
            if (corpse == null) return false;
            float range = Resurrection.Settings.range;
            if (BattleResurrection.FlatDistance(transform.position, corpse.position) <= range)
            {
                Resurrection.TryBegin(corpse);
                return true; // Stand still for the next attempt if we moved earlier this frame.
            }
            if (!PartyAiTactics.ResurrectionApproach(transform, corpse, range, PartyArenaRadius, hazard, out Vector3 best)) return false;
            MoveTowardsXZ(best, corpse.position, GetMoveSpeedMultiplier(1f));
            return true;
        }

        private void CheckAndTriggerHealerRescue(Vector3 healerSafeSpotXZ)
        {
            if (targetBoss == null || !targetBoss.IsCasting)
            {
                return;
            }

            AoETelegraph telegraph = targetBoss.ActiveTelegraph;
            if (telegraph == null)
            {
                return;
            }

            bool isCircleOrDonut =
                telegraph.telegraphType == AoETelegraphType.Circle_Radius6 ||
                telegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12;

            if (!isCircleOrDonut)
            {
                return;
            }

            if (targetBoss.CastProgress < rescueTriggerCastProgress ||
                _rescueCooldownTimer > 0f ||
                _lastRescuedTelegraph == telegraph)
            {
                return;
            }

            var rescueMember = PartyLife.OrderedMembers().Find(member => member != transform && CombatRange.LivingPartyMember(member) &&
                CombatRange.InRange(transform, member, 20) && telegraph.IsPointInsideXZ(member.position));
            if (rescueMember != null)
            {
                Resurrection?.Cancel();
                Potion?.EndRevivalProtection();
                _lastRescuedTelegraph = telegraph;
                _rescueCooldownTimer = rescueCooldown;

                transform.position = new Vector3(healerSafeSpotXZ.x, 1.0f, healerSafeSpotXZ.z);
                Vector3 rescueDestinationXZ = new Vector3(
                    transform.position.x + 0.9f,
                    1.0f,
                    transform.position.z + 0.5f
                );

                if (telegraph.telegraphType == AoETelegraphType.Circle_Radius6)
                {
                    Vector3 fromBoss = new Vector3(
                        rescueDestinationXZ.x - targetBoss.transform.position.x,
                        0f,
                        rescueDestinationXZ.z - targetBoss.transform.position.z
                    );
                    if (fromBoss.magnitude < telegraph.outerRadius + 1.2f)
                    {
                        fromBoss = fromBoss.normalized * (telegraph.outerRadius + 1.5f);
                        rescueDestinationXZ = new Vector3(
                            targetBoss.transform.position.x + fromBoss.x,
                            1.0f,
                            targetBoss.transform.position.z + fromBoss.z
                        );
                    }
                }
                else if (telegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12)
                {
                    if (targetBoss.IsSpreadMechanicActive)
                    {
                        rescueDestinationXZ = new Vector3(
                            targetBoss.transform.position.x,
                            1.0f,
                            targetBoss.transform.position.z - 2.85f
                        );
                    }
                    else
                    {
                        rescueDestinationXZ = new Vector3(
                            targetBoss.transform.position.x - 1.8f,
                            1.0f,
                            targetBoss.transform.position.z - 1.8f
                        );
                    }
                }

                FaceTargetXZ(rescueMember.position);
                var rescuedPlayer = rescueMember.GetComponent<PlayerController>();
                if (rescuedPlayer != null) rescuedPlayer.StartPulledByHealerRescue(rescueDestinationXZ);
                else rescueMember.GetComponent<PartyBotAI>()?.StartPulledByPlayerRescue(rescueDestinationXZ);
                ShowTrackingRescueBeam(rescueMember, 0.55f);

                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.4f,
                    "HEALER 구출(Rescue) 발동!! (쿨타임 30초)",
                    new Color(0.3f, 0.95f, 1f, 1f),
                    true
                );

                targetBoss.ShowExternalAlert(
                    "힐러 봇의 [구출(Rescue)] 발동! 장판 80% 시전 시점에 위험 반경의 플레이어를 끌어당겨 살려냈습니다! (쿨타임 30초)",
                    new Color(0.3f, 0.95f, 1f, 1f),
                    4.0f
                );
            }
        }

        private void TryHealLowestPartyMember()
        {
            Transform target = PartyAiTactics.LowestHealth(out _);
            if (target == null) { CurrentActionTarget = targetBoss != null ? targetBoss.transform : null; return; }
            CurrentActionTarget = target; FaceTargetXZ(target.position);
            var player = target.GetComponent<PlayerController>();
            float healed = player != null ? player.ReceiveHeal(healAmount, transform) : target.GetComponent<PartyBotAI>().ReceiveHeal(healAmount, transform);
            if (healed > 0) Potion?.EndRevivalProtection();
            ShowTrackingHealBeam(target, healBeamDuration);
        }

        // =====================================================================
        // 4. 캐스터/원딜 봇 AI: 남동쪽 외곽에서 무빙하며 보스에게 주기적으로 마법 투사체 발사
        // =====================================================================
        private void UpdateRangedDPSAI()
        {
            _isInterceptingTetherForPlayer = false;
            _strafePhase += Time.deltaTime * rangedStrafeSpeed;

            Vector3 bossPos = targetBoss.transform.position;
            Vector3 strafeOffset = new Vector3(
                Mathf.Sin(_strafePhase) * rangedStrafeAmplitude,
                0f,
                Mathf.Cos(_strafePhase) * (rangedStrafeAmplitude * 0.65f)
            );

            Vector3 desiredSoutheastPos = new Vector3(
                bossPos.x + rangedBaseOffsetXZ.x + strafeOffset.x,
                1.0f,
                bossPos.z + rangedBaseOffsetXZ.z + strafeOffset.z
            );

            if (TryGetPartyMechanicDestinationXZ(out Vector3 mechanicPos))
            {
                desiredSoutheastPos = mechanicPos;
            }
            else if (targetBoss.ActiveTelegraph != null &&
                     targetBoss.ActiveTelegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12)
            {
                desiredSoutheastPos = new Vector3(bossPos.x + 2.2f, 1.0f, bossPos.z - 2.2f);
            }

            MoveTowardsXZ(desiredSoutheastPos, BossLookPointXZ(), GetMoveSpeedMultiplier(targetBoss.IsStackMechanicActive || targetBoss.IsSpreadMechanicActive ? 1.3f : 1.0f));

            _actionTimer += Time.deltaTime * GetAttackSpeedMultiplier();
            if (_actionTimer >= rangedFireInterval)
            {
                _actionTimer = 0f;
                FireProjectileAtBoss();
            }
        }

        private void FireProjectileAtBoss()
        {
            if (targetBoss == null || targetBoss.currentHp <= 0 || isDead || StageManager.CombatSuspended) return;
            Potion?.EndRevivalProtection();
            BossAddMinion priorityAdd = BossAddSpawner.Instance != null
                ? BossAddSpawner.Instance.GetPriorityAddForBot(PartyBotRole.RangedDPS)
                : null;

            if (priorityAdd != null)
            {
                CurrentActionTarget = priorityAdd.transform;
                FaceTargetXZ(priorityAdd.transform.position);
                priorityAdd.ReceiveBotAttack(projectileDamage, transform);
                ShowBeamEffect(
                    transform.position + Vector3.up * 0.35f,
                    priorityAdd.transform.position + Vector3.up * 1.15f,
                    new Color(1.0f, 0.55f, 0.18f, 0.92f),
                    0.15f,
                    0.12f
                );
                return;
            }

            CurrentActionTarget = targetBoss.transform;
            FaceTargetXZ(targetBoss.transform.position);

            Vector3 spawnPos = transform.position + transform.forward * 0.65f + Vector3.up * 0.25f;
            RangedBotProjectile.Spawn(spawnPos, targetBoss, projectileSpeed, projectileDamage, transform);
            RaidNetworkSession.Instance?.BroadcastEffect(spawnPos, targetBoss.transform.position + Vector3.up, new Color(1f, .65f, .15f), null, false, 1);
        }

        // =====================================================================
        // 공통 이동 / 체력 / 보호막 / 이펙트 유틸리티 (XZ 평면 기준)
        // =====================================================================
        private Vector3 BossLookPointXZ()
        {
            return targetBoss != null ? targetBoss.transform.position : Vector3.zero;
        }

        private void MoveTowardsXZ(Vector3 destinationWorld, Vector3 lookTargetWhenArrived, float speedMultiplier = 1.0f)
        {
            Vector3 currentPos = transform.position;
            Vector3 toDestXZ = new Vector3(
                destinationWorld.x - currentPos.x,
                0f,
                destinationWorld.z - currentPos.z
            );

            float distXZ = toDestXZ.magnitude;
            if (distXZ > 0.08f)
            {
                Vector3 moveDir = toDestXZ / distXZ;
                float step = Mathf.Min(distXZ, moveSpeed * speedMultiplier * Time.deltaTime);
                transform.position = new Vector3(
                    currentPos.x + moveDir.x * step,
                    currentPos.y,
                    currentPos.z + moveDir.z * step
                );
                if(targetBoss?.MarionetteEncounter==true)
                    transform.position=MarionetteNormalRules.ClampToStage(transform.position,targetBoss.MarionetteStageHalfSize,combatRadius+.3f);
                if(targetBoss?.JustitiaEncounter==true)
                    transform.position=JustitiaNormalRules.Clamp(transform.position,targetBoss.JustitiaHalfWidth,targetBoss.JustitiaHalfDepth,combatRadius+.3f);
                if(targetBoss?.JanusEncounter==true)
                    transform.position=JanusNormalRules.Clamp(transform.position,targetBoss.janus.arenaRadius-combatRadius-.3f);

                Quaternion moveRot = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, moveRot, rotationSpeed * Time.deltaTime);
            }
            else
            {
                if(targetBoss?.JanusEncounter==true&&targetBoss.JanusAiCounterTarget(transform) is Transform janusCounter)
                {FaceTargetXZ(janusCounter.position);return;}
                FaceTargetXZ(lookTargetWhenArrived);
            }
        }

        private void FaceTargetXZ(Vector3 targetWorldPos)
        {
            Vector3 toTargetXZ = new Vector3(
                targetWorldPos.x - transform.position.x,
                0f,
                targetWorldPos.z - transform.position.z
            );

            if (toTargetXZ.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toTargetXZ.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
        }

        public void TakeDamage(float rawAmount, bool allowLethal = false, CombatHitContext hit = default)
        {
            if (RaidCombatAuthority.Replica || isDead || currentHp <= 0 || rawAmount <= 0 || StageManager.CombatSuspended)
            {
                return;
            }

            if(!_resolvedCombatHits.Accept(hit.EventId))return;
            if (Potion != null && Potion.HasRevivalProtection) return;
            if(hit.Kind!=CombatDamageKind.System&&GetComponent<CombatStatusEffects>()?.CombatInvulnerable==true)return;

            if (IsInvulnerable)
            {
                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.1f,
                    "INVULNERABLE! (구출 무적 0 피해)",
                    new Color(0.35f, 1f, 0.95f, 1f),
                    true
                );
                return;
            }

            ExpireTimedShields();
            float finalAmount = hit.Kind!=CombatDamageKind.Transfer&&isTankDefensiveActive ? rawAmount * 0.22f : rawAmount;
            finalAmount*=AegisExtensions.IncomingScale(transform,hit.Kind);
            finalAmount=CharacterKit.Incoming(transform,finalAmount,hit);
            finalAmount=AegisPaladin.ApplyMirrorProtection(transform,finalAmount,hit);
            finalAmount=AegisExtensions.SharedDamage(transform,finalAmount,hit);

            // 노란색 보호막(Shield) 우선 차감
            float absorbedByShield = 0f;
            if (currentShield > 0f && finalAmount > 0f)
            {
                _metricsShields.Reconcile(currentShield);
                absorbedByShield = Mathf.Min(currentShield, finalAmount);
                currentShield -= absorbedByShield;
                finalAmount -= absorbedByShield;
                _metricsShields.Absorb(absorbedByShield,(id,amount)=>CombatSecondaryDamage.Absorption(id,amount,hit),(id,key,amount)=>NineKeyHealerKit.OnLayerAbsorbed(transform,id,key,(float)amount));
            }

            currentHp = Mathf.Max(0f,CharacterKit.PreserveLife(transform,currentHp-finalAmount,hit.Kind));

            string msg = isTankDefensiveActive
                ? $"-{Mathf.RoundToInt(finalAmount)} (생존기 방어!)"
                : (absorbedByShield > 0.5f
                    ? $"-{Mathf.RoundToInt(finalAmount)} (보호막 {Mathf.RoundToInt(absorbedByShield)} 흡수!)"
                    : $"-{Mathf.RoundToInt(finalAmount)}");

            Color col = isTankDefensiveActive
                ? new Color(0.35f, 0.9f, 1f, 1f)
                : (absorbedByShield > 0.5f ? new Color(1f, 0.92f, 0.3f, 1f) : new Color(1f, 0.45f, 0.45f, 1f));

            FloatingTextPopup.Spawn(transform.position + Vector3.up * 2.0f, msg, col, isTankDefensiveActive);

            if (currentHp <= 0f)
            {
                PartyLife.Ensure(gameObject).Die();
            }
        }

        public float ReceiveHeal(float amount, Transform source = null, HealingKind kind = HealingKind.Ability)
        {
            if (RaidCombatAuthority.Replica || isDead || currentHp <= 0 || amount <= 0f || StageManager.PlayerSkillsSuspended)
            {
                return 0;
            }

            amount*=CharacterKit.HealingScale(source,transform);
            float before = currentHp;
            currentHp = Mathf.Min(maxHp, currentHp + amount);
            float actualHealed = currentHp - before;
            CharacterTrainingSession.Instance?.RecordOverheal(amount - actualHealed);
            CharacterKit.AfterHeal(source,transform,actualHealed,amount,before,kind);
            CombatMetricsManager.Healing(kind == HealingKind.Potion ? transform : source != null ? source : transform, before, currentHp, kind);

            if (actualHealed > 0.5f || (kind == HealingKind.Potion && actualHealed > 0))
            {
                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.1f,
                    kind == HealingKind.Potion ? $"포션 +{actualHealed:0.##} HP" : $"+{Mathf.RoundToInt(actualHealed)} HP",
                    new Color(0.25f, 1f, 0.45f, 1f),
                    false
                );
            }
            return actualHealed;
        }

        public void AddShield(float shieldAmount, Transform source = null)
        {
            if (RaidCombatAuthority.Replica || isDead || currentHp <= 0 || shieldAmount <= 0f || StageManager.PlayerSkillsSuspended)
            {
                return;
            }

            shieldAmount=CombatStatusEffects.ShieldAmount(shieldAmount,source!=null?source:transform);
            _metricsShields.Reconcile(currentShield);
            float shieldBefore = currentShield;
            currentShield = Mathf.Min(maxHp, currentShield + shieldAmount);
            _metricsShields.Grant(CombatMetricsManager.SourceId(source != null ? source : transform), currentShield - shieldBefore);
            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.35f,
                $"+{Mathf.RoundToInt(shieldAmount)} SHIELD (보호막)",
                new Color(1f, 0.92f, 0.25f, 1f),
                false
            );
        }

        private void ApplyBodyColor(Color color)
        {
            if (dualVisual != null)
            {
                dualVisual.SetPrimaryColor(color);
            }

            if (bodyRenderer == null || bodyRenderer.material == null)
            {
                return;
            }
            bodyRenderer.material.color = color;
            if (bodyRenderer.material.HasProperty("_BaseColor"))
            {
                bodyRenderer.material.SetColor("_BaseColor", color);
            }
            if (bodyRenderer.material.HasProperty("_Color"))
            {
                bodyRenderer.material.SetColor("_Color", color);
            }
        }

        private void CreateBeamLineRenderer()
        {
            if (_effectBeam != null)
            {
                return;
            }

            GameObject beamObj = new GameObject("Bot_EffectBeam");
            beamObj.transform.SetParent(transform, false);

            _effectBeam = beamObj.AddComponent<LineRenderer>();
            _effectBeam.useWorldSpace = true;
            _effectBeam.positionCount = 2;
            _effectBeam.numCapVertices = 4;
            _effectBeam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _effectBeam.receiveShadows = false;
            _effectBeam.sortingOrder = 15;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            _effectBeam.sharedMaterial = new Material(shader) { color = Color.white };
            _effectBeam.enabled = false;
        }

        private void ShowBeamEffect(Vector3 start, Vector3 end, Color color, float width, float duration)
        {
            RaidNetworkSession.Instance?.BroadcastEffect(start, end, color, null, false, 1);
            if (_effectBeam == null)
            {
                return;
            }

            if (_beamCoroutine != null)
            {
                StopCoroutine(_beamCoroutine);
            }
            _beamCoroutine = StartCoroutine(StaticBeamRoutine(start, end, color, width, duration));
        }

        private void ShowTrackingHealBeam(Transform healTarget, float duration)
        {
            if (_effectBeam == null || healTarget == null)
            {
                return;
            }

            if (_beamCoroutine != null)
            {
                StopCoroutine(_beamCoroutine);
            }
            _beamCoroutine = StartCoroutine(TrackingBeamRoutine(
                healTarget,
                duration,
                new Color(0.15f, 1f, 0.38f, 0.95f),
                new Color(0.45f, 1f, 0.65f, 0.95f),
                0.22f
            ));
        }

        private void ShowTrackingRescueBeam(Transform rescueTarget, float duration)
        {
            if (rescueTarget != null) RaidNetworkSession.Instance?.BroadcastEffect(transform.position + Vector3.up, rescueTarget.position + Vector3.up, Color.cyan, null, false, 1);
            if (_effectBeam == null || rescueTarget == null)
            {
                return;
            }

            if (_beamCoroutine != null)
            {
                StopCoroutine(_beamCoroutine);
            }
            _beamCoroutine = StartCoroutine(TrackingBeamRoutine(
                rescueTarget,
                duration,
                new Color(1f, 0.92f, 0.25f, 1f),
                new Color(0.25f, 0.95f, 1f, 1f),
                0.30f
            ));
        }

        private IEnumerator StaticBeamRoutine(Vector3 start, Vector3 end, Color color, float width, float duration)
        {
            _effectBeam.enabled = true;
            _effectBeam.startWidth = width;
            _effectBeam.endWidth = width * 0.5f;
            _effectBeam.startColor = color;
            _effectBeam.endColor = color;
            _effectBeam.SetPosition(0, start);
            _effectBeam.SetPosition(1, end);

            yield return new WaitForSeconds(duration);
            _effectBeam.enabled = false;
            _beamCoroutine = null;
        }

        private IEnumerator TrackingBeamRoutine(Transform target, float duration, Color startCol, Color endCol, float width)
        {
            _effectBeam.enabled = true;
            _effectBeam.startColor = startCol;
            _effectBeam.endColor = endCol;
            _effectBeam.startWidth = width;
            _effectBeam.endWidth = width * 0.75f;

            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                _effectBeam.SetPosition(0, transform.position + Vector3.up * 0.25f);
                _effectBeam.SetPosition(1, target.position + Vector3.up * 0.25f);
                yield return null;
            }

            _effectBeam.enabled = false;
            _beamCoroutine = null;
        }

        private void CreateOverheadHpBar()
        {
            if (_hpBarRoot != null)
            {
                return;
            }

            _hpBarRoot = new GameObject("Overhead_HpBar").transform;
            _hpBarRoot.SetParent(transform, false);
            _hpBarRoot.localPosition = new Vector3(0f, 1.55f, 0f);

            GameObject bgObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgObj.name = "HP_Bg";
            Destroy(bgObj.GetComponent<Collider>());
            bgObj.transform.SetParent(_hpBarRoot, false);
            bgObj.transform.localPosition = Vector3.zero;
            bgObj.transform.localScale = new Vector3(1.25f, 0.15f, 1f);
            SetUnlitColor(bgObj.GetComponent<Renderer>(), new Color(0.1f, 0.1f, 0.12f, 0.9f));

            GameObject fillObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fillObj.name = "HP_Fill";
            Destroy(fillObj.GetComponent<Collider>());
            _hpFillBar = fillObj.transform;
            _hpFillBar.SetParent(_hpBarRoot, false);
            _hpFillBar.localPosition = new Vector3(0f, 0f, -0.01f);
            _hpFillBar.localScale = new Vector3(1.20f, 0.11f, 1f);
            _hpFillRenderer = fillObj.GetComponent<Renderer>();

            Color roleColor = GetRoleBrightColor(role);
            SetUnlitColor(_hpFillRenderer, roleColor);

            GameObject nameObj = new GameObject("NameLabel");
            nameObj.transform.SetParent(_hpBarRoot, false);
            nameObj.transform.localPosition = new Vector3(0f, 0.24f, 0f);
            nameObj.transform.localScale = Vector3.one * 0.075f;
            _nameLabelText = nameObj.AddComponent<TextMesh>();
            _nameLabelText.text = botDisplayName;
            _nameLabelText.fontSize = 40;
            _nameLabelText.fontStyle = FontStyle.Bold;
            _nameLabelText.anchor = TextAnchor.MiddleCenter;
            _nameLabelText.alignment = TextAlignment.Center;
            _nameLabelText.color = roleColor;
        }

        private void UpdateOverheadHpBar()
        {
            if (_hpBarRoot == null)
            {
                return;
            }

            bool is2D = ViewModeManager.Is2DViewActive;
            _hpBarRoot.position = transform.position + (is2D ? new Vector3(0f, 1.55f, 1.15f) : new Vector3(0f, 1.55f, 0f));

            Camera cam = Camera.main;
            if (cam != null)
            {
                _hpBarRoot.rotation = cam.transform.rotation;
            }

            if (_hpFillBar != null)
            {
                float ratio = HpRatio;
                const float fullWidth = 1.20f;
                _hpFillBar.localScale = new Vector3(fullWidth * ratio, 0.11f, 1f);
                _hpFillBar.localPosition = new Vector3(-fullWidth * (1f - ratio) * 0.5f, 0f, -0.01f);
            }

            if (_nameLabelText == null)
            {
                return;
            }

            // Keep identity and urgent state readable above the actor. HP/shield
            // totals already have party HUD entries; long AI debug prose hides telegraphs.
            string label = botDisplayName;
            Color color = GetRoleBrightColor(role);
            if (isDead)
            {
                label += " · 전투 불능";
                color = new Color(1f, 0.3f, 0.3f, 1f);
            }
            else if (_isInterceptingTetherForPlayer)
            {
                label += " · 줄 인계";
                color = new Color(0.25f, 1f, 0.65f, 1f);
            }
            else if (targetBoss != null && targetBoss.IsStackMechanicActive)
            {
                label += " · 집결";
                color = new Color(1f, 0.88f, 0.2f, 1f);
            }
            else if (targetBoss != null && targetBoss.IsSpreadMechanicActive)
            {
                label += " · 산개";
                color = new Color(1f, 0.45f, 0.85f, 1f);
            }
            else if (isTankDefensiveActive)
            {
                label += " · 방어";
                color = new Color(0.35f, 0.95f, 1f, 1f);
            }
            else if (role == PartyBotRole.Healer && _rescueCooldownTimer > 0f)
            {
                label += " · 구출 " + Mathf.CeilToInt(_rescueCooldownTimer) + "s";
            }
            _nameLabelText.text = label;
            _nameLabelText.color = color;
        }

        private static void SetUnlitColor(Renderer r, Color color)
        {
            if (r == null)
            {
                return;
            }
            Shader s = Shader.Find("Sprites/Default");
            if (s == null)
            {
                s = Shader.Find("Universal Render Pipeline/Unlit");
            }
            Material mat = new Material(s) { color = color };
            r.sharedMaterial = mat;
        }
    }

    public partial class RangedBotProjectile : MonoBehaviour
    {
        private BossController _targetBoss;
        private Transform _source;
        private int _encounterId;
        private float _speed;
        private int _damage;
        private float _lifetime = 3.0f;
        private Material _ownedMaterial;

        public static RangedBotProjectile Spawn(Vector3 startPos, BossController boss, float speed, int damage, Transform source)
        {
            GameObject projObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projObj.name = "RangedDPS_Projectile";
            projObj.transform.position = startPos;
            projObj.transform.localScale = Vector3.one * 0.38f;

            Collider col = projObj.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            RangedBotProjectile proj = projObj.AddComponent<RangedBotProjectile>();
            Renderer r = projObj.GetComponent<Renderer>();
            if (r != null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }
                proj._ownedMaterial = new Material(shader)
                {
                    color = new Color(1f, 0.55f, 0.12f, 1f)
                };
                r.sharedMaterial = proj._ownedMaterial;
            }

            TrailRenderer trail = projObj.AddComponent<TrailRenderer>();
            trail.time = 0.18f;
            trail.startWidth = 0.25f;
            trail.endWidth = 0.02f;
            trail.startColor = new Color(1f, 0.65f, 0.15f, 0.9f);
            trail.endColor = new Color(1f, 0.3f, 0.05f, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            if (r != null)
            {
                trail.sharedMaterial = r.sharedMaterial;
            }

            proj._targetBoss = boss;
            proj._source = source;
            proj._encounterId = CombatMetricsManager.EncounterId;
            proj._speed = speed;
            proj._damage = damage;
            return proj;
        }

        private void OnDestroy()
        {
            // Release Spawn's one owned material, shared by both the sphere and trail.
            if (_ownedMaterial != null) Destroy(_ownedMaterial);
            _ownedMaterial = null;
        }

        private void Update()
        {
            if (_encounterId != CombatMetricsManager.EncounterId || StageManager.PlayerSkillsSuspended || !CombatRange.LivingPartyMember(_source)) { Destroy(gameObject); return; }
            if (Time.timeScale <= 0.001f) return;
            _lifetime -= Time.deltaTime;
            if (_lifetime <= 0f || _targetBoss == null || _targetBoss.IsBossDefeatedCollapse)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 targetPos = new Vector3(
                _targetBoss.transform.position.x,
                1.35f,
                _targetBoss.transform.position.z
            );

            Vector3 toTarget = targetPos - transform.position;
            Vector2 toTargetXZ = new Vector2(toTarget.x, toTarget.z);

            if (toTargetXZ.magnitude <= 0.85f)
            {
                _targetBoss.ReceiveBotAttackDamage(_damage, true, _source);
                Destroy(gameObject);
                return;
            }

            transform.position = Vector3.MoveTowards(transform.position, targetPos, _speed * Time.deltaTime);
        }
    }
}
