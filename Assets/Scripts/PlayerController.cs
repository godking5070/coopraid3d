using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public enum PlayerRole
    {
        Guardian_Tank = 1,   // 1. 가디언 (탱커 - 파랑)
        Oracle_Healer = 2,   // 2. 오라클 (힐러 - 초록)
        Blade_MeleeDPS = 3,  // 3. 블레이드 (근딜 - 노랑)
        Archmage_Caster = 4  // 4. 아크메이지 (캐스터 - 주황)
    }

    /// <summary>
    /// 내 캐릭터 (4대 역할군: 가디언/오라클/블레이드/아크메이지) 컨트롤러 (Step 1 ~ Step 8 통합):
    /// - WASD로 XZ 평면 이동 및 이동 방향으로 부드러운 회전
    /// - RoleSelectionManager와 연동하여 선택한 직업에 따라 3D/2D 외형 색상, 최대 체력, 방어기, 전용 스킬셋 즉시 전환
    /// - PlayerCombatController와 연동하여 직업별 GCD 3단 진화 콤보, 캐스터 캐스팅 바(이동 시 취소), 힐러 스마트 힐/보호막/성역/구출, 탱커 철벽방어/도발&줄강탈 수행
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public partial class PlayerController : MonoBehaviour
    {
        [Header("Combat Body (world units, independent of visual scale)")]
        [Min(0f)] public float combatRadius = 0.55f;
        [Header("Role Identity (Step 8)")]
        public PlayerRole currentRole = PlayerRole.Blade_MeleeDPS;
        public string playerRoleDisplayName = "ME (블레이드·근딜)";

        [Header("Movement (XZ Plane)")]
        public float moveSpeed = 7.8f;
        public float rotationSmoothSpeed = 720f;
        public float arenaBoundaryRadius = 18f;

        [Header("Common SPACE Dash (all jobs, independent of GCD)")]
        public DashSettings dashSettings = new DashSettings();

        [Header("Health (HP) & Shield (Step 8)")]
        public float maxHp = 1000f;
        public float currentHp = 1000f;
        public float currentShield = 0f;
        private readonly CombatShieldLedger _metricsShields = new CombatShieldLedger();
        [Range(0f, 0.6f)]
        public float tankPassiveDefenseRate = 0.25f; // 가디언(탱커) 기본 패시브 방어력 25%

        [Header("Combat & Skills")]
        public BossController targetBoss;
        public PlayerCombatController combatController;
        public RoguelikeCardManager cardManager;
        public StageManager stageManager;
        public int baseSkillDamage = 100;

        [Header("Skill [3] Personal Defensive Cooldown")]
        public float defensiveDuration = 3.0f;
        [Range(0f, 1f)]
        public float damageReductionRate = 0.80f; // 직군별: 근딜 80%, 탱커(철벽) 60%, 캐스터(마나배리어) 70%

        [Header("Roguelike Card Build Flags (Step 4 ~ Step 8 영구 중첩)")]
        public bool hasMasterOfRear;     // 1) [후면의 지배자 / 직업 주력기 강화]
        public int masterOfRearStacks;
        public bool hasGhostStep;        // 2) [유령 걸음]: 이동속도 30% 증가 및 측·후면 판정 각도 15도 확장
        public int ghostStepStacks;
        public bool hasThornArmor;       // 3) [가시 갑옷]: 줄 가로채기/철벽방어 시 보스에게 큰 반사 피해
        public int thornArmorStacks;
        public bool hasTacticalRescue;   // 4) [전술적 구출]: 구출 발동/피격 시 5초간 공격력 2배 & 힐러 구출 쿨타임 감소
        public int tacticalRescueStacks;
        public bool hasBerserkerShout;   // 5) [광전사의 함성]: 10초마다 파티 전체 공속 버프 자동 발동
        public int berserkerShoutStacks;
        public bool hasVampiricBlade;    // 6) [흡혈 검 / 오버힐 신성 전환]: 공격 시 자힐 & 힐러 오버힐 보스 피해 전환
        public int vampiricBladeStacks;
        public int swiftAwakeningStacks; // 7) [신속의 각성]: 글로벌 쿨타임(GCD) 및 캐스팅 시간 단축

        public float positionalAngleBonusDeg;
        public float thornReflectDamage = 600f;

        [Header("Visuals")]
        public Renderer bodyRenderer;
        public DualVisualController dualVisual;
        public Color normalBodyColor = new Color(1f, 0.85f, 0.12f, 1f);    // 기본(근딜): 노란색
        public Color defensiveBodyColor = new Color(0.15f, 0.52f, 1f, 1f); // 생존기 활성 색상

        [Header("Optional UI References")]
        public Text hudStatusText;

        public bool IsDefensiveBuffActive => _defensiveRemainingTime > 0f;
        public float DefensiveRemainingTime => _defensiveRemainingTime;
        public float CurrentDamageReduction => IsDefensiveBuffActive ? damageReductionRate : 0f;
        public float HpRatio => maxHp > 0f ? Mathf.Clamp01(currentHp / maxHp) : 0f;
        public float ShieldRatio => maxHp > 0f ? Mathf.Clamp01(currentShield / maxHp) : 0f;
        public bool NeedsHealing => !IsDead && currentHp < maxHp - 1f;
        public bool IsBeingRescued => _isBeingRescued;
        public bool IsBeingKnockedBack => _isBeingKnockedBack;
        public bool HasTacticalRescueBuff => _tacticalRescueBuffTimer > 0f;
        public bool IsMovingXZ { get; private set; }
        public bool IsDashing => RaidNetworkSession.Multiplayer ? NetworkMotor?.Dash.Active == true : _dash.Active;
        public DashMotion.Snapshot CaptureDash() => RaidNetworkSession.Multiplayer ? NetworkMotor?.Dash.Capture() ?? default : _dash.Capture();
        public float DashCooldownRemaining => (float)(RaidNetworkSession.Multiplayer ? NetworkMotor?.Dash.CooldownRemaining ?? 0 : _dash.CooldownRemaining);
        public float DashCooldownFraction => RaidNetworkSession.Multiplayer ? Mathf.Clamp01(DashCooldownRemaining / Mathf.Max(0.001f, (float)(NetworkMotor?.Dash.CooldownDuration ?? 0))) : _dash.CooldownDuration > 0
            ? Mathf.Clamp01((float)(_dash.CooldownRemaining / _dash.CooldownDuration)) : 0;
        // Shared voluntary-movement gates for status effects; the current encounters do not apply stuns/roots yet.
        private bool _externalStun;
        public bool IsStunned { get => _externalStun || GetComponent<CombatStatusEffects>()?.Stunned == true; set => _externalStun=value; }
        private bool _externalRoot;
        public bool IsMovementLocked { get => _externalRoot || GetComponent<CombatStatusEffects>()?.Rooted == true; set => _externalRoot=value; }
        public bool CanMoveVoluntarily => isActiveAndEnabled && !IsDead && (!StageManager.CombatSuspended || RaidNetworkSession.Multiplayer) && !IsStunned && !IsMovementLocked &&
            !IsBeingRescued && !IsBeingKnockedBack && !RaidCombatAuthority.InputBlocked(this);
        public bool CanDash => CanMoveVoluntarily && (!RaidCombatAuthority.Active || GetComponent<NetworkRaidAvatar>()?.MovementLocked == false) && !IsDashing && DashCooldownRemaining <= 0;
        public bool HasMovementIntent => RaidCombatAuthority.Active ? IsMovingXZ : !LocalGameplayInput.Blocked && ReadMoveInput().sqrMagnitude > 0.0001f;
        public bool IsCastInterruptImmune => currentRole == PlayerRole.Archmage_Caster && IsDefensiveBuffActive;

        private float _defensiveRemainingTime;
        private float _tacticalRescueBuffTimer;
        private float _rescueSpeedBoostTimer;
        private readonly DashMotion _dash = new DashMotion();
        private readonly DashPressGate _dashPress = new DashPressGate();
        private readonly PlayerMovementCollision _movementCollision = new PlayerMovementCollision();
        private PlayerDashPresentation _dashPresentation;
        public CommonPotion Potion { get; private set; }
        public PartyLife Life { get; private set; }
        public bool IsDead => (Life != null && Life.IsDead) || currentHp <= 0;

        // 힐러 '구출(Rescue)' 끌려감 상태
        private bool _isBeingRescued;
        private Vector3 _rescueDestinationXZ;
        private float _rescuePullSpeed = 30f;

        // Stage 3 보스 '외곽 넉백(Knockback)' 밀려남 상태
        private bool _isBeingKnockedBack;
        private Vector3 _knockbackTargetXZ;
        private float _knockbackSpeed = 24f;

        private LineRenderer _slashEffectLine;
        private Coroutine _slashEffectCoroutine;

        // 머리 위 3D 체력바, 보호막바 및 이름표
        private Transform _hpBarRoot;
        private Transform _hpFillBar;
        private Renderer _hpFillRenderer;
        private Transform _shieldFillBar;
        private TextMesh _overheadNameText;

        private bool _initialized;
        private void Start() => EnsureCombatInitialized();
        public void EnsureCombatInitialized()
        {
            if (_initialized) return; _initialized = true;
            Potion = CommonPotion.Ensure(gameObject);
            Life = PartyLife.Ensure(gameObject);
            if (targetBoss == null)
            {
                targetBoss = FindFirstObjectByType<BossController>();
            }
            if (combatController == null)
            {
                combatController = GetComponent<PlayerCombatController>();
            }
            if (cardManager == null)
            {
                cardManager = FindFirstObjectByType<RoguelikeCardManager>();
            }
            if (stageManager == null)
            {
                stageManager = FindFirstObjectByType<StageManager>();
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
                    bodyRenderer = GetComponent<MeshRenderer>();
                    if (bodyRenderer == null)
                    {
                        bodyRenderer = GetComponentInChildren<MeshRenderer>();
                    }
                }
            }

            CreateSlashEffectLine();
            CreateOverheadHpBar("ME (블레이드·근딜)");
            ApplyRoleStatsAndVisuals(currentRole, false);
            _dashPresentation = GetComponent<PlayerDashPresentation>();
            if (_dashPresentation == null) _dashPresentation = gameObject.AddComponent<PlayerDashPresentation>();
            _dashPresentation.Initialize(this);

            // Step 9: 통합 타겟팅 매니저 및 좌측 적 목록 UI 자동 보장
            if (FindFirstObjectByType<TargetingManager>() == null)
            {
                GameObject tmObj = new GameObject("TargetingManager");
                TargetingManager tm = tmObj.AddComponent<TargetingManager>();
                tm.player = this;
                tm.boss = targetBoss;
            }
            if (FindFirstObjectByType<EnemyListUI>() == null)
            {
                GameObject elObj = new GameObject("EnemyListUI");
                EnemyListUI el = elObj.AddComponent<EnemyListUI>();
                el.player = this;
                el.boss = targetBoss;
            }
        }

        private void Update()
        {
            if (RaidNetworkSession.Multiplayer)
            {
                if (RaidCombatAuthority.Local(this))
                {
                    bool allowed = !LocalGameplayInput.Blocked && RaidNetworkSession.Instance.HasLocalAvatar;
                    bool pressed = _dashPress.Consume(WasDashPressed(), IsDashKeyHeld(), allowed);
                    if (pressed && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                    RaidNetworkSession.Instance.SampleInput(allowed ? GetMoveDirectionXZ(ReadMoveInput()) : Vector3.zero, pressed);
                }
                if (RaidCombatAuthority.Host && !StageManager.CombatSuspended) UpdateTimers();
                UpdateOverheadHpBar(); if (RaidCombatAuthority.Local(this)) UpdateHUD();
                return;
            }
            if (RaidCombatAuthority.Replica || IsDead || StageManager.CombatSuspended)
            {
                _dash.Cancel(); IsMovingXZ = false;
                UpdateOverheadHpBar(); UpdateHUD();
                return;
            }
            bool gameplayAllowed = !RaidCombatAuthority.InputBlocked(this);
            bool dashPressed = _dashPress.Consume(WasDashPressed(), IsDashKeyHeld(), gameplayAllowed);
            if (!CanMoveVoluntarily) _dash.Cancel();
            // Player Update runs before EventSystem: SPACE belongs to dash during gameplay,
            // never to Submit on a previously clicked skill/settings button. Text/menu input is preserved.
            if (dashPressed && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (Time.timeScale <= 0.001f)
            {
                IsMovingXZ = false;
                return;
            }

            UpdateTimers();
            _dash.TickCooldown(Time.deltaTime);
            if (dashPressed) TryStartDash();
            Vector3 positionBeforeMovement = transform.position;

            if (_isBeingRescued)
            {
                UpdateRescuePullMovementXZ();
            }
            else if (_isBeingKnockedBack)
            {
                UpdateKnockbackMovementXZ();
            }
            else if (IsDashing)
            {
                UpdateDashMovementXZ();
            }
            else
            {
                HandleMovementXZ();
                if (combatController == null)
                {
                    HandleLegacySkillInputs();
                }
            }

            Vector3 actualMovement = transform.position - positionBeforeMovement;
            actualMovement.y = 0;
            IsMovingXZ = actualMovement.sqrMagnitude > 0.000001f;
            if (IsMovingXZ && targetBoss != null)
                targetBoss.TryInterceptPlayerMovement(this, positionBeforeMovement, transform.position);
            UpdateOverheadHpBar();
            UpdateHUD();
        }

        public bool TryStartDash()
        {
            if (RaidCombatAuthority.Active || RaidNetworkSession.CombatPreview) return false; // Multiplayer dash is consumed as a host-validated input command.
            Vector3 direction = GetMoveDirectionXZ(ReadMoveInput());
            Vector3 facing = transform.forward;
            var character = CharacterActor.Ensure(gameObject);
            if (!character.Available(CharacterSlot.Movement, out _)) return false;
            if (!character.StartMovement(_dash, dashSettings, CanMoveVoluntarily, direction.x, direction.z, facing.x, facing.z)) return false;
            if (!character.MovementSettings(dashSettings).preserveCast) { GetComponent<BattleResurrection>()?.Cancel(); character.InterruptCast(); }
            Potion?.EndRevivalProtection();
            if (_dashPresentation != null) _dashPresentation.BeginDash();
            return true;
        }

        private void UpdateDashMovementXZ()
        {
            var character = GetComponent<CharacterActor>(); character?.MarkDashMovement(character.MovementSettings(dashSettings).preserveCast);
            Vector3 direction = new Vector3((float)_dash.DirectionX, 0, (float)_dash.DirectionZ);
            Vector3 start = transform.position;
            float distance = (float)_dash.Step(Time.deltaTime);
            transform.position = _movementCollision.Move(this, direction, distance, out bool blocked);
            if (blocked) _dash.Cancel();
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction),
                rotationSmoothSpeed * Time.deltaTime);
            if (_dashPresentation != null) _dashPresentation.RecordMovement(start, transform.position);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            _dash.Cancel();
            _dashPress.RequireRelease();
        }

        private void OnDisable()
        {
            _dash.Cancel();
            _dashPress.RequireRelease();
            IsMovingXZ = false;
            if (_dashPresentation != null) _dashPresentation.ClearTrail();
        }

        private void UpdateTimers()
        {
            if (_defensiveRemainingTime > 0f)
            {
                _defensiveRemainingTime -= Time.deltaTime;
                if (_defensiveRemainingTime <= 0f)
                {
                    _defensiveRemainingTime = 0f;
                    ApplyBodyColor(normalBodyColor);
                }
            }

            if (_tacticalRescueBuffTimer > 0f)
            {
                _tacticalRescueBuffTimer = Mathf.Max(0f, _tacticalRescueBuffTimer - Time.deltaTime);
            }

            if (_rescueSpeedBoostTimer > 0f)
            {
                _rescueSpeedBoostTimer = Mathf.Max(0f, _rescueSpeedBoostTimer - Time.deltaTime);
            }
        }

        public void SetNetworkDisplayName(string displayName, Color color)
        {
            if (_overheadNameText == null) return;
            _overheadNameText.text = displayName;
            _overheadNameText.color = color;
        }

        /// <summary>
        /// Step 8 핵심: 선택한 직업(1.가디언 / 2.오라클 / 3.블레이드 / 4.아크메이지)에 맞춰
        /// 내 캐릭터의 3D/2D 외형 색상(탱=파랑, 힐=초록, 근딜=노랑, 캐스터=주황), 최대 체력, 방어율, 스킬 핫바를 즉시 세팅합니다.
        /// </summary>
        public void ApplyRoleStatsAndVisuals(PlayerRole role, bool restoreFullHp = true)
        {
            currentRole = role;
            _defensiveRemainingTime = 0f;

            switch (role)
            {
                case PlayerRole.Guardian_Tank:
                    // ① 가디언 (탱커): 최대 체력 3배(3,000), 기본 방어력 보유, 외형 색상 파랑
                    maxHp = 3000f;
                    normalBodyColor = new Color(0.18f, 0.55f, 1.0f, 1f);
                    defensiveBodyColor = new Color(0.38f, 0.92f, 1.0f, 1f);
                    defensiveDuration = 4.0f;
                    damageReductionRate = 0.60f; // [3] 철벽 방어: 4초간 받는 모든 피해 60% 감소
                    playerRoleDisplayName = "ME (가디언·탱커)";
                    break;

                case PlayerRole.Oracle_Healer:
                    // ② 오라클 (힐러): 스마트 타겟팅 힐/보호막/성역/구출, 외형 색상 초록
                    maxHp = 1100f;
                    normalBodyColor = new Color(0.18f, 0.90f, 0.38f, 1f);
                    defensiveBodyColor = new Color(0.50f, 1.0f, 0.72f, 1f);
                    defensiveDuration = 4.0f;
                    damageReductionRate = 0.50f;
                    playerRoleDisplayName = "ME (오라클·힐러)";
                    break;

                case PlayerRole.Blade_MeleeDPS:
                    // ③ 블레이드 (근딜): 측·후면 3단 진화 콤보 & 생존기 & 질풍 찌르기, 외형 색상 노랑
                    maxHp = 1000f;
                    normalBodyColor = new Color(1.0f, 0.85f, 0.12f, 1f);
                    defensiveBodyColor = new Color(0.15f, 0.52f, 1.0f, 1f);
                    defensiveDuration = 3.0f;
                    damageReductionRate = 0.80f; // [3] 개인 생존기: 3초간 받는 피해 80% 감소
                    playerRoleDisplayName = "ME (블레이드·근딜)";
                    break;

                case PlayerRole.Archmage_Caster:
                default:
                    // ④ 아크메이지 (캐스터): 캐스팅 바 & 이동 취소 / 무빙 냉기 / 마나 배리어 / 삼연속 마법, 외형 색상 주황
                    maxHp = 1000f;
                    normalBodyColor = new Color(1.0f, 0.48f, 0.12f, 1f);
                    defensiveBodyColor = new Color(0.72f, 0.35f, 1.0f, 1f);
                    defensiveDuration = 4.0f;
                    damageReductionRate = 0.70f; // [3] 마나 배리어: 4초간 피해 70% 감소 + 시전 끊김 면역
                    playerRoleDisplayName = "ME (아크메이지·캐스터)";
                    break;
            }

            if (restoreFullHp)
            {
                currentHp = maxHp;
                currentShield = 0f;
            }
            else
            {
                currentHp = Mathf.Clamp(maxHp, 1f, maxHp);
            }

            ApplyBodyColor(normalBodyColor);
            if (dualVisual != null)
            {
                dualVisual.unitKind = role switch
                {
                    PlayerRole.Guardian_Tank => VisualUnitKind.BotTank,
                    PlayerRole.Oracle_Healer => VisualUnitKind.BotHealer,
                    PlayerRole.Archmage_Caster => VisualUnitKind.BotRangedDPS,
                    _ => VisualUnitKind.PlayerMelee
                };
                dualVisual.SetSecondaryColor(Color.white);
                dualVisual.Refresh2DSpritesAndColors();
            }

            if (_overheadNameText != null)
            {
                _overheadNameText.text = playerRoleDisplayName;
                _overheadNameText.color = Color.Lerp(normalBodyColor, Color.white, 0.30f);
            }

            if (_hpFillRenderer != null)
            {
                SetUnlitColor(_hpFillRenderer, normalBodyColor);
            }

            if (combatController == null)
            {
                combatController = GetComponent<PlayerCombatController>();
            }
            if (combatController != null)
            {
                combatController.OnPlayerRoleChanged(role);
            }
        }

        public void TriggerTacticalRescueBuff(float duration = 5.0f)
        {
            if (hasTacticalRescue)
            {
                _tacticalRescueBuffTimer = Mathf.Max(_tacticalRescueBuffTimer, duration);
                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.85f,
                    "[전술적 구출] 발동!! 5초간 모든 피해/치유량 2배 증가!",
                    new Color(1f, 0.85f, 0.2f, 1f),
                    true
                );
            }
        }

        /// <summary>
        /// Step 6: 스테이지 클리어 후 카드 선택 시 체력 100% 회복 및 초기 위치로 재배치합니다.
        /// </summary>
        public void ResetToFullHealthAndPosition(Vector3 spawnPos)
        {
            Life?.ClearDeadForStagePlacement();
            RestorePartyLife(1f);
            // This is also used for revival/repositioning: preserve the item's cooldown.
            Potion?.EndRevivalProtection();
            _dash.Cancel();
            _dashPress.RequireRelease();
            IsStunned = IsMovementLocked = false;
            if (_dashPresentation != null) _dashPresentation.ClearTrail();
            _isBeingRescued = false;
            _isBeingKnockedBack = false;
            currentHp = maxHp;
            currentShield = 0f;
            transform.position = new Vector3(spawnPos.x, 1.0f, spawnPos.z);
            FaceBossImmediateXZ();

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.3f,
                "HP 100% 회복 & 다음 스테이지 전투 준비 완료!",
                new Color(0.3f, 1f, 0.55f, 1f),
                false
            );
        }

        /// <summary>
        /// Step 6 Stage 3 복합 기믹: 보스 중심에서 외곽 방향으로 강하게 밀쳐내는 넉백(Knockback) 발동
        /// </summary>
        public void StartKnockbackXZ(Vector3 originXZ, float knockbackDistance, float duration = 0.45f)
        {
            if (RaidCombatAuthority.Replica || IsDead || StageManager.CombatSuspended) return;
            if (_isBeingRescued)
            {
                return;
            }

            _dash.Cancel(); // Rescue > knockback > dash > WASD; only one movement branch runs each frame.

            Vector3 dirXZ = new Vector3(
                transform.position.x - originXZ.x,
                0f,
                transform.position.z - originXZ.z
            );

            if (dirXZ.sqrMagnitude < 0.001f)
            {
                dirXZ = Vector3.back;
            }
            dirXZ.Normalize();

            Vector3 targetPos = transform.position + dirXZ * knockbackDistance;
            Vector2 flat = new Vector2(targetPos.x, targetPos.z);
            if (flat.magnitude > arenaBoundaryRadius - 1.2f)
            {
                flat = flat.normalized * (arenaBoundaryRadius - 1.2f);
            }

            _knockbackTargetXZ = new Vector3(flat.x, transform.position.y, flat.y);
            float dist = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(_knockbackTargetXZ.x, _knockbackTargetXZ.z)
            );
            _knockbackSpeed = Mathf.Max(14f, dist / Mathf.Max(0.15f, duration));
            _isBeingKnockedBack = true;

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.3f,
                "외곽 넉백!! (밀쳐내기!)",
                new Color(1f, 0.55f, 0.2f, 1f),
                true
            );
        }

        private void UpdateKnockbackMovementXZ(float seconds = -1)
        {
            Vector3 current = transform.position;
            Vector3 target = new Vector3(_knockbackTargetXZ.x, current.y, _knockbackTargetXZ.z);
            transform.position = Vector3.MoveTowards(current, target, _knockbackSpeed * (seconds < 0 ? Time.deltaTime : seconds));

            Vector3 diffXZ = new Vector3(target.x - transform.position.x, 0f, target.z - transform.position.z);
            if (diffXZ.sqrMagnitude < 0.04f)
            {
                transform.position = target;
                _isBeingKnockedBack = false;
            }
        }

        public void StartPulledByHealerRescue(Vector3 safeDestinationXZ)
        {
            if (RaidCombatAuthority.Replica || IsDead || StageManager.CombatSuspended) return;
            _dash.Cancel();
            _isBeingKnockedBack = false;
            _isBeingRescued = true;
            _rescueSpeedBoostTimer = 3.0f;
            _rescueDestinationXZ = new Vector3(safeDestinationXZ.x, transform.position.y, safeDestinationXZ.z);

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.4f,
                "구출(Rescue)! 힐러가 안전지대로 끌어당깁니다!",
                new Color(0.3f, 0.95f, 1f, 1f),
                true
            );

            TriggerTacticalRescueBuff(5.0f);
        }

        private void UpdateRescuePullMovementXZ(float seconds = -1)
        {
            Vector3 current = transform.position;
            Vector3 target = new Vector3(_rescueDestinationXZ.x, current.y, _rescueDestinationXZ.z);
            transform.position = Vector3.MoveTowards(current, target, _rescuePullSpeed * (seconds < 0 ? Time.deltaTime : seconds));

            Vector3 diffXZ = new Vector3(target.x - transform.position.x, 0f, target.z - transform.position.z);
            if (diffXZ.sqrMagnitude < 0.04f)
            {
                transform.position = target;
                _isBeingRescued = false;
            }
        }

        /// <summary>
        /// 로그라이크 카드 선택 시 효과를 내 캐릭터 및 전투 컨트롤러에 영구 중첩 반영합니다.
        /// </summary>
        public void OnAcquireRoguelikeCard(RoguelikeCardId cardId)
        {
            switch (cardId)
            {
                case RoguelikeCardId.MasterOfRear:
                    hasMasterOfRear = true;
                    masterOfRearStacks++;
                    break;

                case RoguelikeCardId.GhostStep:
                    hasGhostStep = true;
                    ghostStepStacks++;
                    moveSpeed *= 1.30f;
                    positionalAngleBonusDeg += 15f;
                    if (targetBoss != null && !RaidCombatAuthority.Active)
                    {
                        targetBoss.positionalAngleBonus = positionalAngleBonusDeg;
                    }
                    break;

                case RoguelikeCardId.ThornArmor:
                    hasThornArmor = true;
                    thornArmorStacks++;
                    thornReflectDamage = 600f * thornArmorStacks;
                    break;

                case RoguelikeCardId.TacticalRescue:
                    hasTacticalRescue = true;
                    tacticalRescueStacks++;
                    break;

                case RoguelikeCardId.BerserkerShout:
                    hasBerserkerShout = true;
                    berserkerShoutStacks++;
                    break;

                case RoguelikeCardId.VampiricBlade:
                    hasVampiricBlade = true;
                    vampiricBladeStacks++;
                    break;

                case RoguelikeCardId.SwiftAwakening:
                    swiftAwakeningStacks++;
                    if (combatController != null)
                    {
                        combatController.ApplySwiftAwakeningGcdReduction(0.4f);
                    }
                    break;
            }
        }

        private void HandleMovementXZ()
        {
            Vector2 moveInput = CanMoveVoluntarily ? ReadMoveInput() : Vector2.zero;
            Vector3 moveDirXZ = GetMoveDirectionXZ(moveInput);

            if (moveDirXZ.sqrMagnitude > 1f)
            {
                moveDirXZ.Normalize();
            }

            if (moveDirXZ.sqrMagnitude > 0.0001f)
            {
                IsMovingXZ = true;
                float effectiveSpeed = moveSpeed * (_rescueSpeedBoostTimer > 0f ? 1.35f : 1.0f);
                Vector3 requested = transform.position + moveDirXZ * (effectiveSpeed * Time.deltaTime);
                Vector2 bounded = Vector2.ClampMagnitude(new Vector2(requested.x, requested.z), arenaBoundaryRadius);
                Vector3 delta = new Vector3(bounded.x - transform.position.x, 0, bounded.y - transform.position.z);
                // Preserve WASD's existing slide along the arena edge, while sharing obstacle sweeps with dash.
                transform.position = _movementCollision.Move(this, delta, delta.magnitude, out _);

                Quaternion targetRotation = Quaternion.LookRotation(moveDirXZ.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    rotationSmoothSpeed * Time.deltaTime
                );
            }
            else
            {
                IsMovingXZ = false;
            }
        }

        private static Vector3 GetMoveDirectionXZ(Vector2 input)
        {
            QuarterViewCamera viewCamera = QuarterViewCamera.LocalCamera;
            Vector3 direction = viewCamera != null ? viewCamera.GetMovementDirection(input) : new Vector3(input.x, 0, input.y);
            return Vector3.ClampMagnitude(direction, 1f);
        }

        private void HandleLegacySkillInputs()
        {
            if (LocalGameplayInput.Blocked) return;
            if (WasSkill1Pressed()) UseFlankSlash();
            if (WasSkill2Pressed()) UseRearAmbush();
            if (WasSkill3Pressed()) ActivatePersonalDefensiveBuff();
        }

        public void UseFlankSlash()
        {
            if (combatController != null)
            {
                combatController.TryUseSkill1_FlankCombo();
                return;
            }

            if (!CanUseLegacyMeleeAttack()) return;
            Potion?.EndRevivalProtection();
            FaceBossImmediateXZ();
            PlaySlashVisual(new Color(0.25f, 1f, 0.55f, 1f));
            targetBoss.ReceiveSkillAttack(transform.position, PositionalRequirement.Flank_45_To_135, baseSkillDamage, false);
        }

        public void UseRearAmbush()
        {
            if (combatController != null)
            {
                combatController.TryUseSkill2_RearCombo();
                return;
            }

            if (!CanUseLegacyMeleeAttack()) return;
            Potion?.EndRevivalProtection();
            FaceBossImmediateXZ();
            PlaySlashVisual(new Color(0.3f, 0.85f, 1f, 1f));
            targetBoss.ReceiveSkillAttack(transform.position, PositionalRequirement.Rear_135_To_180, baseSkillDamage, hasMasterOfRear);
        }

        private bool CanUseLegacyMeleeAttack()
        {
            if (LocalGameplayInput.Blocked || IsDead || StageManager.CombatSuspended) return false;
            if (targetBoss == null || !CombatRange.LivingEnemy(targetBoss.transform) || Time.timeScale <= 0.001f) return false;
            var defaults = new MeleeRangeSettings();
            float range = defaults.Get(currentRole == PlayerRole.Guardian_Tank ? MeleeSkill.TankPrimary : MeleeSkill.BladeFlank);
            if (CombatRange.InRange(transform, targetBoss.transform, range)) return true;
            targetBoss.ShowExternalAlert("대상이 사거리 밖에 있습니다", Color.gray, 1.25f);
            return false;
        }

        public void TriggerVampiricBladeHeal()
        {
            if (RaidCombatAuthority.Replica || IsDead || StageManager.CombatSuspended) return;
            float heal = (currentRole == PlayerRole.Guardian_Tank ? 30f : 15f) * Mathf.Max(1, vampiricBladeStacks);
            float hpBefore = currentHp;
            currentHp = Mathf.Min(maxHp, currentHp + heal);
            CombatMetricsManager.Healing(transform, hpBefore, currentHp);
            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.25f,
                $"+{Mathf.RoundToInt(heal)} HP [흡혈 회복]",
                new Color(0.75f, 0.35f, 1f, 1f),
                false
            );
        }

        /// <summary>
        /// Step 8: 직군별 [3]번 생존기/방어기 버프 활성화
        /// - 가디언(탱커): [철벽 방어] 4초간 피해 60% 감소
        /// - 블레이드(근딜): [개인 생존기] 3초간 피해 80% 감소
        /// - 아크메이지(캐스터): [마나 배리어] 4초간 피해 70% 감소 + 피격 시 시전 끊김 면역
        /// </summary>
        public void ActivatePersonalDefensiveBuff()
        {
            if (RaidCombatAuthority.Replica || IsDead || StageManager.CombatSuspended) return;
            Potion?.EndRevivalProtection();
            _defensiveRemainingTime = defensiveDuration;
            ApplyBodyColor(defensiveBodyColor);

            string buffPopup = currentRole switch
            {
                PlayerRole.Guardian_Tank => "철벽 방어 ON! (4초간 모든 피해 60% 감소!)",
                PlayerRole.Archmage_Caster => "마나 배리어 ON! (4초간 피해 70% 감소 + 시전 끊김 면역!)",
                _ => "DEFENSIVE ON! (3초간 피해 80% 감소!)"
            };

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.3f,
                buffPopup,
                new Color(0.35f, 0.85f, 1f, 1f),
                true
            );
        }

        public float CalculateFinalDamageTaken(float incomingDamage)
        {
            float damage = incomingDamage;
            // 가디언(탱커) 기본 패시브 방어력 적용
            if (currentRole == PlayerRole.Guardian_Tank)
            {
                damage *= (1f - tankPassiveDefenseRate);
            }

            float multiplier = IsDefensiveBuffActive ? (1f - damageReductionRate) : 1f;
            return damage * multiplier;
        }

        /// <summary>
        /// Step 8 오라클(힐러) [2]번 '구원의 치유 & 보호막': 노란색 보호막(Shield) 부여
        /// </summary>
        public void AddShield(float shieldAmount, Transform source = null)
        {
            if (RaidCombatAuthority.Replica || IsDead || StageManager.CombatSuspended) return;
            if (shieldAmount <= 0f)
            {
                return;
            }

            _metricsShields.Reconcile(currentShield);
            float shieldBefore = currentShield;
            currentShield = Mathf.Min(maxHp, currentShield + shieldAmount);
            _metricsShields.Grant(CombatMetricsManager.SourceId(source != null ? source : transform), currentShield - shieldBefore);
            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.45f,
                $"+{Mathf.RoundToInt(shieldAmount)} SHIELD (노란 보호막!)",
                new Color(1.0f, 0.90f, 0.20f, 1f),
                false
            );
        }

        public void TakeDamage(float rawDamage)
        {
            if (RaidCombatAuthority.Replica || IsDead || StageManager.CombatSuspended || rawDamage <= 0) return;
            if (Potion != null && Potion.HasRevivalProtection) return;
            ExpireTimedShields();
            float finalDamage = CalculateFinalDamageTaken(rawDamage);
            float absorbedByShield = 0f;

            if (currentShield > 0f && finalDamage > 0f)
            {
                _metricsShields.Reconcile(currentShield);
                absorbedByShield = Mathf.Min(currentShield, finalDamage);
                currentShield -= absorbedByShield;
                finalDamage -= absorbedByShield;
                _metricsShields.Absorb(absorbedByShield, CombatMetricsManager.Absorption);
            }

            var aegis = GetComponent<AegisPaladin>();
            if (aegis != null && aegis.IsActive) finalDamage = (float)aegis.DamageState.Split(finalDamage);
            currentHp = Mathf.Max(0f, currentHp - finalDamage);

            // 가디언(탱커) 또는 생존기 발동 중 [가시 갑옷] 카드 보유 시 피격 반사 피해
            if (hasThornArmor && (IsDefensiveBuffActive || currentRole == PlayerRole.Guardian_Tank) && targetBoss != null)
            {
                int reflect = Mathf.RoundToInt(thornReflectDamage * (IsDefensiveBuffActive ? 0.6f : 0.25f));
                if (reflect > 0)
                {
                    targetBoss.ReceiveRoleSkillAttack("가시반사", "철벽 가시 갑옷", 0, reflect, new Color(0.35f, 1f, 0.55f, 1f), false, source: transform);
                }
            }

            string reductionTag = IsDefensiveBuffActive
                ? $" (방어기 {Mathf.RoundToInt(damageReductionRate * 100f)}% 감쇄!)"
                : (currentRole == PlayerRole.Guardian_Tank ? " (탱커 기본방어)" : "");

            if (absorbedByShield > 0.5f)
            {
                reductionTag += $" [보호막 -{Mathf.RoundToInt(absorbedByShield)} 흡수]";
            }

            string dmgText = $"-{Mathf.RoundToInt(finalDamage)}{reductionTag}";
            Color dmgColor = IsDefensiveBuffActive
                ? new Color(0.45f, 0.85f, 1f, 1f)
                : (absorbedByShield > 0.5f ? new Color(1f, 0.9f, 0.3f, 1f) : new Color(1f, 0.35f, 0.35f, 1f));

            FloatingTextPopup.Spawn(transform.position + Vector3.up * 2.1f, dmgText, dmgColor, false);
            if (currentHp <= 0) PartyLife.Ensure(gameObject).Die();
        }

        public float ReceiveHeal(float healAmount, Transform source = null, HealingKind kind = HealingKind.Ability)
        {
            if (RaidCombatAuthority.Replica || healAmount <= 0f || IsDead || StageManager.CombatSuspended)
            {
                return 0;
            }

            float before = currentHp;
            currentHp = Mathf.Min(maxHp, currentHp + healAmount);
            float actualHealed = currentHp - before;
            CombatMetricsManager.Healing(kind == HealingKind.Potion ? transform : source != null ? source : transform, before, currentHp, kind);

            if (actualHealed > 0.5f || (kind == HealingKind.Potion && actualHealed > 0))
            {
                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.2f,
                    kind == HealingKind.Potion ? $"포션 +{actualHealed:0.##} HP" : $"+{Mathf.RoundToInt(actualHealed)} HP",
                    new Color(0.25f, 1f, 0.45f, 1f),
                    false
                );
            }
            return actualHealed;
        }

        public void PlaySkillLineVisual(Vector3 targetWorldPos, Color lineColor)
        {
            if (RaidCombatAuthority.Host) RaidNetworkSession.Instance.BroadcastEffect(transform.position + Vector3.up, targetWorldPos + Vector3.up, lineColor, "", false, 1);
            if (_slashEffectLine == null)
            {
                return;
            }

            if (_slashEffectCoroutine != null)
            {
                StopCoroutine(_slashEffectCoroutine);
            }
            _slashEffectCoroutine = StartCoroutine(CustomLineVisualRoutine(targetWorldPos, lineColor));
        }

        public void OnPartyDeath()
        {
            currentHp = currentShield = 0;
            ClearInterruptedActions();
            ApplyBodyColor(new Color(0.3f, 0.3f, 0.34f, 0.5f));
        }

        private void ClearInterruptedActions()
        {
            _dash.Cancel(); _dashPress.RequireRelease();
            _isBeingRescued = _isBeingKnockedBack = IsMovingXZ = false;
            IsStunned = IsMovementLocked = false;
            _defensiveRemainingTime = _tacticalRescueBuffTimer = _rescueSpeedBoostTimer = 0;
            _metricsShields.Clear();
            StopAllCoroutines(); _slashEffectCoroutine = null;
            if (_slashEffectLine != null) _slashEffectLine.enabled = false;
            _dashPresentation?.ClearTrail();
            combatController?.InterruptForDeath();
        }

        public void RestorePartyLife(float fraction, bool resetCooldowns = false)
        {
            ClearInterruptedActions();
            currentShield = 0;
            currentHp = maxHp * Mathf.Clamp01(fraction);
            ApplyBodyColor(normalBodyColor);
            if (resetCooldowns)
            {
                _dash.ResetForRetry();
                combatController?.OnPlayerRoleChanged(currentRole);
            }
        }

        private IEnumerator CustomLineVisualRoutine(Vector3 targetWorldPos, Color lineColor)
        {
            _slashEffectLine.enabled = true;
            _slashEffectLine.startColor = lineColor;
            _slashEffectLine.endColor = new Color(lineColor.r, lineColor.g, lineColor.b, 0.25f);

            Vector3 start = new Vector3(transform.position.x, 1.0f, transform.position.z);
            Vector3 end = new Vector3(targetWorldPos.x, 1.15f, targetWorldPos.z);
            _slashEffectLine.SetPosition(0, start);
            _slashEffectLine.SetPosition(1, end);

            yield return new WaitForSeconds(0.16f);
            _slashEffectLine.enabled = false;
            _slashEffectCoroutine = null;
        }

        public void FaceBossImmediateXZ()
        {
            FaceAttackTarget(targetBoss != null ? targetBoss.transform : null);
        }

        public void FaceAttackTarget(Transform target)
        {
            // Movement owns body facing. Attack effects aim at the target independently.
            if (target == null || HasMovementIntent || IsMovingXZ || IsDashing || IsBeingRescued || IsBeingKnockedBack) return;
            Vector3 direction = target.position - transform.position;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private void CreateOverheadHpBar(string label)
        {
            _hpBarRoot = new GameObject("Overhead_HpBar").transform;
            _hpBarRoot.SetParent(transform, false);
            _hpBarRoot.localPosition = new Vector3(0f, 1.55f, 0f);

            GameObject bgObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgObj.name = "HP_Bg";
            Destroy(bgObj.GetComponent<Collider>());
            bgObj.transform.SetParent(_hpBarRoot, false);
            bgObj.transform.localPosition = Vector3.zero;
            bgObj.transform.localScale = new Vector3(1.3f, 0.16f, 1f);
            SetUnlitColor(bgObj.GetComponent<Renderer>(), new Color(0.1f, 0.1f, 0.12f, 0.9f));

            GameObject fillObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fillObj.name = "HP_Fill";
            Destroy(fillObj.GetComponent<Collider>());
            _hpFillBar = fillObj.transform;
            _hpFillBar.SetParent(_hpBarRoot, false);
            _hpFillBar.localPosition = new Vector3(0f, 0f, -0.01f);
            _hpFillBar.localScale = new Vector3(1.26f, 0.12f, 1f);
            _hpFillRenderer = fillObj.GetComponent<Renderer>();
            SetUnlitColor(_hpFillRenderer, new Color(1f, 0.88f, 0.15f, 1f));

            GameObject shieldObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shieldObj.name = "Shield_Fill";
            Destroy(shieldObj.GetComponent<Collider>());
            _shieldFillBar = shieldObj.transform;
            _shieldFillBar.SetParent(_hpBarRoot, false);
            _shieldFillBar.localPosition = new Vector3(0f, 0.09f, -0.015f);
            _shieldFillBar.localScale = new Vector3(0f, 0.055f, 1f);
            SetUnlitColor(shieldObj.GetComponent<Renderer>(), new Color(1.0f, 0.92f, 0.18f, 1f));

            GameObject nameObj = new GameObject("NameLabel");
            nameObj.transform.SetParent(_hpBarRoot, false);
            nameObj.transform.localPosition = new Vector3(0f, 0.26f, 0f);
            nameObj.transform.localScale = Vector3.one * 0.08f;
            _overheadNameText = nameObj.AddComponent<TextMesh>();
            _overheadNameText.text = label;
            _overheadNameText.fontSize = 42;
            _overheadNameText.fontStyle = FontStyle.Bold;
            _overheadNameText.anchor = TextAnchor.MiddleCenter;
            _overheadNameText.alignment = TextAlignment.Center;
            _overheadNameText.color = new Color(1f, 0.92f, 0.3f, 1f);
        }

        private void UpdateOverheadHpBar()
        {
            if (_hpBarRoot == null)
            {
                return;
            }

            // 2D 직교 탑다운 뷰(X축 90도)에서는 체력바가 캐릭터 스프라이트를 가리지 않도록 북쪽(+Z) 위쪽에 배치
            bool is2D = ViewModeManager.Is2DViewActive;
            _hpBarRoot.position = transform.position + (is2D ? new Vector3(0f, 1.55f, 1.15f) : new Vector3(0f, 1.55f, 0f));

            Camera cam = Camera.main;
            if (cam != null)
            {
                _hpBarRoot.rotation = cam.transform.rotation;
            }

            const float fullWidth = 1.26f;
            if (_hpFillBar != null)
            {
                float ratio = HpRatio;
                _hpFillBar.localScale = new Vector3(fullWidth * ratio, 0.12f, 1f);
                _hpFillBar.localPosition = new Vector3(-fullWidth * (1f - ratio) * 0.5f, 0f, -0.01f);
            }

            if (_shieldFillBar != null)
            {
                float sRatio = ShieldRatio;
                _shieldFillBar.localScale = new Vector3(fullWidth * sRatio, 0.055f, 1f);
                _shieldFillBar.localPosition = new Vector3(-fullWidth * (1f - sRatio) * 0.5f, 0.09f, -0.015f);
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

        private void CreateSlashEffectLine()
        {
            GameObject slashObj = new GameObject("SkillSlashLine");
            slashObj.transform.SetParent(transform, false);

            _slashEffectLine = slashObj.AddComponent<LineRenderer>();
            _slashEffectLine.useWorldSpace = true;
            _slashEffectLine.positionCount = 2;
            _slashEffectLine.startWidth = 0.22f;
            _slashEffectLine.endWidth = 0.04f;
            _slashEffectLine.numCapVertices = 4;
            _slashEffectLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _slashEffectLine.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            _slashEffectLine.sharedMaterial = new Material(shader) { color = Color.white };
            _slashEffectLine.enabled = false;
        }

        private void PlaySlashVisual(Color slashColor)
        {
            if (_slashEffectLine == null || targetBoss == null)
            {
                return;
            }

            if (_slashEffectCoroutine != null)
            {
                StopCoroutine(_slashEffectCoroutine);
            }
            _slashEffectCoroutine = StartCoroutine(SlashVisualRoutine(slashColor));
        }

        private IEnumerator SlashVisualRoutine(Color slashColor)
        {
            _slashEffectLine.enabled = true;
            _slashEffectLine.startColor = slashColor;
            _slashEffectLine.endColor = new Color(slashColor.r, slashColor.g, slashColor.b, 0.2f);

            Vector3 start = new Vector3(transform.position.x, 1.0f, transform.position.z);
            Vector3 end = new Vector3(targetBoss.transform.position.x, 1.2f, targetBoss.transform.position.z);
            _slashEffectLine.SetPosition(0, start);
            _slashEffectLine.SetPosition(1, end);

            yield return new WaitForSeconds(0.14f);
            _slashEffectLine.enabled = false;
            _slashEffectCoroutine = null;
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

        private void UpdateHUD()
        {
            if (hudStatusText == null || targetBoss == null)
            {
                return;
            }

            PositionalZone zone = targetBoss.GetPositionalZone(transform.position, out float angle);
            string zoneKor = zone switch
            {
                PositionalZone.Front => "<color=#FF5555>정면 (Front)</color>",
                PositionalZone.Flank => "<color=#44FF77>측면 (Flank)</color>",
                PositionalZone.Rear => "<color=#55CCFF>후면 (Rear)</color>",
                _ => "알 수 없음"
            };

            string buffStatus = IsDefensiveBuffActive
                ? $"<color=#44AAFF>활성 ({_defensiveRemainingTime:F1}s / 피해 {Mathf.RoundToInt(damageReductionRate * 100f)}% 감소)</color>"
                : "<color=#AAAAAA>대기 ([3]번 생존/방어기)</color>";

            if (combatController != null && combatController.IsTriplecastActive)
            {
                buffStatus += $"  |  <color=#FF9933>[삼연속 마법] 화염 즉발 ({combatController.TriplecastRemaining:F1}s)</color>";
            }

            if (HasTacticalRescueBuff)
            {
                buffStatus += $"  |  <color=#FFDD33>[전술적 구출] 2배 강화 ({_tacticalRescueBuffTimer:F1}s)</color>";
            }

            string cardsSummary = cardManager != null
                ? cardManager.GetAcquiredCardsSummary()
                : "없음";

            float gcdSec = combatController != null ? combatController.currentGcdDuration : 2.0f;
            int stg = stageManager != null ? stageManager.currentStage : 1;
            string viewModeLabel = ViewModeManager.Is2DViewActive
                ? "<color=#44FFDD>2D 탑다운</color>"
                : "<color=#FFEE55>3D 쿼터뷰</color>";

            string roleLabel = currentRole switch
            {
                PlayerRole.Guardian_Tank => "<color=#44AAFF>1.가디언(탱커)</color>",
                PlayerRole.Oracle_Healer => "<color=#44FF77>2.오라클(힐러)</color>",
                PlayerRole.Blade_MeleeDPS => "<color=#FFEE44>3.블레이드(근딜)</color>",
                PlayerRole.Archmage_Caster => "<color=#FF8833>4.아크메이지(캐스터)</color>",
                _ => "블레이드"
            };

            string shieldStr = currentShield > 0.5f ? $" <color=#FFEE44>(+보호막 {Mathf.RoundToInt(currentShield)})</color>" : "";

            string currentTargetLabel = TargetingManager.Instance != null && TargetingManager.Instance.CurrentTarget != null
                ? TargetingManager.Instance.GetUnitDisplayName(TargetingManager.Instance.CurrentTarget)
                : $"없음 (클릭 / {LocalInputBindings.Label("CycleEnemy")} / {LocalInputBindings.PartyLabels()})";

            string movementHint = ViewModeManager.Is2DViewActive
                ? $"{LocalInputBindings.MovementLabels} 화면 기준 이동 · 휠 줌 · 기본 시야: 전장 전체 / 확대: 플레이어 추적"
                : $"{LocalInputBindings.MovementLabels} 카메라 기준 이동 · 우클릭 드래그 회전 · 휠 줌";

            hudStatusText.text =
                $"<b>[Project: Raid Rogue - Step 9 (STAGE {stg}/3 | 내 직업: {roleLabel} [{LocalInputBindings.Label("Roles")}] | 뷰: {viewModeLabel} [{LocalInputBindings.Label("View")}])]</b>\n" +
                $"• <b>타겟팅</b> : <b>[마우스 좌클릭]</b> 유닛/UI 선택 | <b>[{LocalInputBindings.PartyLabels()}]</b> 파티원 1~4번 | <b>[{LocalInputBindings.Label("CycleEnemy")}]</b> 적(보스·쫄) 순환 | <b>[{LocalInputBindings.Label("TargetOfTarget")}]</b> 대상의 대상 | <b>[{LocalInputBindings.Label("ClearTarget")}]</b> 타겟 해제 | <b>[ESC]</b> 창 닫기 / 메뉴\n" +
                $"• <b>이동/카메라</b> : {movementHint} · {LocalInputBindings.Label("Dash")} 대쉬 · {LocalInputBindings.Label("Potion")} 포션{(currentRole == PlayerRole.Oracle_Healer ? $" · {LocalInputBindings.Label("Skill5")} 부활(사망 아군 선택)" : "")} | [{LocalInputBindings.Label("Settings")}] 설정\n" +
                "--------------------------------------------------------------------------------\n" +
                $"• <b>내 체력</b> : <b>{Mathf.RoundToInt(currentHp)} / {Mathf.RoundToInt(maxHp)}</b>{shieldStr}   |   <b>현재 타겟</b> : <color=#FFEE55>{currentTargetLabel}</color>\n" +
                $"• <b>내 각도 (XZ)</b> : <b>{angle:F1}°</b> [{zoneKor}]   |   <b>최근 전투 로그</b> : {LocalInputBindings.SkillText(targetBoss.LastHitMessage)}\n" +
                $"• <b>누적 카드 빌드</b> : <color=#FFE066>{cardsSummary}</color>";
        }

        private static bool WasDashPressed()
        { return LocalInputBindings.Pressed("Dash"); }

        private static bool IsDashKeyHeld()
        { return LocalInputBindings.Held("Dash"); }

        private static Vector2 ReadMoveInput()
        { return LocalInputBindings.Movement; }

        private static bool WasSkill1Pressed()
        { return LocalInputBindings.Pressed("Skill1"); }

        private static bool WasSkill2Pressed()
        { return LocalInputBindings.Pressed("Skill2"); }

        private static bool WasSkill3Pressed()
        { return LocalInputBindings.Pressed("Skill3"); }
    }
}
