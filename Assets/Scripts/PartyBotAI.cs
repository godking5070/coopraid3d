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
    public class PartyBotAI : MonoBehaviour
    {
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
        public bool isDead;

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
        private float _deadReviveTimer;
        private float _rescueCooldownTimer;
        private float _berserkerAttackSpeedTimer;
        private float _invulnerableTimer;
        private float _rescueSpeedBoostTimer;
        private bool _isInterceptingTetherForPlayer;
        private AoETelegraph _lastRescuedTelegraph;

        // 플레이어 힐러의 '구출(Rescue)'에 의해 끌려가는 상태
        private bool _isBeingRescuedByPlayer;
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
            role = newRole;
            isDead = false;
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
            if (Time.timeScale <= 0.001f || targetBoss == null)
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
                UpdateDeadState();
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
            isDead = false;
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
            if (isDead)
            {
                return;
            }

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
        public void StartKnockbackXZ(Vector3 originXZ, float knockbackDistance, float duration = 0.45f)
        {
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
            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.2f,
                "공격속도 증가! (광전사의 함성)",
                new Color(1f, 0.4f, 0.45f, 1f),
                false
            );
        }

        private float GetAttackSpeedMultiplier()
        {
            return _berserkerAttackSpeedTimer > 0f ? 1.6f : 1.0f;
        }

        private float GetMoveSpeedMultiplier(float baseMult = 1.0f)
        {
            float rescueBonus = _rescueSpeedBoostTimer > 0f ? 1.4f : 1.0f;
            return baseMult * rescueBonus;
        }

        /// <summary>
        /// 파티 협동 기믹(4인 쉐어 / 4인 산개) 목표 위치 계산
        /// </summary>
        private bool TryGetPartyMechanicDestinationXZ(out Vector3 mechanicDestXZ)
        {
            mechanicDestXZ = transform.position;
            if (targetBoss == null)
            {
                return false;
            }

            Vector3 bossPos = targetBoss.transform.position;

            // ① [4인 쉐어 (Stack)] 발동 중 -> 내 캐릭터(플레이어) 위치로 봇 3명이 즉시 달려와 뭉침!
            if (targetBoss.IsStackMechanicActive && humanPlayer != null)
            {
                Vector3 playerPos = humanPlayer.transform.position;
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
            Vector3 bossPos = targetBoss.transform.position;
            Vector3 desiredPos = new Vector3(
                bossPos.x,
                1.0f,
                bossPos.z + tankNorthParkingDistance
            );

            if (TryGetPartyMechanicDestinationXZ(out Vector3 mechanicPos))
            {
                desiredPos = mechanicPos;
            }
            else if (targetBoss.ActiveTelegraph != null &&
                     targetBoss.ActiveTelegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12)
            {
                desiredPos = new Vector3(bossPos.x, 1.0f, bossPos.z + 2.8f);
            }

            MoveTowardsXZ(desiredPos, BossLookPointXZ(), GetMoveSpeedMultiplier(targetBoss.IsStackMechanicActive || targetBoss.IsSpreadMechanicActive ? 1.25f : 1.0f));

            // 플레이어가 탱커가 아닐 때는 탱커 봇이 어그로 1순위를 유지
            if ((humanPlayer == null || humanPlayer.currentRole != PlayerRole.Guardian_Tank) &&
                targetBoss.aggroTarget != transform)
            {
                targetBoss.SetAggroTarget(transform);
            }

            BossAddMinion priorityAdd = BossAddSpawner.Instance != null
                ? BossAddSpawner.Instance.GetPriorityAddForBot(PartyBotRole.Tank)
                : null;

            // [Step 9] 힐러를 추격하는 [암살자] 쫄이 소환되면 탱커 봇이 자동으로 도발하여 힐러를 보호!
            if (priorityAdd != null && priorityAdd.addType == AddMinionType.Assassin_HealerHunter && priorityAdd.aggroTarget != transform)
            {
                priorityAdd.TauntBy(transform, botDisplayName);
            }

            CurrentActionTarget = priorityAdd != null ? priorityAdd.transform : targetBoss.transform;

            _actionTimer += Time.deltaTime * GetAttackSpeedMultiplier();
            if (_actionTimer >= tankAttackInterval)
            {
                _actionTimer = 0f;
                if (priorityAdd != null)
                {
                    priorityAdd.ReceiveBotAttack(tankAttackDamage);
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
                    targetBoss.ReceiveBotAttackDamage(tankAttackDamage, false);
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
            if (targetBoss.IsTetherActive && humanPlayer != null && targetBoss.TetherPrimaryTarget == humanPlayer.transform)
            {
                _isInterceptingTetherForPlayer = true;
                Vector3 interceptPoint = Vector3.Lerp(bossPos, humanPlayer.transform.position, 0.52f);
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
            if (TryGetPartyMechanicDestinationXZ(out Vector3 mechanicPos))
            {
                desiredPos = mechanicPos;
            }
            else if (targetBoss.ActiveTelegraph != null &&
                     targetBoss.ActiveTelegraph.telegraphType == AoETelegraphType.Circle_Radius6)
            {
                // 원형 6m 장판 시전 중에는 남서/남동 바깥 안전거리(7.2m)로 잠시 회피
                Vector3 backDir = -targetBoss.transform.forward;
                backDir.y = 0f;
                if (backDir.sqrMagnitude < 0.01f) backDir = Vector3.back;
                desiredPos = bossPos + backDir.normalized * 7.2f;
                desiredPos.y = 1.0f;
            }
            else if (priorityAdd != null && (targetBoss.ActiveTelegraph == null || targetBoss.ActiveTelegraph.telegraphType != AoETelegraphType.Donut_Inner4_Outer12))
            {
                // [Step 9] 소환된 쫄(동력 공급체 우선)이 살아있으면 쫄에게 접근하여 집중 타격!
                Vector3 toAdd = priorityAdd.transform.position - bossPos;
                toAdd.y = 0f;
                Vector3 offsetFromAdd = toAdd.sqrMagnitude > 0.01f ? -toAdd.normalized * 1.6f : new Vector3(0f, 0f, -1.6f);
                desiredPos = priorityAdd.transform.position + offsetFromAdd;
                desiredPos.y = 1.0f;
            }
            else
            {
                // 평상시 및 도넛/부채꼴 장판 시에는 보스 후면(Rear 180도, 2.8m)에 위치하여 안전하게 후면 딜링!
                Vector3 rearDir = -targetBoss.transform.forward;
                rearDir.y = 0f;
                if (rearDir.sqrMagnitude < 0.01f) rearDir = Vector3.back;
                desiredPos = bossPos + rearDir.normalized * 2.85f;
                desiredPos.y = 1.0f;
            }

            MoveTowardsXZ(desiredPos, lookPoint, GetMoveSpeedMultiplier(1.15f));

            _actionTimer += Time.deltaTime * GetAttackSpeedMultiplier();
            if (_actionTimer >= meleeAttackInterval)
            {
                _actionTimer = 0f;
                if (priorityAdd != null)
                {
                    priorityAdd.ReceiveBotAttack(meleeAttackDamage);
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
                    targetBoss.ReceiveBotAttackDamage(meleeAttackDamage, false);
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

        public void KillTankByUninterceptedTether()
        {
            if (isDead)
            {
                return;
            }

            currentHp = 0f;
            currentShield = 0f;
            isDead = true;
            isTankDefensiveActive = false;
            _deadReviveTimer = 6.5f;

            transform.position = new Vector3(transform.position.x, 0.5f, transform.position.z);
            transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            ApplyBodyColor(new Color(0.25f, 0.25f, 0.28f, 1f));

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.4f,
                $"{role} DEAD!! (줄 가로채기 실패!)",
                new Color(1f, 0.15f, 0.15f, 1f),
                true
            );

            if (targetBoss != null && humanPlayer != null)
            {
                targetBoss.SetAggroTarget(humanPlayer.transform);
            }
        }

        public void ReviveBot()
        {
            isDead = false;
            currentHp = maxHp;
            currentShield = 0f;
            transform.position = new Vector3(transform.position.x, 1.0f, transform.position.z);
            transform.rotation = Quaternion.identity;
            ApplyBodyColor(_defaultBodyColor);

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.3f,
                $"{role} 부활 완료!",
                new Color(0.3f, 1f, 0.55f, 1f),
                false
            );

            if (role == PartyBotRole.Tank && targetBoss != null &&
                (humanPlayer == null || humanPlayer.currentRole != PlayerRole.Guardian_Tank))
            {
                targetBoss.SetAggroTarget(transform);
            }
        }

        private void UpdateDeadState()
        {
            _deadReviveTimer -= Time.deltaTime;
            if (_deadReviveTimer <= 0f)
            {
                ReviveBot();
            }
        }

        // =====================================================================
        // 3. 힐러 봇 AI: 남서쪽 위치 유지, 최저 체력 아군 스마트 회복 & 위험 장판 80% 시 '구출(Rescue)'
        // =====================================================================
        private void UpdateHealerAI()
        {
            _isInterceptingTetherForPlayer = false;
            Vector3 bossPos = targetBoss.transform.position;
            Vector3 desiredSafePos = new Vector3(
                bossPos.x + healerDesiredOffsetXZ.x,
                1.0f,
                bossPos.z + healerDesiredOffsetXZ.z
            );

            if (TryGetPartyMechanicDestinationXZ(out Vector3 mechanicPos))
            {
                desiredSafePos = mechanicPos;
            }
            else if (targetBoss.ActiveTelegraph != null &&
                     targetBoss.ActiveTelegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12)
            {
                desiredSafePos = new Vector3(bossPos.x - 2.1f, 1.0f, bossPos.z - 2.1f);
            }

            MoveTowardsXZ(desiredSafePos, BossLookPointXZ(), GetMoveSpeedMultiplier(targetBoss.IsStackMechanicActive || targetBoss.IsSpreadMechanicActive ? 1.3f : 1.0f));

            CheckAndTriggerHealerRescue(desiredSafePos);

            _actionTimer += Time.deltaTime * GetAttackSpeedMultiplier();
            if (_actionTimer >= healCheckInterval)
            {
                _actionTimer = 0f;
                TryHealLowestPartyMember();
            }
        }

        private void CheckAndTriggerHealerRescue(Vector3 healerSafeSpotXZ)
        {
            if (humanPlayer == null || targetBoss == null || !targetBoss.IsCasting)
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

            if (telegraph.IsPointInsideXZ(humanPlayer.transform.position))
            {
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

                FaceTargetXZ(humanPlayer.transform.position);
                humanPlayer.StartPulledByHealerRescue(rescueDestinationXZ);
                ShowTrackingRescueBeam(humanPlayer.transform, 0.55f);

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
            float lowestRatio = 1.0f;
            bool healPlayer = false;
            PartyBotAI targetBot = null;

            if (humanPlayer != null && humanPlayer.NeedsHealing)
            {
                lowestRatio = humanPlayer.HpRatio;
                healPlayer = true;
            }

            PartyBotAI[] allBots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            foreach (PartyBotAI bot in allBots)
            {
                if (bot != null && bot.NeedsHealing && bot.HpRatio < lowestRatio)
                {
                    lowestRatio = bot.HpRatio;
                    healPlayer = false;
                    targetBot = bot;
                }
            }

            if (healPlayer && humanPlayer != null)
            {
                CurrentActionTarget = humanPlayer.transform;
                FaceTargetXZ(humanPlayer.transform.position);
                humanPlayer.ReceiveHeal(healAmount);
                ShowTrackingHealBeam(humanPlayer.transform, healBeamDuration);
            }
            else if (targetBot != null)
            {
                CurrentActionTarget = targetBot.transform;
                FaceTargetXZ(targetBot.transform.position);
                targetBot.ReceiveHeal(healAmount);
                ShowTrackingHealBeam(targetBot.transform, healBeamDuration);
            }
            else if (targetBoss != null)
            {
                CurrentActionTarget = targetBoss.transform;
            }
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
            if (targetBoss == null)
            {
                return;
            }

            BossAddMinion priorityAdd = BossAddSpawner.Instance != null
                ? BossAddSpawner.Instance.GetPriorityAddForBot(PartyBotRole.RangedDPS)
                : null;

            if (priorityAdd != null)
            {
                CurrentActionTarget = priorityAdd.transform;
                FaceTargetXZ(priorityAdd.transform.position);
                priorityAdd.ReceiveBotAttack(projectileDamage);
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
            RangedBotProjectile.Spawn(spawnPos, targetBoss, projectileSpeed, projectileDamage);
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
                    destinationWorld.y,
                    currentPos.z + moveDir.z * step
                );

                Quaternion moveRot = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, moveRot, rotationSpeed * Time.deltaTime);
            }
            else
            {
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

        public void TakeDamage(float rawAmount, bool allowLethal = false)
        {
            if (isDead)
            {
                return;
            }

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

            float finalAmount = isTankDefensiveActive ? rawAmount * 0.22f : rawAmount;

            // 노란색 보호막(Shield) 우선 차감
            float absorbedByShield = 0f;
            if (currentShield > 0f && finalAmount > 0f)
            {
                absorbedByShield = Mathf.Min(currentShield, finalAmount);
                currentShield -= absorbedByShield;
                finalAmount -= absorbedByShield;
            }

            float minClamp = allowLethal ? 0f : 120f;
            currentHp = Mathf.Max(minClamp, currentHp - finalAmount);

            string msg = isTankDefensiveActive
                ? $"-{Mathf.RoundToInt(finalAmount)} (생존기 방어!)"
                : (absorbedByShield > 0.5f
                    ? $"-{Mathf.RoundToInt(finalAmount)} (보호막 {Mathf.RoundToInt(absorbedByShield)} 흡수!)"
                    : $"-{Mathf.RoundToInt(finalAmount)}");

            Color col = isTankDefensiveActive
                ? new Color(0.35f, 0.9f, 1f, 1f)
                : (absorbedByShield > 0.5f ? new Color(1f, 0.92f, 0.3f, 1f) : new Color(1f, 0.45f, 0.45f, 1f));

            FloatingTextPopup.Spawn(transform.position + Vector3.up * 2.0f, msg, col, isTankDefensiveActive);

            if (allowLethal && currentHp <= 0.1f)
            {
                KillTankByUninterceptedTether();
            }
        }

        public void ReceiveHeal(float amount)
        {
            if (isDead)
            {
                return;
            }

            float before = currentHp;
            currentHp = Mathf.Min(maxHp, currentHp + amount);
            float actualHealed = currentHp - before;

            if (actualHealed > 0.5f)
            {
                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.1f,
                    $"+{Mathf.RoundToInt(actualHealed)} HP",
                    new Color(0.25f, 1f, 0.45f, 1f),
                    false
                );
            }
        }

        public void AddShield(float shieldAmount)
        {
            if (isDead || shieldAmount <= 0f)
            {
                return;
            }

            currentShield = Mathf.Min(maxHp, currentShield + shieldAmount);
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
            _nameLabelText.text = GetRoleDisplayNameKor();
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

            if (_isInterceptingTetherForPlayer)
            {
                _nameLabelText.text = "MELEE BOT [탱커 줄 가로채기 & 생존기 방어!]";
                _nameLabelText.color = new Color(0.25f, 1f, 0.65f, 1f);
                return;
            }

            if (targetBoss != null && targetBoss.IsStackMechanicActive)
            {
                _nameLabelText.text = $"{role} [4인 쉐어 집결 중!]";
                _nameLabelText.color = new Color(1f, 0.88f, 0.2f, 1f);
                return;
            }

            if (targetBoss != null && targetBoss.IsSpreadMechanicActive)
            {
                _nameLabelText.text = $"{role} [4인 산개 위치 이동!]";
                _nameLabelText.color = new Color(1f, 0.45f, 0.85f, 1f);
                return;
            }

            string shieldSuffix = currentShield > 1f ? $" (+보호막 {Mathf.RoundToInt(currentShield)})" : "";

            if (role == PartyBotRole.Tank)
            {
                if (isDead)
                {
                    _nameLabelText.text = $"TANK [사망 - 부활 {_deadReviveTimer:F1}s]";
                    _nameLabelText.color = new Color(1f, 0.3f, 0.3f, 1f);
                }
                else if (isTankDefensiveActive)
                {
                    _nameLabelText.text = $"TANK [방어 뎀감기 ON!]{shieldSuffix}";
                    _nameLabelText.color = new Color(0.35f, 0.95f, 1f, 1f);
                }
                else
                {
                    _nameLabelText.text = $"TANK (북쪽 주차){shieldSuffix}";
                    _nameLabelText.color = new Color(0.25f, 0.65f, 1f, 1f);
                }
            }
            else if (role == PartyBotRole.Healer)
            {
                if (_rescueCooldownTimer > 0f)
                {
                    _nameLabelText.text = $"HEALER (구출 쿨: {_rescueCooldownTimer:F0}s){shieldSuffix}";
                    _nameLabelText.color = new Color(0.65f, 0.9f, 0.7f, 1f);
                }
                else
                {
                    _nameLabelText.text = $"HEALER (구출: 준비됨){shieldSuffix}";
                    _nameLabelText.color = new Color(0.25f, 0.95f, 0.45f, 1f);
                }
            }
            else if (role == PartyBotRole.MeleeDPS)
            {
                _nameLabelText.text = isTankDefensiveActive
                    ? $"MELEE BOT [생존기 방어 ON!]{shieldSuffix}"
                    : $"MELEE (근딜 봇){shieldSuffix}";
                _nameLabelText.color = new Color(1f, 0.90f, 0.22f, 1f);
            }
            else if (role == PartyBotRole.RangedDPS)
            {
                _nameLabelText.text = $"CASTER (캐스터 봇){shieldSuffix}";
                _nameLabelText.color = new Color(1f, 0.58f, 0.15f, 1f);
            }
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

    public class RangedBotProjectile : MonoBehaviour
    {
        private BossController _targetBoss;
        private float _speed;
        private int _damage;
        private float _lifetime = 3.0f;

        public static RangedBotProjectile Spawn(Vector3 startPos, BossController boss, float speed, int damage)
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

            Renderer r = projObj.GetComponent<Renderer>();
            if (r != null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }
                r.sharedMaterial = new Material(shader)
                {
                    color = new Color(1f, 0.55f, 0.12f, 1f)
                };
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

            RangedBotProjectile proj = projObj.AddComponent<RangedBotProjectile>();
            proj._targetBoss = boss;
            proj._speed = speed;
            proj._damage = damage;
            return proj;
        }

        private void Update()
        {
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
                _targetBoss.ReceiveBotAttackDamage(_damage, true);
                Destroy(gameObject);
                return;
            }

            transform.position = Vector3.MoveTowards(transform.position, targetPos, _speed * Time.deltaTime);
        }
    }
}
