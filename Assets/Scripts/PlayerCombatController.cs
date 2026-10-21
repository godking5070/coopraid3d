using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    /// <summary>
    /// 파판14 스타일 글로벌 쿨타임(GCD 2.0초) / 논글로벌 쿨타임(oGCD 끼워넣기) 분리 및
    /// [1]번(측면), [2]번(후면) 원버튼 3단 진화 콤보 시스템을 담당하는 전투 컨트롤러 (Step 5).
    /// </summary>
    public class PlayerCombatController : MonoBehaviour
    {
        [Header("References")]
        public PlayerController playerController;
        public BossController targetBoss;
        public SkillHotbarUI hotbarUI;

        [Header("GCD (글로벌 쿨타임 - [1], [2]번 웨폰스킬 공유)")]
        [Tooltip("기본 글로벌 쿨타임 (2.0초). [신속의 각성] 카드 획득 시마다 0.4초씩 감소합니다.")]
        public float baseGcdDuration = 2.0f;
        public float minGcdDuration = 0.6f;
        public float currentGcdDuration = 2.0f;

        [Header("oGCD (논글로벌 어빌리티 - 글쿨 중 끼워넣기 가능)")]
        [Tooltip("[3]번 개인 생존기 개별 쿨타임 (15초)")]
        public float skill3DefensiveCooldown = 15.0f;

        [Tooltip("[4]번 질풍 찌르기(끼워넣기 어빌리티) 개별 쿨타임 (8초)")]
        public float skill4GaleThrustCooldown = 8.0f;
        public int skill4GaleThrustDamage = 180;

        [Header("One-Button 3-Stage Combo System (8초 유지)")]
        [Tooltip("2단계/3단계 진화 후 다음 단계를 사용하지 않으면 초기화되는 시간 (8초)")]
        public float comboExpireDuration = 8.0f;

        // 현재 GCD 및 oGCD 남은 쿨타임
        private float _gcdRemaining;
        private float _gcdTotalForCurrentCycle = 2.0f;
        private float _skill3CooldownRemaining;
        private float _skill4CooldownRemaining;

        // [1]번 측면 3단 진화 콤보 상태 (1 -> 2 -> 3 -> 1)
        private int _combo1Stage = 1;
        private float _combo1Timer;

        // [2]번 후면 3단 진화 콤보 상태 (1 -> 2 -> 3 -> 1)
        private int _combo2Stage = 1;
        private float _combo2Timer;

        // 단계별 화려한 타격 이펙트용 다중 LineRenderer
        private readonly List<LineRenderer> _fxLines = new List<LineRenderer>();
        private Coroutine _fxCoroutine;

        // 외부(UI 등) 조회 프로퍼티
        public float GcdRemaining => _gcdRemaining;
        public float GcdTotal => _gcdTotalForCurrentCycle;
        public bool IsOnGcd => _gcdRemaining > 0.001f;

        public float Skill3CooldownRemaining => _skill3CooldownRemaining;
        public float Skill4CooldownRemaining => _skill4CooldownRemaining;

        public int Combo1Stage => _combo1Stage;
        public float Combo1TimerRemaining => _combo1Timer;
        public int Combo2Stage => _combo2Stage;
        public float Combo2TimerRemaining => _combo2Timer;

        private void Awake()
        {
            currentGcdDuration = baseGcdDuration;
            _gcdTotalForCurrentCycle = currentGcdDuration;
        }

        private void Start()
        {
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
            if (hotbarUI == null)
            {
                hotbarUI = FindFirstObjectByType<SkillHotbarUI>();
            }

            CreateCombatFxLines();
        }

        private void Update()
        {
            if (Time.timeScale <= 0.001f)
            {
                return;
            }

            UpdateCooldownsAndComboTimers();

            if (playerController == null || !playerController.IsBeingRescued)
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

            // 1. GCD 감소 ([1], [2]번 공유)
            if (_gcdRemaining > 0f)
            {
                _gcdRemaining = Mathf.Max(0f, _gcdRemaining - dt * hasteMultiplier);
            }

            // 2. oGCD [3], [4]번 개별 쿨타임 감소
            if (_skill3CooldownRemaining > 0f)
            {
                _skill3CooldownRemaining = Mathf.Max(0f, _skill3CooldownRemaining - dt);
            }
            if (_skill4CooldownRemaining > 0f)
            {
                _skill4CooldownRemaining = Mathf.Max(0f, _skill4CooldownRemaining - dt * hasteMultiplier);
            }

            // 3. [1]번 측면 콤보 유지 타이머 (8초 경과 시 1단계로 초기화)
            if (_combo1Stage > 1)
            {
                _combo1Timer -= dt;
                if (_combo1Timer <= 0f)
                {
                    _combo1Timer = 0f;
                    _combo1Stage = 1;
                }
            }

            // 4. [2]번 후면 콤보 유지 타이머 (8초 경과 시 1단계로 초기화)
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
        // [1]번 키: 측면 특화 3단 진화 웨폰스킬 (GCD 2.0초 공유)
        // 1단계(섬광 베기: 100) -> 2단계(강화 섬광: 질풍: 기본 150 / 측면 250) -> 3단계(최종 오의: 측면 일섬: 기본 220 / 측면 450)
        // =====================================================================
        public void TryUseSkill1_FlankCombo()
        {
            if (IsOnGcd)
            {
                return;
            }
            if (targetBoss == null)
            {
                return;
            }

            int executedStage = _combo1Stage;
            string skillName = GetSkill1StageName(executedStage);
            GetSkill1StageDamages(executedStage, out int normalDmg, out int positionalDmg);

            // [전술적 구출] 공격력 2배 버프 반영
            float buffMult = (playerController != null && playerController.HasTacticalRescueBuff) ? 2.0f : 1.0f;
            int finalNormal = Mathf.RoundToInt(normalDmg * buffMult);
            int finalPositional = Mathf.RoundToInt(positionalDmg * buffMult);

            if (playerController != null)
            {
                playerController.FaceBossImmediateXZ();
            }

            // 보스 측면(45~135도) 판정 및 데미지 적용
            bool isPositionalCrit = targetBoss.ReceiveComboWeaponSkillAttack(
                transform.position,
                PositionalRequirement.Flank_45_To_135,
                skillName,
                executedStage,
                finalNormal,
                finalPositional,
                false
            );

            // 단계별 점점 화려해지는 3D 베기 이펙트 재생
            PlayStageSlashEffect(executedStage, isLeftFlankStyle: true, isPositionalCrit);

            // [흡혈 검] 카드 효과: 방향 특화 성공 시 내 체력 10 회복
            if (isPositionalCrit && playerController != null && playerController.hasVampiricBlade)
            {
                playerController.TriggerVampiricBladeHeal();
            }

            // [1]번과 [2]번 슬롯 모두에 공통 글로벌 쿨타임(GCD) 시작!
            TriggerSharedGCD();

            // 콤보 단계 진화 (1 -> 2 -> 3 -> 1) 및 8초 타이머 갱신
            if (executedStage == 1)
            {
                _combo1Stage = 2;
                _combo1Timer = comboExpireDuration;
            }
            else if (executedStage == 2)
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

        // =====================================================================
        // [2]번 키: 후면 특화 3단 진화 웨폰스킬 (GCD 2.0초 공유)
        // 1단계(그림자 베기: 100) -> 2단계(강화 그림자: 암습: 기본 150 / 후면 250) -> 3단계(최종 오의: 심장 적출: 기본 220 / 후면 450)
        // =====================================================================
        public void TryUseSkill2_RearCombo()
        {
            if (IsOnGcd)
            {
                return;
            }
            if (targetBoss == null)
            {
                return;
            }

            int executedStage = _combo2Stage;
            string skillName = GetSkill2StageName(executedStage);
            GetSkill2StageDamages(executedStage, out int normalDmg, out int positionalDmg);

            float buffMult = (playerController != null && playerController.HasTacticalRescueBuff) ? 2.0f : 1.0f;
            int finalNormal = Mathf.RoundToInt(normalDmg * buffMult);
            int finalPositional = Mathf.RoundToInt(positionalDmg * buffMult);

            if (playerController != null)
            {
                playerController.FaceBossImmediateXZ();
            }

            bool hasMasterOfRear = playerController != null && playerController.hasMasterOfRear;

            bool isPositionalCrit = targetBoss.ReceiveComboWeaponSkillAttack(
                transform.position,
                PositionalRequirement.Rear_135_To_180,
                skillName,
                executedStage,
                finalNormal,
                finalPositional,
                hasMasterOfRear
            );

            PlayStageSlashEffect(executedStage, isLeftFlankStyle: false, isPositionalCrit);

            if (isPositionalCrit && playerController != null && playerController.hasVampiricBlade)
            {
                playerController.TriggerVampiricBladeHeal();
            }

            // [후면의 지배자] 카드 보유 시 후면 적중하면 쿨타임(GCD) 즉시 초기화! 아니면 공통 GCD 작동
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

            // 콤보 단계 진화 (1 -> 2 -> 3 -> 1) 및 8초 타이머 갱신
            if (executedStage == 1)
            {
                _combo2Stage = 2;
                _combo2Timer = comboExpireDuration;
            }
            else if (executedStage == 2)
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

        // =====================================================================
        // [3]번 키 (oGCD 어빌리티): 개인 생존기 (개별 쿨타임 15초, 3초간 피해 80% 감소)
        // 글쿨(GCD)이 도는 중에도 언제든 즉시 끼워넣기(Weaving) 발동 가능!
        // =====================================================================
        public void TryUseSkill3_PersonalDefensive_oGCD()
        {
            if (_skill3CooldownRemaining > 0.001f)
            {
                return;
            }

            _skill3CooldownRemaining = skill3DefensiveCooldown;

            if (playerController != null)
            {
                playerController.ActivatePersonalDefensiveBuff();
            }
        }

        // =====================================================================
        // [4]번 키 (oGCD 어빌리티): 질풍 찌르기 (개별 쿨타임 8초, 보스에게 즉발 추가 데미지)
        // 글쿨(GCD)이 도는 중에도 언제든 즉시 끼워넣기(Weaving) 발동 가능!
        // =====================================================================
        public void TryUseSkill4_GaleThrust_oGCD()
        {
            if (_skill4CooldownRemaining > 0.001f)
            {
                return;
            }
            if (targetBoss == null)
            {
                return;
            }

            _skill4CooldownRemaining = skill4GaleThrustCooldown;

            if (playerController != null)
            {
                playerController.FaceBossImmediateXZ();
            }

            float buffMult = (playerController != null && playerController.HasTacticalRescueBuff) ? 2.0f : 1.0f;
            int finalDmg = Mathf.RoundToInt(skill4GaleThrustDamage * buffMult);

            targetBoss.ReceiveOGcdAbilityAttack("질풍 찌르기 (oGCD)", finalDmg);
            PlayGaleThrustPierceEffect();
        }

        private void TriggerSharedGCD()
        {
            _gcdTotalForCurrentCycle = Mathf.Max(minGcdDuration, currentGcdDuration);
            _gcdRemaining = _gcdTotalForCurrentCycle;
        }

        /// <summary>
        /// [신속의 각성] 로그라이크 카드 획득 시 글로벌 쿨타임(GCD)이 0.4초 감소합니다.
        /// (예: 2.0초 -> 1.6초 -> 1.2초)
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
                $"[신속의 각성] GCD 감소! ({before:F1}s → {currentGcdDuration:F1}s)",
                new Color(0.3f, 1f, 0.85f, 1f),
                true
            );
        }

        // =====================================================================
        // 스킬 단계별 이름 / 데미지 / 아이콘 색상 헬퍼
        // =====================================================================
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
                    flankCritDmg = 100; // 1단계 (섬광 베기): 기본 데미지 100 -> 적중 시 2단계로 진화
                    break;
                case 2:
                    normalDmg = 150;
                    flankCritDmg = 250; // 2단계 (강화 섬광: 질풍): 기본 150 (측면 타격 시 250)
                    break;
                case 3:
                    normalDmg = 220;
                    flankCritDmg = 450; // 3단계 (최종 오의: 측면 일섬): 기본 220 (측면 타격 시 450!)
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
                    rearCritDmg = 100; // 1단계 (그림자 베기): 기본 데미지 100 -> 적중 시 2단계로 진화
                    break;
                case 2:
                    normalDmg = 150;
                    rearCritDmg = 250; // 2단계 (강화 그림자: 암습): 기본 150 (후면 타격 시 250)
                    break;
                case 3:
                    normalDmg = 220;
                    rearCritDmg = 450; // 3단계 (최종 오의: 심장 적출): 기본 220 (후면 타격 시 450!)
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
            Vector3 bossCenter = new Vector3(targetBoss.transform.position.x, 1.3f, targetBoss.transform.position.z);

            // 단계가 올라갈수록 색상, 굵기, 궤적 개수가 점점 화려해짐
            Color mainColor;
            float baseWidth;
            int activeLineCount;

            if (isFlankSkill)
            {
                mainColor = stage switch
                {
                    1 => new Color(0.25f, 0.95f, 0.55f, 1f),
                    2 => new Color(0.45f, 1.0f, 0.25f, 1f),
                    3 => new Color(1.0f, 0.85f, 0.15f, 1f), // 3단계 최종 오의: 황금빛 일섬!
                    _ => Color.green
                };
            }
            else
            {
                mainColor = stage switch
                {
                    1 => new Color(0.30f, 0.80f, 1.0f, 1f),
                    2 => new Color(0.65f, 0.35f, 1.0f, 1f),
                    3 => new Color(1.0f, 0.22f, 0.45f, 1f), // 3단계 최종 오의: 진홍빛 심장 적출!
                    _ => Color.cyan
                };
            }

            baseWidth = stage switch
            {
                1 => 0.18f,
                2 => 0.34f,
                3 => 0.62f, // 3단계 대형 이펙트
                _ => 0.20f
            };
            if (isCrit)
            {
                baseWidth *= 1.25f;
            }

            activeLineCount = Mathf.Clamp(stage, 1, 3);

            // 1단계: 단일 베기, 2단계: 이중 교차 베기, 3단계: 삼중 대형 오의 참격 + 충격파 링!
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

                Vector3 sideOffset = transform.right * ((i - (activeLineCount - 1) * 0.5f) * 0.9f);
                lr.SetPosition(0, start + sideOffset * 0.5f);
                lr.SetPosition(1, bossCenter - sideOffset * 1.1f + Vector3.up * (i * 0.35f));
            }

            // 3단계 최종 오의일 때 보스 주변에 대형 오의 충격파 원형 이펙트 추가!
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

                float radius = isCrit ? 4.2f : 3.0f;
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
            Vector3 end = new Vector3(targetBoss.transform.position.x, 1.4f, targetBoss.transform.position.z);
            mainPierce.SetPosition(0, start);
            mainPierce.SetPosition(1, end);

            yield return new WaitForSeconds(0.18f);
            mainPierce.enabled = false;
            _fxCoroutine = null;
        }

        private static bool WasKeyPressed(int numberKey)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                switch (numberKey)
                {
                    case 1:
                        if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame)
                            return true;
                        break;
                    case 2:
                        if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame)
                            return true;
                        break;
                    case 3:
                        if (Keyboard.current.digit3Key.wasPressedThisFrame || Keyboard.current.numpad3Key.wasPressedThisFrame)
                            return true;
                        break;
                    case 4:
                        if (Keyboard.current.digit4Key.wasPressedThisFrame || Keyboard.current.numpad4Key.wasPressedThisFrame)
                            return true;
                        break;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            switch (numberKey)
            {
                case 1:
                    if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) return true;
                    break;
                case 2:
                    if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) return true;
                    break;
                case 3:
                    if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) return true;
                    break;
                case 4:
                    if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) return true;
                    break;
            }
#endif
            return false;
        }
    }
}
