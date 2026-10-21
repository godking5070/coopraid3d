using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    /// <summary>
    /// 내 캐릭터 (근거리 딜러) 컨트롤러 (Step 1 ~ Step 6 통합):
    /// - WASD로 XZ 평면 이동 및 이동 방향으로 부드러운 회전
    /// - PlayerCombatController와 연동하여 파판14 스타일 GCD/oGCD 및 원버튼 3단 진화 콤보 수행
    /// - 힐러 봇의 '구출(Rescue)'에 의해 안전지대로 끌려가는 이동 처리 및 Stage 3 '외곽 넉백' 밀려남 처리
    /// - 스테이지 클리어 시 체력 100% 회복 및 초기 위치 재배치, 7종 로그라이크 카드 영구 중첩 적용
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement (XZ Plane)")]
        public float moveSpeed = 7.8f;
        public float rotationSmoothSpeed = 720f;
        public float arenaBoundaryRadius = 18f;

        [Header("Health (HP)")]
        public float maxHp = 1000f;
        public float currentHp = 1000f;

        [Header("Combat & Skills")]
        public BossController targetBoss;
        public PlayerCombatController combatController;
        public RoguelikeCardManager cardManager;
        public StageManager stageManager;
        public int baseSkillDamage = 100;

        [Header("Skill [3] Personal Defensive Cooldown")]
        public float defensiveDuration = 3.0f;
        [Range(0f, 1f)]
        public float damageReductionRate = 0.80f; // 받는 피해 80% 감소

        [Header("Roguelike Card Build Flags (Step 4 ~ Step 6 영구 중첩)")]
        public bool hasMasterOfRear;     // 1) [후면의 지배자]: 후면 기습 성공 시 피해량 50% 추가 증가 및 쿨타임 초기화
        public int masterOfRearStacks;
        public bool hasGhostStep;        // 2) [유령 걸음]: 이동속도 30% 증가 및 측·후면 판정 각도 15도 확장
        public int ghostStepStacks;
        public bool hasThornArmor;       // 3) [가시 갑옷]: 줄 가로채기 후 생존기 방어 성공 시 보스에게 큰 반사 피해
        public int thornArmorStacks;
        public bool hasTacticalRescue;   // 4) [전술적 구출]: 힐러에게 구출로 끌려갈 경우 5초간 공격력 2배 증가
        public int tacticalRescueStacks;
        public bool hasBerserkerShout;   // 5) [광전사의 함성]: 10초마다 파티 전체 공격속도 증가 버프 자동 발동
        public int berserkerShoutStacks;
        public bool hasVampiricBlade;    // 6) [흡혈 검]: 방향 특화(Positional) 공격 성공 시 내 체력 회복
        public int vampiricBladeStacks;
        public int swiftAwakeningStacks; // 7) [신속의 각성]: 글로벌 쿨타임(GCD) 0.4초씩 감소

        public float positionalAngleBonusDeg;
        public float thornReflectDamage = 600f;

        [Header("Visuals")]
        public Renderer bodyRenderer;
        public Color normalBodyColor = new Color(1f, 0.85f, 0.12f, 1f);    // 노란색
        public Color defensiveBodyColor = new Color(0.15f, 0.52f, 1f, 1f); // 파란색

        [Header("Optional UI References")]
        public Text hudStatusText;

        public bool IsDefensiveBuffActive => _defensiveRemainingTime > 0f;
        public float DefensiveRemainingTime => _defensiveRemainingTime;
        public float CurrentDamageReduction => IsDefensiveBuffActive ? damageReductionRate : 0f;
        public float HpRatio => maxHp > 0f ? Mathf.Clamp01(currentHp / maxHp) : 0f;
        public bool NeedsHealing => currentHp < maxHp - 1f;
        public bool IsBeingRescued => _isBeingRescued;
        public bool IsBeingKnockedBack => _isBeingKnockedBack;
        public bool HasTacticalRescueBuff => _tacticalRescueBuffTimer > 0f;

        private float _defensiveRemainingTime;
        private float _tacticalRescueBuffTimer;

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

        // 머리 위 3D 체력바 및 이름표
        private Transform _hpBarRoot;
        private Transform _hpFillBar;

        private void Start()
        {
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

            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponent<MeshRenderer>();
                if (bodyRenderer == null)
                {
                    bodyRenderer = GetComponentInChildren<MeshRenderer>();
                }
            }

            currentHp = Mathf.Clamp(currentHp, 1f, maxHp);
            ApplyBodyColor(normalBodyColor);
            CreateSlashEffectLine();
            CreateOverheadHpBar("ME (근딜)");
        }

        private void Update()
        {
            if (Time.timeScale <= 0.001f)
            {
                return;
            }

            UpdateTimers();

            if (_isBeingRescued)
            {
                UpdateRescuePullMovementXZ();
            }
            else if (_isBeingKnockedBack)
            {
                UpdateKnockbackMovementXZ();
            }
            else
            {
                HandleMovementXZ();
                if (combatController == null)
                {
                    HandleLegacySkillInputs();
                }
            }

            UpdateOverheadHpBar();
            UpdateHUD();
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
        }

        /// <summary>
        /// Step 6: 스테이지 클리어 후 카드 선택 시 체력 100% 회복 및 초기 위치로 재배치합니다.
        /// </summary>
        public void ResetToFullHealthAndPosition(Vector3 spawnPos)
        {
            _isBeingRescued = false;
            _isBeingKnockedBack = false;
            currentHp = maxHp;
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
            if (_isBeingRescued)
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

        private void UpdateKnockbackMovementXZ()
        {
            Vector3 current = transform.position;
            Vector3 target = new Vector3(_knockbackTargetXZ.x, current.y, _knockbackTargetXZ.z);
            transform.position = Vector3.MoveTowards(current, target, _knockbackSpeed * Time.deltaTime);

            Vector3 diffXZ = new Vector3(target.x - transform.position.x, 0f, target.z - transform.position.z);
            if (diffXZ.sqrMagnitude < 0.04f)
            {
                transform.position = target;
                _isBeingKnockedBack = false;
            }
        }

        public void StartPulledByHealerRescue(Vector3 safeDestinationXZ)
        {
            _isBeingKnockedBack = false;
            _isBeingRescued = true;
            _rescueDestinationXZ = new Vector3(safeDestinationXZ.x, transform.position.y, safeDestinationXZ.z);

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.4f,
                "구출(Rescue)! 힐러가 안전지대로 끌어당깁니다!",
                new Color(0.3f, 0.95f, 1f, 1f),
                true
            );

            if (hasTacticalRescue)
            {
                _tacticalRescueBuffTimer = 5.0f;
                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.85f,
                    "[전술적 구출] 발동!! 5초간 공격력 2배 증가!",
                    new Color(1f, 0.85f, 0.2f, 1f),
                    true
                );
            }
        }

        private void UpdateRescuePullMovementXZ()
        {
            Vector3 current = transform.position;
            Vector3 target = new Vector3(_rescueDestinationXZ.x, current.y, _rescueDestinationXZ.z);
            transform.position = Vector3.MoveTowards(current, target, _rescuePullSpeed * Time.deltaTime);

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
                    if (targetBoss != null)
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
            Vector2 moveInput = ReadMoveInput();
            Vector3 moveDirXZ = new Vector3(moveInput.x, 0f, moveInput.y);

            if (moveDirXZ.sqrMagnitude > 1f)
            {
                moveDirXZ.Normalize();
            }

            if (moveDirXZ.sqrMagnitude > 0.0001f)
            {
                Vector3 nextPos = transform.position + moveDirXZ * (moveSpeed * Time.deltaTime);
                nextPos.y = transform.position.y;

                Vector2 flatPos = new Vector2(nextPos.x, nextPos.z);
                if (flatPos.magnitude > arenaBoundaryRadius)
                {
                    flatPos = flatPos.normalized * arenaBoundaryRadius;
                    nextPos.x = flatPos.x;
                    nextPos.z = flatPos.y;
                }

                transform.position = nextPos;

                Quaternion targetRotation = Quaternion.LookRotation(moveDirXZ.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    rotationSmoothSpeed * Time.deltaTime
                );
            }
        }

        private void HandleLegacySkillInputs()
        {
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

            if (targetBoss == null) return;
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

            if (targetBoss == null) return;
            FaceBossImmediateXZ();
            PlaySlashVisual(new Color(0.3f, 0.85f, 1f, 1f));
            targetBoss.ReceiveSkillAttack(transform.position, PositionalRequirement.Rear_135_To_180, baseSkillDamage, hasMasterOfRear);
        }

        public void TriggerVampiricBladeHeal()
        {
            float heal = 10f * Mathf.Max(1, vampiricBladeStacks);
            currentHp = Mathf.Min(maxHp, currentHp + heal);
            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.25f,
                $"+{Mathf.RoundToInt(heal)} HP [흡혈 검]",
                new Color(0.75f, 0.35f, 1f, 1f),
                false
            );
        }

        /// <summary>
        /// [3]번 개인 생존기 버프 활성화 (3초간 받는 피해 80% 감소 + 몸 색상 파란색 변경)
        /// </summary>
        public void ActivatePersonalDefensiveBuff()
        {
            _defensiveRemainingTime = defensiveDuration;
            ApplyBodyColor(defensiveBodyColor);

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.3f,
                "DEFENSIVE ON! (3초간 피해 80% 감소)",
                new Color(0.35f, 0.75f, 1f, 1f),
                false
            );
        }

        public float CalculateFinalDamageTaken(float incomingDamage)
        {
            float multiplier = IsDefensiveBuffActive ? (1f - damageReductionRate) : 1f;
            return incomingDamage * multiplier;
        }

        public void TakeDamage(float rawDamage)
        {
            float finalDamage = CalculateFinalDamageTaken(rawDamage);
            currentHp = Mathf.Max(100f, currentHp - finalDamage);

            string dmgText = IsDefensiveBuffActive
                ? $"-{Mathf.RoundToInt(finalDamage)} (생존기 80% 감쇄!)"
                : $"-{Mathf.RoundToInt(finalDamage)}";
            Color dmgColor = IsDefensiveBuffActive
                ? new Color(0.45f, 0.8f, 1f, 1f)
                : new Color(1f, 0.35f, 0.35f, 1f);

            FloatingTextPopup.Spawn(transform.position + Vector3.up * 2.1f, dmgText, dmgColor, false);
        }

        public void ReceiveHeal(float healAmount)
        {
            if (currentHp >= maxHp)
            {
                return;
            }

            float before = currentHp;
            currentHp = Mathf.Min(maxHp, currentHp + healAmount);
            float actualHealed = currentHp - before;

            if (actualHealed > 0.5f)
            {
                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.2f,
                    $"+{Mathf.RoundToInt(actualHealed)} HP",
                    new Color(0.25f, 1f, 0.45f, 1f),
                    false
                );
            }
        }

        public void FaceBossImmediateXZ()
        {
            if (targetBoss == null)
            {
                return;
            }

            Vector3 toBossXZ = new Vector3(
                targetBoss.transform.position.x - transform.position.x,
                0f,
                targetBoss.transform.position.z - transform.position.z
            );

            if (toBossXZ.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(toBossXZ.normalized, Vector3.up);
            }
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
            SetUnlitColor(fillObj.GetComponent<Renderer>(), new Color(1f, 0.88f, 0.15f, 1f));

            GameObject nameObj = new GameObject("NameLabel");
            nameObj.transform.SetParent(_hpBarRoot, false);
            nameObj.transform.localPosition = new Vector3(0f, 0.24f, 0f);
            nameObj.transform.localScale = Vector3.one * 0.08f;
            TextMesh tm = nameObj.AddComponent<TextMesh>();
            tm.text = label;
            tm.fontSize = 42;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.92f, 0.3f, 1f);
        }

        private void UpdateOverheadHpBar()
        {
            if (_hpBarRoot == null)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                _hpBarRoot.rotation = cam.transform.rotation;
            }

            if (_hpFillBar != null)
            {
                float ratio = HpRatio;
                const float fullWidth = 1.26f;
                _hpFillBar.localScale = new Vector3(fullWidth * ratio, 0.12f, 1f);
                _hpFillBar.localPosition = new Vector3(-fullWidth * (1f - ratio) * 0.5f, 0f, -0.01f);
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
                PositionalZone.Flank => "<color=#44FF77>측면 (Flank) → [1] 측면 콤보 CRIT!</color>",
                PositionalZone.Rear => "<color=#55CCFF>후면 (Rear) → [2] 후면 콤보 CRIT!</color>",
                _ => "알 수 없음"
            };

            string buffStatus = IsDefensiveBuffActive
                ? $"<color=#44AAFF>활성 ({_defensiveRemainingTime:F1}s / 피해 80% 감소)</color>"
                : "<color=#AAAAAA>비활성 ([3] oGCD 쿨 15초)</color>";

            if (HasTacticalRescueBuff)
            {
                buffStatus += $"  |  <color=#FFDD33>[전술적 구출] 공 2배 ({_tacticalRescueBuffTimer:F1}s)</color>";
            }

            string cardsSummary = cardManager != null
                ? cardManager.GetAcquiredCardsSummary()
                : "없음";

            float gcdSec = combatController != null ? combatController.currentGcdDuration : 2.0f;
            int stg = stageManager != null ? stageManager.currentStage : 1;

            hudStatusText.text =
                $"<b>[Project: Raid Rogue - Step 6 (STAGE {stg}/3 보스 러시 & 산개/쉐어 협동 기믹)]</b>\n" +
                $"• <b>[1] 측면 3단</b> & <b>[2] 후면 3단</b> (GCD <b>{gcdSec:F1}s</b>) | <b>[3] 생존기</b> & <b>[4] 질풍 찌르기</b> (oGCD 끼워넣기)\n" +
                "• <b>테스트 키</b> : <b>[F1]</b> 현재 보스 즉시 처치(클리어 보상) | <b>[F2]</b> 줄 가로채기 | <b>[F3]</b> 4인 산개 | <b>[F4]</b> 4인 쉐어 | <b>[F5]</b> 복합 기믹\n" +
                "--------------------------------------------------------------------------------\n" +
                $"• <b>내 체력</b> : <b>{Mathf.RoundToInt(currentHp)} / {Mathf.RoundToInt(maxHp)}</b>   |   <b>생존기/버프</b> : {buffStatus}\n" +
                $"• <b>내 각도 (XZ)</b> : <b>{angle:F1}°</b> [{zoneKor}]   |   <b>최근 타격</b> : {targetBoss.LastHitMessage}\n" +
                $"• <b>누적 카드 빌드</b> : <color=#FFE066>{cardsSummary}</color>";
        }

        private static Vector2 ReadMoveInput()
        {
            Vector2 input = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (input.sqrMagnitude < 0.001f)
            {
                input.x = Input.GetAxisRaw("Horizontal");
                input.y = Input.GetAxisRaw("Vertical");
            }
#endif
            return input;
        }

        private static bool WasSkill1Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null &&
                (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame))
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                return true;
            }
#endif
            return false;
        }

        private static bool WasSkill2Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null &&
                (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame))
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                return true;
            }
#endif
            return false;
        }

        private static bool WasSkill3Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null &&
                (Keyboard.current.digit3Key.wasPressedThisFrame || Keyboard.current.numpad3Key.wasPressedThisFrame))
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                return true;
            }
#endif
            return false;
        }
    }
}
