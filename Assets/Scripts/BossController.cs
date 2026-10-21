using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public enum PositionalRequirement
    {
        Flank_45_To_135, // 측면 베기 [1]: 기본 45도 ~ 135도
        Rear_135_To_180  // 후면 기습 [2]: 기본 135도 ~ 180도
    }

    public enum PositionalZone
    {
        Front, // 0 ~ 45도
        Flank, // 45 ~ 135도
        Rear   // 135 ~ 180도
    }

    public enum BossPhase
    {
        Phase1_100To50 = 1,     // 1페이즈 (HP 100% ~ 50%)
        GroggyTransition = 2,   // 50% 도달 시 5초간 무력화(그로기, 받는 피해 50% 증가)
        Phase2_50To0 = 3        // 2페이즈 (HP 50% ~ 0%, 도넛 장판 + 줄 가로채기 + 스테이지 심화 기믹)
    }

    public enum BossFSMState
    {
        IdleChasing,  // 어그로 1순위 추적(초당 90도 회전) 및 다음 스킬 대기
        CastingSkill, // 스킬 시전 중 (회전 완전 정지 & 시선 고정)
        Groggy,       // 50% 페이즈 전환 시 5초간 그로기 (받는 피해 +50%)
        Defeated      // 보스 HP 0 도달 시 쓰러짐 연출 상태 (1.5초 후 보상 카드 팝업)
    }

    public enum BossSkillId
    {
        CircleAoE_6m,              // [원형 장판] 보스 주변 반경 6m
        FrontConeSlash_120Deg,     // [전방 부채꼴 베기] 전방 120도 부채꼴
        SingleTankbuster,          // [단발 탱크버스터] 탱커 봇이 혼자 뎀감기 켜고 방어
        DonutAoE_4mTo12m,          // [도넛 장판] 내경 4m ~ 외경 12m (2페이즈 추가)
        DoubleTankbuster_Tether,   // [2연타 탱크버스터: 줄 가로채기] (2페이즈 핵심 기믹)
        PartySpread_4P,            // [Stage 2+] ① 4인 산개 (Spread) - 분홍색 원형 장판 3초 뒤 폭발 (겹치면 2배 피해)
        PartyStack_4P,             // [Stage 2+] ② 4인 쉐어 (Stack) - 노란색 화살표 쉐어 징표 4초 뒤 폭발 (1/N 피해 분담)
        Combo_DonutPlusSpread,     // [Stage 3] 복합 기믹 ① 도넛 장판 + 4인 산개 동시 발동
        Combo_KnockbackIntoStack   // [Stage 3] 복합 기믹 ② 외곽 넉백(밀쳐내기) 직후 4인 쉐어 발동
    }

    [Serializable]
    public class BossSkillEntry
    {
        public BossSkillId skillId;
        public string displayName;
        public int weight = 30;
        public float castDuration = 2.5f;
        public float internalCooldown = 8.0f;
        public bool phase2Only;
        public int minStage = 1;
        [HideInInspector] public float nextAvailableTime;
    }

    /// <summary>
    /// 보스 컨트롤러 (Step 1 ~ Step 6 통합):
    /// - Stage 1 (보라색, 1.0배, HP 3,000) -> Stage 2 (청록색, 1.2배, HP 6,000, 4인 산개/4인 쉐어) -> Stage 3 (적금색, 1.5배, HP 10,000, 시전속도 +25% & 복합 기믹)
    /// - 보스 처치(HP 0) 시 쓰러지는 연출 및 StageManager.NotifyBossDefeated() 호출 (중간 체력 카드 팝업 완전 제거)
    /// </summary>
    public class BossController : MonoBehaviour
    {
        [Header("Stage & Boss Identity (Step 6)")]
        public int currentStageNumber = 1;
        public string currentBossTitle = "STAGE 1 : 공허의 파수꾼";
        public float stageCastSpeedMultiplier = 1.0f; // Stage 3에서 1.25배 가속 (시전시간 * 0.8)

        [Header("Boss Health & Phase (Step 3 ~ Step 6)")]
        public float maxHp = 3000f;
        public float currentHp = 3000f;
        public BossPhase currentPhase = BossPhase.Phase1_100To50;
        public BossFSMState fsmState = BossFSMState.IdleChasing;

        [Tooltip("50% 도달 시 그로기 지속 시간 (5초)")]
        public float groggyDuration = 5.0f;

        [Tooltip("그로기 상태일 때 보스가 받는 피해 배율 (1.5 = +50% 증가)")]
        public float groggyDamageMultiplier = 1.5f;

        [Header("Positional Angle Bonus (Step 4 - [유령 걸음])")]
        [Tooltip("[유령 걸음] 카드 획득 시 측·후면 판정 각도가 15도 넓어집니다.")]
        public float positionalAngleBonus = 0f;

        [Header("Aggro & Smooth Rotation (Step 2)")]
        public Transform aggroTarget;
        public Transform targetPlayer;
        public float rotationSpeed = 90f;
        public bool enableRotation = true;

        [Header("FSM Skill Pattern Settings")]
        public float patternInterval = 2.0f;
        public float autoAttackInterval = 2.0f;
        public float autoAttackDamageToTank = 150f;
        public List<BossSkillEntry> skillTable = new List<BossSkillEntry>();

        [Header("Tether Intercept Settings (줄 가로채기)")]
        public float tetherInterceptRadius = 1.25f;

        [Header("Party Mechanic Settings (Step 6: 4인 산개 & 4인 쉐어)")]
        public float normalSpreadRadius = 2.6f;
        public float donutComboSpreadRadius = 1.65f;
        public float spreadBaseDamage = 240f;
        public float stackRadius = 3.2f;
        public float stackTotalDamageToSplit = 1120f; // 4인 쉐어 시 1인당 280 피해, 혼자 맞으면 1120 치명상!

        [Header("Top Screen Boss UI References")]
        public Image bossHpFillImage;
        public Text bossHpText;
        public Text bossPhaseText;
        public GameObject bossCastBarRoot;
        public Image bossCastFillImage;
        public Text bossCastText;
        public Text mechanicAlertBannerText;

        [Header("Popup & Visual References")]
        public float popupHeightOffset = 4.6f;
        public Color positionalCritColor = new Color(1f, 0.52f, 0.05f, 1f);
        public Color normalHitColor = Color.white;
        public Renderer bodyRenderer;
        public TargetRingRenderer targetRing;
        public StageManager stageManager;

        public bool IsCasting => fsmState == BossFSMState.CastingSkill;
        public bool IsGroggy => fsmState == BossFSMState.Groggy;
        public bool IsBossDefeatedCollapse => fsmState == BossFSMState.Defeated;
        public float HpRatio => maxHp > 0f ? Mathf.Clamp01(currentHp / maxHp) : 0f;
        public float DamageTakenMultiplier => IsGroggy ? groggyDamageMultiplier : 1.0f;
        public string CurrentCastSkillName => _currentCastSkillName;
        public float CastRemainingTime => _castRemainingTime;
        public float CastProgress => _castTotalDuration > 0.01f ? Mathf.Clamp01(1f - (_castRemainingTime / _castTotalDuration)) : 0f;
        public string AggroTargetName => aggroTarget != null ? aggroTarget.name : (targetPlayer != null ? targetPlayer.name : "없음");
        public AoETelegraph ActiveTelegraph => _activeTelegraph;
        public bool IsTetherActive => _isTetherActive;
        public bool IsTetherIntercepted => _isTetherInterceptedByPlayer;
        public bool IsSpreadMechanicActive => _isSpreadActive;
        public bool IsStackMechanicActive => _isStackActive;
        public float CurrentSpreadRadius => _currentSpreadRadius;

        public string LastHitMessage { get; private set; } = "대기 중 ([1] 측면 3단 콤보 / [2] 후면 3단 콤보)";
        public Color LastHitColor { get; private set; } = Color.white;
        public float LastHitAngle { get; private set; }

        private BossSkillEntry _activeSkill;
        private string _currentCastSkillName = "";
        private float _castRemainingTime;
        private float _castTotalDuration;
        private float _patternWaitTimer = 1.4f;
        private float _autoAttackTimer;
        private float _groggyRemainingTime;
        private bool _hasTriggeredGroggy;
        private bool _forceSignatureMechanicNext;

        private AoETelegraph _activeTelegraph;
        private LineRenderer _tetherLineRenderer;
        private bool _isTetherActive;
        private bool _isTetherInterceptedByPlayer;
        private Transform _tetherTankTarget;

        // Step 6: 4인 산개(Spread) 및 4인 쉐어(Stack) 활성 마커 리스트
        private bool _isSpreadActive;
        private float _currentSpreadRadius = 2.6f;
        private readonly List<AttachedPartyMechanicMarker> _activePartyMarkers = new List<AttachedPartyMechanicMarker>();
        private bool _isStackActive;
        private AttachedPartyMechanicMarker _activeStackMarker;

        private Color _stagePhase1BodyColor = new Color(0.50f, 0.16f, 0.76f, 1f);
        private Color _stagePhase2BodyColor = new Color(0.62f, 0.12f, 0.48f, 1f);
        private Vector3 _baseBodyLocalScale = new Vector3(2.2f, 1.8f, 2.2f);
        private Vector3 _baseBodyLocalPos = new Vector3(0f, 1.8f, 0f);
        private Vector3 _baseVisorLocalScale = new Vector3(1.1f, 0.35f, 0.45f);
        private Vector3 _baseVisorLocalPos = new Vector3(0f, 2.6f, 1.05f);
        private Coroutine _hitFlashCoroutine;
        private Coroutine _collapseCoroutine;
        private float _alertBannerClearTimer;

        private Transform _castBannerRoot;
        private TextMesh _castBannerText;

        private void Awake()
        {
            InitializeDefaultSkillTable();
        }

        private void Start()
        {
            if (stageManager == null)
            {
                stageManager = FindFirstObjectByType<StageManager>();
            }

            if (targetPlayer == null)
            {
                PlayerController player = FindFirstObjectByType<PlayerController>();
                if (player != null)
                {
                    targetPlayer = player.transform;
                }
            }

            if (aggroTarget == null)
            {
                PartyBotAI tank = FindBotByRole(PartyBotRole.Tank);
                aggroTarget = tank != null ? tank.transform : targetPlayer;
            }

            if (bodyRenderer == null)
            {
                Transform bodyTr = GetBossBodyTransform();
                if (bodyTr != null)
                {
                    bodyRenderer = bodyTr.GetComponent<Renderer>();
                }
                else
                {
                    bodyRenderer = GetComponentInChildren<MeshRenderer>();
                }
            }

            if (targetRing == null)
            {
                targetRing = GetComponentInChildren<TargetRingRenderer>();
            }

            CreateTetherLineRenderer();
            CreateCastBanner();
            EnsureFillBarSprites();

            if (stageManager == null)
            {
                SetupBossForStage(1);
            }
            UpdateTopBossUI();
        }

        private void EnsureFillBarSprites()
        {
            Sprite whiteSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f)
            );

            if (bossHpFillImage != null)
            {
                if (bossHpFillImage.sprite == null) bossHpFillImage.sprite = whiteSprite;
                bossHpFillImage.type = Image.Type.Filled;
                bossHpFillImage.fillMethod = Image.FillMethod.Horizontal;
                bossHpFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            }

            if (bossCastFillImage != null)
            {
                if (bossCastFillImage.sprite == null) bossCastFillImage.sprite = whiteSprite;
                bossCastFillImage.type = Image.Type.Filled;
                bossCastFillImage.fillMethod = Image.FillMethod.Horizontal;
                bossCastFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
        }

        private void InitializeDefaultSkillTable()
        {
            skillTable = new List<BossSkillEntry>
            {
                // Stage 1+ 기본 패턴
                new BossSkillEntry
                {
                    skillId = BossSkillId.CircleAoE_6m,
                    displayName = "원형 장판 (반경 6m 폭발)",
                    weight = 35,
                    castDuration = 2.8f,
                    internalCooldown = 7.0f,
                    phase2Only = false,
                    minStage = 1
                },
                new BossSkillEntry
                {
                    skillId = BossSkillId.FrontConeSlash_120Deg,
                    displayName = "전방 120° 부채꼴 베기",
                    weight = 28,
                    castDuration = 2.4f,
                    internalCooldown = 6.5f,
                    phase2Only = false,
                    minStage = 1
                },
                new BossSkillEntry
                {
                    skillId = BossSkillId.SingleTankbuster,
                    displayName = "단발 탱크버스터 (탱커 뎀감기 방어)",
                    weight = 26,
                    castDuration = 2.5f,
                    internalCooldown = 8.5f,
                    phase2Only = false,
                    minStage = 1
                },
                new BossSkillEntry
                {
                    skillId = BossSkillId.DonutAoE_4mTo12m,
                    displayName = "도넛 장판 (내경 4m 안전지대 ~ 외경 12m)",
                    weight = 35,
                    castDuration = 3.0f,
                    internalCooldown = 8.0f,
                    phase2Only = true,
                    minStage = 1
                },
                new BossSkillEntry
                {
                    skillId = BossSkillId.DoubleTankbuster_Tether,
                    displayName = "2연타 탱크버스터 [2타: 줄 가로채기!]",
                    weight = 45,
                    castDuration = 4.0f,
                    internalCooldown = 11.0f,
                    phase2Only = true,
                    minStage = 1
                },

                // Stage 2+ 중급 파티 협동 기믹 2종 (① 4인 산개, ② 4인 쉐어)
                new BossSkillEntry
                {
                    skillId = BossSkillId.PartySpread_4P,
                    displayName = "4인 산개 (Spread - 겹치면 2배 피해!)",
                    weight = 45,
                    castDuration = 3.0f,
                    internalCooldown = 9.0f,
                    phase2Only = false,
                    minStage = 2
                },
                new BossSkillEntry
                {
                    skillId = BossSkillId.PartyStack_4P,
                    displayName = "4인 쉐어 (Stack - 4인 집결 시 1/4 피해 분담!)",
                    weight = 45,
                    castDuration = 4.0f,
                    internalCooldown = 10.0f,
                    phase2Only = false,
                    minStage = 2
                },

                // Stage 3 최종 보스 광폭화 복합 기믹 2종
                new BossSkillEntry
                {
                    skillId = BossSkillId.Combo_DonutPlusSpread,
                    displayName = "[복합 기믹] 도넛 장판 + 4인 산개 동시 발동!",
                    weight = 55,
                    castDuration = 3.6f,
                    internalCooldown = 10.0f,
                    phase2Only = false,
                    minStage = 3
                },
                new BossSkillEntry
                {
                    skillId = BossSkillId.Combo_KnockbackIntoStack,
                    displayName = "[복합 기믹] 외곽 넉백(밀쳐내기) 직후 4인 쉐어!",
                    weight = 55,
                    castDuration = 4.2f,
                    internalCooldown = 11.0f,
                    phase2Only = false,
                    minStage = 3
                }
            };
        }

        /// <summary>
        /// Step 6: 스테이지(Stage 1 ~ 3)에 맞춰 보스의 색상, 크기, 최대 체력, 시전 속도 및 패턴 풀을 세팅합니다.
        /// </summary>
        public void SetupBossForStage(int stageNumber)
        {
            CleanupActiveTelegraphAndTether();
            if (_collapseCoroutine != null)
            {
                StopCoroutine(_collapseCoroutine);
                _collapseCoroutine = null;
            }

            currentStageNumber = Mathf.Clamp(stageNumber, 1, 3);
            InitializeDefaultSkillTable();

            float scaleMultiplier = 1.0f;

            switch (currentStageNumber)
            {
                case 1:
                    // Stage 1 보스 (보라색, 크기 1.0배, HP 3,000 - 입문용)
                    currentBossTitle = "STAGE 1 : 공허의 파수꾼 (입문)";
                    maxHp = 3000f;
                    scaleMultiplier = 1.0f;
                    stageCastSpeedMultiplier = 1.0f;
                    _stagePhase1BodyColor = new Color(0.50f, 0.16f, 0.76f, 1f);
                    _stagePhase2BodyColor = new Color(0.62f, 0.12f, 0.48f, 1f);
                    break;

                case 2:
                    // Stage 2 보스 (청록색, 크기 1.2배, HP 6,000 - 중급 기믹: 4인 산개 & 4인 쉐어 추가)
                    currentBossTitle = "STAGE 2 : 심연의 집행자 (중급 - 산개 & 쉐어)";
                    maxHp = 6000f;
                    scaleMultiplier = 1.2f;
                    stageCastSpeedMultiplier = 1.0f;
                    _stagePhase1BodyColor = new Color(0.06f, 0.72f, 0.78f, 1f);
                    _stagePhase2BodyColor = new Color(0.08f, 0.88f, 0.65f, 1f);
                    break;

                case 3:
                default:
                    // Stage 3 최종 보스 (적금색, 크기 1.5배, HP 10,000 - 시전 25% 가속 & 복합 기믹)
                    currentBossTitle = "STAGE 3 : 종말의 황제 (최종 보스 - 복합 기믹)";
                    maxHp = 10000f;
                    scaleMultiplier = 1.5f;
                    stageCastSpeedMultiplier = 0.75f; // 시전 시간 25% 단축!
                    _stagePhase1BodyColor = new Color(0.88f, 0.15f, 0.12f, 1f);
                    _stagePhase2BodyColor = new Color(1.0f, 0.68f, 0.12f, 1f);
                    break;
            }

            currentHp = maxHp;
            currentPhase = BossPhase.Phase1_100To50;
            fsmState = BossFSMState.IdleChasing;
            _hasTriggeredGroggy = false;
            _forceSignatureMechanicNext = currentStageNumber >= 2;
            _patternWaitTimer = 1.6f;

            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;

            // 보스 바디 크기 및 위치 복구/적용
            Transform bodyTr = GetBossBodyTransform();
            if (bodyTr != null)
            {
                bodyTr.localRotation = Quaternion.identity;
                bodyTr.localScale = _baseBodyLocalScale * scaleMultiplier;
                bodyTr.localPosition = new Vector3(0f, _baseBodyLocalPos.y * scaleMultiplier, 0f);
                if (bodyRenderer == null)
                {
                    bodyRenderer = bodyTr.GetComponent<Renderer>();
                }
            }

            Transform visorTr = transform.Find("Boss_FrontVisor");
            if (visorTr != null)
            {
                visorTr.localRotation = Quaternion.identity;
                visorTr.localScale = _baseVisorLocalScale * scaleMultiplier;
                visorTr.localPosition = _baseVisorLocalPos * scaleMultiplier;
                Renderer visorRenderer = visorTr.GetComponent<Renderer>();
                if (visorRenderer != null)
                {
                    Color visorColor = currentStageNumber == 3
                        ? new Color(1.0f, 0.85f, 0.15f, 1f) // Stage 3 적금색 보스는 황금빛 바이저
                        : new Color(1.0f, 0.22f, 0.22f, 1f);
                    SetRendererColor(visorRenderer, visorColor);
                }
            }

            SetRendererColor(bodyRenderer, _stagePhase1BodyColor);
            popupHeightOffset = 4.2f * scaleMultiplier + 0.5f;

            if (_castBannerRoot != null)
            {
                _castBannerRoot.localPosition = new Vector3(0f, 3.6f * scaleMultiplier + 0.4f, 0f);
            }

            if (targetRing != null)
            {
                targetRing.outerRadius = 3.2f * Mathf.Lerp(1f, scaleMultiplier, 0.7f);
                targetRing.innerRadius = 1.4f * Mathf.Lerp(1f, scaleMultiplier, 0.7f);
                targetRing.BuildTargetRing();
            }

            PartyBotAI tank = FindBotByRole(PartyBotRole.Tank);
            if (tank != null)
            {
                aggroTarget = tank.transform;
            }

            string stageIntroMsg = currentStageNumber switch
            {
                1 => "★ STAGE 1 전투 시작! (HP 3,000) 보스를 처치하면 클리어 보상 로그라이크 카드 3택이 열립니다! ★",
                2 => "★ STAGE 2 보스 등장! (청록색 1.2배 / HP 6,000) 신규 파티 기믹 [4인 산개] & [4인 쉐어]가 추가됩니다! ★",
                3 => "★ FINAL STAGE 3 최종 보스 등장!! (적금색 1.5배 / HP 10,000) 시전 속도 +25% 가속 & [도넛+산개 / 넉백+쉐어] 복합 기믹 발동! ★",
                _ => ""
            };

            SetMechanicAlert(stageIntroMsg, new Color(1f, 0.9f, 0.3f, 1f), 4.5f);
            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * popupHeightOffset,
                $"{currentBossTitle} 등장!! (HP {Mathf.RoundToInt(maxHp)})",
                new Color(1f, 0.88f, 0.25f, 1f),
                true
            );

            UpdateTopBossUI();
        }

        /// <summary>
        /// 보스 HP가 0이 되었을 때 쓰러지는 3D 연출을 재생합니다.
        /// </summary>
        public void PlayBossCollapseAnimation()
        {
            CleanupActiveTelegraphAndTether();
            fsmState = BossFSMState.Defeated;
            _currentCastSkillName = "BOSS CLEAR!!";

            if (_collapseCoroutine != null)
            {
                StopCoroutine(_collapseCoroutine);
            }
            _collapseCoroutine = StartCoroutine(BossCollapseRoutine());
        }

        private Transform GetBossBodyTransform()
        {
            Transform bodyTr = transform.Find("Boss_Body_Cylinder");
            if (bodyTr == null)
            {
                bodyTr = transform.Find("Boss_Body");
            }
            if (bodyTr == null && bodyRenderer != null)
            {
                bodyTr = bodyRenderer.transform;
            }
            return bodyTr;
        }

        private IEnumerator BossCollapseRoutine()
        {
            Transform bodyTr = GetBossBodyTransform();
            SetRendererColor(bodyRenderer, new Color(0.35f, 0.35f, 0.4f, 1f));

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * (popupHeightOffset + 0.5f),
                $"★ STAGE {currentStageNumber} BOSS CLEAR!! ★",
                new Color(1f, 0.9f, 0.2f, 1f),
                true
            );

            if (bodyTr != null)
            {
                Quaternion startRot = bodyTr.localRotation;
                Quaternion endRot = Quaternion.Euler(78f, 0f, 25f);
                Vector3 startPos = bodyTr.localPosition;
                Vector3 endPos = new Vector3(startPos.x, 0.75f, startPos.z);

                float elapsed = 0f;
                const float duration = 0.65f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    bodyTr.localRotation = Quaternion.Slerp(startRot, endRot, t);
                    bodyTr.localPosition = Vector3.Lerp(startPos, endPos, t);
                    yield return null;
                }
            }
        }

        private void Update()
        {
            if (Time.timeScale <= 0.001f)
            {
                return;
            }

            HandleDebugShortcuts();
            UpdateAlertBannerTimer();

            switch (fsmState)
            {
                case BossFSMState.IdleChasing:
                    RotateTowardsAggroTargetXZ();
                    UpdateAutoAttack();
                    UpdatePatternSelection();
                    break;

                case BossFSMState.CastingSkill:
                    UpdateSkillCasting();
                    break;

                case BossFSMState.Groggy:
                    UpdateGroggyState();
                    break;

                case BossFSMState.Defeated:
                    break;
            }

            UpdateCastBanner();
            UpdateTopBossUI();
        }

        private void HandleDebugShortcuts()
        {
            if (fsmState == BossFSMState.Defeated)
            {
                return;
            }

            bool toggleRot = false;
            bool instantKillBoss = false;
            bool forceTether = false;
            bool forceSpread = false;
            bool forceStack = false;
            bool forceStage3Combo = false;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame) toggleRot = true;
                if (Keyboard.current.f1Key.wasPressedThisFrame) instantKillBoss = true;
                if (Keyboard.current.f2Key.wasPressedThisFrame) forceTether = true;
                if (Keyboard.current.f3Key.wasPressedThisFrame) forceSpread = true;
                if (Keyboard.current.f4Key.wasPressedThisFrame) forceStack = true;
                if (Keyboard.current.f5Key.wasPressedThisFrame) forceStage3Combo = true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.R)) toggleRot = true;
            if (Input.GetKeyDown(KeyCode.F1)) instantKillBoss = true;
            if (Input.GetKeyDown(KeyCode.F2)) forceTether = true;
            if (Input.GetKeyDown(KeyCode.F3)) forceSpread = true;
            if (Input.GetKeyDown(KeyCode.F4)) forceStack = true;
            if (Input.GetKeyDown(KeyCode.F5)) forceStage3Combo = true;
