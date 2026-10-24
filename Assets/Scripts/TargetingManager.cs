using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public enum TargetFaction
    {
        None = 0,
        Friendly = 1,
        Enemy = 2
    }

    /// <summary>
    /// Step 9 핵심 요구사항 1: 파판14(FF14) 스타일 통합 타겟팅 매니저 (TargetingManager.cs)
    /// - 플레이어는 현재 선택한 타겟(currentTarget)을 가지며, 아군(Player, PartyBotAI)과 적(Boss, BossAddMinion) 모두 타겟이 될 수 있습니다.
    /// - 시각적 타겟 인디케이터:
    ///   1) 발밑 선택 링: 아군 = 밝은 하늘색(#33E6FF), 적 = 밝은 노란색(#FFE626)
    ///   2) 머리 위 역삼각형 타겟 화살표 마커(▼): 위아래로 부드럽게 움직이며 3D/2D 뷰 모두 표시
    ///   3) 화면 상단 중앙 '타겟 정보창 UI': 현재 내 타겟의 이름, 체력바, 시전/상태, '타겟의 대상(Target of Target)' 실시간 표시
    /// - 타겟팅 조작 방식:
    ///   1) 마우스 좌클릭: 3D 캡슐 / 2D 스프라이트 Raycast + XZ 평면 반경 판정, 좌측 파티 리스트 UI / 적 목록 UI 슬롯 클릭 연동
    ///   2) F1 ~ F8 단축키: 좌측 파티 리스트 순서에 따라 F1(나 자신), F2(2번 파티원), F3(3번 파티원), F4(4번 파티원) 즉시 타겟 지정 (F8까지 확장 구조)
    ///   3) Tab 키: 전장에 살아있는 적(보스 및 소환된 쫄들)을 플레이어로부터 가까운 거리순으로 순환 타겟팅
    ///   4) [T] 키 (대상의 대상 전환) & [ESC] (타겟 해제)
    /// </summary>
    public class TargetingManager : MonoBehaviour
    {
        public static TargetingManager Instance { get; private set; }

        [Header("Core References")]
        public PlayerController player;
        public BossController boss;
        public RoleSelectionManager roleManager;
        public PartyListUI partyListUI;

        [Header("Current Target State")]
        [SerializeField] private Transform currentTarget;
        public Transform CurrentTarget => currentTarget;

        // 발밑 선택 링 & 머리 위 역삼각형 마커
        private LineRenderer _outerSelectionRing;
        private LineRenderer _innerSelectionRing;
        private Transform _overheadMarkerRoot;
        private MeshRenderer _overheadMarkerRenderer3D;
        private SpriteRenderer _overheadMarkerRenderer2D;

        // 상단 중앙 타겟 정보창 UI
        private GameObject _targetHudRoot;
        private Image _targetHudBg;
        private Outline _targetHudOutline;
        private Text _targetNameText;
        private Image _targetHpFill;
        private Text _targetHpText;
        private Text _targetStatusText;

        // 타겟의 대상 (Target of Target) UI
        private GameObject _totBoxRoot;
        private Image _totBgImage;
        private Text _totNameText;
        private Text _totHpText;
        private Button _totButton;

        // 화면 공간 플로팅 역삼각형 마커 배지
        private RectTransform _floatingMarkerRect;
        private Text _floatingMarkerArrowText;
        private Text _floatingMarkerLabelText;

        private static readonly Color FriendlyRingColor = new Color(0.20f, 0.90f, 1.00f, 0.98f); // 밝은 하늘색 (아군)
        private static readonly Color EnemyRingColor = new Color(1.00f, 0.90f, 0.15f, 0.98f);    // 밝은 노란색 (적)

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            RefreshSceneReferences();
            EnsureUnitCollidersForRaycast();
            BuildVisualIndicators();
            BuildTargetInfoHUD();

            if (boss != null && currentTarget == null)
            {
                SetTarget(boss.transform);
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
            if (player == null || boss == null)
            {
                RefreshSceneReferences();
            }

            ValidateCurrentTarget();
            if (!LocalGameplayInput.Blocked)
            {
                HandleKeyboardShortcuts();
                HandleMouseClickTargeting();
            }
            UpdateVisualIndicators();
            UpdateTargetInfoHUD();
        }

        public void RefreshSceneReferences()
        {
            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (boss == null) boss = FindFirstObjectByType<BossController>();
            if (roleManager == null) roleManager = FindFirstObjectByType<RoleSelectionManager>();
            if (partyListUI == null) partyListUI = FindFirstObjectByType<PartyListUI>();
        }

        /// <summary>
        /// 플레이어, AI 봇, 보스 오브젝트 루트에 마우스 좌클릭 Raycast용 Collider가 보장되도록 합니다.
        /// </summary>
        public void EnsureUnitCollidersForRaycast()
        {
            if (player != null && player.GetComponent<Collider>() == null)
            {
                CapsuleCollider col = player.gameObject.AddComponent<CapsuleCollider>();
                col.radius = 0.75f;
                col.height = 2.2f;
                col.center = Vector3.zero;
            }

            if (boss != null && boss.GetComponent<Collider>() == null)
            {
                CapsuleCollider col = boss.gameObject.AddComponent<CapsuleCollider>();
                col.radius = 1.65f;
                col.height = 3.8f;
                col.center = new Vector3(0f, 1.8f, 0f);
            }

            PartyBotAI[] bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            foreach (PartyBotAI bot in bots)
            {
                if (bot != null && bot.GetComponent<Collider>() == null)
                {
                    CapsuleCollider col = bot.gameObject.AddComponent<CapsuleCollider>();
                    col.radius = 0.75f;
                    col.height = 2.2f;
                    col.center = Vector3.zero;
                }
            }
        }

        // =====================================================================
        // 타겟 상태 검증 및 진영/정보 조회
        // =====================================================================

        private void ValidateCurrentTarget()
        {
            if (currentTarget == null) return;

            BossAddMinion add = currentTarget.GetComponent<BossAddMinion>();
            if (add != null && add.isDead)
            {
                AutoSwitchTargetAfterEnemyDeath();
            }
        }

        public Transform GetLivingOffensiveTarget(BossController fallbackBoss)
        {
            if (currentTarget != null)
            {
                if (CombatRange.LivingEnemy(currentTarget)) return currentTarget;
                if (!CombatRange.LivingPartyMember(currentTarget)) return null;
                Transform enemy = GetTargetOfTarget(currentTarget);
                if (CombatRange.LivingEnemy(enemy)) return enemy;
            }
            // Preserve Step 9's no-target / friendly-target boss fallback.
            return fallbackBoss != null && CombatRange.LivingEnemy(fallbackBoss.transform) ? fallbackBoss.transform : null;
        }

        public void AutoSwitchTargetAfterEnemyDeath()
        {
            // 살아있는 다른 쫄이 있으면 최우선 자동 타겟팅
            if (BossAddSpawner.Instance != null)
            {
                BossAddSpawner.Instance.CleanupNullOrDeadAdds();
                for (int i = 0; i < BossAddSpawner.Instance.activeAdds.Count; i++)
                {
                    BossAddMinion add = BossAddSpawner.Instance.activeAdds[i];
                    if (add != null && !add.isDead)
                    {
                        SetTarget(add.transform);
                        return;
                    }
                }
            }

            // 남은 쫄이 없으면 보스로 자동 타겟 복귀
            if (boss != null && !boss.IsBossDefeatedCollapse)
            {
                SetTarget(boss.transform);
            }
            else
            {
                ClearTarget();
            }
        }

        public TargetFaction GetTargetFaction(Transform t)
        {
            if (t == null) return TargetFaction.None;
            if (t.GetComponent<NetworkRaidAvatar>() != null) return TargetFaction.Friendly;
            if (t.GetComponent<PlayerController>() != null || t.GetComponent<PartyBotAI>() != null)
                return TargetFaction.Friendly;
            if (t.GetComponent<BossController>() != null || t.GetComponent<BossAddMinion>() != null)
                return TargetFaction.Enemy;
            return TargetFaction.None;
        }

        public bool IsCurrentTargetFriendly => GetTargetFaction(currentTarget) == TargetFaction.Friendly;
        public bool IsCurrentTargetEnemy => GetTargetFaction(currentTarget) == TargetFaction.Enemy;

        public void SetTarget(Transform newTarget)
        {
            if (newTarget == null)
            {
                ClearTarget();
                return;
            }

            Transform resolved = ResolveUnitRoot(newTarget);
            if (resolved == null) return;

            currentTarget = resolved;
        }

        public void ClearTarget()
        {
            currentTarget = null;
        }

        private Transform ResolveUnitRoot(Transform hitTransform)
        {
            if (hitTransform == null) return null;
            var network = hitTransform.GetComponentInParent<NetworkRaidAvatar>();
            if (network != null) return network.transform;

            BossAddMinion add = hitTransform.GetComponentInParent<BossAddMinion>();
            if (add != null && !add.isDead) return add.transform;

            BossController b = hitTransform.GetComponentInParent<BossController>();
            if (b != null) return b.transform;

            PlayerController p = hitTransform.GetComponentInParent<PlayerController>();
            if (p != null) return p.transform;

            PartyBotAI bot = hitTransform.GetComponentInParent<PartyBotAI>();
            if (bot != null) return bot.transform;

            return null;
        }

        /// <summary>
        /// 대상(Unit)이 현재 바라보고 있는 '대상의 대상(Target of Target)'을 반환합니다.
        /// </summary>
        public Transform GetTargetOfTarget(Transform unit)
        {
            if (unit == null) return null;
            if (unit.GetComponent<NetworkRaidAvatar>() is NetworkRaidAvatar avatar) return avatar.CombatTarget;

            // 1. 보스 -> 보스의 어그로 1순위 대상 (탱커 등)
            BossController b = unit.GetComponent<BossController>();
            if (b != null)
            {
                return b.aggroTarget != null ? b.aggroTarget : (player != null ? player.transform : null);
            }

            // 2. 보스 쫄 -> 쫄의 타겟 (동력 공급체는 보스, 암살자는 힐러 또는 도발한 탱커)
            BossAddMinion add = unit.GetComponent<BossAddMinion>();
            if (add != null)
            {
                return add.aggroTarget != null ? add.aggroTarget : (boss != null ? boss.transform : null);
            }

            // 3. 플레이어 -> 플레이어의 현재 타겟 (자기 자신 타겟 중이면 보스 반환)
            PlayerController p = unit.GetComponent<PlayerController>();
            if (p != null)
            {
                if (currentTarget != null && currentTarget != p.transform)
                {
                    return currentTarget;
                }
                return boss != null ? boss.transform : null;
            }

            // 4. AI 파티원 봇 -> 봇이 현재 바라보는 타겟 (쫄 우선 공격 중이면 해당 쫄, 아니면 보스)
            PartyBotAI bot = unit.GetComponent<PartyBotAI>();
            if (bot != null)
            {
                if (bot.CurrentActionTarget != null)
                {
                    return bot.CurrentActionTarget;
                }
                return boss != null ? boss.transform : null;
            }

            return null;
        }

        public string GetUnitDisplayName(Transform unit)
        {
            if (unit == null) return "없음";
            var network = unit.GetComponent<NetworkRaidAvatar>();
            if (network != null) return network.DisplayName + " · " + network.RoleName;

            BossController b = unit.GetComponent<BossController>();
            if (b != null)
            {
                return $"[BOSS] {b.currentBossTitle}";
            }

            BossAddMinion add = unit.GetComponent<BossAddMinion>();
            if (add != null)
            {
                return add.addDisplayName;
            }

            PlayerController p = unit.GetComponent<PlayerController>();
            if (p != null)
            {
                return $"★ {p.playerRoleDisplayName}";
            }

            PartyBotAI bot = unit.GetComponent<PartyBotAI>();
            if (bot != null)
            {
                return bot.botDisplayName;
            }

            return unit.name;
        }

        public void GetUnitHP(Transform unit, out float currentHp, out float maxHp, out float shield)
        {
            currentHp = 0f;
            maxHp = 1f;
            shield = 0f;
            if (unit == null) return;
            if (RaidCombatAuthority.Replica && unit.GetComponent<NetworkRaidAvatar>()?.State is RaidCombatActor state) { currentHp = state.hp; maxHp = state.maxHp; shield = state.shield; return; }

            BossController b = unit.GetComponent<BossController>();
            if (b != null)
            {
                currentHp = b.currentHp;
                maxHp = b.maxHp;
                return;
            }

            BossAddMinion add = unit.GetComponent<BossAddMinion>();
            if (add != null)
            {
                currentHp = add.currentHp;
                maxHp = add.maxHp;
                return;
            }

            PlayerController p = unit.GetComponent<PlayerController>();
            if (p != null)
            {
                currentHp = p.currentHp;
                maxHp = p.maxHp;
                shield = p.currentShield;
                return;
            }

            PartyBotAI bot = unit.GetComponent<PartyBotAI>();
            if (bot != null)
            {
                currentHp = bot.currentHp;
                maxHp = bot.maxHp;
                shield = bot.currentShield;
                return;
            }
        }

        public string GetUnitStatusSummary(Transform unit)
        {
            if (unit == null) return "";
            if (unit.GetComponent<NetworkRaidAvatar>()?.State is RaidCombatActor state)
                return state.dead ? "전투 불능 · [5] 부활 대상" : state.resurrecting ? $"부활 시전 {state.resurrection:0.0}s" : state.casting ? $"{state.castName} {state.castRemaining:0.0}s" : $"{(state.protection > 0 ? "부활 보호 · " : "")}{(state.defense > 0 ? "방어기 · " : "")}전투 중";

            BossController b = unit.GetComponent<BossController>();
            if (b != null)
            {
                bool hasSupplier = BossAddSpawner.Instance != null && BossAddSpawner.Instance.HasLivingPowerSupplier;
                string supplierTag = hasSupplier ? " | ★동력공급: 받는피해 80% 감소!" : "";
                if (b.IsCasting)
                {
                    int pct = Mathf.Clamp(Mathf.RoundToInt(b.CastProgress * 100f), 0, 100);
                    return $"⚡ 시전 중: [{b.CurrentCastSkillName}] ({pct}%){supplierTag}";
                }
                if (b.IsGroggy)
                {
                    return $"★ 무력화(그로기) 상태! 받는 피해 +50% 증가!{supplierTag}";
                }
                return $"페이즈 {(int)b.currentPhase} 전투 중{supplierTag}";
            }

            BossAddMinion add = unit.GetComponent<BossAddMinion>();
            if (add != null)
            {
                return add.CurrentCastOrStatusText;
            }

            PlayerController p = unit.GetComponent<PlayerController>();
            if (p != null)
            {
                List<string> tags = new List<string>();
                if (p.IsDead) return "전투 불능 · [5] 힐러 부활 대상";
                if (p.Potion != null && p.Potion.HasRevivalProtection) tags.Add("부활 보호");
                if (p.IsDefensiveBuffActive) tags.Add($"생존기 ON (피해감소 {Mathf.RoundToInt(p.CurrentDamageReduction * 100f)}%)");
                if (p.currentShield > 1f) tags.Add($"보호막 +{Mathf.RoundToInt(p.currentShield)}");
                if (p.HasTacticalRescueBuff) tags.Add("전술적 구출(공격력 2배)");
                return tags.Count > 0 ? string.Join(" | ", tags) : "전투 정상 (F1 = 나 자신 타겟)";
            }

            PartyBotAI bot = unit.GetComponent<PartyBotAI>();
            if (bot != null)
            {
                if (bot.isDead) return "전투 불능 (부활 대기 중)";
                List<string> tags = new List<string>();
                if (bot.Potion != null && bot.Potion.HasRevivalProtection) tags.Add("부활 보호");
                if (bot.isTankDefensiveActive) tags.Add("철벽 방어 ON");
                if (bot.IsInvulnerable) tags.Add("구출 무적 ON");
                if (bot.currentShield > 1f) tags.Add($"보호막 +{Mathf.RoundToInt(bot.currentShield)}");
                if (bot.IsInterceptingTetherForPlayer) tags.Add("★탱버 줄 가로채기 중!");
                return tags.Count > 0 ? string.Join(" | ", tags) : "AI 자율 레이드 수행 중";
            }

            return "";
        }

        // =====================================================================
        // 1~4) 키보드 단축키 & 마우스 좌클릭 타겟팅
        // =====================================================================

        private void HandleKeyboardShortcuts()
        {
            for (int i = 0; i < 8; i++) if (LocalInputBindings.Pressed("Party" + (i + 1))) { SelectPartyMemberByIndex(i); break; }
            if (LocalInputBindings.Pressed("ClearTarget")) ClearTarget();
            if (LocalInputBindings.Pressed("CycleEnemy")) CycleEnemyTarget();
            if (LocalInputBindings.Pressed("TargetOfTarget")) SwapToTargetOfTarget();
        }

        /// <summary>
        /// 좌측 파티 리스트 순서(0: F1 나 자신, 1: F2 2번 파티원, 2: F3 3번 파티원, 3: F4 4번 파티원, 최대 F8 확장)에 따라 즉시 타겟팅합니다.
        /// </summary>
        public void SelectPartyMemberByIndex(int partyIndex)
        {
            List<Transform> partyMembers = GetOrderedPartyMembers();
            if (partyIndex >= 0 && partyIndex < partyMembers.Count && partyMembers[partyIndex] != null)
            {
                SetTarget(partyMembers[partyIndex]);
            }
        }

        public List<Transform> GetOrderedPartyMembers()
        {
            if (RaidNetworkSession.Multiplayer) return RaidNetworkSession.Instance.OrderedParty();
            List<Transform> list = new List<Transform>(8);
            if (player != null)
            {
                list.Add(player.transform);
            }

            if (partyListUI != null && partyListUI.partyBots != null && partyListUI.partyBots.Count > 0)
            {
                for (int i = 0; i < partyListUI.partyBots.Count; i++)
                {
                    if (partyListUI.partyBots[i] != null)
                    {
                        list.Add(partyListUI.partyBots[i].transform);
                    }
                }
            }
            else if (roleManager != null && roleManager.partyBots != null && roleManager.partyBots.Count > 0)
            {
                for (int i = 0; i < roleManager.partyBots.Count; i++)
                {
                    if (roleManager.partyBots[i] != null)
                    {
                        list.Add(roleManager.partyBots[i].transform);
                    }
                }
            }
            else
            {
                PartyBotAI[] found = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
                Array.Sort(found, (a, b) => string.Compare(a.gameObject.name, b.gameObject.name, StringComparison.Ordinal));
                for (int i = 0; i < found.Length; i++)
                {
                    if (found[i] != null) list.Add(found[i].transform);
                }
            }

            return list;
        }

        /// <summary>
        /// [Tab] 키를 누를 때마다 현재 전장에 살아있는 적(보스 및 소환된 쫄들)을
        /// 플레이어로부터 가까운 거리순으로 하나씩 순환하며 타겟을 변경합니다.
        /// (현재 타겟이 아군이거나 없을 때 Tab을 누르면 살아있는 쫄 또는 가장 가까운 적을 우선 타겟팅)
        /// </summary>
        public void CycleEnemyTarget()
        {
            List<Transform> enemies = GetLivingEnemiesSortedByDistance();
            if (enemies.Count == 0) return;

            if (currentTarget == null || IsCurrentTargetFriendly)
            {
                // 살아있는 쫄이 있으면 가장 가까운 쫄을 최우선 선택, 없으면 가장 가까운 적(보스) 선택
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i].GetComponent<BossAddMinion>() != null)
                    {
                        SetTarget(enemies[i]);
                        return;
                    }
                }
                SetTarget(enemies[0]);
                return;
            }

            int currentIdx = enemies.IndexOf(currentTarget);
            int nextIdx = (currentIdx + 1) % enemies.Count;
            SetTarget(enemies[nextIdx]);
        }

        public List<Transform> GetLivingEnemiesSortedByDistance()
        {
            List<Transform> enemies = new List<Transform>(8);

            if (BossAddSpawner.Instance != null)
            {
                BossAddSpawner.Instance.CleanupNullOrDeadAdds();
                var adds = BossAddSpawner.Instance.activeAdds;
                for (int i = 0; i < adds.Count; i++)
                {
                    if (adds[i] != null && !adds[i].isDead)
                    {
                        enemies.Add(adds[i].transform);
                    }
                }
            }

            if (boss != null && !boss.IsBossDefeatedCollapse)
            {
                enemies.Add(boss.transform);
            }

            Vector3 refPos = player != null ? player.transform.position : Vector3.zero;
            enemies.Sort((a, b) =>
            {
                float dA = Vector2.Distance(new Vector2(a.position.x, a.position.z), new Vector2(refPos.x, refPos.z));
                float dB = Vector2.Distance(new Vector2(b.position.x, b.position.z), new Vector2(refPos.x, refPos.z));
                return dA.CompareTo(dB);
            });

            return enemies;
        }

        /// <summary>
        /// [T] 키: 현재 타겟이 바라보고 있는 대상(Target of Target)으로 즉시 타겟 스왑
        /// </summary>
        public void SwapToTargetOfTarget()
        {
            if (currentTarget == null)
            {
                if (boss != null) SetTarget(boss.transform);
                return;
            }

            Transform tot = GetTargetOfTarget(currentTarget);
            if (tot != null && tot != currentTarget)
            {
                SetTarget(tot);
            }
        }

        /// <summary>
        /// 마우스 좌클릭 타겟팅:
        /// 1) 좌측 파티 리스트 UI / 적 목록 UI 슬롯 클릭 직접 판정
        /// 2) 3D 캡슐 콜라이더 RaycastAll 판정
        /// 3) 화면 좌표(Screen Space) + XZ 평면 반경 판정 (3D 쿼터뷰 및 2D 탑다운 스프라이트 모두 100% 정밀 클릭 보장)
        /// </summary>
        private void HandleMouseClickTargeting()
        {
            if (QuarterViewCamera.LocalCamera != null && QuarterViewCamera.LocalCamera.IsOrbitDragging) return;
            bool leftClickDown = false;
            Vector3 mousePos = Vector3.zero;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    leftClickDown = true;
                }
                Vector2 mp = Mouse.current.position.ReadValue();
                mousePos = new Vector3(mp.x, mp.y, 0f);
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!leftClickDown && Input.GetMouseButtonDown(0))
            {
                leftClickDown = true;
            }
            if (mousePos.sqrMagnitude < 0.001f)
            {
                mousePos = Input.mousePosition;
            }
