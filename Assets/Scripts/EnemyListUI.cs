using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>
    /// Step 9 요구사항 3: 파판14(FF14) 스타일 좌측 '적 목록(Enemy List) UI' (EnemyListUI.cs)
    /// - 좌측 파티 리스트 아래쪽에 현재 살아있는 모든 적(보스 + 소환된 쫄들)의
    ///   [어그로 상태 아이콘(나를 보고 있으면 빨간색 ●, 다른 아군을 보고 있으면 초록색 ●)],
    ///   [이름], [체력바], [현재 캐스팅 스킬/기믹 상태]를 나열합니다.
    /// - 마우스로 적 목록 슬롯을 클릭하면 해당 적(보스 또는 특정 쫄)이 즉시 타겟으로 지정됩니다.
    /// </summary>
    public class EnemyListUI : MonoBehaviour
    {
        public static EnemyListUI Instance { get; private set; }

        [Header("References")]
        public BossController boss;
        public PlayerController player;

        private class EnemyRowUI
        {
            public GameObject rootObj;
            public Image bgImage;
            public Outline selectionOutline;
            public Button selectButton;
            public Image aggroIconImage;
            public Text aggroIconLabel;
            public Text nameText;
            public Image hpFill;
            public Text hpText;
            public Text castStatusText;
            public Transform boundTarget;
        }

        private GameObject _panelRoot;
        private Text _headerCountText;
        private readonly List<EnemyRowUI> _rowPool = new List<EnemyRowUI>();
        private Sprite _whiteSprite;
        private const int MaxRows = 5;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (boss == null) boss = FindFirstObjectByType<BossController>();
            if (player == null) player = FindFirstObjectByType<PlayerController>();

            BuildEnemyListHUD();
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
            if (boss == null) boss = FindFirstObjectByType<BossController>();
            if (player == null) player = FindFirstObjectByType<PlayerController>();

            if (_panelRoot == null)
            {
                BuildEnemyListHUD();
            }

            RefreshEnemyRows();
        }

        public bool TrySelectEnemyAtScreenPoint(Vector3 screenPos)
        {
            if (_rowPool == null || TargetingManager.Instance == null) return false;

            Vector2 pt = new Vector2(screenPos.x, screenPos.y);
            for (int i = 0; i < _rowPool.Count; i++)
            {
                EnemyRowUI row = _rowPool[i];
                if (row == null || row.rootObj == null || !row.rootObj.activeInHierarchy || row.boundTarget == null)
                    continue;

                RectTransform rt = row.rootObj.GetComponent<RectTransform>();
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, pt, null))
                {
                    TargetingManager.Instance.SetTarget(row.boundTarget);
                    return true;
                }
            }
            return false;
        }

        public void BuildEnemyListHUD()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;

            Transform old = canvas.transform.Find("EnemyList_LeftHUD");
            if (old != null)
            {
                DestroyImmediate(old.gameObject);
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (_whiteSprite == null)
            {
                _whiteSprite = Sprite.Create(
                    Texture2D.whiteTexture,
                    new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                    new Vector2(0.5f, 0.5f)
                );
            }

            // 좌측 파티 리스트(PartyList_LeftHUD) 바로 아래쪽에 정렬 배치
            _panelRoot = new GameObject("EnemyList_LeftHUD");
            _panelRoot.transform.SetParent(canvas.transform, false);
            RectTransform panelRect = _panelRoot.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0.5f);
            panelRect.anchorMax = new Vector2(0f, 0.5f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(18f, -104f);
            panelRect.sizeDelta = new Vector2(335f, 210f);

            Image panelBg = _panelRoot.AddComponent<Image>();
            panelBg.color = new Color(0.08f, 0.05f, 0.09f, 0.88f);

            Outline panelOutline = _panelRoot.AddComponent<Outline>();
            panelOutline.effectColor = new Color(0.95f, 0.35f, 0.28f, 0.75f);
            panelOutline.effectDistance = new Vector2(2f, -2f);

            // 상단 헤더 타이틀
            _headerCountText = CreateText(_panelRoot.transform, "EnemyListHeader",
                "◆ ENEMY LIST (적 목록 | 클릭·Tab 타겟)",
                font, 12, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(1f, 0.82f, 0.55f));
            RectTransform headerRect = _headerCountText.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0f, 1f);
            headerRect.anchoredPosition = new Vector2(10f, -5f);
            headerRect.sizeDelta = new Vector2(-102f, 22f);
            _headerCountText.raycastTarget = false;

            // 우측 상단: [+쫄 즉시소환] 버튼 (언제든 클릭 한 번으로 보스 쫄 소환 기믹 테스트 가능)
            GameObject spawnBtnObj = new GameObject("ManualSpawnAddsButton");
            spawnBtnObj.transform.SetParent(_panelRoot.transform, false);
            RectTransform spawnBtnRect = spawnBtnObj.AddComponent<RectTransform>();
            spawnBtnRect.anchorMin = new Vector2(1f, 1f);
            spawnBtnRect.anchorMax = new Vector2(1f, 1f);
            spawnBtnRect.pivot = new Vector2(1f, 1f);
            spawnBtnRect.anchoredPosition = new Vector2(-8f, -4f);
            spawnBtnRect.sizeDelta = new Vector2(88f, 22f);

            Image spawnBtnBg = spawnBtnObj.AddComponent<Image>();
            spawnBtnBg.color = new Color(0.50f, 0.16f, 0.65f, 0.96f);
            Outline spawnBtnOutline = spawnBtnObj.AddComponent<Outline>();
            spawnBtnOutline.effectColor = new Color(0.9f, 0.6f, 1f, 0.8f);
            spawnBtnOutline.effectDistance = new Vector2(1f, -1f);

            Button spawnBtn = spawnBtnObj.AddComponent<Button>();
            spawnBtn.targetGraphic = spawnBtnBg;
            spawnBtn.onClick.AddListener(() =>
            {
                if (BossAddSpawner.Instance != null)
                {
                    BossAddSpawner.Instance.SpawnAddPairWave("수동 테스트 쫄 소환!");
                }
            });

            Text spawnBtnTxt = CreateText(spawnBtnObj.transform, "BtnLabel", "+쫄 소환", font, 11, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 1f));
            RectTransform sbtRect = spawnBtnTxt.GetComponent<RectTransform>();
            sbtRect.anchorMin = Vector2.zero;
            sbtRect.anchorMax = Vector2.one;
            sbtRect.sizeDelta = Vector2.zero;
            spawnBtnTxt.raycastTarget = false;

            _rowPool.Clear();
            for (int i = 0; i < MaxRows; i++)
            {
                EnemyRowUI row = CreateEnemyRow(_panelRoot.transform, i, font);
                _rowPool.Add(row);
            }
        }

        private EnemyRowUI CreateEnemyRow(Transform parent, int index, Font font)
        {
            EnemyRowUI row = new EnemyRowUI();

            GameObject rowObj = new GameObject($"EnemyRow_{index}");
            rowObj.transform.SetParent(parent, false);
            RectTransform rect = rowObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -30f - index * 54f);
            rect.sizeDelta = new Vector2(-14f, 50f);

            row.rootObj = rowObj;
            row.bgImage = rowObj.AddComponent<Image>();
            row.bgImage.color = new Color(0.14f, 0.10f, 0.16f, 0.94f);

            row.selectionOutline = rowObj.AddComponent<Outline>();
            row.selectionOutline.effectColor = new Color(1f, 0.9f, 0.15f, 0f);
            row.selectionOutline.effectDistance = new Vector2(2f, -2f);

            row.selectButton = rowObj.AddComponent<Button>();
            row.selectButton.targetGraphic = row.bgImage;
            EnemyRowUI capturedRow = row;
            row.selectButton.onClick.AddListener(() =>
            {
                if (capturedRow.boundTarget != null && TargetingManager.Instance != null)
                {
                    TargetingManager.Instance.SetTarget(capturedRow.boundTarget);
                }
            });

            // 좌측 어그로 상태 아이콘 (나를 보면 빨강 ●, 다른 아군을 보면 초록 ●)
            GameObject aggroObj = new GameObject("AggroIcon");
            aggroObj.transform.SetParent(rowObj.transform, false);
            RectTransform aggroRect = aggroObj.AddComponent<RectTransform>();
            aggroRect.anchorMin = new Vector2(0f, 1f);
            aggroRect.anchorMax = new Vector2(0f, 1f);
            aggroRect.pivot = new Vector2(0f, 1f);
            aggroRect.anchoredPosition = new Vector2(6f, -4f);
            aggroRect.sizeDelta = new Vector2(16f, 16f);

            row.aggroIconImage = aggroObj.AddComponent<Image>();
            row.aggroIconImage.color = new Color(0.2f, 0.85f, 0.35f, 1f);
            row.aggroIconImage.raycastTarget = false;

            row.aggroIconLabel = CreateText(aggroObj.transform, "Dot", "●", font, 11, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            RectTransform dotRect = row.aggroIconLabel.GetComponent<RectTransform>();
            dotRect.anchorMin = Vector2.zero;
            dotRect.anchorMax = Vector2.one;
            dotRect.sizeDelta = Vector2.zero;
            row.aggroIconLabel.raycastTarget = false;

            // 적 이름
            row.nameText = CreateText(rowObj.transform, "EnemyName", "적 이름", font, 12, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            RectTransform nameRect = row.nameText.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = new Vector2(26f, -3f);
            nameRect.sizeDelta = new Vector2(-32f, 18f);
            row.nameText.raycastTarget = false;

            // 체력바 배경
            GameObject hpBgObj = new GameObject("HPBarBg");
            hpBgObj.transform.SetParent(rowObj.transform, false);
            RectTransform hpBgRect = hpBgObj.AddComponent<RectTransform>();
            hpBgRect.anchorMin = new Vector2(0f, 1f);
            hpBgRect.anchorMax = new Vector2(1f, 1f);
            hpBgRect.pivot = new Vector2(0.5f, 1f);
            hpBgRect.anchoredPosition = new Vector2(0f, -22f);
            hpBgRect.sizeDelta = new Vector2(-14f, 13f);
            Image hpBgImg = hpBgObj.AddComponent<Image>();
            hpBgImg.color = new Color(0.06f, 0.06f, 0.09f, 0.96f);
            hpBgImg.raycastTarget = false;

            GameObject hpFillObj = new GameObject("HPBarFill");
            hpFillObj.transform.SetParent(hpBgObj.transform, false);
            RectTransform hpFillRect = hpFillObj.AddComponent<RectTransform>();
            hpFillRect.anchorMin = Vector2.zero;
            hpFillRect.anchorMax = Vector2.one;
            hpFillRect.sizeDelta = Vector2.zero;
            row.hpFill = hpFillObj.AddComponent<Image>();
            row.hpFill.sprite = _whiteSprite;
            row.hpFill.color = new Color(0.92f, 0.25f, 0.22f, 1f);
            row.hpFill.type = Image.Type.Filled;
            row.hpFill.fillMethod = Image.FillMethod.Horizontal;
            row.hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            row.hpFill.raycastTarget = false;

            row.hpText = CreateText(hpBgObj.transform, "HPText", "100%", font, 10, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            RectTransform hpTxtRect = row.hpText.GetComponent<RectTransform>();
            hpTxtRect.anchorMin = Vector2.zero;
            hpTxtRect.anchorMax = Vector2.one;
            hpTxtRect.sizeDelta = Vector2.zero;
            row.hpText.raycastTarget = false;

            // 캐스팅 스킬 / 상태 텍스트
            row.castStatusText = CreateText(rowObj.transform, "CastStatus", "전투 중", font, 10, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.95f, 0.85f, 0.60f));
            RectTransform castRect = row.castStatusText.GetComponent<RectTransform>();
            castRect.anchorMin = new Vector2(0f, 0f);
            castRect.anchorMax = new Vector2(1f, 0f);
            castRect.pivot = new Vector2(0.5f, 0f);
            castRect.anchoredPosition = new Vector2(2f, 2f);
            castRect.sizeDelta = new Vector2(-14f, 14f);
            row.castStatusText.raycastTarget = false;

            return row;
        }

        private void RefreshEnemyRows()
        {
            if (_panelRoot == null) return;

            List<Transform> livingEnemies = new List<Transform>(MaxRows);
            if (boss != null && !boss.IsBossDefeatedCollapse)
            {
                livingEnemies.Add(boss.transform);
            }

            if (BossAddSpawner.Instance != null)
            {
                BossAddSpawner.Instance.CleanupNullOrDeadAdds();
                var adds = BossAddSpawner.Instance.activeAdds;
                for (int i = 0; i < adds.Count; i++)
                {
                    if (adds[i] != null && !adds[i].isDead)
                    {
                        livingEnemies.Add(adds[i].transform);
                    }
                }
            }

            if (_headerCountText != null)
            {
                _headerCountText.text = $"◆ ENEMY LIST (생존 적: {livingEnemies.Count} | 클릭·Tab)";
            }

            RectTransform panelRect = _panelRoot.GetComponent<RectTransform>();
            int visibleCount = Mathf.Clamp(livingEnemies.Count, 1, MaxRows);
            panelRect.sizeDelta = new Vector2(335f, 36f + visibleCount * 54f);

            Transform currentSelected = TargetingManager.Instance != null ? TargetingManager.Instance.CurrentTarget : null;

            for (int i = 0; i < _rowPool.Count; i++)
            {
                EnemyRowUI row = _rowPool[i];
                if (i >= livingEnemies.Count)
                {
                    row.rootObj.SetActive(false);
                    row.boundTarget = null;
                    continue;
                }

                row.rootObj.SetActive(true);
                Transform enemy = livingEnemies[i];
                row.boundTarget = enemy;

                bool isSelected = (currentSelected == enemy);
                row.selectionOutline.effectColor = isSelected
                    ? new Color(1.00f, 0.92f, 0.18f, 1.00f)
                    : new Color(0f, 0f, 0f, 0f);
                row.bgImage.color = isSelected
                    ? new Color(0.28f, 0.24f, 0.10f, 0.96f)
                    : new Color(0.13f, 0.10f, 0.15f, 0.92f);

                BossController b = enemy.GetComponent<BossController>();
                BossAddMinion add = enemy.GetComponent<BossAddMinion>();

                if (b != null)
                {
                    row.nameText.text = $"{(isSelected ? "▶ " : "")}[BOSS] {b.currentBossTitle}";
                    row.nameText.color = isSelected ? new Color(1f, 0.95f, 0.45f) : Color.white;

                    float ratio = b.HpRatio;
                    row.hpFill.fillAmount = ratio;
                    bool shieldProtected = BossAddSpawner.Instance != null && BossAddSpawner.Instance.HasLivingPowerSupplier;
                    row.hpFill.color = shieldProtected
                        ? new Color(0.68f, 0.30f, 0.95f, 1f)
                        : new Color(0.92f, 0.24f, 0.22f, 1f);
                    row.hpText.text = $"{Mathf.CeilToInt(b.currentHp):N0} ({Mathf.RoundToInt(ratio * 100f)}%)";

                    bool aggroOnMe = (player != null && b.aggroTarget == player.transform);
                    UpdateAggroIcon(row, aggroOnMe);

                    if (b.IsCasting)
                    {
                        int pct = Mathf.Clamp(Mathf.RoundToInt(b.CastProgress * 100f), 0, 100);
                        row.castStatusText.text = $"⚡ 시전: {b.CurrentCastSkillName} ({pct}%)";
                        row.castStatusText.color = new Color(1f, 0.78f, 0.25f);
                    }
                    else if (shieldProtected)
                    {
                        row.castStatusText.text = "★ 동력 공급 받는 중 (보스 피해 80% 감소!)";
                        row.castStatusText.color = new Color(0.90f, 0.55f, 1f);
                    }
                    else
                    {
                        string targetLabel = b.aggroTarget != null && TargetingManager.Instance != null
                            ? TargetingManager.Instance.GetUnitDisplayName(b.aggroTarget)
                            : "탱커";
                        row.castStatusText.text = $"어그로 1순위: {targetLabel}";
                        row.castStatusText.color = new Color(0.80f, 0.85f, 0.92f);
                    }
                }
                else if (add != null)
                {
                    row.nameText.text = $"{(isSelected ? "▶ " : "")}{add.addDisplayName}";
                    row.nameText.color = add.addType == AddMinionType.PowerSupplier_BossTether
                        ? new Color(0.92f, 0.65f, 1f)
                        : new Color(1f, 0.55f, 0.55f);

                    float ratio = add.HpRatio;
                    row.hpFill.fillAmount = ratio;
                    row.hpFill.color = add.addType == AddMinionType.PowerSupplier_BossTether
                        ? new Color(0.75f, 0.25f, 0.95f, 1f)
                        : new Color(0.95f, 0.30f, 0.25f, 1f);
                    row.hpText.text = $"{Mathf.CeilToInt(add.currentHp):N0} ({Mathf.RoundToInt(ratio * 100f)}%)";

                    bool aggroOnMe = (player != null && add.aggroTarget == player.transform);
                    UpdateAggroIcon(row, aggroOnMe);

                    row.castStatusText.text = add.CurrentCastOrStatusText;
                    row.castStatusText.color = new Color(1f, 0.88f, 0.55f);
                }
            }
        }

        private static void UpdateAggroIcon(EnemyRowUI row, bool isAggroOnPlayer)
        {
            if (isAggroOnPlayer)
            {
                row.aggroIconImage.color = new Color(0.95f, 0.18f, 0.18f, 1f);
                row.aggroIconLabel.text = "●";
                row.aggroIconLabel.color = new Color(1f, 0.9f, 0.9f);
            }
            else
            {
                row.aggroIconImage.color = new Color(0.18f, 0.78f, 0.32f, 1f);
                row.aggroIconLabel.text = "●";
                row.aggroIconLabel.color = Color.white;
            }
        }

        private static Text CreateText(Transform parent, string name, string content, Font font, int size, FontStyle style, TextAnchor align, Color color)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(160f, 20f);
            Text txt = obj.AddComponent<Text>();
            txt.font = font;
            txt.text = content;
            txt.fontSize = size;
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
