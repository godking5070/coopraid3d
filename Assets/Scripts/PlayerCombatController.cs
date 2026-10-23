using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    /// <summary>
    /// Step 8: 4대 역할군(1.가디언 탱커 / 2.오라클 힐러 / 3.블레이드 근딜 / 4.아크메이지 캐스터) 전용 스킬셋 및
    /// 파판14 스타일 글로벌 쿨타임(GCD) / 논글로벌 쿨타임(oGCD) / 캐스터 캐스팅 바(이동 시 시전 취소) 통합 컨트롤러.
    /// </summary>
    public partial class PlayerCombatController : MonoBehaviour
    {
        [Header("Melee Reach (combat boundary distance in world units)")]
        public MeleeRangeSettings meleeRanges = new MeleeRangeSettings();
        private CharacterDefinition CommonDefinition => GetComponent<CharacterActor>()?.Definition;
        public bool IsMeleeRole => CommonDefinition != null && CommonDefinition.legacyRole == 0 ? CommonDefinition.classification == AttackClassification.Melee : CurrentRole == PlayerRole.Guardian_Tank || CurrentRole == PlayerRole.Blade_MeleeDPS;
        public float PrimaryMeleeRange => CommonDefinition != null && CommonDefinition.legacyRole == 0 ? GetComponent<CharacterActor>().PrimaryRange : meleeRanges.Get(CurrentRole == PlayerRole.Guardian_Tank ? MeleeSkill.TankPrimary : MeleeSkill.BladeFlank, _combo1Stage);
        public string RangeFeedback { get; private set; }
        private float _rangeFeedbackUntil;
        public bool HasRangeFeedback => Time.unscaledTime < _rangeFeedbackUntil;
        [Header("References")]
        public PlayerController playerController;
        public BossController targetBoss;
        public SkillHotbarUI hotbarUI;

        [Header("GCD (글로벌 쿨타임 - [1], [2]번 웨폰스킬/마법 공유)")]
        [Tooltip("기본 글로벌 쿨타임 (2.0초). [신속의 각성] 카드 획득 시마다 0.4초씩 감소합니다.")]
        public float baseGcdDuration = 2.0f;
        public float minGcdDuration = 0.6f;
        public float currentGcdDuration = 2.0f;

        [Header("oGCD (논글로벌 어빌리티 - 글쿨 중 끼워넣기 가능)")]
        public float skill3CooldownTotal = 15.0f;
        public float skill4CooldownTotal = 8.0f;
        public int skill4GaleThrustDamage = 180;

        [Header("One-Button 3-Stage Combo System (8초 유지)")]
        [Tooltip("2단계/3단계 진화 후 다음 단계를 사용하지 않으면 초기화되는 시간 (8초)")]
        public float comboExpireDuration = 8.0f;

        // 현재 GCD 및 oGCD 남은 쿨타임
        private float _gcdRemaining;
        private float _gcdTotalForCurrentCycle = 2.0f;
        private float _skill3CooldownRemaining;
        private float _skill4CooldownRemaining;

        // [1]번 3단 진화 콤보 상태 (1 -> 2 -> 3 -> 1)
        private int _combo1Stage = 1;
        private float _combo1Timer;

        // [2]번 3단 진화 콤보 상태 (1 -> 2 -> 3 -> 1)
        private int _combo2Stage = 1;
        private float _combo2Timer;

        // Step 8 ④ 아크메이지 (캐스터) 전용 캐스팅 바 & 삼연속 마법 상태
        private bool _isCastingSpell;
        private string _castingSpellName = "";
        private int _castingSpellComboStage = 1;
        private int _castingSpellBaseDamage = 200;
        private float _castDurationTotal = 1.2f;
        private float _castElapsed;
        private float _triplecastBuffRemaining;
        private float _interruptAlertTimer;
        private string _interruptAlertMessage = "";

        // 단계별 화려한 타격/마법/치유 이펙트용 다중 LineRenderer
        private readonly List<LineRenderer> _fxLines = new List<LineRenderer>();
        private Coroutine _fxCoroutine;

        // 외부(UI 등) 조회 프로퍼티
        public float GcdRemaining => _gcdRemaining;
        public float GcdTotal => _gcdTotalForCurrentCycle;
        public bool IsOnGcd => _gcdRemaining > 0.001f;

        public float Skill3CooldownRemaining => _skill3CooldownRemaining;
        public float Skill3CooldownMax => skill3CooldownTotal;
        public float Skill4CooldownRemaining => _skill4CooldownRemaining;
        public float Skill4CooldownMax => skill4CooldownTotal;

        public int Combo1Stage => _combo1Stage;
        public float Combo1TimerRemaining => _combo1Timer;
        public int Combo2Stage => _combo2Stage;
        public float Combo2TimerRemaining => _combo2Timer;

        // 캐스터 캐스팅 바 조회 프로퍼티
        public bool IsCastingSpell => _isCastingSpell;
        public string CastingSpellName => _castingSpellName;
        public float CastProgress => _castDurationTotal > 0.01f ? Mathf.Clamp01(_castElapsed / _castDurationTotal) : 0f;
        public float CastRemaining => Mathf.Max(0f, _castDurationTotal - _castElapsed);
        public float CastDurationTotal => _castDurationTotal;
        public bool IsTriplecastActive => _triplecastBuffRemaining > 0.001f;
        public float TriplecastRemaining => _triplecastBuffRemaining;
        public bool HasInterruptAlert => _interruptAlertTimer > 0.001f;
        public string InterruptAlertMessage => _interruptAlertMessage;

        public PlayerRole CurrentRole => playerController != null ? playerController.currentRole : PlayerRole.Blade_MeleeDPS;
        private bool CanUseCombatAction => !StageManager.CombatSuspended && playerController != null &&
            !playerController.IsDead && !playerController.IsStunned && !(GetComponent<BattleResurrection>()?.IsCasting ?? false);

        public void InterruptForDeath()
        {
            GetComponent<CharacterActor>()?.InterruptCast();
            _isCastingSpell = false; _castElapsed = 0; _triplecastBuffRemaining = 0;
            _combo1Stage = _combo2Stage = 1; _combo1Timer = _combo2Timer = 0;
            StopAllCoroutines(); _fxCoroutine = null;
            foreach (var line in _fxLines) if (line != null) line.enabled = false;
        }

        private void Awake()
        {
            currentGcdDuration = baseGcdDuration;
            _gcdTotalForCurrentCycle = currentGcdDuration;
        }

        private bool _initialized;
        private void Start() => EnsureCombatInitialized();
        public void EnsureCombatInitialized()
        {
            if (_initialized) return; _initialized = true;
            if (RaidCombatAuthority.Local(this) && GetComponent<PlayerAttackRangeDisplay>() == null) gameObject.AddComponent<PlayerAttackRangeDisplay>();
            if (playerController == null)
            {
                playerController = GetComponent<PlayerController>();
            }
            if (targetBoss == null)
            {
                targetBoss = playerController != null && playerController.targetBoss != null
                    ? playerController.targetBoss
                    : FindFirstObjectByType<BossController>();
            }
            if (RaidCombatAuthority.Local(this) && hotbarUI == null)
            {
                hotbarUI = FindFirstObjectByType<SkillHotbarUI>();
            }

            CreateCombatFxLines();
            OnPlayerRoleChanged(CurrentRole);
            CharacterActor.Ensure(gameObject);
        }

        /// <summary>
        /// Step 8: 직업 변경 시 해당 직업의 oGCD 쿨타임 규격 및 콤보 상태를 즉시 초기화합니다.
        /// </summary>
        public void OnPlayerRoleChanged(PlayerRole newRole)
        {
            _isCastingSpell = false;
            _castElapsed = 0f;
            _triplecastBuffRemaining = 0f;
            _interruptAlertTimer = 0f;
            _gcdRemaining = 0f;
            _skill3CooldownRemaining = 0f;
            _skill4CooldownRemaining = 0f;
            _combo1Stage = 1;
            _combo1Timer = 0f;
            _combo2Stage = 1;
            _combo2Timer = 0f;

            switch (newRole)
            {
                case PlayerRole.Guardian_Tank:
                    // [3] 철벽 방어 (쿨 12초) / [4] 도발 및 줄 강탈 (쿨 15초)
                    skill3CooldownTotal = 12.0f;
                    skill4CooldownTotal = 15.0f;
                    break;

                case PlayerRole.Oracle_Healer:
                    // [3] 치유의 성역 (쿨 15초) / [4] 구출 - Rescue (쿨 20초, 전술적 구출 카드 시 단축)
                    skill3CooldownTotal = 15.0f;
                    skill4CooldownTotal = (playerController != null && playerController.hasTacticalRescue) ? 12.0f : 20.0f;
                    break;

                case PlayerRole.Blade_MeleeDPS:
                    // [3] 개인 생존기 (쿨 15초) / [4] 질풍 찌르기 (쿨 8초)
                    skill3CooldownTotal = 15.0f;
                    skill4CooldownTotal = 8.0f;
                    break;

                case PlayerRole.Archmage_Caster:
                default:
                    // [3] 마나 배리어 (쿨 15초) / [4] 삼연속 마법 (쿨 25초)
                    skill3CooldownTotal = 15.0f;
                    skill4CooldownTotal = 25.0f;
                    break;
            }
        }

        private void Update()
        {
            if (RaidCombatAuthority.Replica) { if (RaidCombatAuthority.Local(this) && !LocalGameplayInput.Blocked) HandleCombatHotkeys(); return; }
            if (Time.timeScale <= 0.001f || StageManager.CombatSuspended)
            {
                return;
            }

            UpdateCooldownsAndComboTimers();
            if (playerController != null && playerController.IsDead) { InterruptForDeath(); return; }
            UpdateCasterSpellCasting();

            if (RaidCombatAuthority.Local(this) && !RaidCombatAuthority.InputBlocked(this) && (playerController == null || !playerController.IsBeingRescued))
            {
                HandleCombatHotkeys();
            }
        }

        private void UpdateCooldownsAndComboTimers()
        {
            float dt = Time.deltaTime;

            // [광전사의 함성] 파티 공격속도 버프 시 쿨타임 가속 반영
            float hasteMultiplier = (playerController != null &&
                                     playerController.cardManager != null &&
                                     playerController.cardManager.HasPartyAttackSpeedBuff)
                ? 1.35f
                : 1.0f;

            if (_gcdRemaining > 0f)
            {
                _gcdRemaining = Mathf.Max(0f, _gcdRemaining - dt * hasteMultiplier);
            }

            if (_skill3CooldownRemaining > 0f)
            {
                _skill3CooldownRemaining = Mathf.Max(0f, _skill3CooldownRemaining - dt);
            }

            if (_skill4CooldownRemaining > 0f)
            {
                _skill4CooldownRemaining = Mathf.Max(0f, _skill4CooldownRemaining - dt * hasteMultiplier);
            }

            if (_triplecastBuffRemaining > 0f)
            {
                _triplecastBuffRemaining = Mathf.Max(0f, _triplecastBuffRemaining - dt);
            }

            if (_interruptAlertTimer > 0f)
            {
                _interruptAlertTimer = Mathf.Max(0f, _interruptAlertTimer - dt);
            }

            if (_combo1Stage > 1 && !_isCastingSpell)
            {
                _combo1Timer -= dt;
                if (_combo1Timer <= 0f)
                {
                    _combo1Timer = 0f;
                    _combo1Stage = 1;
                }
            }

            if (_combo2Stage > 1)
            {
                _combo2Timer -= dt;
                if (_combo2Timer <= 0f)
                {
                    _combo2Timer = 0f;
                    _combo2Stage = 1;
                }
            }
        }

        /// <summary>
        /// Step 8 ④ 아크메이지(캐스터): 캐스팅 게이지 진행 및 WASD 이동 시 시전 취소("CAST INTERRUPTED!") 처리
        /// </summary>
        private void UpdateCasterSpellCasting()
        {
            if (!_isCastingSpell)
            {
                return;
            }

            // 캐스팅 도중 WASD로 이동하면 즉시 시전 취소!
            if (playerController != null && playerController.IsMovingXZ && GetComponent<CharacterActor>()?.PreservesCastWhileDashing != true)
            {
                CancelActiveSpellCastByMovement();
                return;
            }

            float hasteMult = 1.0f;
            if (playerController != null && playerController.cardManager != null && playerController.cardManager.HasPartyAttackSpeedBuff)
            {
                hasteMult *= 1.30f;
            }

            _castElapsed += Time.deltaTime * hasteMult;
            if (_castElapsed >= _castDurationTotal)
            {
                CompleteActiveFireSpellCast();
            }
        }

        private void CancelActiveSpellCastByMovement()
        {
            string canceledName = _castingSpellName;
            _isCastingSpell = false;
            _castElapsed = 0f;
            _gcdRemaining = 0.25f; // 취소 직후 재시전 가능하도록 짧은 후딜레이만 남김

            _interruptAlertMessage = $"CAST INTERRUPTED! (이동으로 [{canceledName}] 시전 취소!)";
            _interruptAlertTimer = 1.85f;

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.55f,
                "CAST INTERRUPTED! (이동으로 시전 취소)",
                new Color(1.0f, 0.25f, 0.25f, 1f),
                true
            );

            if (targetBoss != null)
            {
                targetBoss.ShowExternalAlert(
                    "CAST INTERRUPTED! (이동으로 시전 취소 — 장판을 피한 뒤 제자리에 멈춰 서서 캐스팅하거나 [2]번 무빙 냉기 콤보 / [4]번 삼연속 마법을 쓰세요!)",
                    new Color(1.0f, 0.35f, 0.25f, 1f),
                    2.5f
                );
            }
        }

        private void CompleteActiveFireSpellCast()
        {
            if (!CombatRange.LivingEnemy(_castEnemy) || !CombatRange.InRange(transform, _castEnemy, rangedSkillRange)) { _isCastingSpell = false; ShowRangeFeedback("시전 대상이 유효하지 않거나 사거리 밖입니다"); return; }
            _isCastingSpell = false;
            _castElapsed = 0f;

            _resolvingCast = true;
            try { ExecuteArchmageFireSpellImpact(_castingSpellComboStage, _castingSpellName, _castingSpellBaseDamage, wasInstantTriplecast: false); }
            finally { _resolvingCast = false; }
        }

        private void HandleCombatHotkeys()
        {
            if (WasKeyPressed(1))
            {
                TryUseSkill1_FlankCombo();
            }
            if (WasKeyPressed(2))
            {
                TryUseSkill2_RearCombo();
            }
            if (WasKeyPressed(3))
            {
                TryUseSkill3_PersonalDefensive_oGCD();
            }
            if (WasKeyPressed(4))
            {
                TryUseSkill4_GaleThrust_oGCD();
            }
        }

        // =====================================================================
        // [1]번 키 (GCD 주력 3단 진화 콤보 - 현재 직업에 따라 분기)
        // =====================================================================
        public void TryUseSkill1_FlankCombo()
        {
            if (CharacterActor.Ensure(gameObject).HandleLegacyInput(1)) return;
            if (RaidCombatAuthority.Route(this, 1)) return;
            if (!ValidateCombatSkill(1, out string failure)) { ShowRangeFeedback(failure); return; }
            if (!CanUseCombatAction) return;
            if (GetOffensiveTarget() == null) { ShowRangeFeedback("공격할 수 있는 대상이 없습니다"); return; }
            if (RaidCombatAuthority.InputBlocked(this) || (playerController != null && playerController.IsBeingRescued)) return;
            if (IsOnGcd || _isCastingSpell)
            {
                return;
            }

            switch (CurrentRole)
            {
                case PlayerRole.Guardian_Tank:
                    ExecuteTankSkill1_ShieldSmashCombo();
                    break;
                case PlayerRole.Oracle_Healer:
                    ExecuteHealerSkill1_LightOfJudgmentCombo();
                    break;
                case PlayerRole.Archmage_Caster:
                    ExecuteCasterSkill1_FirestormCastCombo();
                    break;
                case PlayerRole.Blade_MeleeDPS:
                default:
                    ExecuteMeleeSkill1_FlankCombo();
                    break;
            }
        }

        // =====================================================================
        // [2]번 키 (GCD 보조 3단 콤보 또는 힐러 치유/보호막 - 현재 직업에 따라 분기)
        // =====================================================================
        public void TryUseSkill2_RearCombo()
        {
            if (CharacterActor.Ensure(gameObject).HandleLegacyInput(2)) return;
            if (RaidCombatAuthority.Route(this, 2)) return;
            if (!ValidateCombatSkill(2, out string failure)) { ShowRangeFeedback(failure); return; }
            if (!CanUseCombatAction) return;
            Transform selected = SelectedCombatTarget;
            if (CurrentRole == PlayerRole.Oracle_Healer && selected != null && selected.GetComponent<PartyLife>()?.IsDead == true)
            { ShowRangeFeedback("사망한 아군에게는 [5] 부활을 사용하세요"); return; }
            if (CurrentRole != PlayerRole.Oracle_Healer && GetOffensiveTarget() == null) { ShowRangeFeedback("공격할 수 있는 대상이 없습니다"); return; }
            if (RaidCombatAuthority.InputBlocked(this) || (playerController != null && playerController.IsBeingRescued)) return;
            if (IsOnGcd || _isCastingSpell)
            {
                return;
            }

            switch (CurrentRole)
            {
                case PlayerRole.Guardian_Tank:
                    ExecuteTankSkill2_JudgmentJavelinCombo();
                    break;
                case PlayerRole.Oracle_Healer:
                    ExecuteHealerSkill2_SalvationHealAndShield();
                    break;
                case PlayerRole.Archmage_Caster:
                    ExecuteCasterSkill2_FrostPulseInstantCombo();
                    break;
                case PlayerRole.Blade_MeleeDPS:
                default:
                    ExecuteMeleeSkill2_RearCombo();
                    break;
            }
        }

        // =====================================================================
        // [3]번 키 (oGCD 어빌리티 - 철벽 방어 / 치유의 성역 / 개인 생존기 / 마나 배리어)
        // =====================================================================
        public void TryUseSkill3_PersonalDefensive_oGCD()
        {
            if (CharacterActor.Ensure(gameObject).HandleLegacyInput(3)) return;
            if (RaidCombatAuthority.Route(this, 3)) return;
            if (!ValidateCombatSkill(3, out string failure)) { ShowRangeFeedback(failure); return; }
            if (!CanUseCombatAction) return;
            if (RaidCombatAuthority.InputBlocked(this) || (playerController != null && playerController.IsBeingRescued)) return;
            if (_skill3CooldownRemaining > 0.001f)
            {
                return;
            }

            _skill3CooldownRemaining = skill3CooldownTotal;
            playerController.Potion?.EndRevivalProtection();

            switch (CurrentRole)
            {
                case PlayerRole.Guardian_Tank:
                    // ① 가디언 [3] 철벽 방어 (oGCD, 쿨 12초): 4초간 받는 모든 피해 60% 감소
                    if (playerController != null)
                    {
                        playerController.ActivatePersonalDefensiveBuff();
                    }
                    break;

                case PlayerRole.Oracle_Healer:
                    // ② 오라클 [3] 치유의 성역 (oGCD, 쿨 15초): 내 발밑에 6초간 유지되는 초록색 광역 힐 장판 설치
                    ExecuteHealerSkill3_HealingSanctuaryZone();
                    break;

                case PlayerRole.Archmage_Caster:
                    // ④ 아크메이지 [3] 마나 배리어 (oGCD, 쿨 15초): 4초간 피해 70% 감소 + 시전 끊김 면역 + 노란 마나 보호막 250
                    if (playerController != null)
                    {
                        playerController.ActivatePersonalDefensiveBuff();
                        playerController.AddShield(250f);
                    }
                    break;

                case PlayerRole.Blade_MeleeDPS:
                default:
                    // ③ 블레이드 [3] 개인 생존기 (oGCD, 쿨 15초): 3초간 피해 80% 감소
                    if (playerController != null)
                    {
                        playerController.ActivatePersonalDefensiveBuff();
                    }
                    break;
            }
        }

        // =====================================================================
        // [4]번 키 (oGCD 핵심 어빌리티 - 도발&줄강탈 / 구출(Rescue) / 질풍 찌르기 / 삼연속 마법)
        // =====================================================================
        public void TryUseSkill4_GaleThrust_oGCD()
        {
            if (CharacterActor.Ensure(gameObject).HandleLegacyInput(4)) return;
            if (RaidCombatAuthority.Route(this, 4)) return;
            if (!ValidateCombatSkill(4, out string failure)) { ShowRangeFeedback(failure); return; }
            if (!CanUseCombatAction) return;
            Transform selected = SelectedCombatTarget;
            if (CurrentRole == PlayerRole.Oracle_Healer && selected != null && selected.GetComponent<PartyLife>()?.IsDead == true)
            { ShowRangeFeedback("사망한 아군에게는 [5] 부활을 사용하세요"); return; }
            if (CurrentRole == PlayerRole.Guardian_Tank && GetOffensiveTarget() == null) return;
            if (RaidCombatAuthority.InputBlocked(this) || (playerController != null && playerController.IsBeingRescued)) return;
            if (_skill4CooldownRemaining > 0.001f)
            {
                return;
            }

            switch (CurrentRole)
            {
                case PlayerRole.Guardian_Tank:
                    ExecuteTankSkill4_ProvokeAndTetherSnatch();
                    break;
                case PlayerRole.Oracle_Healer:
                    ExecuteHealerSkill4_RescueAlly();
                    break;
                case PlayerRole.Archmage_Caster:
                    ExecuteCasterSkill4_Triplecast();
                    break;
                case PlayerRole.Blade_MeleeDPS:
                default:
                    ExecuteMeleeSkill4_GaleThrust();
                    break;
            }
        }

        // =====================================================================
        // Step 9: 통합 타겟팅 연동 헬퍼 (공격 타겟 판정 / 아군 지정 타겟 우선 판정)
        // =====================================================================
        private Transform _lastOffensiveVisualTarget;
        private Transform _castEnemy;
        private bool _resolvingCast;

        // This query is also used by the ring and target HUD. It has no facing/resource side effects.
        public Transform GetOffensiveTarget()
        {
            if (_resolvingCast) return CombatRange.LivingEnemy(_castEnemy) ? _castEnemy : null;
            if (RaidCombatAuthority.Active)
            {
                Transform selected = SelectedCombatTarget;
                if (selected != null && !PartyLife.OrderedMembers().Contains(selected)) return CombatRange.LivingEnemy(selected) ? selected : null;
                var ally = selected != null ? selected.GetComponent<NetworkRaidAvatar>() : null;
                if (ally != null && CombatRange.LivingEnemy(ally.CombatTarget)) return ally.CombatTarget;
                return targetBoss != null && CombatRange.LivingEnemy(targetBoss.transform) ? targetBoss.transform : null;
            }
            return TargetingManager.Instance != null
                ? TargetingManager.Instance.GetLivingOffensiveTarget(targetBoss)
                : (targetBoss != null && CombatRange.LivingEnemy(targetBoss.transform) ? targetBoss.transform : null);
        }

        private bool TryBeginMeleeAttack(MeleeSkill skill, int comboStage, out BossAddMinion targetedAdd)
        {
            targetedAdd = null;
            if (RaidCombatAuthority.InputBlocked(this)) return false;
            Transform target = GetOffensiveTarget();
            if (target == null || !CombatRange.LivingPartyMember(transform))
            {
                ShowRangeFeedback("공격할 수 있는 대상이 없습니다");
                return false;
            }
            if (!CombatRange.InRange(transform, target, meleeRanges.Get(skill, comboStage)))
            {
                ShowRangeFeedback("대상이 사거리 밖에 있습니다");
                return false;
            }
            targetedAdd = target.GetComponent<BossAddMinion>();
            FaceOffensiveTarget(target);
            return true;
        }

        private void ShowRangeFeedback(string message)
        {
            RangeFeedback = LocalInputBindings.SkillText(message);
            _rangeFeedbackUntil = Time.unscaledTime + 1.25f;
        }

        private void FaceOffensiveTarget(Transform target)
        {
            _lastOffensiveVisualTarget = target;
            if (target == null) return;
            if (playerController != null) { playerController.FaceAttackTarget(target); return; }
            Vector3 direction = target.position - transform.position;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        /// <summary>
        /// 공격 스킬([1], [2], [4] 딜/도발) 사용 시:
        /// - 현재 타겟이 특정 '쫄(BossAddMinion)'이면 해당 쫄을 공격합니다.
        /// - 현재 타겟이 '아군'이거나 없으면, 아군 타겟을 해제하지 않고 그대로 유지한 채
        ///   그 아군이 싸우고 있는 적(또는 보스)을 향해 공격이 나갑니다!
        /// </summary>
        private void ResolveOffensiveTarget(out BossAddMinion targetedAdd, out Transform enemyVisualTr)
        {
            enemyVisualTr = GetOffensiveTarget();
            targetedAdd = enemyVisualTr != null ? enemyVisualTr.GetComponent<BossAddMinion>() : null;
            FaceOffensiveTarget(enemyVisualTr);
        }

        // =====================================================================
        // ① 가디언 (탱커 - Tank) 전용 스킬 구현
        // =====================================================================
        private void ExecuteTankSkill1_ShieldSmashCombo()
        {
            if (targetBoss == null) return;
            if (!TryBeginMeleeAttack(MeleeSkill.TankPrimary, _combo1Stage, out BossAddMinion targetedAdd)) return;

            int stage = _combo1Stage;
            string skillName = GetRoleSkill1StageName(PlayerRole.Guardian_Tank, stage);
            int baseDmg = stage switch
            {
                1 => 100, // 1단 방패 가격(100)
                2 => 180, // 2단 회전 베기(180 + 어그로 대폭 획득)
                3 => 300, // 3단 대지 분쇄(300 + 내 체력 15% 자힐)
                _ => 100
            };

            float buffMult = GetPlayerDamageBuffMultiplier();
            int finalDmg = Mathf.RoundToInt(baseDmg * buffMult);

            // 탱커 공격 시 보스 어그로 1순위 유지
            targetBoss.SetAggroTarget(transform);

            string extraNote = "";
            if (stage == 2)
            {
                extraNote = "어그로 대폭 획득!";
            }
            else if (stage == 3 && playerController != null)
            {
                float selfHeal = playerController.maxHp * 0.15f; // 내 체력 15% 자힐 (450 HP)
                playerController.ReceiveHeal(selfHeal);
                extraNote = $"내 체력 15% 자힐 +{Mathf.RoundToInt(selfHeal)}";
            }

            if (playerController != null && playerController.hasVampiricBlade)
            {
                playerController.TriggerVampiricBladeHeal();
            }

            Color hitCol = stage == 3 ? new Color(0.25f, 0.90f, 1.0f, 1f) : new Color(0.35f, 0.72f, 1.0f, 1f);
            if (targetedAdd != null)
            {
                if (stage >= 2)
                {
                    targetedAdd.TauntBy(transform, playerController != null ? playerController.playerRoleDisplayName : "가디언");
                }
                targetedAdd.ReceivePlayerAttack("가디언", $"{stage}단 {skillName}", finalDmg, hitCol, stage == 3, source: transform);
            }
            else
            {
                targetBoss.ReceiveRoleSkillAttack(
                    "가디언",
                    skillName,
                    stage,
                    finalDmg,
                    hitCol,
                    stage == 3,
                    extraNote,
                    source: transform
                );
            }

            PlayStageSlashEffect(stage, isLeftFlankStyle: true, isCrit: stage == 3);
            TriggerSharedGCD();
            AdvanceCombo1Stage();
        }

        private void ExecuteTankSkill2_JudgmentJavelinCombo()
        {
            if (targetBoss == null) return;

            int stage = _combo2Stage;
            string skillName = GetRoleSkill2StageName(PlayerRole.Guardian_Tank, stage);
            int baseDmg = stage switch
            {
                1 => 110, // 1단 심판의 투창(110)
                2 => 170, // 2단 관통의 창(170 + 원거리 주차 유도)
                3 => 260, // 3단 성광 방패 투척(260 + 보호막 200)
                _ => 110
            };

            float buffMult = GetPlayerDamageBuffMultiplier();
            int finalDmg = Mathf.RoundToInt(baseDmg * buffMult);

            ResolveOffensiveTarget(out BossAddMinion targetedAdd, out _);

            targetBoss.SetAggroTarget(transform);

            string extraNote = "원거리 어그로 유지";
            if (stage == 3 && playerController != null)
            {
                playerController.AddShield(200f);
                extraNote = "성광 보호막 +200";
            }

            Color hitCol = new Color(0.45f, 0.85f, 1.0f, 1f);
            if (targetedAdd != null)
            {
                targetedAdd.ReceivePlayerAttack("투창콤보", $"{stage}단 {skillName}", finalDmg, hitCol, stage == 3, source: transform);
            }
            else
            {
                targetBoss.ReceiveRoleSkillAttack(
                    "투창콤보",
                    skillName,
                    stage,
                    finalDmg,
                    hitCol,
                    stage == 3,
                    extraNote,
                    source: transform
                );
            }

            PlayStageSlashEffect(stage, isLeftFlankStyle: false, isCrit: stage == 3);
            TriggerSharedGCD();
            AdvanceCombo2Stage();
        }

        private void ExecuteTankSkill4_ProvokeAndTetherSnatch()
        {
            if (targetBoss == null) return;

            _skill4CooldownRemaining = skill4CooldownTotal;
            playerController?.Potion?.EndRevivalProtection();
            ResolveOffensiveTarget(out BossAddMinion targetedAdd, out _);

            string myName = playerController != null ? playerController.playerRoleDisplayName : "가디언(탱커)";
            bool snatchedTether = targetBoss.ForceSnatchTetherToPlayer(transform);
            int bonusDmg = Mathf.RoundToInt(160f * GetPlayerDamageBuffMultiplier());

            // 1) 특정 쫄을 타겟팅 중이면 해당 쫄을 즉시 도발 + 타격
            if (targetedAdd != null)
            {
                targetedAdd.TauntBy(transform, myName);
                targetedAdd.ReceivePlayerAttack("oGCD 도발", "도발 (어그로 강탈!)", bonusDmg, new Color(0.25f, 0.95f, 1.0f, 1f), true, source: transform);
            }
            else
            {
                // 2) 보스를 타겟 중이더라도 전장에 힐러를 추격 중인 [암살자] 쫄이 있다면 함께 도발하여 힐러를 구출!
                if (BossAddSpawner.Instance != null)
                {
                    foreach (BossAddMinion m in BossAddSpawner.Instance.activeAdds)
                    {
                        if (m != null && !m.isDead && m.addType == AddMinionType.Assassin_HealerHunter)
                        {
                            m.TauntBy(transform, myName);
                        }
                    }
                }

                targetBoss.ReceiveRoleSkillAttack(
                    "oGCD 도발",
                    snatchedTether ? "도발 및 줄 강탈 (Tether Snatch!)" : "도발 (어그로 최우선 고정!)",
                    0,
                    bonusDmg,
                    new Color(0.25f, 0.95f, 1.0f, 1f),
                    true,
                    source: transform
                );
            }
            PlayGaleThrustPierceEffect();
        }

        // =====================================================================
        // ② 오라클 (힐러 - Healer) 전용 스킬 구현
        // =====================================================================
        private void ExecuteHealerSkill1_LightOfJudgmentCombo()
        {
            if (targetBoss == null) return;

            int stage = _combo1Stage;
            string skillName = GetRoleSkill1StageName(PlayerRole.Oracle_Healer, stage);
            int baseDmg = stage switch
            {
                1 => 120, // 1단 빛의 탄환(120)
                2 => 200, // 2단 단죄의 빛(200)
                3 => 350, // 3단 천벌의 기둥(350, 원거리 마법 딜링)
                _ => 120
            };

            float buffMult = GetPlayerDamageBuffMultiplier();
            int finalDmg = Mathf.RoundToInt(baseDmg * buffMult);

            // 아군을 타겟팅한 상태에서 [1]번 공격 스킬을 눌러도 아군 타겟을 풀지 않고 적(보스/쫄)을 자동 타격!
            ResolveOffensiveTarget(out BossAddMinion targetedAdd, out _);

            Color hitCol = stage == 3 ? new Color(0.55f, 1.0f, 0.45f, 1f) : new Color(0.35f, 0.95f, 0.55f, 1f);
            if (targetedAdd != null)
            {
                targetedAdd.ReceivePlayerAttack("오라클", $"{stage}단 {skillName}", finalDmg, hitCol, stage == 3, source: transform);
            }
            else
            {
                targetBoss.ReceiveRoleSkillAttack(
                    "오라클",
                    skillName,
                    stage,
                    finalDmg,
                    hitCol,
                    stage == 3,
                    source: transform
                );
            }

            PlayStageSlashEffect(stage, isLeftFlankStyle: true, isCrit: stage == 3);
            TriggerSharedGCD();
            AdvanceCombo1Stage();
        }

        /// <summary>
        /// ② 오라클 [2] 구원의 치유 & 보호막 (GCD):
        /// - Step 9 스마트 타겟팅 연동:
        ///   내가 현재 아군을 타겟팅(F1~F4 또는 마우스 클릭)하고 있다면, 무조건 내가 지정한 그 아군에게 단일 힐+보호막 시전!
        ///   만약 적(보스/쫄)을 타겟팅 중이거나 타겟이 없을 때는 기존처럼 체력 비율이 가장 낮은 아군에게 스마트 힐 발동!
        /// </summary>
        private void ExecuteHealerSkill2_SalvationHealAndShield()
        {
            Transform targetTr = ResolveFriendlyCombatTarget(false);
            if (targetTr == null || !CombatRange.LivingPartyMember(targetTr)) return;
            PlayerController targetSelf = targetTr.GetComponent<PlayerController>();
            PartyBotAI targetBot = targetTr.GetComponent<PartyBotAI>();
            string targetName = targetSelf != null ? targetSelf.playerRoleDisplayName : targetBot.botDisplayName;
            bool isExplicitTarget = targetTr == SelectedCombatTarget;
            float mult = GetPlayerDamageBuffMultiplier();
            string modePrefix = isExplicitTarget ? "[지정 타겟 치유]" : "[스마트 치유]";

            if (targetBot != null)
            {
                float healAmt = targetBot.maxHp * 0.40f * mult;
                float shieldAmt = targetBot.maxHp * 0.20f * mult;
                targetBot.ReceiveHeal(healAmt, transform);
                targetBot.AddShield(shieldAmt, transform);

                if (playerController != null)
                {
                    playerController.PlaySkillLineVisual(targetBot.transform.position, new Color(0.25f, 1.0f, 0.55f, 1f));
                }

                FloatingTextPopup.Spawn(
                    targetBot.transform.position + Vector3.up * 2.65f,
                    $"{modePrefix} {targetName} HP +40% & 보호막 +20%!",
                    new Color(0.30f, 1.0f, 0.55f, 1f),
                    true
                );
            }
            else if (targetSelf != null)
            {
                float healAmt = targetSelf.maxHp * 0.40f * mult;
                float shieldAmt = targetSelf.maxHp * 0.20f * mult;
                targetSelf.ReceiveHeal(healAmt, transform);
                targetSelf.AddShield(shieldAmt, transform);

                FloatingTextPopup.Spawn(
                    targetSelf.transform.position + Vector3.up * 2.65f,
                    $"{modePrefix} 내 체력 +40% & 노란 보호막 +20%!",
                    new Color(0.30f, 1.0f, 0.55f, 1f),
                    true
                );
            }

            // 힐러 전용 로그라이크 카드 연동 ([흡혈 검 / 성스러운 역류]): 치유 시 보스에게 성스러운 반격 피해!
            if (playerController != null && playerController.hasVampiricBlade && targetBoss != null)
            {
                int holyBacklash = Mathf.RoundToInt(220f * Mathf.Max(1, playerController.vampiricBladeStacks) * mult);
                targetBoss.ReceiveRoleSkillAttack(
                    "오버힐 전환",
                    "성스러운 역류 (치유→딜 전환)",
                    0,
                    holyBacklash,
                    new Color(1.0f, 0.92f, 0.30f, 1f),
                    true,
                    source: transform
                );
            }

            TriggerSharedGCD();
        }

        /// <summary>
        /// ② 오라클 [3] 치유의 성역 (oGCD, 쿨 15초):
        /// 내 발밑에 6초간 유지되는 초록색 광역 힐 장판을 깔아 범위 내 모든 파티원 지속 회복
        /// </summary>
        private void ExecuteHealerSkill3_HealingSanctuaryZone()
        {
            float mult = GetPlayerDamageBuffMultiplier();
            HealingSanctuaryZone.Spawn(transform.position, radius: 5.5f, duration: 6.0f, healPerTick: 110f * mult, shieldPerTick: 45f * mult, source: transform);

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.6f,
                "★ [치유의 성역] 설치! (6초간 반경 5.5m 광역 지속 회복 & 보호막!) ★",
                new Color(0.25f, 1.0f, 0.50f, 1f),
                true
            );

            if (targetBoss != null)
            {
                targetBoss.ShowExternalAlert(
                    "★ 오라클 [치유의 성역] 발동! 바닥의 초록색 성역 원 위에서 모든 파티원의 체력이 6초간 지속 회복됩니다! ★",
                    new Color(0.30f, 1.0f, 0.55f, 1f),
                    3.5f
                );
            }
        }

        /// <summary>
        /// ② 오라클 [4] 구출 - Rescue (oGCD, 쿨 20초):
        /// - Step 9 스마트 타겟팅 연동:
        ///   내가 현재 아군 파티원(F2~F4 또는 클릭)을 타겟팅하고 있다면 무조건 내가 지정한 그 아군 봇을 구출(끌어당기기+무적)!
        ///   그렇지 않으면 위험 장판(AoE) 안에 서 있는 아군 봇(없으면 가장 먼 아군)을 스마트 구출!
        /// </summary>
        private void ExecuteHealerSkill4_RescueAlly()
        {
            Transform ally = ResolveFriendlyCombatTarget(true);
            if (!CombatRange.LivingPartyMember(ally) || !CombatRange.InRange(transform, ally, friendlySkillRange)) return;
            float rescueCd = playerController.hasTacticalRescue ? Mathf.Max(8f, 20f - 6f * playerController.tacticalRescueStacks) : 20f;
            skill4CooldownTotal = _skill4CooldownRemaining = rescueCd;
            playerController.Potion?.EndRevivalProtection();
            Vector3 destination = transform.position + transform.forward * 1.1f;
            var pc = ally.GetComponent<PlayerController>(); var bot = ally.GetComponent<PartyBotAI>();
            if (pc != null) { pc.StartPulledByHealerRescue(destination); pc.AddShield(pc.maxHp * .25f, transform); }
            else if (bot != null) { bot.StartPulledByPlayerRescue(destination); bot.AddShield(bot.maxHp * .25f, transform); }
            playerController.PlaySkillLineVisual(ally.position, new Color(.3f, .95f, 1f));
            playerController.TriggerTacticalRescueBuff(5);
        }

        private void FindLowestHpRatioPartyMember(
            out PlayerController targetSelf,
            out PartyBotAI targetBot,
            out Transform targetTr,
            out string targetName)
        {
            targetSelf = playerController;
            targetBot = null;
            targetTr = transform;
            targetName = playerController != null ? playerController.playerRoleDisplayName : "ME";

            float lowestRatio = playerController != null ? playerController.HpRatio : 1f;

            PartyBotAI[] bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            foreach (PartyBotAI bot in bots)
            {
                if (bot == null || bot.isDead) continue;
                if (bot.HpRatio < lowestRatio - 0.01f)
                {
                    lowestRatio = bot.HpRatio;
                    targetBot = bot;
                    targetSelf = null;
                    targetTr = bot.transform;
                    targetName = bot.botDisplayName;
                }
            }
        }

        private PartyBotAI FindBestBotForHealerRescue()
        {
            PartyBotAI[] bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            PartyBotAI endangeredBot = null;
            float endangeredDist = -1f;

            PartyBotAI farthestBot = null;
            float maxDist = -1f;

            AoETelegraph activeAoE = targetBoss != null ? targetBoss.ActiveTelegraph : null;

            foreach (PartyBotAI bot in bots)
            {
                if (bot == null || bot.isDead) continue;

                float dist = Vector2.Distance(
                    new Vector2(transform.position.x, transform.position.z),
                    new Vector2(bot.transform.position.x, bot.transform.position.z)
                );

                if (activeAoE != null && activeAoE.IsPointInsideXZ(bot.transform.position))
                {
                    if (dist > endangeredDist)
                    {
                        endangeredDist = dist;
                        endangeredBot = bot;
                    }
                }

                if (dist > maxDist)
                {
                    maxDist = dist;
                    farthestBot = bot;
                }
            }

            return endangeredBot != null ? endangeredBot : farthestBot;
        }

        // =====================================================================
        // ③ 블레이드 (근딜 - Melee DPS) 전용 스킬 구현 (Step 5 + Step 9 쫄 타겟팅 연동)
        // =====================================================================
        private void ExecuteMeleeSkill1_FlankCombo()
        {
            if (targetBoss == null) return;
            if (!TryBeginMeleeAttack(MeleeSkill.BladeFlank, _combo1Stage, out BossAddMinion targetedAdd)) return;

            int executedStage = _combo1Stage;
            string skillName = GetSkill1StageName(executedStage);
            GetSkill1StageDamages(executedStage, out int normalDmg, out int positionalDmg);

            float buffMult = GetPlayerDamageBuffMultiplier();
            int finalNormal = Mathf.RoundToInt(normalDmg * buffMult);
            int finalPositional = Mathf.RoundToInt(positionalDmg * buffMult);

            bool isPositionalCrit;
            if (targetedAdd != null)
            {
                isPositionalCrit = true;
                targetedAdd.ReceivePlayerAttack(
                    "블레이드",
                    $"{executedStage}단 {skillName}",
                    finalPositional,
                    new Color(1.0f, 0.85f, 0.15f, 1f),
                    executedStage == 3,
                    source: transform
                );
            }
            else
            {
                isPositionalCrit = targetBoss.ReceiveComboWeaponSkillAttack(
                    transform.position,
                    PositionalRequirement.Flank_45_To_135,
                    skillName,
                    executedStage,
                    finalNormal,
                    finalPositional,
                    false,
                    source: transform
                );
            }

            PlayStageSlashEffect(executedStage, isLeftFlankStyle: true, isPositionalCrit);

            if (isPositionalCrit && playerController != null && playerController.hasVampiricBlade)
            {
                playerController.TriggerVampiricBladeHeal();
            }

            TriggerSharedGCD();
            AdvanceCombo1Stage();
        }

        private void ExecuteMeleeSkill2_RearCombo()
        {
            if (targetBoss == null) return;
            if (!TryBeginMeleeAttack(MeleeSkill.BladeRear, _combo2Stage, out BossAddMinion targetedAdd)) return;

            int executedStage = _combo2Stage;
            string skillName = GetSkill2StageName(executedStage);
            GetSkill2StageDamages(executedStage, out int normalDmg, out int positionalDmg);

            float buffMult = GetPlayerDamageBuffMultiplier();
            int finalNormal = Mathf.RoundToInt(normalDmg * buffMult);
            int finalPositional = Mathf.RoundToInt(positionalDmg * buffMult);

            bool hasMasterOfRear = playerController != null && playerController.hasMasterOfRear;

            bool isPositionalCrit;
            if (targetedAdd != null)
            {
                isPositionalCrit = true;
                int addDmg = hasMasterOfRear ? Mathf.RoundToInt(finalPositional * 1.5f) : finalPositional;
                targetedAdd.ReceivePlayerAttack(
                    "블레이드",
                    $"{executedStage}단 {skillName}",
                    addDmg,
                    new Color(1.0f, 0.55f, 0.18f, 1f),
                    executedStage == 3,
                    source: transform
                );
            }
            else
            {
                isPositionalCrit = targetBoss.ReceiveComboWeaponSkillAttack(
                    transform.position,
                    PositionalRequirement.Rear_135_To_180,
                    skillName,
                    executedStage,
                    finalNormal,
                    finalPositional,
                    hasMasterOfRear,
                    source: transform
                );
            }

            PlayStageSlashEffect(executedStage, isLeftFlankStyle: false, isPositionalCrit);

            if (isPositionalCrit && playerController != null && playerController.hasVampiricBlade)
            {
                playerController.TriggerVampiricBladeHeal();
            }

            if (isPositionalCrit && hasMasterOfRear)
            {
                _gcdRemaining = 0f;
                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.5f,
                    "[후면의 지배자] GCD 즉시 초기화!",
                    new Color(1f, 0.5f, 0.15f, 1f),
                    true
                );
            }
            else
            {
                TriggerSharedGCD();
            }

            AdvanceCombo2Stage();
        }

        private void ExecuteMeleeSkill4_GaleThrust()
        {
            if (targetBoss == null) return;
            if (!TryBeginMeleeAttack(MeleeSkill.BladeGale, 1, out BossAddMinion targetedAdd)) return;

            _skill4CooldownRemaining = skill4CooldownTotal;
            playerController?.Potion?.EndRevivalProtection();

            float buffMult = GetPlayerDamageBuffMultiplier();
            int finalDmg = Mathf.RoundToInt(skill4GaleThrustDamage * buffMult);

            if (targetedAdd != null)
            {
                targetedAdd.ReceivePlayerAttack("oGCD 질풍", "질풍 찌르기", finalDmg, new Color(0.30f, 1.0f, 0.90f, 1f), true, source: transform);
            }
            else
            {
                targetBoss.ReceiveOGcdAbilityAttack("질풍 찌르기 (oGCD)", finalDmg, source: transform);
            }
            PlayGaleThrustPierceEffect();
        }

        // =====================================================================
        // ④ 아크메이지 (캐스터 - Caster DPS) 전용 스킬 구현
        // =====================================================================
        /// <summary>
        /// ④ 아크메이지 [1] 화염 폭풍 3단 진화 캐스팅 콤보 (GCD - 말뚝 딜):
        /// - 1단 파이어 (1.2초 캐스팅, 데미지 200)
        /// - 2단 파이가 (1.5초 캐스팅, 데미지 380)
        /// - 3단 플레어 (2.0초 캐스팅, 데미지 750 초고화력 폭발!)
        /// - 캐스팅 도중 WASD로 이동하면 "CAST INTERRUPTED! (이동으로 시전 취소)"가 뜨며 취소됨!
        /// - 단, [4]번 '삼연속 마법' 버프 중(6초간)에는 캐스팅 시간 0초(즉발 무빙샷!)로 발사!
        /// </summary>
        private void ExecuteCasterSkill1_FirestormCastCombo()
        {
            if (targetBoss == null) return;

            int stage = _combo1Stage;
            string spellName = GetRoleSkill1StageName(PlayerRole.Archmage_Caster, stage);
            int baseDmg = stage switch
            {
                1 => 200, // 1단 파이어(200)
                2 => 380, // 2단 파이가(380)
                3 => 750, // 3단 플레어(750 초고화력 폭발!)
                _ => 200
            };

            float baseCastTime = stage switch
            {
                1 => 1.2f,
                2 => 1.5f,
                3 => 2.0f,
                _ => 1.2f
            };

            // [신속의 각성] 카드 획득 시 캐스팅 시간도 비례하여 단축!
            int swiftStacks = playerController != null ? playerController.swiftAwakeningStacks : 0;
            float castSpeedReduction = Mathf.Clamp(swiftStacks * 0.22f, 0f, 0.9f);
            float effectiveCastTime = Mathf.Max(0.45f, baseCastTime - castSpeedReduction);

            _castEnemy = GetOffensiveTarget();
            FaceOffensiveTarget(_castEnemy);
            // [4]번 '삼연속 마법' 활성 중이면 캐스팅 시간 0초 즉발 무빙샷!
            if (IsTriplecastActive)
            {
                ExecuteArchmageFireSpellImpact(stage, spellName, baseDmg, wasInstantTriplecast: true);
                return;
            }

            // 이미 이동 중일 때 누르면 즉시 경고 표시
            if (playerController != null && playerController.IsMovingXZ && GetComponent<CharacterActor>()?.PreservesCastWhileDashing != true)
            {
                _interruptAlertMessage = "CAST INTERRUPTED! (이동 중에는 화염 캐스팅 불가 — 멈춰 서거나 [2]냉기 / [4]삼연속 마법 사용!)";
                _interruptAlertTimer = 1.6f;
                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.5f,
                    "CAST INTERRUPTED! (이동으로 시전 취소)",
                    new Color(1f, 0.25f, 0.25f, 1f),
                    true
                );
                return;
            }

            // 말뚝 캐스팅 시작!
            playerController?.Potion?.EndRevivalProtection();
            _isCastingSpell = true;
            _castingSpellComboStage = stage;
            _castingSpellName = spellName;
            _castingSpellBaseDamage = baseDmg;
            _castDurationTotal = effectiveCastTime;
            _castElapsed = 0f;
        }

        private void ExecuteArchmageFireSpellImpact(int stage, string spellName, int baseDmg, bool wasInstantTriplecast)
        {
            if (targetBoss == null) return;

            float buffMult = GetPlayerDamageBuffMultiplier();
            // 캐스터가 [후면의 지배자 / 대마도사의 비전] 카드 보유 시 화염 콤보 데미지 +35% 증폭!
            if (playerController != null && playerController.hasMasterOfRear)
            {
                buffMult *= (1.0f + 0.35f * Mathf.Max(1, playerController.masterOfRearStacks));
            }

            int finalDmg = Mathf.RoundToInt(baseDmg * buffMult);

            ResolveOffensiveTarget(out BossAddMinion targetedAdd, out _);

            if (playerController != null && playerController.hasVampiricBlade)
            {
                playerController.TriggerVampiricBladeHeal();
            }

            string extraNote = wasInstantTriplecast ? "삼연속 마법 즉발!!" : (stage == 3 ? "초고화력 폭발!!" : "");
            Color fireColor = stage switch
            {
                1 => new Color(1.0f, 0.55f, 0.15f, 1f),
                2 => new Color(1.0f, 0.38f, 0.12f, 1f),
                3 => new Color(1.0f, 0.85f, 0.15f, 1f),
                _ => new Color(1.0f, 0.5f, 0.15f, 1f)
            };

            if (targetedAdd != null)
            {
                targetedAdd.ReceivePlayerAttack("아크메이지", $"{stage}단 {spellName}", finalDmg, fireColor, stage == 3 || wasInstantTriplecast, source: transform);
            }
            else
            {
                targetBoss.ReceiveRoleSkillAttack(
                    "아크메이지",
                    spellName,
                    stage,
                    finalDmg,
                    fireColor,
                    stage == 3 || wasInstantTriplecast,
                    extraNote,
                    source: transform
                );
            }

            PlayStageSlashEffect(stage, isLeftFlankStyle: true, isCrit: stage == 3 || wasInstantTriplecast);
            TriggerSharedGCD();
            AdvanceCombo1Stage();
        }

        /// <summary>
        /// ④ 아크메이지 [2] 냉기 파동 3단 콤보 (GCD - 무빙샷):
        /// 데미지는 낮지만(100 -> 150 -> 220) 캐스팅 없이 이동하면서 즉시 발사할 수 있는 마법 (장판 피할 때 사용)
        /// </summary>
        private void ExecuteCasterSkill2_FrostPulseInstantCombo()
        {
            if (targetBoss == null) return;

            int stage = _combo2Stage;
            string spellName = GetRoleSkill2StageName(PlayerRole.Archmage_Caster, stage);
            int baseDmg = stage switch
            {
                1 => 100, // 1단 냉기 탄환(100)
                2 => 150, // 2단 빙결 파동(150)
                3 => 220, // 3단 절대영도(220)
                _ => 100
            };

            float buffMult = GetPlayerDamageBuffMultiplier();
            int finalDmg = Mathf.RoundToInt(baseDmg * buffMult);

            ResolveOffensiveTarget(out BossAddMinion targetedAdd, out _);

            Color frostCol = new Color(0.35f, 0.88f, 1.0f, 1f);
            if (targetedAdd != null)
            {
                targetedAdd.ReceivePlayerAttack("무빙냉기", $"{stage}단 {spellName}", finalDmg, frostCol, stage == 3, source: transform);
            }
            else
            {
                targetBoss.ReceiveRoleSkillAttack(
                    "무빙냉기",
                    spellName,
                    stage,
                    finalDmg,
                    frostCol,
                    stage == 3,
                    "즉발 무빙샷",
                    source: transform
                );
            }

            PlayStageSlashEffect(stage, isLeftFlankStyle: false, isCrit: stage == 3);
            TriggerSharedGCD();
            AdvanceCombo2Stage();
        }

        /// <summary>
        /// ④ 아크메이지 [4] 삼연속 마법 (oGCD, 쿨 25초):
        /// 발동 후 6초 동안 [1]번 화염 캐스팅 콤보가 캐스팅 시간 0초(즉발 무빙샷!)로 나가는 폭딜 버프!
        /// </summary>
        private void ExecuteCasterSkill4_Triplecast()
        {
            _skill4CooldownRemaining = skill4CooldownTotal;
            playerController?.Potion?.EndRevivalProtection();
            _triplecastBuffRemaining = 6.0f;

            // 이미 캐스팅 중이었다면 즉시 발사 완료시켜 줌!
            if (_isCastingSpell)
            {
                CompleteActiveFireSpellCast();
            }

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.65f,
                "★ [삼연속 마법 (Triplecast)] 발동! 6초간 [1]번 화염 콤보 캐스팅 0초 즉발 무빙샷!! ★",
                new Color(1.0f, 0.65f, 0.15f, 1f),
                true
            );

            if (targetBoss != null)
            {
                targetBoss.ShowExternalAlert(
                    "★ 아크메이지 [삼연속 마법] ON! 6초 동안 이동하면서 [1]번 파이어→파이가→플레어(750)를 캐스팅 없이 즉발 난사할 수 있습니다! ★",
                    new Color(1.0f, 0.72f, 0.20f, 1f),
                    4.0f
                );
            }
        }

        // =====================================================================
        // 공통 헬퍼 (콤보 진화 / GCD / 역할별 스킬명 조회)
        // =====================================================================
        private float GetPlayerDamageBuffMultiplier()
        {
            float mult = (playerController != null && playerController.HasTacticalRescueBuff) ? 2.0f : 1.0f;
            if (playerController != null && playerController.hasMasterOfRear && CurrentRole != PlayerRole.Blade_MeleeDPS && CurrentRole != PlayerRole.Archmage_Caster)
            {
                mult *= (1.0f + 0.25f * Mathf.Max(1, playerController.masterOfRearStacks));
            }
            return mult;
        }

        private void AdvanceCombo1Stage()
        {
            if (_combo1Stage == 1)
            {
                _combo1Stage = 2;
                _combo1Timer = comboExpireDuration;
            }
            else if (_combo1Stage == 2)
            {
                _combo1Stage = 3;
                _combo1Timer = comboExpireDuration;
            }
            else
            {
                _combo1Stage = 1;
                _combo1Timer = 0f;
            }
        }

        private void AdvanceCombo2Stage()
        {
            if (_combo2Stage == 1)
            {
                _combo2Stage = 2;
                _combo2Timer = comboExpireDuration;
            }
            else if (_combo2Stage == 2)
            {
                _combo2Stage = 3;
                _combo2Timer = comboExpireDuration;
            }
            else
            {
                _combo2Stage = 1;
                _combo2Timer = 0f;
            }
        }

        private void TriggerSharedGCD()
        {
            playerController?.Potion?.EndRevivalProtection();
            _gcdTotalForCurrentCycle = Mathf.Max(minGcdDuration, currentGcdDuration);
            _gcdRemaining = _gcdTotalForCurrentCycle;
        }

        /// <summary>
        /// [신속의 각성] 로그라이크 카드 획득 시 글로벌 쿨타임(GCD)이 0.4초 감소합니다.
        /// </summary>
        public void ApplySwiftAwakeningGcdReduction(float reductionSeconds = 0.4f)
        {
            float before = currentGcdDuration;
            currentGcdDuration = Mathf.Max(minGcdDuration, currentGcdDuration - reductionSeconds);
            _gcdTotalForCurrentCycle = currentGcdDuration;

            if (_gcdRemaining > currentGcdDuration)
            {
                _gcdRemaining = currentGcdDuration;
            }

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.75f,
                $"[신속의 각성] GCD & 캐스팅 단축! ({before:F1}s → {currentGcdDuration:F1}s)",
                new Color(0.3f, 1f, 0.85f, 1f),
                true
            );
        }

        public static string GetRoleSkill1StageName(PlayerRole role, int stage)
        {
            return role switch
            {
                PlayerRole.Guardian_Tank => stage switch
                {
                    1 => "방패 가격",
                    2 => "회전 베기",
                    3 => "대지 분쇄",
                    _ => "방패 가격"
                },
                PlayerRole.Oracle_Healer => stage switch
                {
                    1 => "빛의 탄환",
                    2 => "단죄의 빛",
                    3 => "천벌의 기둥",
                    _ => "빛의 탄환"
                },
                PlayerRole.Archmage_Caster => stage switch
                {
                    1 => "파이어 (Fire)",
                    2 => "파이가 (Firaga)",
                    3 => "플레어 (Flare)",
                    _ => "파이어 (Fire)"
                },
                _ => GetSkill1StageName(stage)
            };
        }

        public static string GetRoleSkill2StageName(PlayerRole role, int stage)
        {
            return role switch
            {
                PlayerRole.Guardian_Tank => stage switch
                {
                    1 => "심판의 투창",
                    2 => "관통의 창",
                    3 => "성광 방패 투척",
                    _ => "심판의 투창"
                },
                PlayerRole.Oracle_Healer => "구원의 치유 & 보호막",
                PlayerRole.Archmage_Caster => stage switch
                {
                    1 => "냉기 탄환",
                    2 => "빙결 파동",
                    3 => "절대영도",
                    _ => "냉기 탄환"
                },
                _ => GetSkill2StageName(stage)
            };
        }

        public static string GetSkill1StageName(int stage)
        {
            return stage switch
            {
                1 => "섬광 베기",
                2 => "강화 섬광: 질풍",
                3 => "최종 오의: 측면 일섬",
                _ => "섬광 베기"
            };
        }

        public static void GetSkill1StageDamages(int stage, out int normalDmg, out int flankCritDmg)
        {
            switch (stage)
            {
                case 1:
                    normalDmg = 100;
                    flankCritDmg = 100;
                    break;
                case 2:
                    normalDmg = 150;
                    flankCritDmg = 250;
                    break;
                case 3:
                    normalDmg = 220;
                    flankCritDmg = 450;
                    break;
                default:
                    normalDmg = 100;
                    flankCritDmg = 100;
                    break;
            }
        }

        public static string GetSkill2StageName(int stage)
        {
            return stage switch
            {
                1 => "그림자 베기",
                2 => "강화 그림자: 암습",
                3 => "최종 오의: 심장 적출",
                _ => "그림자 베기"
            };
        }

        public static void GetSkill2StageDamages(int stage, out int normalDmg, out int rearCritDmg)
        {
            switch (stage)
            {
                case 1:
                    normalDmg = 100;
                    rearCritDmg = 100;
                    break;
                case 2:
                    normalDmg = 150;
                    rearCritDmg = 250;
                    break;
                case 3:
                    normalDmg = 220;
                    rearCritDmg = 450;
                    break;
                default:
                    normalDmg = 100;
                    rearCritDmg = 100;
                    break;
            }
        }

        // =====================================================================
        // 1단 -> 2단 -> 3단 단계별 크기/화려함이 진화하는 3D 타격 이펙트
        // =====================================================================
        private void CreateCombatFxLines()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            Material fxMat = new Material(shader) { color = Color.white };

            for (int i = 0; i < 4; i++)
            {
                GameObject fxObj = new GameObject($"ComboFx_Line_{i}");
                fxObj.transform.SetParent(transform, false);
                LineRenderer lr = fxObj.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.numCapVertices = 5;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = fxMat;
                lr.enabled = false;
                _fxLines.Add(lr);
            }
        }

        private void PlayStageSlashEffect(int stage, bool isLeftFlankStyle, bool isCrit)
        {
            if (RaidCombatAuthority.Host && _lastOffensiveVisualTarget != null) RaidNetworkSession.Instance.BroadcastEffect(transform.position + Vector3.up, _lastOffensiveVisualTarget.position + Vector3.up, playerController.normalBodyColor, "", false, 1);
            if (targetBoss == null || _fxLines.Count == 0)
            {
                return;
            }

            if (_fxCoroutine != null)
            {
                StopCoroutine(_fxCoroutine);
            }
            _fxCoroutine = StartCoroutine(StageSlashEffectRoutine(stage, isLeftFlankStyle, isCrit));
        }

        private void PlayGaleThrustPierceEffect()
        {
            if (RaidCombatAuthority.Host && _lastOffensiveVisualTarget != null) RaidNetworkSession.Instance.BroadcastEffect(transform.position + Vector3.up, _lastOffensiveVisualTarget.position + Vector3.up, playerController.normalBodyColor, "", false, 1);
            if (targetBoss == null || _fxLines.Count == 0)
            {
                return;
            }

            if (_fxCoroutine != null)
            {
                StopCoroutine(_fxCoroutine);
            }
            _fxCoroutine = StartCoroutine(GaleThrustEffectRoutine());
        }

        private IEnumerator StageSlashEffectRoutine(int stage, bool isFlankSkill, bool isCrit)
        {
            foreach (LineRenderer lr in _fxLines)
            {
                lr.enabled = false;
            }

            Vector3 start = new Vector3(transform.position.x, 1.0f, transform.position.z);
            Transform fxTargetTr = _lastOffensiveVisualTarget != null ? _lastOffensiveVisualTarget : targetBoss.transform;
            Vector3 bossCenter = new Vector3(fxTargetTr.position.x, 1.3f, fxTargetTr.position.z);
            Vector3 attackForward = bossCenter - start;
            attackForward.y = 0;
            Vector3 attackRight = Vector3.Cross(Vector3.up, attackForward.normalized);

            Color mainColor;
            if (CurrentRole == PlayerRole.Archmage_Caster)
            {
                mainColor = isFlankSkill
                    ? (stage == 3 ? new Color(1.0f, 0.85f, 0.15f, 1f) : new Color(1.0f, 0.42f, 0.12f, 1f))
                    : new Color(0.35f, 0.88f, 1.0f, 1f);
            }
            else if (CurrentRole == PlayerRole.Guardian_Tank)
            {
                mainColor = stage == 3 ? new Color(0.25f, 0.95f, 1.0f, 1f) : new Color(0.25f, 0.65f, 1.0f, 1f);
            }
            else if (CurrentRole == PlayerRole.Oracle_Healer)
            {
                mainColor = stage == 3 ? new Color(0.65f, 1.0f, 0.35f, 1f) : new Color(0.25f, 0.98f, 0.55f, 1f);
            }
            else if (isFlankSkill)
            {
                mainColor = stage switch
                {
                    1 => new Color(0.25f, 0.95f, 0.55f, 1f),
                    2 => new Color(0.45f, 1.0f, 0.25f, 1f),
                    3 => new Color(1.0f, 0.85f, 0.15f, 1f),
                    _ => Color.green
                };
            }
            else
            {
                mainColor = stage switch
                {
                    1 => new Color(0.30f, 0.80f, 1.0f, 1f),
                    2 => new Color(0.65f, 0.35f, 1.0f, 1f),
                    3 => new Color(1.0f, 0.22f, 0.45f, 1f),
                    _ => Color.cyan
                };
            }

            float baseWidth = stage switch
            {
                1 => 0.18f,
                2 => 0.34f,
                3 => 0.62f,
                _ => 0.20f
            };
            if (isCrit)
            {
                baseWidth *= 1.25f;
            }

            int activeLineCount = Mathf.Clamp(stage, 1, 3);

            for (int i = 0; i < activeLineCount && i < _fxLines.Count; i++)
            {
                LineRenderer lr = _fxLines[i];
                lr.enabled = true;
                lr.loop = false;
                lr.positionCount = 2;
                lr.startWidth = baseWidth * (1f - i * 0.18f);
                lr.endWidth = baseWidth * 0.25f;
                lr.startColor = mainColor;
                lr.endColor = new Color(1f, 0.95f, 0.7f, 0.85f);

                Vector3 sideOffset = attackRight * ((i - (activeLineCount - 1) * 0.5f) * 0.9f);
                lr.SetPosition(0, start + sideOffset * 0.5f);
                lr.SetPosition(1, bossCenter - sideOffset * 1.1f + Vector3.up * (i * 0.35f));
            }

            if (stage == 3 && _fxLines.Count >= 4)
            {
                LineRenderer ringLr = _fxLines[3];
                ringLr.enabled = true;
                ringLr.loop = true;
                const int ringSegs = 36;
                ringLr.positionCount = ringSegs;
                ringLr.startWidth = 0.32f;
                ringLr.endWidth = 0.32f;
                ringLr.startColor = mainColor;
                ringLr.endColor = mainColor;

                float radius = isCrit ? 4.4f : 3.0f;
                for (int s = 0; s < ringSegs; s++)
                {
                    float rad = (float)s / ringSegs * Mathf.PI * 2f;
                    ringLr.SetPosition(s, new Vector3(
                        bossCenter.x + Mathf.Sin(rad) * radius,
                        0.35f,
                        bossCenter.z + Mathf.Cos(rad) * radius
                    ));
                }
            }

            float duration = stage == 3 ? 0.28f : (stage == 2 ? 0.20f : 0.14f);
            yield return new WaitForSeconds(duration);

            foreach (LineRenderer lr in _fxLines)
            {
                lr.enabled = false;
            }
            _fxCoroutine = null;
        }

        private IEnumerator GaleThrustEffectRoutine()
        {
            foreach (LineRenderer lr in _fxLines)
            {
                lr.enabled = false;
            }

            LineRenderer mainPierce = _fxLines[0];
            mainPierce.enabled = true;
            mainPierce.loop = false;
            mainPierce.positionCount = 2;
            mainPierce.startWidth = 0.38f;
            mainPierce.endWidth = 0.05f;
            Color galeColor = new Color(0.35f, 1.0f, 0.92f, 1f);
            mainPierce.startColor = Color.white;
            mainPierce.endColor = galeColor;

            Vector3 start = new Vector3(transform.position.x, 1.1f, transform.position.z);
            Transform fxTargetTr = _lastOffensiveVisualTarget != null ? _lastOffensiveVisualTarget : targetBoss.transform;
            Vector3 end = new Vector3(fxTargetTr.position.x, 1.4f, fxTargetTr.position.z);
            mainPierce.SetPosition(0, start);
            mainPierce.SetPosition(1, end);

            yield return new WaitForSeconds(0.18f);
            mainPierce.enabled = false;
            _fxCoroutine = null;
        }

        private static bool WasKeyPressed(int numberKey)
        { return LocalInputBindings.Pressed("Skill" + numberKey); }
    }

    /// <summary>
    /// Step 8 ② 오라클(힐러) [3]번 '치유의 성역 (oGCD, 쿨 15초)':
    /// 바닥(XZ 평면)에 6초간 유지되는 초록색 광역 힐 장판을 생성하여 범위 내 모든 파티원을 지속 회복시킵니다.
    /// 2D 탑다운 뷰와 3D 쿼터뷰 양쪽에서 선명하게 보입니다.
    /// </summary>
    public partial class HealingSanctuaryZone : MonoBehaviour
    {
        private Transform _source;
        private int _encounterId;
        public float radius = 5.5f;
        public float duration = 6.0f;
        public float tickInterval = 1.0f;
        public float healPerTick = 110f;
        public float shieldPerTick = 45f;

        private float _elapsed;
        private float _tickTimer;
        private LineRenderer _outerRing;
        private LineRenderer _innerCross;
        private MeshRenderer _discRenderer;

        public static HealingSanctuaryZone Spawn(Vector3 centerPos, float radius, float duration, float healPerTick, float shieldPerTick, Transform source)
        {
            GameObject go = new GameObject("HealingSanctuaryZone_AoE");
            go.transform.position = new Vector3(centerPos.x, 0.045f, centerPos.z);

            HealingSanctuaryZone zone = go.AddComponent<HealingSanctuaryZone>();
            zone._source = source;
            zone._encounterId = CombatMetricsManager.EncounterId;
            zone.radius = radius;
            zone.duration = duration;
            zone.healPerTick = healPerTick;
            zone.shieldPerTick = shieldPerTick;
            zone.BuildVisuals();
            zone._networkStart = RaidCombatAuthority.ServerTime;
            zone.ApplyHealingTick(); // 설치 즉시 1회 틱 회복!
            return zone;
        }

        private void BuildVisuals()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            // 반투명 초록색 바닥 원판 메쉬
            GameObject discObj = new GameObject("Sanctuary_Disc");
            discObj.transform.SetParent(transform, false);
            MeshFilter mf = discObj.AddComponent<MeshFilter>();
            _discRenderer = discObj.AddComponent<MeshRenderer>();
            _discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _discRenderer.receiveShadows = false;
            _discRenderer.sharedMaterial = new Material(shader)
            {
                color = new Color(0.18f, 0.98f, 0.45f, 0.22f)
            };
            mf.sharedMesh = CreateDiscMesh(radius, 48);

            // 외곽 밝은 초록색 테두리 링
            GameObject ringObj = new GameObject("Sanctuary_OuterRing");
            ringObj.transform.SetParent(transform, false);
            _outerRing = ringObj.AddComponent<LineRenderer>();
            _outerRing.useWorldSpace = false;
            _outerRing.loop = true;
            _outerRing.positionCount = 48;
            _outerRing.startWidth = 0.20f;
            _outerRing.endWidth = 0.20f;
            _outerRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _outerRing.receiveShadows = false;
            _outerRing.sharedMaterial = new Material(shader) { color = Color.white };
            Color ringColor = new Color(0.25f, 1.0f, 0.55f, 0.95f);
            _outerRing.startColor = ringColor;
            _outerRing.endColor = ringColor;

            for (int i = 0; i < 48; i++)
            {
                float rad = (float)i / 48f * Mathf.PI * 2f;
                _outerRing.SetPosition(i, new Vector3(Mathf.Sin(rad) * radius, 0.02f, Mathf.Cos(rad) * radius));
            }
        }

        private static Mesh CreateDiscMesh(float r, int segments)
        {
            Mesh m = new Mesh { name = "HealingSanctuaryDisc" };
            Vector3[] verts = new Vector3[segments + 1];
            int[] tris = new int[segments * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float rad = (float)i / segments * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Sin(rad) * r, 0f, Mathf.Cos(rad) * r);
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segments + 1;
            }
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            return m;
        }

        private void Update()
        {
            if (Time.timeScale <= 0.001f)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            _tickTimer += Time.deltaTime;

            if (_discRenderer != null && _discRenderer.sharedMaterial != null)
            {
                float alpha = 0.18f + Mathf.Sin(Time.time * 5f) * 0.06f;
                _discRenderer.sharedMaterial.color = new Color(0.18f, 0.98f, 0.45f, alpha);
            }

            if (_tickTimer >= tickInterval)
            {
                _tickTimer -= tickInterval;
                ApplyHealingTick();
            }

            if (_elapsed >= duration)
            {
                Destroy(gameObject);
            }
        }

        private void ApplyHealingTick()
        {
            // A lingering zone cannot heal or grant shields in the next encounter.
            if (_encounterId != CombatMetricsManager.EncounterId || StageManager.CombatSuspended || (_source != null && !CombatRange.LivingPartyMember(_source))) { Destroy(gameObject); return; }
            Vector2 centerXZ = new Vector2(transform.position.x, transform.position.z);

            foreach (var member in PartyLife.OrderedMembers())
            {
                if (!CombatRange.LivingPartyMember(member) || BattleResurrection.FlatDistance(transform.position, member.position) > radius) continue;
                var pc = member.GetComponent<PlayerController>(); var bot = member.GetComponent<PartyBotAI>();
                if (pc != null) { pc.ReceiveHeal(healPerTick, _source); pc.AddShield(shieldPerTick, _source); }
                else if (bot != null) { bot.ReceiveHeal(healPerTick, _source); bot.AddShield(shieldPerTick, _source); }
            }
        }
    }
}