#endif

            if (toggleRot)
            {
                enableRotation = !enableRotation;
            }

            // [F1] 단축키: 현재 스테이지 보스 즉시 처치 (HP 0 -> BOSS CLEAR 연출 -> 1.5초 후 카드 선택 -> 다음 스테이지!)
            if (instantKillBoss)
            {
                ApplyBossHpDamage(currentHp + 99999f);
                return;
            }

            // [F2] 단축키: 2연타 탱크버스터 (줄 가로채기) 즉시 시전
            if (forceTether)
            {
                CleanupActiveTelegraphAndTether();
                BossSkillEntry skill = skillTable.Find(s => s.skillId == BossSkillId.DoubleTankbuster_Tether);
                if (skill != null) BeginSkillCast(skill);
            }

            // [F3] 단축키: [4인 산개 (Spread)] 즉시 시전
            if (forceSpread)
            {
                CleanupActiveTelegraphAndTether();
                BossSkillEntry skill = skillTable.Find(s => s.skillId == BossSkillId.PartySpread_4P);
                if (skill != null) BeginSkillCast(skill);
            }

            // [F4] 단축키: [4인 쉐어 (Stack)] 즉시 시전
            if (forceStack)
            {
                CleanupActiveTelegraphAndTether();
                BossSkillEntry skill = skillTable.Find(s => s.skillId == BossSkillId.PartyStack_4P);
                if (skill != null) BeginSkillCast(skill);
            }

            // [F5] 단축키: Stage 3 복합 기믹 ([도넛+4인 산개] / [외곽 넉백+4인 쉐어] 교차 테스트)
            if (forceStage3Combo)
            {
                CleanupActiveTelegraphAndTether();
                BossSkillId targetComboId = UnityEngine.Random.value < 0.5f
                    ? BossSkillId.Combo_DonutPlusSpread
                    : BossSkillId.Combo_KnockbackIntoStack;
                BossSkillEntry skill = skillTable.Find(s => s.skillId == targetComboId);
                if (skill != null) BeginSkillCast(skill);
            }
        }

        public void SetAggroTarget(Transform newAggroTarget)
        {
            if (newAggroTarget != null)
            {
                aggroTarget = newAggroTarget;
            }
        }

        public void ShowExternalAlert(string message, Color color, float duration)
        {
            SetMechanicAlert(message, color, duration);
        }

        private void RotateTowardsAggroTargetXZ()
        {
            if (!enableRotation || fsmState != BossFSMState.IdleChasing)
            {
                return;
            }

            Transform currentTarget = aggroTarget != null ? aggroTarget : targetPlayer;
            if (currentTarget == null)
            {
                return;
            }

            Vector3 toTargetXZ = new Vector3(
                currentTarget.position.x - transform.position.x,
                0f,
                currentTarget.position.z - transform.position.z
            );

            if (toTargetXZ.sqrMagnitude < 0.001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(toTargetXZ.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        private void UpdateAutoAttack()
        {
            _autoAttackTimer += Time.deltaTime;
            if (_autoAttackTimer >= autoAttackInterval)
            {
                _autoAttackTimer = 0f;
                PartyBotAI tank = FindBotByRole(PartyBotRole.Tank);
                if (tank != null && !tank.isDead)
                {
                    tank.TakeDamage(autoAttackDamageToTank);
                }
                else if (targetPlayer != null)
                {
                    PlayerController pc = targetPlayer.GetComponent<PlayerController>();
                    if (pc != null)
                    {
                        pc.TakeDamage(autoAttackDamageToTank);
                    }
                }
            }
        }

        private void UpdatePatternSelection()
        {
            _patternWaitTimer -= Time.deltaTime;
            if (_patternWaitTimer > 0f)
            {
                return;
            }

            BossSkillEntry chosenSkill = SelectNextWeightedSkill();
            if (chosenSkill != null)
            {
                BeginSkillCast(chosenSkill);
            }
        }

        private BossSkillEntry SelectNextWeightedSkill()
        {
            // Stage 2 시작 시 4인 산개/쉐어를, Stage 3 시작 시 복합 기믹을 빠르게 한 번 보여주어 체감도를 높임
            if (_forceSignatureMechanicNext)
            {
                _forceSignatureMechanicNext = false;
                if (currentStageNumber == 2)
                {
                    BossSkillId sigId = UnityEngine.Random.value < 0.5f ? BossSkillId.PartySpread_4P : BossSkillId.PartyStack_4P;
                    BossSkillEntry sig = skillTable.Find(s => s.skillId == sigId);
                    if (sig != null) return sig;
                }
                else if (currentStageNumber >= 3)
                {
                    BossSkillId sigId = UnityEngine.Random.value < 0.5f ? BossSkillId.Combo_DonutPlusSpread : BossSkillId.Combo_KnockbackIntoStack;
                    BossSkillEntry sig = skillTable.Find(s => s.skillId == sigId);
                    if (sig != null) return sig;
                }
                else if (currentPhase == BossPhase.Phase2_50To0)
                {
                    BossSkillEntry tether = skillTable.Find(s => s.skillId == BossSkillId.DoubleTankbuster_Tether);
                    if (tether != null) return tether;
                }
            }

            float now = Time.time;
            List<BossSkillEntry> candidates = new List<BossSkillEntry>();
            int totalWeight = 0;

            foreach (BossSkillEntry entry in skillTable)
            {
                if (entry.minStage > currentStageNumber)
                {
                    continue;
                }

                if (entry.phase2Only && currentPhase != BossPhase.Phase2_50To0)
                {
                    continue;
                }

                if (entry.skillId == BossSkillId.SingleTankbuster || entry.skillId == BossSkillId.DoubleTankbuster_Tether)
                {
                    PartyBotAI tank = FindBotByRole(PartyBotRole.Tank);
                    if (tank == null || tank.isDead)
                    {
                        continue;
                    }
                }

                if (now >= entry.nextAvailableTime)
                {
                    candidates.Add(entry);
                    totalWeight += Mathf.Max(1, entry.weight);
                }
            }

            if (candidates.Count == 0)
            {
                BossSkillEntry earliest = null;
                float earliestTime = float.MaxValue;
                foreach (BossSkillEntry entry in skillTable)
                {
                    if (entry.minStage > currentStageNumber) continue;
                    if (entry.phase2Only && currentPhase != BossPhase.Phase2_50To0) continue;

                    if (entry.nextAvailableTime < earliestTime)
                    {
                        earliestTime = entry.nextAvailableTime;
                        earliest = entry;
                    }
                }
                return earliest;
            }

            int roll = UnityEngine.Random.Range(0, totalWeight);
            int accum = 0;
            foreach (BossSkillEntry candidate in candidates)
            {
                accum += Mathf.Max(1, candidate.weight);
                if (roll < accum)
                {
                    return candidate;
                }
            }

            return candidates[candidates.Count - 1];
        }

        private void BeginSkillCast(BossSkillEntry skill)
        {
            CleanupActiveTelegraphAndTether();

            _activeSkill = skill;
            _activeSkill.nextAvailableTime = Time.time + (_activeSkill.internalCooldown * stageCastSpeedMultiplier);

            fsmState = BossFSMState.CastingSkill;
            _currentCastSkillName = skill.displayName;

            // Stage 3에서는 시전 속도가 25% 빨라짐 (단, 4인 산개 3초 / 4인 쉐어 4초도 Stage 3에서는 긴박하게 가속)
            float effectiveDuration = skill.castDuration;
            if (currentStageNumber >= 3 &&
                skill.skillId != BossSkillId.PartySpread_4P &&
                skill.skillId != BossSkillId.PartyStack_4P)
            {
                effectiveDuration = skill.castDuration * stageCastSpeedMultiplier;
            }

            _castTotalDuration = Mathf.Max(0.4f, effectiveDuration);
            _castRemainingTime = _castTotalDuration;

            PartyBotAI tank = FindBotByRole(PartyBotRole.Tank);

            switch (skill.skillId)
            {
                case BossSkillId.CircleAoE_6m:
                    _activeTelegraph = AoETelegraph.Spawn(
                        AoETelegraphType.Circle_Radius6,
                        transform.position,
                        transform.forward,
                        _castTotalDuration,
                        0f,
                        6f,
                        360f
                    );
                    SetMechanicAlert("보스 시전: [원형 장판] 반경 6m 밖으로 피하세요! (80%까지 못 피하면 힐러가 '구출' 발동!)", new Color(1f, 0.45f, 0.3f, 1f), _castTotalDuration);
                    break;

                case BossSkillId.DonutAoE_4mTo12m:
                    _activeTelegraph = AoETelegraph.Spawn(
                        AoETelegraphType.Donut_Inner4_Outer12,
                        transform.position,
                        transform.forward,
                        _castTotalDuration,
                        4f,
                        12f,
                        360f
                    );
                    SetMechanicAlert("보스 시전: [도넛 장판] 보스 발밑(반경 4m 이내 안전지대)으로 모이세요! (80% 시 힐러 구출!)", new Color(1f, 0.75f, 0.25f, 1f), _castTotalDuration);
                    break;

                case BossSkillId.FrontConeSlash_120Deg:
                    _activeTelegraph = AoETelegraph.Spawn(
                        AoETelegraphType.FrontCone_120Deg,
                        transform.position,
                        transform.forward,
                        _castTotalDuration,
                        0f,
                        9.5f,
                        120f
                    );
                    SetMechanicAlert("보스 시전: [전방 120° 부채꼴 베기] 보스 시선이 고정됩니다! 측·후면은 안전합니다!", new Color(1f, 0.55f, 0.2f, 1f), _castTotalDuration);
                    break;

                case BossSkillId.SingleTankbuster:
                    if (tank != null && !tank.isDead)
                    {
                        tank.ActivateTankDefensive(_castTotalDuration + 0.6f);
                    }
                    SetMechanicAlert("보스 시전: [단발 탱크버스터] → 탱커 봇이 스스로 뎀감기를 켜고 방어합니다!", new Color(0.35f, 0.9f, 1f, 1f), _castTotalDuration);
                    break;

                case BossSkillId.DoubleTankbuster_Tether:
                    StartDoubleTankbusterTether(tank, _castTotalDuration);
                    break;

                case BossSkillId.PartySpread_4P:
                    StartPartySpreadMechanic(normalSpreadRadius, _castTotalDuration, isDonutCombo: false);
                    break;

                case BossSkillId.PartyStack_4P:
                    StartPartyStackMechanic(_castTotalDuration, isKnockbackCombo: false);
                    break;

                case BossSkillId.Combo_DonutPlusSpread:
                    // Stage 3 복합 기믹 ①: 도넛 장판(내경 4m 안전지대) + 4인 산개 동시 발동!
                    _activeTelegraph = AoETelegraph.Spawn(
                        AoETelegraphType.Donut_Inner4_Outer12,
                        transform.position,
                        transform.forward,
                        _castTotalDuration,
                        4f,
                        12f,
                        360f
                    );
                    StartPartySpreadMechanic(donutComboSpreadRadius, _castTotalDuration, isDonutCombo: true);
                    break;

                case BossSkillId.Combo_KnockbackIntoStack:
                    // Stage 3 복합 기믹 ②: 외곽 넉백(밀쳐내기) 직후 4인 쉐어 발동!
                    TriggerArenaKnockbackToAllParty(8.5f, 0.45f);
                    StartPartyStackMechanic(_castTotalDuration, isKnockbackCombo: true);
                    break;
            }
        }

        private void StartPartySpreadMechanic(float spreadRadius, float duration, bool isDonutCombo)
        {
            _isSpreadActive = true;
            _currentSpreadRadius = spreadRadius;

            List<Transform> partyMembers = GetLivingPartyMemberTransforms();
            foreach (Transform member in partyMembers)
            {
                AttachedPartyMechanicMarker marker = AttachedPartyMechanicMarker.SpawnSpreadMarker(member, spreadRadius, duration);
                if (marker != null)
                {
                    _activePartyMarkers.Add(marker);
                }
            }

            if (isDonutCombo)
            {
                SetMechanicAlert(
                    "★ Stage 3 복합 기믹: [도넛 장판 + 4인 산개 동시 발동]! 도넛 안쪽(4m 이내) 남쪽 자리로 들어가 봇들과 겹치지 않게 산개하세요!",
                    new Color(1f, 0.35f, 0.85f, 1f),
                    duration
                );
            }
            else
            {
                SetMechanicAlert(
                    "파티 기믹: [4인 산개 (Spread)]! 3초 뒤 각자 발밑의 분홍색 원이 폭발합니다! (원이 겹치면 2배 피해 — 서로 멀리 흩어지세요!)",
                    new Color(1f, 0.4f, 0.85f, 1f),
                    duration
                );
            }
        }

        private void StartPartyStackMechanic(float duration, bool isKnockbackCombo)
        {
            _isStackActive = true;
            if (targetPlayer != null)
            {
                _activeStackMarker = AttachedPartyMechanicMarker.SpawnStackMarker(targetPlayer, stackRadius, duration);
                if (_activeStackMarker != null)
                {
                    _activePartyMarkers.Add(_activeStackMarker);
                }
            }

            if (isKnockbackCombo)
            {
                SetMechanicAlert(
                    "★ Stage 3 복합 기믹: [외곽 넉백 직후 4인 쉐어(Stack)]! 외곽으로 밀려난 AI 파티원들과 함께 모여 1/4로 피해를 나누어 받으세요!",
                    new Color(1f, 0.88f, 0.2f, 1f),
                    duration
                );
            }
            else
            {
                SetMechanicAlert(
                    "파티 기믹: [4인 쉐어 (Stack)]! 내 캐릭터에 노란 쉐어 징표가 생성되었습니다! AI 봇들이 내 위치로 달려와 뭉칩니다 (4인 집결 시 1/4 피해)!",
                    new Color(1f, 0.88f, 0.2f, 1f),
                    duration
                );
            }
        }

        private void TriggerArenaKnockbackToAllParty(float knockbackDistance, float duration)
        {
            Vector3 bossOriginXZ = transform.position;

            if (targetPlayer != null)
            {
                PlayerController pc = targetPlayer.GetComponent<PlayerController>();
                if (pc != null)
                {
                    pc.StartKnockbackXZ(bossOriginXZ, knockbackDistance, duration);
                }
            }

            PartyBotAI[] bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            foreach (PartyBotAI bot in bots)
            {
                if (bot != null && !bot.isDead)
                {
                    bot.StartKnockbackXZ(bossOriginXZ, knockbackDistance, duration);
                }
            }

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * (popupHeightOffset + 0.4f),
                "충격파 외곽 넉백!! → 즉시 4인 쉐어 집결!",
                new Color(1f, 0.65f, 0.15f, 1f),
                true
            );
        }

        private List<Transform> GetLivingPartyMemberTransforms()
        {
            List<Transform> list = new List<Transform>(4);
            if (targetPlayer != null)
            {
                list.Add(targetPlayer);
            }

            PartyBotAI[] bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            foreach (PartyBotAI bot in bots)
            {
                if (bot != null && !bot.isDead)
                {
                    list.Add(bot.transform);
                }
            }
            return list;
        }

        private void StartDoubleTankbusterTether(PartyBotAI tank, float duration)
        {
            if (tank != null && !tank.isDead)
            {
                tank.TakeDamage(550f, false);
                FloatingTextPopup.Spawn(
                    tank.transform.position + Vector3.up * 2.5f,
                    "탱크버스터 1타 피격! (2타 위험!)",
                    new Color(1f, 0.25f, 0.25f, 1f),
                    true
                );
                _tetherTankTarget = tank.transform;
            }
            else
            {
                _tetherTankTarget = aggroTarget != null ? aggroTarget : targetPlayer;
            }

            _isTetherActive = true;
            _isTetherInterceptedByPlayer = false;
            _tetherLineRenderer.enabled = true;

            SetMechanicAlert(
                "긴급 기믹: [2연타 탱크버스터] 보스와 탱커 사이의 빨간 줄을 몸으로 지나가 가로채세요!",
                new Color(1f, 0.25f, 0.25f, 1f),
                duration
            );
        }

        private void UpdateSkillCasting()
        {
            if (_isTetherActive)
            {
                UpdateTetherInterceptMechanic();
            }

            _castRemainingTime -= Time.deltaTime;
            if (_castRemainingTime <= 0f)
            {
                _castRemainingTime = 0f;
                ResolveCompletedSkillCast();
            }
        }

        private void UpdateTetherInterceptMechanic()
        {
            if (_tetherLineRenderer == null || _tetherTankTarget == null)
            {
                return;
            }

            Vector3 bossPoint = new Vector3(transform.position.x, 1.15f, transform.position.z);

            if (!_isTetherInterceptedByPlayer && targetPlayer != null)
            {
                Vector2 aXZ = new Vector2(transform.position.x, transform.position.z);
                Vector2 bXZ = new Vector2(_tetherTankTarget.position.x, _tetherTankTarget.position.z);
                Vector2 pXZ = new Vector2(targetPlayer.position.x, targetPlayer.position.z);

                float distToSegment = DistancePointToSegmentXZ(pXZ, aXZ, bXZ, out float tOnSegment);

                if (distToSegment <= tetherInterceptRadius && tOnSegment >= 0.05f && tOnSegment <= 0.98f)
                {
                    _isTetherInterceptedByPlayer = true;

                    SetMechanicAlert(
                        "줄 가로채기 성공! [3]번 생존기를 켜세요!",
                        new Color(0.2f, 1f, 0.45f, 1f),
                        _castRemainingTime + 1.5f
                    );

                    FloatingTextPopup.Spawn(
                        targetPlayer.position + Vector3.up * 2.5f,
                        "줄 가로채기 성공! [3]번 생존기를 켜세요!",
                        new Color(0.2f, 1f, 0.45f, 1f),
                        true
                    );
                }
            }

            Transform currentEndTarget = (_isTetherInterceptedByPlayer && targetPlayer != null)
                ? targetPlayer
                : _tetherTankTarget;

            Vector3 targetPoint = new Vector3(currentEndTarget.position.x, 1.05f, currentEndTarget.position.z);
            _tetherLineRenderer.SetPosition(0, bossPoint);
            _tetherLineRenderer.SetPosition(1, targetPoint);

            float pulse = 0.26f + Mathf.Sin(Time.time * 18f) * 0.05f;
            _tetherLineRenderer.startWidth = pulse;
            _tetherLineRenderer.endWidth = pulse * 0.85f;

            if (_isTetherInterceptedByPlayer)
            {
                Color greenTether = new Color(0.15f, 1.0f, 0.35f, 0.98f);
                _tetherLineRenderer.startColor = greenTether;
                _tetherLineRenderer.endColor = greenTether;
            }
            else
            {
                Color redLaser = new Color(1.0f, 0.12f, 0.12f, 0.98f);
                _tetherLineRenderer.startColor = redLaser;
                _tetherLineRenderer.endColor = new Color(1.0f, 0.35f, 0.15f, 0.98f);
            }
        }

        private static float DistancePointToSegmentXZ(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f)
            {
                t = 0f;
                return Vector2.Distance(p, a);
            }

            t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 projection = a + t * ab;
            return Vector2.Distance(p, projection);
        }

        private void ResolveCompletedSkillCast()
        {
            BossSkillEntry completedSkill = _activeSkill;
            AoETelegraph finishedTelegraph = _activeTelegraph;
            bool wasTetherIntercepted = _isTetherInterceptedByPlayer;
            float resolvedSpreadRadius = _currentSpreadRadius;

            _activeSkill = null;
            _activeTelegraph = null;
            _isTetherActive = false;
            _isSpreadActive = false;
            _isStackActive = false;
            _activeStackMarker = null;

            if (_tetherLineRenderer != null)
            {
                _tetherLineRenderer.enabled = false;
            }

            ClearActivePartyMarkers();

            fsmState = BossFSMState.IdleChasing;
            _patternWaitTimer = patternInterval * stageCastSpeedMultiplier;

            if (completedSkill == null)
            {
                if (finishedTelegraph != null) Destroy(finishedTelegraph.gameObject);
                return;
            }

            PartyBotAI tank = FindBotByRole(PartyBotRole.Tank);
            PlayerController player = targetPlayer != null ? targetPlayer.GetComponent<PlayerController>() : null;

            switch (completedSkill.skillId)
            {
                case BossSkillId.CircleAoE_6m:
                case BossSkillId.DonutAoE_4mTo12m:
                case BossSkillId.FrontConeSlash_120Deg:
                    ResolveTelegraphAoEDamage(finishedTelegraph, completedSkill.displayName, player, tank);
                    break;

                case BossSkillId.SingleTankbuster:
                    if (tank != null && !tank.isDead)
                    {
                        tank.TakeDamage(680f);
                    }
                    break;

                case BossSkillId.DoubleTankbuster_Tether:
                    ResolveDoubleTankbusterTether(wasTetherIntercepted, player, tank);
                    break;

                case BossSkillId.PartySpread_4P:
                    ResolvePartySpreadExplosion(resolvedSpreadRadius, player);
                    break;

                case BossSkillId.PartyStack_4P:
                case BossSkillId.Combo_KnockbackIntoStack:
                    ResolvePartyStackExplosion(player);
                    break;

                case BossSkillId.Combo_DonutPlusSpread:
                    ResolveTelegraphAoEDamage(finishedTelegraph, "도넛 장판", player, tank);
                    ResolvePartySpreadExplosion(resolvedSpreadRadius, player);
                    break;
            }

            if (finishedTelegraph != null)
            {
                Destroy(finishedTelegraph.gameObject);
            }
        }

        private void ResolveTelegraphAoEDamage(AoETelegraph telegraph, string skillLabel, PlayerController player, PartyBotAI tank)
        {
            if (telegraph == null)
            {
                return;
            }

            if (player != null && telegraph.IsPointInsideXZ(player.transform.position))
            {
                player.TakeDamage(320f);
                FloatingTextPopup.Spawn(
                    player.transform.position + Vector3.up * 2.5f,
                    $"장판 피격! ({skillLabel})",
                    new Color(1f, 0.3f, 0.3f, 1f),
                    true
                );
            }

            if (tank != null && !tank.isDead && telegraph.IsPointInsideXZ(tank.transform.position))
            {
                tank.TakeDamage(220f);
            }
        }

        /// <summary>
        /// Step 6 ① [4인 산개 (Spread)] 폭발 판정:
        /// 4명의 파티원 각자의 원이 서로 겹치면(중심 간 XZ 거리 < 반경 * 1.85f) 2배의 피해를 입습니다!
        /// </summary>
        private void ResolvePartySpreadExplosion(float spreadRadius, PlayerController player)
        {
            List<Transform> members = GetLivingPartyMemberTransforms();
            float overlapDistanceThreshold = spreadRadius * 1.85f;
            bool playerOverlapped = false;

            for (int i = 0; i < members.Count; i++)
            {
                Transform mA = members[i];
                if (mA == null) continue;

                bool hasOverlap = false;
                Vector2 posA = new Vector2(mA.position.x, mA.position.z);

                for (int j = 0; j < members.Count; j++)
                {
                    if (i == j) continue;
                    Transform mB = members[j];
                    if (mB == null) continue;

                    Vector2 posB = new Vector2(mB.position.x, mB.position.z);
                    if (Vector2.Distance(posA, posB) < overlapDistanceThreshold)
                    {
                        hasOverlap = true;
                        break;
                    }
                }

                float dmg = hasOverlap ? spreadBaseDamage * 2.0f : spreadBaseDamage;

                if (player != null && mA == player.transform)
                {
                    playerOverlapped = hasOverlap;
                    player.TakeDamage(dmg);
                    FloatingTextPopup.Spawn(
                        player.transform.position + Vector3.up * 2.6f,
                        hasOverlap
                            ? $"4인 산개 실패 (원 겹침 2배 피해!! -{Mathf.RoundToInt(dmg)})"
                            : $"4인 산개 성공! (단독 피격 -{Mathf.RoundToInt(dmg)})",
                        hasOverlap ? new Color(1f, 0.2f, 0.3f, 1f) : new Color(1f, 0.65f, 0.9f, 1f),
                        true
                    );
                }
                else
                {
                    PartyBotAI bot = mA.GetComponent<PartyBotAI>();
                    if (bot != null && !bot.isDead)
                    {
                        bot.TakeDamage(dmg);
                    }
                }
            }

            if (playerOverlapped)
            {
                SetMechanicAlert(
                    "4인 산개(Spread) 원이 파티원과 겹쳐 2배 피해를 입었습니다! 다음엔 서로 멀리 떨어지세요!",
                    new Color(1f, 0.25f, 0.35f, 1f),
                    3.8f
                );
            }
            else
            {
                SetMechanicAlert(
                    "4인 산개(Spread) 처리 성공! 4명 모두 원이 겹치지 않고 안전하게 폭발을 넘겼습니다!",
                    new Color(0.35f, 1f, 0.55f, 1f),
                    3.5f
                );
            }
        }

        /// <summary>
        /// Step 6 ② [4인 쉐어 (Stack)] 폭발 판정:
        /// 내 캐릭터 주변 원(반경 stackRadius) 안에 들어온 파티원 수(N)만큼 피해를 1/N로 나누어 받습니다!
        /// </summary>
        private void ResolvePartyStackExplosion(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            Vector2 centerXZ = new Vector2(player.transform.position.x, player.transform.position.z);
            List<Transform> allMembers = GetLivingPartyMemberTransforms();
            List<Transform> stackedMembers = new List<Transform>();

            foreach (Transform member in allMembers)
            {
                if (member == null) continue;
                Vector2 mXZ = new Vector2(member.position.x, member.position.z);
                if (Vector2.Distance(centerXZ, mXZ) <= stackRadius + 0.35f)
                {
                    stackedMembers.Add(member);
                }
            }

            if (!stackedMembers.Contains(player.transform))
            {
                stackedMembers.Add(player.transform);
            }

            int stackCount = Mathf.Max(1, stackedMembers.Count);
            float damagePerMember = stackTotalDamageToSplit / stackCount;

            foreach (Transform member in stackedMembers)
            {
                if (member == player.transform)
                {
                    player.TakeDamage(damagePerMember);
                }
                else
                {
                    PartyBotAI bot = member.GetComponent<PartyBotAI>();
                    if (bot != null && !bot.isDead)
                    {
                        bot.TakeDamage(damagePerMember);
                    }
                }
            }

            bool isFullStackSuccess = stackCount >= 3;
            FloatingTextPopup.Spawn(
                player.transform.position + Vector3.up * 2.7f,
                $"4인 쉐어 폭발! ({stackCount}명 분담 → 1/{stackCount} 피해: -{Mathf.RoundToInt(damagePerMember)})",
                isFullStackSuccess ? new Color(1f, 0.92f, 0.25f, 1f) : new Color(1f, 0.3f, 0.2f, 1f),
                true
            );

            SetMechanicAlert(
                $"4인 쉐어(Stack) 폭발 완료! 원 안에 {stackCount}명이 뭉쳐 피해를 1/{stackCount} ({Mathf.RoundToInt(damagePerMember)})로 나누어 받았습니다!",
                isFullStackSuccess ? new Color(0.35f, 1f, 0.55f, 1f) : new Color(1f, 0.4f, 0.2f, 1f),
                3.8f
            );
        }

        private void ResolveDoubleTankbusterTether(bool wasTetherIntercepted, PlayerController player, PartyBotAI tank)
        {
            if (wasTetherIntercepted && player != null)
            {
                if (player.IsDefensiveBuffActive)
                {
                    player.TakeDamage(1500f); // 80% 감소로 300 피해만 입고 생존!

                    if (player.hasThornArmor)
                    {
                        int reflectDmg = Mathf.RoundToInt(player.thornReflectDamage);
                        ApplyBossHpDamage(reflectDmg);
                        FloatingTextPopup.Spawn(
                            transform.position + Vector3.up * (popupHeightOffset + 0.5f),
                            $"[가시 갑옷] 반사 피해!! - {reflectDmg}",
                            new Color(0.35f, 1f, 0.45f, 1f),
                            true
                        );
                        SetMechanicAlert(
                            $"줄 가로채기 + 생존기 방어 성공! [가시 갑옷] 효과로 보스에게 {reflectDmg} 반사 피해를 입혔습니다!",
                            new Color(0.35f, 1f, 0.45f, 1f),
                            4.0f
                        );
                    }
                    else
                    {
                        SetMechanicAlert(
                            "2연타 탱크버스터 방어 대성공!! (줄 가로채기 + [3]번 생존기 80% 피해 감소!)",
                            new Color(0.25f, 1f, 0.55f, 1f),
                            4.0f
                        );
                    }

                    FloatingTextPopup.Spawn(
                        player.transform.position + Vector3.up * 2.6f,
                        "탱크버스터 2타 방어 성공!! (-80% 감쇄)",
                        new Color(0.3f, 0.95f, 1f, 1f),
                        true
                    );
                }
                else
                {
                    player.TakeDamage(950f);
                    SetMechanicAlert(
                        "줄은 가로챘지만 [3]번 생존기를 켜지 않아 치명상을 입었습니다!",
                        new Color(1f, 0.4f, 0.2f, 1f),
                        4.0f
                    );
                }
            }
            else
            {
                if (tank != null && !tank.isDead)
                {
                    tank.KillTankByUninterceptedTether();
                }
                SetMechanicAlert(
                    "줄 가로채기 실패!! 탱커가 2타에 직격당해 사망했습니다!",
                    new Color(1f, 0.2f, 0.2f, 1f),
                    4.5f
                );
            }
        }

        private void CheckPhaseTransition()
        {
            if (fsmState == BossFSMState.Defeated)
            {
                return;
            }

            if (!_hasTriggeredGroggy && HpRatio <= 0.50f && currentHp > 0.1f)
            {
                _hasTriggeredGroggy = true;
                EnterGroggyTransition();
            }
        }

        private void EnterGroggyTransition()
        {
            CleanupActiveTelegraphAndTether();

            currentPhase = BossPhase.GroggyTransition;
            fsmState = BossFSMState.Groggy;
            _groggyRemainingTime = groggyDuration;
            _currentCastSkillName = "무력화 (GROGGY - 받는 피해 +50%)";

            SetRendererColor(bodyRenderer, new Color(0.95f, 0.78f, 0.22f, 1f));

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * (popupHeightOffset + 0.5f),
                "PHASE TRANSITION! 5초간 그로기 폭딜 타임!! (받는 피해 +50%)",
                new Color(1f, 0.85f, 0.15f, 1f),
                true
            );

            SetMechanicAlert(
                "보스 체력 50% 도달! 5초간 무력화(그로기) 폭딜 타임!! (받는 피해 50% 증가!)",
                new Color(1f, 0.88f, 0.2f, 1f),
                groggyDuration
            );
        }

        private void UpdateGroggyState()
        {
            _groggyRemainingTime -= Time.deltaTime;
            if (_groggyRemainingTime <= 0f)
            {
                _groggyRemainingTime = 0f;
                currentPhase = BossPhase.Phase2_50To0;
                fsmState = BossFSMState.IdleChasing;
                _patternWaitTimer = 1.0f;
                _forceSignatureMechanicNext = true;

                SetRendererColor(bodyRenderer, _stagePhase2BodyColor);

                SetMechanicAlert(
                    $"STAGE {currentStageNumber} 보스 2페이즈 돌입! [도넛 장판] 및 [2연타 탱크버스터: 줄 가로채기] 패턴이 추가됩니다!",
                    new Color(1f, 0.35f, 0.75f, 1f),
                    4.0f
                );
            }
        }

        private void CleanupActiveTelegraphAndTether()
        {
            if (_activeTelegraph != null)
            {
                Destroy(_activeTelegraph.gameObject);
                _activeTelegraph = null;
            }

            _isTetherActive = false;
            _isTetherInterceptedByPlayer = false;
            if (_tetherLineRenderer != null)
            {
                _tetherLineRenderer.enabled = false;
            }

            _isSpreadActive = false;
            _isStackActive = false;
            _activeStackMarker = null;
            ClearActivePartyMarkers();
        }

        private void ClearActivePartyMarkers()
        {
            for (int i = 0; i < _activePartyMarkers.Count; i++)
            {
                if (_activePartyMarkers[i] != null)
                {
                    Destroy(_activePartyMarkers[i].gameObject);
                }
            }
            _activePartyMarkers.Clear();
        }

        public float CalculateAngleFromBossFrontXZ(Vector3 worldPos)
        {
            Vector3 bossForwardXZ = new Vector3(transform.forward.x, 0f, transform.forward.z);
            if (bossForwardXZ.sqrMagnitude < 0.0001f)
            {
                bossForwardXZ = Vector3.forward;
            }
            bossForwardXZ.Normalize();

            Vector3 toTargetXZ = new Vector3(
                worldPos.x - transform.position.x,
                0f,
                worldPos.z - transform.position.z
            );

            if (toTargetXZ.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }
            toTargetXZ.Normalize();

            return Vector3.Angle(bossForwardXZ, toTargetXZ);
        }

        public PositionalZone GetPositionalZone(Vector3 worldPos, out float angleDeg)
        {
            angleDeg = CalculateAngleFromBossFrontXZ(worldPos);
            float rearStart = Mathf.Max(90f, 135f - positionalAngleBonus);
            float flankStart = Mathf.Max(15f, 45f - positionalAngleBonus);

            if (angleDeg >= rearStart)
            {
                return PositionalZone.Rear;
            }
            if (angleDeg >= flankStart)
            {
                return PositionalZone.Flank;
            }
            return PositionalZone.Front;
        }

        public bool ReceiveSkillAttack(
            Vector3 attackerPosition,
            PositionalRequirement requirement,
            int baseDamage = 100,
            bool hasMasterOfRearBonus = false)
        {
            if (fsmState == BossFSMState.Defeated)
            {
                return false;
            }

            float angle = CalculateAngleFromBossFrontXZ(attackerPosition);
            bool isPositionalCrit = false;

            float flankMin = Mathf.Max(15f, 45f - positionalAngleBonus);
            float flankMax = Mathf.Min(170f, 135f + positionalAngleBonus);
            float rearMin = Mathf.Max(90f, 135f - positionalAngleBonus);

            switch (requirement)
            {
                case PositionalRequirement.Flank_45_To_135:
                    isPositionalCrit = (angle >= flankMin && angle <= flankMax);
                    break;

                case PositionalRequirement.Rear_135_To_180:
                    isPositionalCrit = (angle >= rearMin && angle <= 180f);
                    break;
            }

            float damageCalc = isPositionalCrit ? baseDamage * 2f : baseDamage;

            if (isPositionalCrit && requirement == PositionalRequirement.Rear_135_To_180 && hasMasterOfRearBonus)
            {
                damageCalc *= 1.5f;
            }

            int finalDamage = Mathf.RoundToInt(damageCalc * DamageTakenMultiplier);
            ApplyBossHpDamage(finalDamage);

            string popupText = isPositionalCrit
                ? $"POSITIONAL CRIT!! - {finalDamage}"
                : $"HIT - {finalDamage}";

            if (IsGroggy)
            {
                popupText += " (GROGGY +50%!)";
            }

            Color popupColor = isPositionalCrit ? positionalCritColor : normalHitColor;

            LastHitAngle = angle;
            LastHitMessage = $"{popupText}  (각도: {angle:F1}°)";
            LastHitColor = popupColor;

            Vector3 spawnPos = transform.position + Vector3.up * popupHeightOffset;
            FloatingTextPopup.Spawn(spawnPos, popupText, popupColor, isPositionalCrit);

            TriggerHitFlash(isPositionalCrit);
            return isPositionalCrit;
        }

        /// <summary>
        /// Step 5: 원버튼 3단 진화 웨폰스킬(GCD) 피격 및 측·후면 각도 판정 처리
        /// </summary>
        public bool ReceiveComboWeaponSkillAttack(
            Vector3 attackerPosition,
            PositionalRequirement requirement,
            string skillName,
            int comboStage,
            int normalDamage,
            int positionalDamage,
            bool hasMasterOfRearBonus)
        {
            if (fsmState == BossFSMState.Defeated)
            {
                return false;
            }

            float angle = CalculateAngleFromBossFrontXZ(attackerPosition);
            bool isPositionalCrit = false;

            float flankMin = Mathf.Max(15f, 45f - positionalAngleBonus);
            float flankMax = Mathf.Min(170f, 135f + positionalAngleBonus);
            float rearMin = Mathf.Max(90f, 135f - positionalAngleBonus);

            switch (requirement)
            {
                case PositionalRequirement.Flank_45_To_135:
                    isPositionalCrit = (angle >= flankMin && angle <= flankMax);
                    break;

                case PositionalRequirement.Rear_135_To_180:
                    isPositionalCrit = (angle >= rearMin && angle <= 180f);
                    break;
            }

            float damageCalc = isPositionalCrit ? positionalDamage : normalDamage;

            if (isPositionalCrit && requirement == PositionalRequirement.Rear_135_To_180 && hasMasterOfRearBonus)
            {
                damageCalc *= 1.5f;
            }

            int finalDamage = Mathf.RoundToInt(damageCalc * DamageTakenMultiplier);
            ApplyBossHpDamage(finalDamage);

            string popupText;
            Color popupColor;

            if (isPositionalCrit)
            {
                popupText = comboStage == 3
                    ? $"★최종오의★ POSITIONAL CRIT!! [{skillName}] - {finalDamage}"
                    : $"POSITIONAL CRIT!! [{comboStage}단 {skillName}] - {finalDamage}";
                popupColor = comboStage == 3
                    ? new Color(1.0f, 0.82f, 0.12f, 1f)
                    : positionalCritColor;
            }
            else
            {
                popupText = $"HIT [{comboStage}단 {skillName}] - {finalDamage}";
                popupColor = normalHitColor;
            }

            if (IsGroggy)
            {
                popupText += " (GROGGY +50%!)";
            }

            LastHitAngle = angle;
            LastHitMessage = $"{popupText}  (각도: {angle:F1}°)";
            LastHitColor = popupColor;

            Vector3 spawnPos = transform.position + Vector3.up * popupHeightOffset;
            FloatingTextPopup.Spawn(spawnPos, popupText, popupColor, isPositionalCrit || comboStage == 3);

            TriggerHitFlash(isPositionalCrit || comboStage == 3);
            return isPositionalCrit;
        }

        /// <summary>
        /// Step 5: [4]번 '질풍 찌르기' 등 oGCD 끼워넣기 어빌리티 즉발 추가 피해 처리
        /// </summary>
        public void ReceiveOGcdAbilityAttack(string abilityName, int baseDamage)
        {
            if (fsmState == BossFSMState.Defeated)
            {
                return;
            }

            int finalDamage = Mathf.RoundToInt(baseDamage * DamageTakenMultiplier);
            ApplyBossHpDamage(finalDamage);

            string popupText = $"oGCD [{abilityName}]!! - {finalDamage}";
            Color popupColor = new Color(0.30f, 1.0f, 0.90f, 1f);

            LastHitMessage = popupText;
            LastHitColor = popupColor;

            Vector3 spawnPos = transform.position + Vector3.up * (popupHeightOffset + 0.35f);
            FloatingTextPopup.Spawn(spawnPos, popupText, popupColor, true);
            TriggerHitFlash(true);
        }

        public void ReceiveBotAttackDamage(int baseDamage, bool isRanged)
        {
            if (fsmState == BossFSMState.Defeated)
            {
                return;
            }

            int finalDamage = Mathf.RoundToInt(baseDamage * DamageTakenMultiplier);
            ApplyBossHpDamage(finalDamage);

            if (isRanged)
            {
                Vector3 spawnPos = transform.position + new Vector3(UnityEngine.Random.Range(-0.8f, 0.8f), popupHeightOffset - 0.6f, 0f);
                FloatingTextPopup.Spawn(spawnPos, $"RANGED - {finalDamage}", new Color(1f, 0.72f, 0.35f, 0.9f), false);
            }
        }

        /// <summary>
        /// Step 6 핵심: 보스 체력 중간에는 절대 카드가 뜨지 않으며,
        /// 오직 보스를 완전히 처치(HP 0 도달)했을 때만 StageManager.NotifyBossDefeated()를 호출합니다!
        /// </summary>
        private void ApplyBossHpDamage(float damage)
        {
            if (fsmState == BossFSMState.Defeated)
            {
                return;
            }

            currentHp = Mathf.Max(0f, currentHp - damage);

            if (currentHp <= 0.1f)
            {
                currentHp = 0f;
                UpdateTopBossUI();

                if (stageManager == null)
                {
                    stageManager = FindFirstObjectByType<StageManager>();
                }

                if (stageManager != null)
                {
                    stageManager.NotifyBossDefeated();
                }
                else
                {
                    PlayBossCollapseAnimation();
                }
                return;
            }

            CheckPhaseTransition();
        }

        private PartyBotAI FindBotByRole(PartyBotRole targetRole)
        {
            PartyBotAI[] bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            foreach (PartyBotAI bot in bots)
            {
                if (bot.role == targetRole)
                {
                    return bot;
                }
            }
            return null;
        }

        private void CreateTetherLineRenderer()
        {
            GameObject tetherObj = new GameObject("Boss_TetherLaser_Line");
            tetherObj.transform.SetParent(transform, false);

            _tetherLineRenderer = tetherObj.AddComponent<LineRenderer>();
            _tetherLineRenderer.useWorldSpace = true;
            _tetherLineRenderer.positionCount = 2;
            _tetherLineRenderer.startWidth = 0.26f;
            _tetherLineRenderer.endWidth = 0.22f;
            _tetherLineRenderer.numCapVertices = 6;
            _tetherLineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tetherLineRenderer.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            _tetherLineRenderer.sharedMaterial = new Material(shader) { color = Color.white };
            _tetherLineRenderer.enabled = false;
        }

        private void SetMechanicAlert(string message, Color color, float duration)
        {
            if (mechanicAlertBannerText == null)
            {
                return;
            }
            mechanicAlertBannerText.text = message;
            mechanicAlertBannerText.color = color;
            _alertBannerClearTimer = duration;
        }

        private void UpdateAlertBannerTimer()
        {
            if (_alertBannerClearTimer > 0f)
            {
                _alertBannerClearTimer -= Time.deltaTime;
                if (_alertBannerClearTimer <= 0f && mechanicAlertBannerText != null)
                {
                    mechanicAlertBannerText.text = "";
                }
            }
        }

        private void UpdateTopBossUI()
        {
            if (bossHpFillImage != null)
            {
                bossHpFillImage.fillAmount = HpRatio;
                bossHpFillImage.color = currentPhase switch
                {
                    BossPhase.Phase1_100To50 => new Color(0.85f, 0.18f, 0.28f, 1f),
                    BossPhase.GroggyTransition => new Color(1.0f, 0.80f, 0.15f, 1f),
                    BossPhase.Phase2_50To0 => new Color(0.75f, 0.12f, 0.88f, 1f),
                    _ => Color.red
                };
            }

            if (bossHpText != null)
            {
                bossHpText.text = $"{currentBossTitle}   |   HP : {Mathf.CeilToInt(currentHp)} / {Mathf.CeilToInt(maxHp)} ({HpRatio * 100f:F1}%)";
            }

            if (bossPhaseText != null)
            {
                if (fsmState == BossFSMState.Defeated)
                {
                    bossPhaseText.text = $"★ STAGE {currentStageNumber} BOSS CLEAR!! ★";
                }
                else
                {
                    string stageMechanicHint = currentStageNumber switch
                    {
                        1 => "입문 기믹: 원형/부채꼴/탱버 (2페이즈: 도넛 장판 & 줄 가로채기)",
                        2 => "중급 기믹 추가: ① [4인 산개(분홍 원)] & ② [4인 쉐어(노란 화살표)]",
                        3 => "최종 보스 (시전 +25% 가속): [도넛+4인 산개 동시] & [외곽 넉백+4인 쉐어]",
                        _ => ""
                    };

                    bossPhaseText.text = currentPhase switch
                    {
                        BossPhase.Phase1_100To50 => $"1페이즈 (100%~50%) — {stageMechanicHint}",
                        BossPhase.GroggyTransition => $"★ 그로기 폭딜 타임!! (남은 시간 {_groggyRemainingTime:F1}s | 받는 피해 +50% 증가!) ★",
                        BossPhase.Phase2_50To0 => $"2페이즈 (50%~0%) — {stageMechanicHint}",
                        _ => ""
                    };
                }
            }

            if (bossCastBarRoot != null)
            {
                bool showCastBar = IsCasting || IsGroggy;
                if (bossCastBarRoot.activeSelf != showCastBar)
                {
                    bossCastBarRoot.SetActive(showCastBar);
                }

                if (showCastBar)
                {
                    if (bossCastFillImage != null)
                    {
                        if (IsGroggy)
                        {
                            bossCastFillImage.fillAmount = _groggyRemainingTime / Mathf.Max(0.1f, groggyDuration);
                            bossCastFillImage.color = new Color(1f, 0.85f, 0.2f, 1f);
                        }
                        else
                        {
                            bossCastFillImage.fillAmount = CastProgress;
                            bossCastFillImage.color = _isTetherActive
                                ? (_isTetherInterceptedByPlayer ? new Color(0.2f, 0.95f, 0.4f, 1f) : new Color(1f, 0.15f, 0.15f, 1f))
                                : (_isStackActive ? new Color(1f, 0.85f, 0.15f, 1f)
                                : (_isSpreadActive ? new Color(1f, 0.35f, 0.8f, 1f)
                                : new Color(1f, 0.55f, 0.12f, 1f)));
                        }
                    }

                    if (bossCastText != null)
                    {
                        bossCastText.text = IsGroggy
                            ? $"무력화 (GROGGY - 받는 피해 +50%)  [{_groggyRemainingTime:F1}s]"
                            : $"시전 중: {_currentCastSkillName}  ({CastProgress * 100f:F0}% | {_castRemainingTime:F1}s)";
                    }
                }
            }
        }

        private void TriggerHitFlash(bool isCrit)
        {
            if (bodyRenderer != null && gameObject.activeInHierarchy && !IsGroggy && fsmState != BossFSMState.Defeated)
            {
                if (_hitFlashCoroutine != null)
                {
                    StopCoroutine(_hitFlashCoroutine);
                }
                _hitFlashCoroutine = StartCoroutine(HitFlashRoutine(isCrit));
            }
        }

        private IEnumerator HitFlashRoutine(bool isCrit)
        {
            Color flashColor = isCrit ? new Color(1f, 0.65f, 0.2f, 1f) : new Color(0.85f, 0.85f, 0.98f, 1f);
            SetRendererColor(bodyRenderer, flashColor);
            yield return new WaitForSeconds(0.11f);
            if (fsmState != BossFSMState.Defeated && !IsGroggy)
            {
                Color restoreColor = currentPhase == BossPhase.Phase2_50To0
                    ? _stagePhase2BodyColor
                    : _stagePhase1BodyColor;
                SetRendererColor(bodyRenderer, restoreColor);
            }
            _hitFlashCoroutine = null;
        }

        private void CreateCastBanner()
        {
            _castBannerRoot = new GameObject("Boss_CastBanner").transform;
            _castBannerRoot.SetParent(transform, false);
            _castBannerRoot.localPosition = new Vector3(0f, 3.85f, 0f);
            _castBannerRoot.localScale = Vector3.one * 0.1f;

            _castBannerText = _castBannerRoot.gameObject.AddComponent<TextMesh>();
            _castBannerText.text = "";
            _castBannerText.fontSize = 48;
            _castBannerText.fontStyle = FontStyle.Bold;
            _castBannerText.anchor = TextAnchor.MiddleCenter;
            _castBannerText.alignment = TextAlignment.Center;
            _castBannerText.color = new Color(1f, 0.75f, 0.2f, 1f);
        }

        private void UpdateCastBanner()
        {
            if (_castBannerRoot == null || _castBannerText == null)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                _castBannerRoot.rotation = cam.transform.rotation;
            }

            if (fsmState == BossFSMState.Defeated)
            {
                _castBannerText.text = $"★ STAGE {currentStageNumber} BOSS CLEAR! ★";
                _castBannerText.color = new Color(1f, 0.9f, 0.2f, 1f);
            }
            else if (IsGroggy)
            {
                _castBannerText.text = $"★ GROGGY!! 폭딜 타임 (+50% 피해) [{_groggyRemainingTime:F1}s] ★";
                _castBannerText.color = new Color(1f, 0.88f, 0.2f, 1f);
            }
            else if (IsCasting)
            {
                _castBannerText.text = $"[시전 중: {_currentCastSkillName} ({_castRemainingTime:F1}s) - 시선 고정!]";
                _castBannerText.color = new Color(1f, 0.45f, 0.2f, 1f);
            }
            else
            {
                _castBannerText.text = $"STAGE {currentStageNumber} BOSS [{(currentPhase == BossPhase.Phase2_50To0 ? "2페이즈" : "1페이즈")}] (어그로: {AggroTargetName})";
                _castBannerText.color = new Color(0.85f, 0.85f, 1f, 0.95f);
            }
        }

        private static void SetRendererColor(Renderer r, Color color)
        {
            if (r == null || r.material == null)
            {
                return;
            }
            r.material.color = color;
            if (r.material.HasProperty("_BaseColor"))
            {
                r.material.SetColor("_BaseColor", color);
            }
            if (r.material.HasProperty("_Color"))
            {
                r.material.SetColor("_Color", color);
            }
        }
    }
}
