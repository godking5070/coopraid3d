using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>Non-destructive presentation layer for the existing raid HUD and combat bindings.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class RaidHudTheme : MonoBehaviour
    {
        public static readonly Color Ink = new Color32(14, 20, 36, 242);
        public static readonly Color Line = new Color32(67, 83, 112, 255);
        public static readonly Color Mint = new Color32(93, 226, 201, 255);
        public static readonly Color Violet = new Color32(177, 139, 250, 255);
        public static readonly Color Gold = new Color32(246, 201, 111, 255);
        private static readonly Color[] Accents = { Gold, Violet, new Color32(111, 181, 255, 255), Mint };
        [SerializeField] private PlayerController player;
        [SerializeField] private BossController boss;
        private static Sprite whiteSprite;
        [SerializeField] private StageManager stage;
        [SerializeField] private SkillHotbarUI hotbar;
        [SerializeField] private PartyBotAI[] bots;
        [SerializeField] private Image[] health = new Image[4];
        [SerializeField] private Text[] healthText = new Text[4];
        [SerializeField] private Text timer, playerState;
        [SerializeField] private Font font;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                {
                    if (canvas.name == "RaidRogue_HUD_Canvas" && canvas.GetComponent<RaidHudTheme>() == null)
                        canvas.gameObject.AddComponent<RaidHudTheme>();
                }
            }
        }

        private void Start() { Apply(); }

        public void Apply()
        {
            // Calling twice is safe; gameplay references and listeners stay intact.
            if (transform.Find("Theme_Party") != null) return;
            player = FindFirstObjectByType<PlayerController>();
            boss = FindFirstObjectByType<BossController>();
            stage = FindFirstObjectByType<StageManager>();
            hotbar = GetComponentInChildren<SkillHotbarUI>(true);
            bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var scaler = GetComponent<CanvasScaler>();
            // Fit the full 1920x1080 composition at narrower and wider aspect ratios.
            if (scaler != null) scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                text.raycastTarget = false;
                text.color = new Color32(228, 235, 247, 255);
            }
            foreach (Image image in GetComponentsInChildren<Image>(true))
            {
                image.raycastTarget = image.GetComponent<Button>() != null ||
                    image.name == "RoguelikeCard_ModalRoot" || image.name == "AllStagesClear_VictoryModalRoot";
                if (image.type == Image.Type.Filled)
                {
                    if (whiteSprite == null) whiteSprite = Resources.Load<Sprite>("RaidUI/White");
                    if (whiteSprite == null) whiteSprite = Sprite.Create(Texture2D.whiteTexture,
                        new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), new Vector2(.5f, .5f));
                    image.sprite = whiteSprite;
                }
                else image.sprite = null;
            }

            Hide("HUD_TopLeft_Panel");
            Place("Top_StageHeader_Badge", new Vector2(.5f, 1), new Vector2(0, -22), new Vector2(760, 30));
            Place("Top_BossHP_Frame", new Vector2(.5f, 1), new Vector2(0, -62), new Vector2(760, 78));
            Place("Top_BossCastBar_Frame", new Vector2(.5f, 1), new Vector2(0, -150), new Vector2(590, 30));
            Place("Center_MechanicAlert_Text", new Vector2(.5f, 1), new Vector2(0, -196), new Vector2(1040, 58));
            Paint("Top_StageHeader_Badge", Color.clear);
            Paint("Top_BossHP_Frame", Ink, true);
            Paint("Boss_HP_Bg", new Color32(34, 31, 53, 255));
            Paint("Boss_HP_Fill", Violet);
            Paint("Top_BossCastBar_Frame", Ink, true);
            Paint("Boss_Cast_Fill", new Color32(171, 100, 56, 255));
            SetTextSize("Boss_HP_Text", 16);
            SetTextSize("Boss_PhaseText", 17);
            SetTextSize("Stage_Header_Text", 15);

            BuildParty();
            var info = Panel("Theme_Expedition", transform, new Vector2(1, 1), new Vector2(-32, -32), new Vector2(245, 94));
            Label("Title", info, "RAID / EXPEDITION", new Vector2(18, -14), new Vector2(210, 20), 13, Mint);
            timer = Label("Timer", info, "00:00", new Vector2(18, -39), new Vector2(210, 36), 29, Color.white);

            if (hotbar != null) StyleHotbar();
            StyleRewards();
            Paint("Victory_ContentBox", Ink, true);
            Paint("Restart_Button", new Color32(40, 91, 86, 255), true);
            Paint("BossClear_CenterBanner", Ink, true);

            // Keep all modal blockers above the new presentation-only elements.
            BringToFront("BossClear_CenterBanner");
            BringToFront("RoguelikeCard_ModalRoot");
            BringToFront("AllStagesClear_VictoryModalRoot");
        }

        private void BuildParty()
        {
            var panel = Panel("Theme_Party", transform, new Vector2(0, 1), new Vector2(32, -32), new Vector2(286, 318));
            Label("Heading", panel, "RAID PARTY     /     04", new Vector2(18, -16), new Vector2(250, 24), 14, Mint);
            string[] names = { "YOU  /  근접 딜러", "AEGIS  /  탱커", "LUMEN  /  힐러", "EMBER  /  원거리" };
            Color[] colors = { Gold, Accents[2], Mint, new Color32(245, 159, 104, 255) };
            for (int i = 0; i < 4; i++)
            {
                float y = -56 - i * 62;
                Label("Role" + i, panel, (i + 1).ToString("00"), new Vector2(18, y), new Vector2(27, 22), 13, colors[i]);
                Label("Name" + i, panel, names[i], new Vector2(52, y), new Vector2(220, 22), 15, Color.white);
                var track = Panel("Track" + i, panel, new Vector2(0, 1), new Vector2(52, y - 29), new Vector2(210, 5));
                track.GetComponent<Image>().color = Line;
                health[i] = Panel("Health", track, Vector2.zero, Vector2.zero, new Vector2(210, 5)).GetComponent<Image>();
                health[i].color = colors[i];
                health[i].rectTransform.pivot = Vector2.zero;
                healthText[i] = Label("Value" + i, panel, "100%", new Vector2(52, y + 1), new Vector2(210, 22), 12, colors[i]);
                healthText[i].alignment = TextAnchor.MiddleRight;
            }
            var help = Panel("Theme_Help", transform, Vector2.zero, new Vector2(32, 32), new Vector2(286, 77));
            Label("Controls", help, "W A S D   이동    /    1–4   스킬", new Vector2(16, -12), new Vector2(260, 23), 14, Color.white);
            Label("Tip", help, "측면 · 후면을 공략하세요", new Vector2(16, -40), new Vector2(260, 20), 13, Mint);
        }

        private void StyleHotbar()
        {
            Place("Bottom_SkillHotbar_Panel", new Vector2(.5f, 0), new Vector2(0, 26), new Vector2(740, 224));
            Paint("Bottom_SkillHotbar_Panel", Ink, true);
            var state = Panel("Theme_PlayerState", hotbar.transform, new Vector2(.5f, 1), new Vector2(0, 42), new Vector2(420, 32));
            playerState = Label("State", state, "", new Vector2(10, -3), new Vector2(400, 26), 15, Mint);
            playerState.alignment = TextAnchor.MiddleCenter;
            if (hotbar.gcdSummaryText != null) hotbar.gcdSummaryText.fontSize = 13;
            for (int i = 0; i < hotbar.slots.Length; i++)
            {
                var slot = hotbar.slots[i];
                if (slot == null || slot.slotButton == null) continue;
                RectTransform rt = slot.slotButton.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(150, 150);
                var icon = new GameObject("Theme_SkillGlyph", typeof(RectTransform), typeof(RaidSkillGlyph));
                icon.transform.SetParent(slot.iconBackgroundImage.transform, false);
                var glyph = icon.GetComponent<RaidSkillGlyph>();
                glyph.kind = i;
                glyph.color = Accents[i];
                SetRect((RectTransform)icon.transform, new Vector2(.5f, 1), new Vector2(0, -33), new Vector2(42, 42));
                glyph.Draw();
                SetRect(slot.keybindBadgeText.rectTransform, new Vector2(0, 1), new Vector2(10, -6), new Vector2(130, 21));
                slot.keybindBadgeText.alignment = TextAnchor.MiddleLeft;
                slot.keybindBadgeText.fontSize = 13;
                SetRect(slot.stageBadgeText.rectTransform, new Vector2(.5f, 1), new Vector2(0, -76), new Vector2(138, 18));
                slot.stageBadgeText.alignment = TextAnchor.MiddleCenter;
                SetRect(slot.skillNameText.rectTransform, new Vector2(.5f, 0), new Vector2(0, 5), new Vector2(142, 40));
                slot.skillNameText.fontSize = 14;
                slot.skillNameText.alignment = TextAnchor.MiddleCenter;
                var colors = slot.slotButton.colors;
                colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
                colors.pressedColor = new Color(.65f, .8f, .9f);
                slot.slotButton.colors = colors;
            }
        }

        private void StyleRewards()
        {
            Paint("RoguelikeCard_ModalRoot", new Color32(5, 9, 19, 241));
            for (int i = 1; i <= 3; i++)
            {
                Transform card = Find("CardButton_" + i);
                if (card == null) continue;
                card.GetComponent<Image>().color = Ink;
                Outline outline = card.GetComponent<Outline>() ?? card.gameObject.AddComponent<Outline>();
                outline.effectColor = Line;
                outline.effectDistance = Vector2.one;
                Label("Theme_Index", card, "AUGMENT   /   0" + i, new Vector2(24, -30), new Vector2(295, 20), 13, Mint);
                Transform title = card.Find("Card_TitleText");
                if (title != null) SetRect((RectTransform)title, new Vector2(.5f, 1), new Vector2(0, -72), new Vector2(297, 75));
                var desc = card.Find("Card_DescText") as RectTransform;
                if (desc != null) desc.offsetMax = new Vector2(-24, -155);
                Label("Theme_Choose", card, "선택하여 다음 레이드로  →", new Vector2(24, -382), new Vector2(297, 26), 14, Mint);
            }
        }

        private void LateUpdate()
        {
            if (stage != null && stage.stageHeaderText != null)
                stage.stageHeaderText.text = $"VOID EXPEDITION     /     STAGE {stage.currentStage:00} OF {stage.maxStage:00}";
            if (boss != null)
            {
                if (boss.bossPhaseText != null)
                    boss.bossPhaseText.text = boss.IsBossDefeatedCollapse ? "토벌 완료" :
                        $"{boss.currentBossTitle}    /    {(boss.currentPhase == BossPhase.Phase2_50To0 ? "PHASE II" : "PHASE I")}";
                if (boss.bossHpText != null)
                    boss.bossHpText.text = $"{Mathf.CeilToInt(boss.currentHp):N0} / {Mathf.CeilToInt(boss.maxHp):N0}     {boss.HpRatio * 100:0.0}%";
            }
            if (timer != null && stage != null)
            {
                int seconds = Mathf.FloorToInt(stage.TotalPlayTime);
                timer.text = $"{seconds / 60:00}:{seconds % 60:00}";
            }
            for (int i = 0; i < health.Length; i++)
            {
                if (health[i] == null) continue;
                float ratio = player != null && i == 0 ? player.HpRatio : 0;
                bool present = player != null && i == 0;
                if (i > 0 && bots != null)
                    foreach (PartyBotAI bot in bots)
                        if (bot != null && (int)bot.role == i - 1) { ratio = bot.HpRatio; present = true; break; }
                health[i].rectTransform.sizeDelta = new Vector2(210 * ratio, 5);
                healthText[i].text = present ? $"{ratio * 100:0}%" : "—";
            }
            if (playerState != null && player != null)
                playerState.text = player.IsBeingRescued ? "구출 중 · 안전지대로 이동" :
                    player.IsDefensiveBuffActive ? $"방어 활성 · {player.DefensiveRemainingTime:0.0}s" :
                    $"HP  {Mathf.CeilToInt(player.currentHp)} / {Mathf.CeilToInt(player.maxHp)}";
            if (hotbar != null && hotbar.combatController != null)
            {
                var combat = hotbar.combatController;
                if (hotbar.gcdSummaryText != null)
                    hotbar.gcdSummaryText.text = combat.IsOnGcd ? $"다음 공격  {combat.GcdRemaining:0.0}s    /    3 · 4 추가 스킬 사용 가능" : "WEAPON SKILLS    1 · 2     /     ABILITIES    3 · 4";
                for (int i = 0; i < hotbar.slots.Length && i < 4; i++)
                    if (hotbar.slots[i]?.iconBackgroundImage != null)
                        hotbar.slots[i].iconBackgroundImage.color = Color.Lerp(Ink, Accents[i], .13f);
            }
        }

        private Transform Find(string objectName)
        {
            foreach (RectTransform rt in GetComponentsInChildren<RectTransform>(true))
                if (rt.name == objectName) return rt;
            return null;
        }
        private void Hide(string objectName) { var t = Find(objectName); if (t != null) t.gameObject.SetActive(false); }
        private void BringToFront(string objectName) { var t = Find(objectName); if (t != null) t.SetAsLastSibling(); }
        private void SetTextSize(string objectName, int size) { var t = Find(objectName); if (t != null && t.TryGetComponent<Text>(out var text)) text.fontSize = size; }
        private void Place(string objectName, Vector2 anchor, Vector2 pos, Vector2 size)
        { var t = Find(objectName); if (t != null) SetRect((RectTransform)t, anchor, pos, size); }
        private void Paint(string objectName, Color color, bool border = false)
        {
            var t = Find(objectName);
            if (t == null || !t.TryGetComponent<Image>(out var image)) return;
            image.color = color;
            if (!border) return;
            var outline = t.GetComponent<Outline>() ?? t.gameObject.AddComponent<Outline>();
            outline.effectColor = Line;
            outline.effectDistance = new Vector2(1, -1);
        }
        private static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        { rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor; rt.sizeDelta = size; rt.anchoredPosition = pos; }
        private RectTransform Panel(string objectName, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetRect(rt, anchor, pos, size);
            var image = go.GetComponent<Image>(); image.color = Ink; image.raycastTarget = false;
            return rt;
        }
        private Text Label(string objectName, Transform parent, string value, Vector2 pos, Vector2 size, int fontSize, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            SetRect((RectTransform)go.transform, new Vector2(0, 1), pos, size);
            Text text = go.GetComponent<Text>();
            text.font = font; text.text = value; text.fontSize = fontSize; text.color = color;
            text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
            return text;
        }
    }
}
