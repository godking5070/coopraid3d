using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public enum AddMinionType
    {
        PowerSupplier_BossTether = 0, // 쫄 A: [동력 공급체] - 보스에게 보라색 레이저 줄을 연결해 보스가 받는 피해를 80% 감소시킴
        Assassin_HealerHunter = 1     // 쫄 B: [암살자] - 소환 즉시 힐러를 어그로 1순위로 잡고 추격하여 강공격/자폭 시도
    }

    /// <summary>
    /// Step 9 요구사항 4: 보스가 소환하는 개별 쫄(똘마니 - Add) 컨트롤러.
    /// - 쫄 A [동력 공급체 (보스 강화 쫄)]: 살아있는 동안 보스에게 보라색 레이저 줄을 연결해 보스가 받는 피해를 80% 감소시킴.
    /// - 쫄 B [암살자 (힐러 추격 쫄)]: 소환 즉시 힐러(플레이어 힐러 또는 힐러 봇)를 어그로 1순위로 잡고 달려가 강공격/자폭 시전.
    /// - 탱커의 [4]번 '도발' 스킬로 어그로를 강탈할 수 있으며, [Visual_3D]와 [Visual_2D] 이중 외형을 지원합니다.
    /// </summary>
    public class BossAddMinion : MonoBehaviour
    {
        [Min(0f)] public float combatRadius = 0.65f;
        [Header("Add Identity & Stats")]
        public AddMinionType addType = AddMinionType.PowerSupplier_BossTether;
        public string addDisplayName = "동력 공급체";
        public float maxHp = 850f;
        public float currentHp = 850f;
        public bool isDead;

        [Header("References")]
        public BossController ownerBoss;
        public BossAddSpawner spawner;
        public Transform aggroTarget;
        public DualVisualController dualVisual;
        public Renderer bodyRenderer;

        [Header("Combat & Movement (XZ Plane)")]
        public float moveSpeed = 5.6f;
        public float attackRange = 2.3f;
        public float attackInterval = 2.2f;
        public float attackDamage = 190f;

        private float _attackTimer;
        private float _castProgress;
        private bool _isCastingHeavyStrike;
        private string _currentCastSkillName = "";

        private LineRenderer _tetherOrAttackLine;
        private Transform _hpBarRoot;
        private Transform _hpFillBar;
        private Renderer _hpFillRenderer;
        private TextMesh _nameLabelText;
        private Color _baseColor;
        private Coroutine _hitFlashCoroutine;

        public float HpRatio => maxHp > 0f ? Mathf.Clamp01(currentHp / maxHp) : 0f;
        public string CurrentCastOrStatusText => _currentCastSkillName;
        public float CastProgress => _castProgress;
        public bool IsCastingSkill => _isCastingHeavyStrike;

        public void InitializeAdd(
            AddMinionType type,
            BossController boss,
            BossAddSpawner ownerSpawner,
            Vector3 spawnPos,
            int stageNum)
        {
            addType = type;
            ownerBoss = boss;
            spawner = ownerSpawner;
            isDead = false;

            float stageHpScale = stageNum == 1 ? 1.0f : (stageNum == 2 ? 1.35f : 1.75f);

            if (type == AddMinionType.PowerSupplier_BossTether)
            {
                addDisplayName = "쫄 A : 동력 공급체";
                maxHp = Mathf.RoundToInt(650f * stageHpScale);
                currentHp = maxHp;
                moveSpeed = 1.8f;
                _baseColor = new Color(0.68f, 0.22f, 1.0f, 1f); // 보라색 동력체
                _currentCastSkillName = "보스 동력 공급 (보스 피해 80% 감소!)";
                aggroTarget = boss != null ? boss.transform : null;
            }
            else
            {
                addDisplayName = "쫄 B : 암살자 (힐러 추격)";
                maxHp = Mathf.RoundToInt(750f * stageHpScale);
                currentHp = maxHp;
                moveSpeed = 5.4f;
                attackDamage = 180f * stageHpScale;
                _baseColor = new Color(0.95f, 0.18f, 0.35f, 1f); // 진홍색 암살자
                _currentCastSkillName = "목표: 힐러 암살 추격 중!";
                AcquireHealerAsInitialAggroTarget();
            }

            transform.position = new Vector3(spawnPos.x, 1.0f, spawnPos.z);

            BuildAddDualVisuals();
            CreateTetherOrAttackLine();
            CreateOverheadHpBar();
        }

        private void AcquireHealerAsInitialAggroTarget()
        {
            PlayerController pc = FindFirstObjectByType<PlayerController>();
            if (pc != null && pc.currentRole == PlayerRole.Oracle_Healer)
            {
                aggroTarget = pc.transform;
                return;
            }

            PartyBotAI[] bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            foreach (PartyBotAI bot in bots)
            {
                if (bot != null && !bot.isDead && bot.role == PartyBotRole.Healer)
                {
                    aggroTarget = bot.transform;
                    return;
                }
            }

            if (pc != null)
            {
                aggroTarget = pc.transform;
            }
        }

        private void BuildAddDualVisuals()
        {
            Transform existing3D = transform.Find("[Visual_3D]");
            if (existing3D != null)
            {
                DestroyImmediate(existing3D.gameObject);
            }

            GameObject visual3D = new GameObject("[Visual_3D]");
            visual3D.transform.SetParent(transform, false);
            visual3D.transform.localPosition = Vector3.zero;
            visual3D.transform.localRotation = Quaternion.identity;

            PrimitiveType prim = addType == AddMinionType.PowerSupplier_BossTether
                ? PrimitiveType.Cylinder
                : PrimitiveType.Capsule;

            GameObject bodyObj = GameObject.CreatePrimitive(prim);
            bodyObj.name = "Add_Body_Mesh";
            bodyObj.transform.SetParent(visual3D.transform, false);
            bodyObj.transform.localPosition = Vector3.zero;
            bodyObj.transform.localScale = addType == AddMinionType.PowerSupplier_BossTether
                ? new Vector3(1.15f, 0.95f, 1.15f)
                : new Vector3(0.95f, 0.95f, 0.95f);

            // 마우스 클릭 레이캐스트 타겟팅을 위해 루트에 캡슐 콜라이더 유지/부여
            Collider childCol = bodyObj.GetComponent<Collider>();
            if (childCol != null)
            {
                Destroy(childCol);
            }
            CapsuleCollider rootCol = GetComponent<CapsuleCollider>();
            if (rootCol == null)
            {
                rootCol = gameObject.AddComponent<CapsuleCollider>();
            }
            rootCol.radius = 0.85f;
            rootCol.height = 2.2f;
            rootCol.center = Vector3.zero;

            bodyRenderer = bodyObj.GetComponent<Renderer>();
            SetRendererColor(bodyRenderer, _baseColor);

            // 정면 바이저 표시
            GameObject eyeObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eyeObj.name = "Add_FrontVisor";
            Destroy(eyeObj.GetComponent<Collider>());
            eyeObj.transform.SetParent(visual3D.transform, false);
            eyeObj.transform.localPosition = new Vector3(0f, 0.35f, 0.45f);
            eyeObj.transform.localScale = new Vector3(0.55f, 0.22f, 0.28f);
            SetRendererColor(eyeObj.GetComponent<Renderer>(), new Color(1f, 0.9f, 0.2f, 1f));

            dualVisual = GetComponent<DualVisualController>();
            if (dualVisual == null)
            {
                dualVisual = gameObject.AddComponent<DualVisualController>();
            }
            dualVisual.unitKind = addType == AddMinionType.PowerSupplier_BossTether
                ? VisualUnitKind.BotTank
                : VisualUnitKind.BotRangedDPS;
            dualVisual.visual3DRoot = visual3D;
            dualVisual.primary3DRenderer = bodyRenderer;
            dualVisual.primaryColor = _baseColor;
            dualVisual.accentColor = new Color(1f, 0.9f, 0.2f, 1f);
            dualVisual.base2DScale = 1.35f;
            dualVisual.floorYOffset = 0.14f;
            dualVisual.EnsureDualVisualHierarchy();
            dualVisual.Refresh2DSpritesAndColors();
            dualVisual.ApplyViewMode(ViewModeManager.Is2DViewActive);
        }

        private void CreateTetherOrAttackLine()
        {
            GameObject lineObj = new GameObject("Add_LaserLine");
            lineObj.transform.SetParent(transform, false);
            _tetherOrAttackLine = lineObj.AddComponent<LineRenderer>();
            _tetherOrAttackLine.useWorldSpace = true;
            _tetherOrAttackLine.positionCount = 2;
            _tetherOrAttackLine.numCapVertices = 5;
            _tetherOrAttackLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tetherOrAttackLine.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            _tetherOrAttackLine.sharedMaterial = new Material(shader) { color = Color.white };
            _tetherOrAttackLine.enabled = addType == AddMinionType.PowerSupplier_BossTether;
        }

        private void CreateOverheadHpBar()
        {
            _hpBarRoot = new GameObject("Add_OverheadHpBar").transform;
            _hpBarRoot.SetParent(transform, false);
            _hpBarRoot.localPosition = new Vector3(0f, 1.65f, 0f);

            GameObject bgObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgObj.name = "HP_Bg";
            Destroy(bgObj.GetComponent<Collider>());
            bgObj.transform.SetParent(_hpBarRoot, false);
            bgObj.transform.localScale = new Vector3(1.45f, 0.17f, 1f);
            SetUnlitColor(bgObj.GetComponent<Renderer>(), new Color(0.08f, 0.08f, 0.10f, 0.92f));

            GameObject fillObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fillObj.name = "HP_Fill";
            Destroy(fillObj.GetComponent<Collider>());
            _hpFillBar = fillObj.transform;
            _hpFillBar.SetParent(_hpBarRoot, false);
            _hpFillBar.localPosition = new Vector3(0f, 0f, -0.01f);
            _hpFillBar.localScale = new Vector3(1.40f, 0.13f, 1f);
            _hpFillRenderer = fillObj.GetComponent<Renderer>();
            SetUnlitColor(_hpFillRenderer, _baseColor);

            GameObject nameObj = new GameObject("NameLabel");
            nameObj.transform.SetParent(_hpBarRoot, false);
            nameObj.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            nameObj.transform.localScale = Vector3.one * 0.08f;
            _nameLabelText = nameObj.AddComponent<TextMesh>();
            _nameLabelText.text = addDisplayName;
            _nameLabelText.fontSize = 42;
            _nameLabelText.fontStyle = FontStyle.Bold;
            _nameLabelText.anchor = TextAnchor.MiddleCenter;
            _nameLabelText.alignment = TextAlignment.Center;
            _nameLabelText.color = Color.Lerp(_baseColor, Color.white, 0.35f);
        }

        private void Update()
        {
            if (isDead || Time.timeScale <= 0.001f)
            {
                return;
            }

            if (ownerBoss == null || ownerBoss.IsBossDefeatedCollapse)
            {
                KillAddMinion(silent: true);
                return;
            }

            if (addType == AddMinionType.PowerSupplier_BossTether)
            {
                UpdatePowerSupplierBehavior();
            }
            else
            {
                UpdateAssassinBehavior();
            }

            UpdateOverheadHpBar();
        }

        private void UpdatePowerSupplierBehavior()
        {
            if (ownerBoss == null) return;

            // 보스를 바라보며 보라색 강화 레이저 줄을 연결
            Vector3 toBossXZ = new Vector3(
                ownerBoss.transform.position.x - transform.position.x,
                0f,
                ownerBoss.transform.position.z - transform.position.z
            );
            if (toBossXZ.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(toBossXZ.normalized, Vector3.up),
                    180f * Time.deltaTime
                );
            }

            if (_tetherOrAttackLine != null)
            {
                _tetherOrAttackLine.enabled = true;
                float widthPulse = 0.24f + Mathf.Sin(Time.time * 12f) * 0.06f;
                _tetherOrAttackLine.startWidth = widthPulse;
                _tetherOrAttackLine.endWidth = widthPulse * 1.2f;
                Color purpleBeam = new Color(0.75f, 0.22f, 1.0f, 0.95f);
                _tetherOrAttackLine.startColor = purpleBeam;
                _tetherOrAttackLine.endColor = new Color(0.95f, 0.45f, 1.0f, 0.98f);
                _tetherOrAttackLine.SetPosition(0, new Vector3(transform.position.x, 1.0f, transform.position.z));
                _tetherOrAttackLine.SetPosition(1, new Vector3(ownerBoss.transform.position.x, 1.35f, ownerBoss.transform.position.z));
            }

            _currentCastSkillName = "보스 동력 공급 중 (보스 피해 80% 감쇄!)";
            _castProgress = Mathf.PingPong(Time.time * 0.8f, 1f);
        }

        private void UpdateAssassinBehavior()
        {
            // 타겟이 죽었거나 없으면 다시 힐러/플레이어 탐색
            if (aggroTarget == null)
            {
                AcquireHealerAsInitialAggroTarget();
            }
            else
            {
                PartyBotAI bot = aggroTarget.GetComponent<PartyBotAI>();
                if (bot != null && bot.isDead)
                {
                    AcquireHealerAsInitialAggroTarget();
                }
            }

            if (aggroTarget == null) return;

            Vector3 diffXZ = new Vector3(
                aggroTarget.position.x - transform.position.x,
                0f,
                aggroTarget.position.z - transform.position.z
            );
            float distXZ = diffXZ.magnitude;

            if (diffXZ.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(diffXZ.normalized, Vector3.up),
                    540f * Time.deltaTime
                );
            }

            // 어그로 대상에게 접근
            if (distXZ > attackRange)
            {
                Vector3 nextPos = transform.position + diffXZ.normalized * (moveSpeed * Time.deltaTime);
                nextPos.y = 1.0f;
                transform.position = nextPos;
            }

            // 추격 점선/붉은 타겟 선 표시
            if (_tetherOrAttackLine != null)
            {
                _tetherOrAttackLine.enabled = true;
                _tetherOrAttackLine.startWidth = 0.11f;
                _tetherOrAttackLine.endWidth = 0.05f;
                Color chaseCol = new Color(1.0f, 0.22f, 0.30f, 0.75f);
                _tetherOrAttackLine.startColor = chaseCol;
                _tetherOrAttackLine.endColor = chaseCol;
                _tetherOrAttackLine.SetPosition(0, new Vector3(transform.position.x, 0.9f, transform.position.z));
                _tetherOrAttackLine.SetPosition(1, new Vector3(aggroTarget.position.x, 0.9f, aggroTarget.position.z));
            }

            string targetName = GetAggroTargetDisplayName();
            _attackTimer += Time.deltaTime;
            _castProgress = Mathf.Clamp01(_attackTimer / Mathf.Max(0.2f, attackInterval));
            _isCastingHeavyStrike = _castProgress >= 0.45f;
            _currentCastSkillName = _isCastingHeavyStrike
                ? $"암살 강공격 시전! ({targetName})"
                : $"추격 중 ➔ {targetName}";

            if (_attackTimer >= attackInterval)
            {
                _attackTimer = 0f;
                if (distXZ <= attackRange + 1.4f)
                {
                    ExecuteAssassinStrikeOnTarget();
                }
            }
        }

        private void ExecuteAssassinStrikeOnTarget()
        {
            if (aggroTarget == null) return;

            PlayerController pc = aggroTarget.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.TakeDamage(attackDamage);
                FloatingTextPopup.Spawn(
                    pc.transform.position + Vector3.up * 2.4f,
                    $"암살자 기습 피격! (-{Mathf.RoundToInt(attackDamage)})",
                    new Color(1f, 0.25f, 0.35f, 1f),
                    true
                );
                return;
            }

            PartyBotAI bot = aggroTarget.GetComponent<PartyBotAI>();
            if (bot != null && !bot.isDead)
            {
                bot.TakeDamage(attackDamage);
                FloatingTextPopup.Spawn(
                    bot.transform.position + Vector3.up * 2.4f,
                    $"암살자 기습! ({bot.botDisplayName} 피격!)",
                    new Color(1f, 0.25f, 0.35f, 1f),
                    false
                );
            }
        }

        public string GetAggroTargetDisplayName()
        {
            if (addType == AddMinionType.PowerSupplier_BossTether)
            {
                return ownerBoss != null ? "보스 동력 연결" : "보스";
            }

            if (aggroTarget == null) return "없음";
            PlayerController pc = aggroTarget.GetComponent<PlayerController>();
            if (pc != null) return pc.playerRoleDisplayName;
            PartyBotAI bot = aggroTarget.GetComponent<PartyBotAI>();
            if (bot != null) return bot.botDisplayName;
            return aggroTarget.name;
        }

        /// <summary>
        /// 탱커(플레이어 가디언 또는 탱커 봇)가 도발을 사용했을 때 이 쫄의 어그로 1순위를 즉시 변경합니다.
        /// </summary>
        public void TauntBy(Transform newTarget, string taunterName)
        {
            if (isDead || newTarget == null) return;

            aggroTarget = newTarget;
            _attackTimer = 0f;

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.6f,
                $"★ 도발 적중! [{addDisplayName}] 어그로 ➔ {taunterName}! ★",
                new Color(0.25f, 0.95f, 1.0f, 1f),
                true
            );

            if (ownerBoss != null)
            {
                ownerBoss.ShowExternalAlert(
                    $"★ [{taunterName}]이(가) [{addDisplayName}]를 도발하여 힐러를 안전하게 보호합니다! ★",
                    new Color(0.30f, 0.95f, 1.0f, 1f),
                    3.2f
                );
            }
        }

        public void ReceivePlayerAttack(
            string roleTag,
            string skillName,
            int damage,
            Color popupColor,
            bool isBigHighlight = false,
            Transform source = null)
        {
            if (isDead) return;

            float mult = ownerBoss != null ? ownerBoss.DamageTakenMultiplier : 1.0f;
            int finalDmg = Mathf.RoundToInt(damage * mult);
            float hpBefore = currentHp;
            currentHp = Mathf.Max(0f, currentHp - finalDmg);
            if (currentHp <= 0.1f) currentHp = 0f;
            CombatMetricsManager.Damage(source != null ? source : (ownerBoss != null ? ownerBoss.targetPlayer : null), hpBefore, currentHp, true);

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.35f,
                $"[{roleTag}] {skillName} - {finalDmg}",
                popupColor,
                isBigHighlight
            );

            if (ownerBoss != null)
            {
                ownerBoss.SetLastHitLogFromExternal($"[{addDisplayName} 타격] {skillName} - {finalDmg}", popupColor);
            }

            if (currentHp <= 0.1f)
            {
                KillAddMinion(silent: false);
            }
            else if (gameObject.activeInHierarchy)
            {
                if (_hitFlashCoroutine != null) StopCoroutine(_hitFlashCoroutine);
                _hitFlashCoroutine = StartCoroutine(HitFlashRoutine());
            }
        }

        public void ReceiveBotAttack(int damage, Transform source)
        {
            var attacker = source != null ? source.GetComponent<PartyBotAI>() : null;
            if (attacker != null && (attacker.role == PartyBotRole.Tank || attacker.role == PartyBotRole.MeleeDPS) && !attacker.CanMeleeHit(transform)) return;
            if (isDead) return;

            float hpBefore = currentHp;
            currentHp = Mathf.Max(0f, currentHp - damage);
            if (currentHp <= 0.1f) currentHp = 0f;
            CombatMetricsManager.Damage(source, hpBefore, currentHp, true);
            if (currentHp <= 0.1f)
            {
                KillAddMinion(silent: false);
            }
        }

        public void KillAddMinion(bool silent)
        {
            if (isDead) return;
            isDead = true;
            currentHp = 0f;

            if (_tetherOrAttackLine != null)
            {
                _tetherOrAttackLine.enabled = false;
            }

            if (!silent)
            {
                string deathMsg = addType == AddMinionType.PowerSupplier_BossTether
                    ? "★ [동력 공급체] 처치 완료! 보스의 80% 피해 감소 보호막이 해제되었습니다! ★"
                    : "★ [암살자] 처치 완료! 힐러가 위협에서 벗어났습니다! ★";

                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.6f,
                    $"{addDisplayName} 처치 완료!!",
                    new Color(1f, 0.92f, 0.25f, 1f),
                    true
                );

                if (ownerBoss != null)
                {
                    ownerBoss.ShowExternalAlert(deathMsg, new Color(0.35f, 1.0f, 0.55f, 1f), 3.8f);
                }
            }

            if (spawner != null)
            {
                spawner.NotifyAddKilled(this);
            }

            Destroy(gameObject, 0.05f);
        }

        private IEnumerator HitFlashRoutine()
        {
            SetAddVisualColor(Color.white);
            yield return new WaitForSeconds(0.09f);
            if (!isDead)
            {
                SetAddVisualColor(_baseColor);
            }
            _hitFlashCoroutine = null;
        }

        private void SetAddVisualColor(Color c)
        {
            if (dualVisual != null)
            {
                dualVisual.SetPrimaryColor(c);
            }
            SetRendererColor(bodyRenderer, c);
        }

        private void UpdateOverheadHpBar()
        {
            if (_hpBarRoot == null) return;

            bool is2D = ViewModeManager.Is2DViewActive;
            _hpBarRoot.position = transform.position + (is2D ? new Vector3(0f, 1.65f, 1.25f) : new Vector3(0f, 1.65f, 0f));

            Camera cam = Camera.main;
            if (cam != null)
            {
                _hpBarRoot.rotation = cam.transform.rotation;
            }

            if (_hpFillBar != null)
            {
                float ratio = HpRatio;
                const float fullWidth = 1.40f;
                _hpFillBar.localScale = new Vector3(fullWidth * ratio, 0.13f, 1f);
                _hpFillBar.localPosition = new Vector3(-fullWidth * (1f - ratio) * 0.5f, 0f, -0.01f);
            }

            if (_nameLabelText != null)
            {
                _nameLabelText.text = addType == AddMinionType.PowerSupplier_BossTether
                    ? $"[쫄 A] 동력 공급체 (보스 뎀감 80% | HP {Mathf.CeilToInt(currentHp)})"
                    : $"[쫄 B] 암살자 (표적: {GetAggroTargetDisplayName()} | HP {Mathf.CeilToInt(currentHp)})";
            }
        }

        private static void SetUnlitColor(Renderer r, Color color)
        {
            if (r == null) return;
            Shader s = Shader.Find("Sprites/Default");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
            r.sharedMaterial = new Material(s) { color = color };
        }

        private static void SetRendererColor(Renderer r, Color color)
        {
            if (r == null || r.material == null) return;
            r.material.color = color;
            if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", color);
            if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", color);
        }
    }

    /// <summary>
    /// Step 9 요구사항 4: 보스 '쫄(똘마니) 소환' 기믹 스포너 (BossAddSpawner.cs)
    /// - 보스 체력 70%, 30% 구간 도달 시 또는 보스가 [권속 소환 (Summon Adds)] 패턴을 시전할 때
    ///   전장 외곽에 2마리의 쫄(① 동력 공급체 + ② 암살자)을 동시에 소환합니다.
    /// </summary>
    public class BossAddSpawner : MonoBehaviour
    {
        public static BossAddSpawner Instance { get; private set; }

        [Header("References")]
        public BossController boss;
        public readonly List<BossAddMinion> activeAdds = new List<BossAddMinion>();

        [Header("Spawn Positions (XZ Plane)")]
        public Vector3 powerSupplierSpawnPos = new Vector3(-7.5f, 1.0f, 3.8f);
        public Vector3 assassinSpawnPos = new Vector3(7.5f, 1.0f, 3.8f);

        private bool _triggered70PercentWave;
        private bool _triggered30PercentWave;
        private int _lastObservedStage = -1;

        public bool HasLivingAdds
        {
            get
            {
                CleanupNullOrDeadAdds();
                return activeAdds.Count > 0;
            }
        }

        public bool HasLivingPowerSupplier
        {
            get
            {
                CleanupNullOrDeadAdds();
                for (int i = 0; i < activeAdds.Count; i++)
                {
                    if (activeAdds[i] != null &&
                        !activeAdds[i].isDead &&
                        activeAdds[i].addType == AddMinionType.PowerSupplier_BossTether)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (boss == null)
            {
                boss = GetComponent<BossController>();
            }
            if (boss == null)
            {
                boss = FindFirstObjectByType<BossController>();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (boss == null || Time.timeScale <= 0.001f)
            {
                return;
            }

            // 스테이지 전환 시 웨이브 트리거 초기화
            if (_lastObservedStage != boss.currentStageNumber)
            {
                _lastObservedStage = boss.currentStageNumber;
                ResetWaveFlagsAndClearAdds();
            }

            if (boss.IsBossDefeatedCollapse)
            {
                ClearAllActiveAdds();
                return;
            }

            CleanupNullOrDeadAdds();

            // 보스 체력 70%, 30% 구간 도달 시 자동으로 '권속 소환 (Summon Adds)' 발동!
            float hpRatio = boss.HpRatio;
            if (!_triggered70PercentWave && hpRatio <= 0.72f && hpRatio > 0.35f)
            {
                _triggered70PercentWave = true;
                SpawnAddPairWave("보스 HP 70% 구간 권속 소환!");
            }
            else if (!_triggered30PercentWave && hpRatio <= 0.32f && hpRatio > 0.02f)
            {
                _triggered30PercentWave = true;
                SpawnAddPairWave("보스 HP 30% 구간 긴급 권속 소환!");
            }
        }

        public void ResetWaveFlagsAndClearAdds()
        {
            _triggered70PercentWave = false;
            _triggered30PercentWave = false;
            ClearAllActiveAdds();
        }

        public void ClearAllActiveAdds()
        {
            for (int i = activeAdds.Count - 1; i >= 0; i--)
            {
                if (activeAdds[i] != null)
                {
                    Destroy(activeAdds[i].gameObject);
                }
            }
            activeAdds.Clear();
        }

        public void SpawnAddPairWave(string reasonLabel = "권속 소환 (Summon Adds)")
        {
            if (boss == null || boss.IsBossDefeatedCollapse)
            {
                return;
            }

            CleanupNullOrDeadAdds();

            // 이미 동력 공급체나 암살자가 살아있다면 중복 소환 방지 후 없는 종류만 충원
            bool hasSupplier = false;
            bool hasAssassin = false;
            foreach (BossAddMinion m in activeAdds)
            {
                if (m == null || m.isDead) continue;
                if (m.addType == AddMinionType.PowerSupplier_BossTether) hasSupplier = true;
                if (m.addType == AddMinionType.Assassin_HealerHunter) hasAssassin = true;
            }

            int stageNum = boss.currentStageNumber;

            if (!hasSupplier)
            {
                GameObject supplierObj = new GameObject("Add_PowerSupplier_Purple");
                BossAddMinion supplier = supplierObj.AddComponent<BossAddMinion>();
                supplier.InitializeAdd(
                    AddMinionType.PowerSupplier_BossTether,
                    boss,
                    this,
                    powerSupplierSpawnPos,
                    stageNum
                );
                activeAdds.Add(supplier);
            }

            if (!hasAssassin)
            {
                GameObject assassinObj = new GameObject("Add_Assassin_Crimson");
                BossAddMinion assassin = assassinObj.AddComponent<BossAddMinion>();
                assassin.InitializeAdd(
                    AddMinionType.Assassin_HealerHunter,
                    boss,
                    this,
                    assassinSpawnPos,
                    stageNum
                );
                activeAdds.Add(assassin);
            }

            boss.ShowExternalAlert(
                $"★ {reasonLabel} — [쫄 A: 동력 공급체(보스 피해 80% 감소)] & [쫄 B: 암살자(힐러 추격)] 등장! [Tab]키나 적 목록 클릭으로 타겟팅해 처치/도발하세요! ★",
                new Color(0.95f, 0.45f, 1.0f, 1f),
                5.0f
            );

            FloatingTextPopup.Spawn(
                boss.transform.position + Vector3.up * (boss.popupHeightOffset + 0.5f),
                "권속 소환 (Summon Adds)!! [동력 공급체 & 암살자 등장!]",
                new Color(0.90f, 0.40f, 1.0f, 1f),
                true
            );
        }

        public void NotifyAddKilled(BossAddMinion minion)
        {
            activeAdds.Remove(minion);
            CleanupNullOrDeadAdds();

            if (TargetingManager.Instance != null && minion != null && TargetingManager.Instance.CurrentTarget == minion.transform)
            {
                // 처치된 쫄을 타겟팅 중이었다면 남은 쫄 또는 보스로 자동 타겟 전환!
                TargetingManager.Instance.AutoSwitchTargetAfterEnemyDeath();
            }
        }

        /// <summary>
        /// AI 봇들이 쫄 소환 시 우선 공격/도발할 대상을 반환합니다.
        /// - 딜러 봇(MeleeDPS / RangedDPS): [동력 공급체] 1순위 ➔ [암살자] 2순위
        /// - 탱커 봇(Tank): 힐러를 노리는 [암살자] 1순위 도발/공격 ➔ [동력 공급체] 2순위
        /// </summary>
        public BossAddMinion GetPriorityAddForBot(PartyBotRole botRole)
        {
            CleanupNullOrDeadAdds();
            if (activeAdds.Count == 0)
            {
                return null;
            }

            AddMinionType preferredType = botRole == PartyBotRole.Tank
                ? AddMinionType.Assassin_HealerHunter
                : AddMinionType.PowerSupplier_BossTether;

            for (int i = 0; i < activeAdds.Count; i++)
            {
                BossAddMinion m = activeAdds[i];
                if (m != null && !m.isDead && m.addType == preferredType)
                {
                    return m;
                }
            }

            for (int i = 0; i < activeAdds.Count; i++)
            {
                BossAddMinion m = activeAdds[i];
                if (m != null && !m.isDead)
                {
                    return m;
                }
            }

            return null;
        }

        public void CleanupNullOrDeadAdds()
        {
            for (int i = activeAdds.Count - 1; i >= 0; i--)
            {
                if (activeAdds[i] == null || activeAdds[i].isDead)
                {
                    activeAdds.RemoveAt(i);
                }
            }
        }
    }
}