#endif
            if (!leftClickDown) return;
            if (CombatMetricsUI.BlocksPointer(mousePos)) return;

            // 0차: 좌측 파티 리스트(PartyListUI) 슬롯 직접 클릭 체크
            if (partyListUI != null && partyListUI.TrySelectPartyMemberAtScreenPoint(mousePos))
            {
                return;
            }

            // 0-B차: 좌측 적 목록(EnemyListUI) 슬롯 직접 클릭 체크
            if (EnemyListUI.Instance != null && EnemyListUI.Instance.TrySelectEnemyAtScreenPoint(mousePos))
            {
                return;
            }

            // UI 버튼(스킬 핫바, 직업 선택 모달, 카드 모달, 뷰 전환 버튼 등) 위를 클릭한 경우 월드 클릭 무시
            if (LocalGameplayInput.PointerOverUI(mousePos))
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(mousePos);

            // 1차: Physics.RaycastAll (3D 캡슐 콜라이더 정밀 클릭)
            RaycastHit[] hits = Physics.RaycastAll(ray, 250f);
            Transform bestHit = null;
            float bestHitDist = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                Transform unit = ResolveUnitRoot(hits[i].transform);
                if (unit != null && hits[i].distance < bestHitDist)
                {
                    bestHit = unit;
                    bestHitDist = hits[i].distance;
                }
            }

            if (bestHit != null)
            {
                SetTarget(bestHit);
                return;
            }

            // 후보군 수집: 살아있는 쫄 -> 파티원/나 -> 보스
            List<Transform> candidates = new List<Transform>();
            if (BossAddSpawner.Instance != null)
            {
                foreach (BossAddMinion add in BossAddSpawner.Instance.activeAdds)
                {
                    if (add != null && !add.isDead) candidates.Add(add.transform);
                }
            }
            candidates.AddRange(GetOrderedPartyMembers());
            if (boss != null && !boss.IsBossDefeatedCollapse) candidates.Add(boss.transform);

            // 2차: 화면 공간(Screen Space) 픽셀 반경 판정 (3D 캡슐 상단이나 2D 스프라이트를 클릭했을 때 즉시 선택)
            Transform bestScreenCandidate = null;
            float bestScreenNorm = 1f;
            float dpiScale = Mathf.Max(0.75f, Screen.height / 1080f);

            for (int i = 0; i < candidates.Count; i++)
            {
                Transform c = candidates[i];
                if (c == null) continue;

                bool isBossUnit = c.GetComponent<BossController>() != null;
                bool isAddUnit = c.GetComponent<BossAddMinion>() != null;

                Vector3 worldCenter = isBossUnit
                    ? c.position + new Vector3(0f, 1.5f, 0f)
                    : c.position + new Vector3(0f, 0.5f, 0f);
                if (ViewModeManager.Is2DViewActive)
                {
                    worldCenter = new Vector3(c.position.x, 0.15f, c.position.z);
                }

                Vector3 sp = cam.WorldToScreenPoint(worldCenter);
                if (sp.z <= 0f) continue;

                float maxPixelRadius = (isBossUnit ? 95f : (isAddUnit ? 62f : 56f)) * dpiScale;
                float pixelDist = Vector2.Distance(new Vector2(mousePos.x, mousePos.y), new Vector2(sp.x, sp.y));
                if (pixelDist <= maxPixelRadius)
                {
                    float norm = pixelDist / maxPixelRadius;
                    if (norm < bestScreenNorm)
                    {
                        bestScreenNorm = norm;
                        bestScreenCandidate = c;
                    }
                }
            }

            if (bestScreenCandidate != null)
            {
                SetTarget(bestScreenCandidate);
                return;
            }

            // 3차: XZ 평면(Y=1.0f 유닛 중심 높이) 교차점 기반 타겟 판정
            Plane unitPlane = new Plane(Vector3.up, new Vector3(0f, 1.0f, 0f));
            if (unitPlane.Raycast(ray, out float enter))
            {
                Vector3 clickWorldPos = ray.GetPoint(enter);
                Vector2 clickXZ = new Vector2(clickWorldPos.x, clickWorldPos.z);

                Transform closestCandidate = null;
                float minNormalizedDist = 1f;

                for (int i = 0; i < candidates.Count; i++)
                {
                    Transform c = candidates[i];
                    if (c == null) continue;

                    float pickRadius = 1.65f;
                    if (c.GetComponent<BossController>() != null) pickRadius = Mathf.Max(0.01f, CombatRange.Radius(c));
                    else if (c.GetComponent<BossAddMinion>() != null) pickRadius = 1.85f;

                    Vector2 cXZ = new Vector2(c.position.x, c.position.z);
                    float dist = Vector2.Distance(clickXZ, cXZ);
                    if (dist <= pickRadius)
                    {
                        float norm = dist / pickRadius;
                        if (norm < minNormalizedDist)
                        {
                            minNormalizedDist = norm;
                            closestCandidate = c;
                        }
                    }
                }

                if (closestCandidate != null)
                {
                    SetTarget(closestCandidate);
                }
            }
        }

        // =====================================================================
        // 시각적 타겟 인디케이터 (발밑 링 + 머리 위 역삼각형 화살표 마커)
        // =====================================================================

        private void BuildVisualIndicators()
        {
            Shader lineShader = Shader.Find("Sprites/Default");
            if (lineShader == null) lineShader = Shader.Find("Universal Render Pipeline/Unlit");

            // 1. 발밑 외곽 선택 링
            GameObject ringObj = new GameObject("TargetSelectionRing_Outer");
            ringObj.transform.SetParent(transform, false);
            _outerSelectionRing = ringObj.AddComponent<LineRenderer>();
            _outerSelectionRing.useWorldSpace = true;
            _outerSelectionRing.loop = true;
            _outerSelectionRing.startWidth = 0.15f;
            _outerSelectionRing.endWidth = 0.15f;
            _outerSelectionRing.sharedMaterial = new Material(lineShader);
            _outerSelectionRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _outerSelectionRing.receiveShadows = false;

            // 2. 발밑 내부 보조 선택 링
            GameObject innerObj = new GameObject("TargetSelectionRing_Inner");
            innerObj.transform.SetParent(transform, false);
            _innerSelectionRing = innerObj.AddComponent<LineRenderer>();
            _innerSelectionRing.useWorldSpace = true;
            _innerSelectionRing.loop = true;
            _innerSelectionRing.startWidth = 0.06f;
            _innerSelectionRing.endWidth = 0.06f;
            _innerSelectionRing.sharedMaterial = new Material(lineShader);
            _innerSelectionRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _innerSelectionRing.receiveShadows = false;

            // 3. 머리 위 상하로 움직이는 역삼각형 타겟 화살표 마커 (▼)
            _overheadMarkerRoot = new GameObject("TargetOverheadArrowMarker").transform;
            _overheadMarkerRoot.SetParent(transform, false);

            GameObject meshChild = new GameObject("Arrow3DMesh");
            meshChild.transform.SetParent(_overheadMarkerRoot, false);
            MeshFilter mf = meshChild.AddComponent<MeshFilter>();
            mf.sharedMesh = CreateInvertedTriangleMesh();
            _overheadMarkerRenderer3D = meshChild.AddComponent<MeshRenderer>();
            _overheadMarkerRenderer3D.sharedMaterial = new Material(lineShader);
            _overheadMarkerRenderer3D.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _overheadMarkerRenderer3D.receiveShadows = false;

            // 2D 탑다운 뷰에서도 선명하게 보이는 바닥 평행 역삼각형 스프라이트
            GameObject sprite2DChild = new GameObject("Arrow2DSprite");
            sprite2DChild.transform.SetParent(_overheadMarkerRoot, false);
            sprite2DChild.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sprite2DChild.transform.localScale = new Vector3(0.85f, 0.85f, 1f);
            _overheadMarkerRenderer2D = sprite2DChild.AddComponent<SpriteRenderer>();
            _overheadMarkerRenderer2D.sprite = CreateInvertedTriangleSprite();
            _overheadMarkerRenderer2D.sortingOrder = 55;
        }

        private static Mesh CreateInvertedTriangleMesh()
        {
            Mesh m = new Mesh { name = "TargetInvertedTriangle" };
            Vector3[] verts = new Vector3[]
            {
                new Vector3(0f, -0.45f, 0f),    // 아래 꼭짓점
                new Vector3(-0.38f, 0.35f, 0f), // 좌상단
                new Vector3(0.38f, 0.35f, 0f),  // 우상단
                new Vector3(0f, 0.25f, -0.22f), // 앞면 볼륨
                new Vector3(0f, 0.25f, 0.22f)   // 뒷면 볼륨
            };
            int[] tris = new int[]
            {
                0, 1, 3,
                0, 3, 2,
                1, 2, 3,
                0, 4, 1,
                0, 2, 4,
                1, 4, 2
            };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            return m;
        }

        private static Sprite CreateInvertedTriangleSprite()
        {
            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Color clear = new Color(0f, 0f, 0f, 0f);

            for (int y = 0; y < size; y++)
            {
                float ny = (float)y / (size - 1);
                float halfWidth = Mathf.Lerp(0.03f, 0.45f, ny);
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs((float)x / (size - 1) - 0.5f);
                    if (nx <= halfWidth && ny >= 0.08f && ny <= 0.92f)
                    {
                        bool border = (nx >= halfWidth - 0.06f) || (ny >= 0.85f) || (ny <= 0.14f);
                        tex.SetPixel(x, y, border ? Color.white : new Color(1f, 1f, 1f, 0.92f));
                    }
                    else
                    {
                        tex.SetPixel(x, y, clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 64f);
        }

        private void UpdateVisualIndicators()
        {
            if (currentTarget == null)
            {
                if (_outerSelectionRing != null) _outerSelectionRing.enabled = false;
                if (_innerSelectionRing != null) _innerSelectionRing.enabled = false;
                if (_overheadMarkerRoot != null) _overheadMarkerRoot.gameObject.SetActive(false);
                if (_floatingMarkerRect != null) _floatingMarkerRect.gameObject.SetActive(false);
                return;
            }

            TargetFaction faction = GetTargetFaction(currentTarget);
            Color baseColor = (faction == TargetFaction.Friendly) ? FriendlyRingColor : EnemyRingColor;
            float pulse = 0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * 7.5f);
            Color activeColor = new Color(baseColor.r, baseColor.g, baseColor.b, pulse);

            float baseRadius = 1.08f;
            float headHeight = 2.85f;
            BossController selectedBoss = currentTarget.GetComponent<BossController>();
            if (selectedBoss != null)
            {
                baseRadius = selectedBoss.CombatRadius;
                headHeight = 4.45f;
            }
            else if (currentTarget.GetComponent<BossAddMinion>() != null)
            {
                baseRadius = 1.28f;
                headHeight = 3.05f;
            }

            // Pulse only the color for bosses; the visible boundary must not change its hitbox size.
            float animatedRadius = selectedBoss != null ? baseRadius : baseRadius + Mathf.Sin(Time.unscaledTime * 6f) * 0.06f;

            if (_outerSelectionRing != null)
            {
                // Preserve the boss's red/green/blue positional sectors instead of covering them in yellow.
                // The overhead marker and target HUD still indicate selection; a ring is drawn as fallback.
                bool hasBossPositionalRing = selectedBoss != null && selectedBoss.targetRing != null &&
                    selectedBoss.targetRing.gameObject.activeInHierarchy;
                _outerSelectionRing.enabled = !hasBossPositionalRing;
                _outerSelectionRing.startColor = activeColor;
                _outerSelectionRing.endColor = activeColor;
                DrawCircleOnXZ(_outerSelectionRing, currentTarget.position, animatedRadius, 48, 0.07f);
            }

            if (_innerSelectionRing != null)
            {
                _innerSelectionRing.enabled = selectedBoss == null;
                Color innerCol = new Color(baseColor.r, baseColor.g, baseColor.b, 0.55f);
                _innerSelectionRing.startColor = innerCol;
                _innerSelectionRing.endColor = innerCol;
                DrawCircleOnXZ(_innerSelectionRing, currentTarget.position, animatedRadius * 0.84f, 36, 0.065f);
            }

            if (_overheadMarkerRoot != null)
            {
                _overheadMarkerRoot.gameObject.SetActive(true);
                float bobOffset = Mathf.Sin(Time.unscaledTime * 5.5f) * 0.25f;
                _overheadMarkerRoot.position = currentTarget.position + new Vector3(0f, headHeight + bobOffset, 0f);
                _overheadMarkerRoot.rotation = Quaternion.Euler(0f, Time.unscaledTime * 110f, 0f);

                if (_overheadMarkerRenderer3D != null && _overheadMarkerRenderer3D.sharedMaterial != null)
                {
                    _overheadMarkerRenderer3D.sharedMaterial.color = baseColor;
                }

                if (_overheadMarkerRenderer2D != null)
                {
                    _overheadMarkerRenderer2D.color = baseColor;
                    _overheadMarkerRenderer2D.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    float zOffset2D = (currentTarget.GetComponent<BossController>() != null) ? 2.65f : 1.45f;
                    _overheadMarkerRenderer2D.transform.position = new Vector3(
                        currentTarget.position.x,
                        0.26f,
                        currentTarget.position.z + zOffset2D + bobOffset * 0.4f
                    );
                }
            }

            if (_floatingMarkerRect != null && Camera.main != null)
            {
                bool is2D = ViewModeManager.Is2DViewActive;
                Vector3 worldAnchor = currentTarget.position + new Vector3(0f, headHeight + 0.35f, 0f);
                if (is2D)
                {
                    float zOff = (currentTarget.GetComponent<BossController>() != null) ? 2.4f : 1.35f;
                    worldAnchor = currentTarget.position + new Vector3(0f, 0.2f, zOff);
                }

                Vector3 screenPos = Camera.main.WorldToScreenPoint(worldAnchor);
                if (screenPos.z > 0f)
                {
                    _floatingMarkerRect.gameObject.SetActive(true);
                    float bobPx = Mathf.Sin(Time.unscaledTime * 6f) * 6f;
                    _floatingMarkerRect.position = new Vector3(screenPos.x, screenPos.y + bobPx, 0f);
                    if (_floatingMarkerArrowText != null) _floatingMarkerArrowText.color = baseColor;
                    if (_floatingMarkerLabelText != null)
                    {
                        _floatingMarkerLabelText.color = baseColor;
                        _floatingMarkerLabelText.text = (faction == TargetFaction.Friendly) ? "▼ [아군 타겟]" : "▼ [공격 타겟]";
                    }
                }
                else
                {
                    _floatingMarkerRect.gameObject.SetActive(false);
                }
            }
        }

        private static void DrawCircleOnXZ(LineRenderer lr, Vector3 center, float radius, int segments, float yHeight)
        {
            lr.positionCount = segments;
            float angleStep = 360f / segments;
            for (int i = 0; i < segments; i++)
            {
                float rad = i * angleStep * Mathf.Deg2Rad;
                lr.SetPosition(i, new Vector3(
                    center.x + Mathf.Sin(rad) * radius,
                    yHeight,
                    center.z + Mathf.Cos(rad) * radius
                ));
            }
        }

        // =====================================================================
        // 상단 중앙 '타겟 정보창 UI (Target Info Bar + Target of Target)'
        // =====================================================================

        private void BuildTargetInfoHUD()
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;

            Transform oldMarker = canvas.transform.Find("TargetFloatingArrowUI");
            if (oldMarker != null) DestroyImmediate(oldMarker.gameObject);
            Transform oldHud = canvas.transform.Find("TargetInfoHUD_FFXIV");
            if (oldHud != null) DestroyImmediate(oldHud.gameObject);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            Sprite whiteSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f)
            );

            // 1. 화면 공간 플로팅 역삼각형 마커 배지
            GameObject floatMarkerObj = new GameObject("TargetFloatingArrowUI");
            floatMarkerObj.transform.SetParent(canvas.transform, false);
            _floatingMarkerRect = floatMarkerObj.AddComponent<RectTransform>();
            _floatingMarkerRect.sizeDelta = new Vector2(140f, 44f);

            _floatingMarkerLabelText = CreateUIText(floatMarkerObj.transform, "Label", "▼ [타겟]", font, 12, FontStyle.Bold, TextAnchor.MiddleCenter, FriendlyRingColor);
            _floatingMarkerLabelText.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 10f);
            _floatingMarkerLabelText.raycastTarget = false;

            _floatingMarkerArrowText = CreateUIText(floatMarkerObj.transform, "BigArrow", "▼", font, 22, FontStyle.Bold, TextAnchor.MiddleCenter, FriendlyRingColor);
            _floatingMarkerArrowText.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -8f);
            _floatingMarkerArrowText.raycastTarget = false;

            // 2. 상단 중앙 통합 타겟 정보창 (보스 체력바/캐스팅바 바로 아래에 배치)
            _targetHudRoot = new GameObject("TargetInfoHUD_FFXIV");
            _targetHudRoot.transform.SetParent(canvas.transform, false);
            RectTransform rootRect = _targetHudRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -166f);
            rootRect.sizeDelta = new Vector2(680f, 62f);

            _targetHudBg = _targetHudRoot.AddComponent<Image>();
            _targetHudBg.color = new Color(0.05f, 0.07f, 0.12f, 0.92f);

            _targetHudOutline = _targetHudRoot.AddComponent<Outline>();
            _targetHudOutline.effectColor = EnemyRingColor;
            _targetHudOutline.effectDistance = new Vector2(2f, -2f);

            // 좌측 메인 타겟 이름
            _targetNameText = CreateUIText(_targetHudRoot.transform, "TargetName", "[현재 타겟]", font, 14, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            RectTransform nameRect = _targetNameText.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(0f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = new Vector2(12f, -4f);
            nameRect.sizeDelta = new Vector2(400f, 20f);
            _targetNameText.raycastTarget = false;

            // 체력바 배경
            GameObject hpBgObj = new GameObject("TargetHPBarBg");
            hpBgObj.transform.SetParent(_targetHudRoot.transform, false);
            RectTransform hpBgRect = hpBgObj.AddComponent<RectTransform>();
            hpBgRect.anchorMin = new Vector2(0f, 1f);
            hpBgRect.anchorMax = new Vector2(0f, 1f);
            hpBgRect.pivot = new Vector2(0f, 1f);
            hpBgRect.anchoredPosition = new Vector2(12f, -26f);
            hpBgRect.sizeDelta = new Vector2(405f, 16f);
            Image hpBgImg = hpBgObj.AddComponent<Image>();
            hpBgImg.color = new Color(0.12f, 0.13f, 0.18f, 0.96f);
            hpBgImg.raycastTarget = false;

            GameObject hpFillObj = new GameObject("TargetHPBarFill");
            hpFillObj.transform.SetParent(hpBgObj.transform, false);
            RectTransform hpFillRect = hpFillObj.AddComponent<RectTransform>();
            hpFillRect.anchorMin = Vector2.zero;
            hpFillRect.anchorMax = Vector2.one;
            hpFillRect.sizeDelta = Vector2.zero;
            _targetHpFill = hpFillObj.AddComponent<Image>();
            _targetHpFill.sprite = whiteSprite;
            _targetHpFill.color = new Color(0.95f, 0.25f, 0.25f, 1f);
            _targetHpFill.type = Image.Type.Filled;
            _targetHpFill.fillMethod = Image.FillMethod.Horizontal;
            _targetHpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _targetHpFill.raycastTarget = false;

            _targetHpText = CreateUIText(hpBgObj.transform, "TargetHPText", "100%", font, 11, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            RectTransform hpTxtRect = _targetHpText.GetComponent<RectTransform>();
            hpTxtRect.anchorMin = Vector2.zero;
            hpTxtRect.anchorMax = Vector2.one;
            hpTxtRect.sizeDelta = Vector2.zero;
            _targetHpText.raycastTarget = false;

            // 타겟 시전/상태 텍스트
            _targetStatusText = CreateUIText(_targetHudRoot.transform, "TargetStatus", "", font, 11, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.85f, 0.92f, 1f));
            RectTransform statusRect = _targetStatusText.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(0f, 0f);
            statusRect.pivot = new Vector2(0f, 0f);
            statusRect.anchoredPosition = new Vector2(12f, 3f);
            statusRect.sizeDelta = new Vector2(405f, 16f);
            _targetStatusText.raycastTarget = false;

            // 우측: 타겟의 대상 (Target of Target) 박스 (클릭 또는 [T] 키로 즉시 스왑)
            _totBoxRoot = new GameObject("TargetOfTargetBox");
            _totBoxRoot.transform.SetParent(_targetHudRoot.transform, false);
            RectTransform totRect = _totBoxRoot.AddComponent<RectTransform>();
            totRect.anchorMin = new Vector2(1f, 0.5f);
            totRect.anchorMax = new Vector2(1f, 0.5f);
            totRect.pivot = new Vector2(1f, 0.5f);
            totRect.anchoredPosition = new Vector2(-10f, 4f);
            totRect.sizeDelta = new Vector2(224f, 40f);

            _totBgImage = _totBoxRoot.AddComponent<Image>();
            _totBgImage.color = new Color(0.11f, 0.15f, 0.23f, 0.96f);

            Outline totOutline = _totBoxRoot.AddComponent<Outline>();
            totOutline.effectColor = new Color(0.45f, 0.75f, 1f, 0.8f);
            totOutline.effectDistance = new Vector2(1.5f, -1.5f);

            _totButton = _totBoxRoot.AddComponent<Button>();
            _totButton.targetGraphic = _totBgImage;
            _totButton.onClick.AddListener(SwapToTargetOfTarget);

            _totNameText = CreateUIText(_totBoxRoot.transform, "ToTName", "➔ 타겟의 대상: 없음", font, 12, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(1f, 0.92f, 0.45f));
            RectTransform totNameRect = _totNameText.GetComponent<RectTransform>();
            totNameRect.anchorMin = new Vector2(0f, 1f);
            totNameRect.anchorMax = new Vector2(1f, 1f);
            totNameRect.pivot = new Vector2(0.5f, 1f);
            totNameRect.anchoredPosition = new Vector2(0f, -3f);
            totNameRect.sizeDelta = new Vector2(-12f, 18f);
            _totNameText.raycastTarget = false;

            _totHpText = CreateUIText(_totBoxRoot.transform, "ToTHP", $"[{LocalInputBindings.Label("TargetOfTarget")}] 키 또는 클릭하여 전환", font, 10, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 1f));
            RectTransform totHpRect = _totHpText.GetComponent<RectTransform>();
            totHpRect.anchorMin = new Vector2(0f, 0f);
            totHpRect.anchorMax = new Vector2(1f, 0f);
            totHpRect.pivot = new Vector2(0.5f, 0f);
            totHpRect.anchoredPosition = new Vector2(0f, 3f);
            totHpRect.sizeDelta = new Vector2(-12f, 16f);
            _totHpText.raycastTarget = false;

            // 하단 조작 가이드 힌트
            Text hintText = CreateUIText(_targetHudRoot.transform, "HotkeyHint",
                "",
                font, 10, FontStyle.Normal, TextAnchor.MiddleRight, new Color(0.72f, 0.82f, 0.95f, 0.92f));
            RectTransform hintRect = hintText.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(1f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(1f, 0f);
            hintRect.anchoredPosition = new Vector2(-10f, 1f);
            hintRect.sizeDelta = new Vector2(360f, 14f);
            hintText.raycastTarget = false;
            LocalShortcutText.Set(hintText, "클릭/{key:Party1}~{key:Party4}: 아군 | {key:CycleEnemy}: 적 순환 | {key:TargetOfTarget}: 대상의 대상 | {key:ClearTarget}: 해제");
        }

        private void UpdateTargetInfoHUD()
        {
            if (_targetHudRoot == null) return;

            if (currentTarget == null)
            {
                _targetNameText.text = $"타겟 없음 (클릭 / {LocalInputBindings.Label("CycleEnemy")} / {LocalInputBindings.PartyLabels()})";
                _targetNameText.color = new Color(0.75f, 0.78f, 0.85f);
                _targetHpFill.fillAmount = 0f;
                _targetHpText.text = "-";
                _targetStatusText.text = "공격 시 보스 자동 타겟 / 힐 시 최저 체력 아군 스마트 타겟 작동 중";
                _targetHudOutline.effectColor = new Color(0.4f, 0.45f, 0.55f, 0.8f);
                if (_totBoxRoot != null) _totBoxRoot.SetActive(false);
                return;
            }

            TargetFaction faction = GetTargetFaction(currentTarget);
            Color themeColor = (faction == TargetFaction.Friendly) ? FriendlyRingColor : EnemyRingColor;
            _targetHudOutline.effectColor = themeColor;

            string factionTag = (faction == TargetFaction.Friendly) ? "<color=#33E6FF>[아군 타겟]</color> " : "<color=#FFE626>[적 타겟]</color> ";
            _targetNameText.text = $"{factionTag}{GetUnitDisplayName(currentTarget)}";
            _targetNameText.color = Color.white;

            GetUnitHP(currentTarget, out float curHp, out float maxHp, out float shield);
            float ratio = maxHp > 0f ? Mathf.Clamp01(curHp / maxHp) : 0f;
            _targetHpFill.fillAmount = ratio;
            _targetHpFill.color = (faction == TargetFaction.Friendly)
                ? new Color(0.20f, 0.82f, 0.42f, 1f)
                : new Color(0.95f, 0.25f, 0.22f, 1f);

            string shieldStr = shield > 1f ? $" (+보호막 {Mathf.RoundToInt(shield)})" : "";
            _targetHpText.text = $"{Mathf.CeilToInt(curHp):N0} / {Mathf.CeilToInt(maxHp):N0} ({Mathf.RoundToInt(ratio * 100f)}%){shieldStr}";

            _targetStatusText.text = GetUnitStatusSummary(currentTarget);
            string extension=currentTarget.GetComponent<CombatStatusEffects>()?.ExtensionLabel()??"";
            if(extension.Length>0)_targetStatusText.text=extension+" | "+_targetStatusText.text;
            if (player != null && player.combatController != null && player.combatController.IsMeleeRole)
            {
                var combat = player.combatController;
                Transform offensiveTarget = combat.GetOffensiveTarget();
                string reach = offensiveTarget == null ? "유효한 공격 대상 없음" :
                    CombatRange.InRange(player.transform, offensiveTarget, combat.PrimaryMeleeRange) ? "사거리 안" : "사거리 밖";
                _targetStatusText.text = $"[{LocalInputBindings.Label("Skill1")}] {reach} | {_targetStatusText.text}";
                _targetStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
                _targetStatusText.verticalOverflow = VerticalWrapMode.Truncate;
            }

            Transform tot = GetTargetOfTarget(currentTarget);
            if (_totBoxRoot != null)
            {
                _totBoxRoot.SetActive(true);
                if (tot != null)
                {
                    TargetFaction totFaction = GetTargetFaction(tot);
                    GetUnitHP(tot, out float totCur, out float totMax, out _);
                    int totPct = totMax > 0f ? Mathf.RoundToInt((totCur / totMax) * 100f) : 0;

                    _totNameText.text = $"➔ {GetUnitDisplayName(tot)}";
                    _totNameText.color = (totFaction == TargetFaction.Friendly)
                        ? new Color(0.45f, 0.95f, 1f)
                        : new Color(1f, 0.85f, 0.35f);
                    _totHpText.text = $"타겟의 대상 (HP {totPct}%) | [{LocalInputBindings.Label("TargetOfTarget")}] 전환";
                }
                else
                {
                    _totNameText.text = "➔ 타겟의 대상: 없음";
                    _totNameText.color = Color.gray;
                    _totHpText.text = "-";
                }
            }
        }

        private static Text CreateUIText(Transform parent, string name, string content, Font font, int fontSize, FontStyle style, TextAnchor align, Color color)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200f, 24f);
            Text txt = obj.AddComponent<Text>();
            txt.font = font;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.alignment = align;
            txt.color = color;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;

            Shadow shadow = obj.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1f, -1f);
            return txt;
        }
    }
}
