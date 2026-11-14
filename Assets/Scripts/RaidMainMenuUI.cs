using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // Runtime-built presentation only; the existing session owns all connection actions.
    public sealed partial class RaidConnectionUI
    {
        private enum MainMenuPage { Home, Create, Join, LanHost, LanJoin, Saves }
        private MainMenuPage _menuPage;
        private RectTransform _menuLayout;
        private GameObject _homeMenu, _createMenu, _joinMenu, _lanMenu;
        private Text _menuTitle, _menuSubtitle, _trainingAccess, _trainingAction, _homeSave;
        private Button _menuBack, _hostEntry, _joinEntry, _friends, _homeContinue, _difficultyToggle;
        private static readonly Color MenuGold = new Color(.88f, .73f, .44f);
        private static readonly Color MenuMuted = new Color(.63f, .71f, .79f);

        private void BuildMainMenu(Transform overlay)
        {
            _menuLayout = Rect(overlay, "MenuLayout", Vector2.zero, new Vector2(1200, 840));
            Label(_menuLayout, "P A R T   0 1     /     C O - O P   R A I D", new Vector2(0, 342), new Vector2(850, 30), 15).color = MenuGold;
            _menuTitle = Label(_menuLayout, "", new Vector2(0, 278), new Vector2(1000, 70), 46);
            _menuSubtitle = Label(_menuLayout, "", new Vector2(0, 218), new Vector2(1000, 42), 19);
            _menuSubtitle.color = MenuMuted;
            Box(_menuLayout, "HeaderLine", new Vector2(0, 177), new Vector2(1130, 1), new Color(.24f, .29f, .34f)).GetComponent<Image>().raycastTarget = false;
            _menuBack = ButtonAt(_menuLayout, "<  뒤로", new Vector2(-505, 342), new Vector2(120, 38), BackMainMenu);
            _status = Label(_menuLayout, "", new Vector2(0, -332), new Vector2(1080, 60), 17);
            _status.supportRichText = false;
            _start = Rect(_menuLayout, "StartOptions", Vector2.zero, new Vector2(1160, 700)).gameObject;
            _homeMenu = Rect(_start.transform, "Home", Vector2.zero, new Vector2(1160, 700)).gameObject;
            _hostEntry = MenuCard(_homeMenu.transform, -380, "01", "방 만들기", "새로운 레이드를 시작하세요.\n혼자 또는 친구와 함께 출발합니다.", "새 원정 시작  >", () => ShowMenuPage(MainMenuPage.Create), out _, out _);
            _joinEntry = MenuCard(_homeMenu.transform, 0, "02", "들어가기", "친구의 파티에 합류하세요.\n함께 역할을 나누고 전투를 준비합니다.", "파티 참가  >", () => ShowMenuPage(MainMenuPage.Join), out _, out _);
            _training = MenuCard(_homeMenu.transform, 380, "03", "훈련소", "", "", _session.EnterTraining, out _trainingAccess, out _trainingAction);
            _homeContinue = ButtonAt(_homeMenu.transform, "이어하기  >", new Vector2(-420, -220), new Vector2(280, 48), ContinueFromMenu);
            _homeSave = Label(_homeMenu.transform, "", new Vector2(145, -220), new Vector2(800, 50), 18);
            _homeSave.color = MenuMuted; _homeSave.alignment = TextAnchor.MiddleLeft;
            ButtonAt(_homeMenu.transform, "저장 관리", new Vector2(-240, -290), new Vector2(220, 42), () => ShowMenuPage(MainMenuPage.Saves));
            ButtonAt(_homeMenu.transform, "설정", new Vector2(0, -290), new Vector2(220, 42), () => FindFirstObjectByType<LocalCameraSettingsUI>()?.Open());
            ButtonAt(_homeMenu.transform, "게임 종료", new Vector2(240, -290), new Vector2(220, 42), Application.Quit);

            _createMenu = Rect(_start.transform, "CreateRoom", Vector2.zero, new Vector2(900, 400)).gameObject;
            _single = ButtonAt(_createMenu.transform, "혼자 시작  ·  AI 파티와 출발", new Vector2(0, 135), new Vector2(680, 68), () => RequestNewCampaign(_session.SinglePlay));
            _steamCreate = ButtonAt(_createMenu.transform, "Steam 친구 방 만들기", new Vector2(0, 55), new Vector2(680, 68), () => RequestNewCampaign(() => RaidSteamRuntime.Instance?.CreateRoom()));
            _difficultyToggle = ButtonAt(_createMenu.transform, "Stage 1 난이도: 노멀", new Vector2(0, -25), new Vector2(680, 58), () => {
                var campaign = CampaignRuntime.Instance;
                campaign?.SelectDifficulty(campaign.SelectedDifficulty == CampaignCatalog.Hard ? CampaignCatalog.Normal : CampaignCatalog.Hard);
            });
            Label(_createMenu.transform, "최대 4인  ·  탱커 1 / 힐러 1 / 딜러 2\n빈 역할은 AI 동료가 함께합니다.", new Vector2(0, -105), new Vector2(800, 60), 18).color = MenuMuted;
            ButtonAt(_createMenu.transform, "LAN 직접 연결  >", new Vector2(0, -175), new Vector2(340, 40), () => ShowMenuPage(MainMenuPage.LanHost));

            _joinMenu = Rect(_start.transform, "JoinRoom", Vector2.zero, new Vector2(900, 400)).gameObject;
            _friends = ButtonAt(_joinMenu.transform, "Steam 친구 목록 열기", new Vector2(0, 75), new Vector2(680, 68), () => RaidSteamRuntime.Instance?.OpenFriends());
            Label(_joinMenu.transform, "친구가 보낸 초대를 수락해 파티에 참가하세요.\n친구 목록에서 진행 중인 게임에 참가할 수도 있습니다.", new Vector2(0, -5), new Vector2(850, 70), 20).color = MenuMuted;
            ButtonAt(_joinMenu.transform, "LAN 주소로 들어가기  >", new Vector2(0, -100), new Vector2(450, 48), () => ShowMenuPage(MainMenuPage.LanJoin));
            _recover = ButtonAt(_joinMenu.transform, "끊긴 세션에 다시 연결", new Vector2(0, -175), new Vector2(450, 44), () => RaidSteamRuntime.Instance?.RequestRecovery());

            _lanMenu = Rect(_start.transform, "DirectConnection", Vector2.zero, new Vector2(900, 400)).gameObject;
            Label(_lanMenu.transform, "플레이어 이름", new Vector2(-285, 80), new Vector2(220, 44), 20);
            _name = Field(_lanMenu.transform, new Vector2(100, 80), PlayerPrefs.GetString("Raid.LAN.Name", "Player"), 16);
            Label(_lanMenu.transform, "주소 : 포트", new Vector2(-285, 15), new Vector2(220, 44), 20);
            _address = Field(_lanMenu.transform, new Vector2(100, 15), PlayerPrefs.GetString("Raid.LAN.Address", "127.0.0.1:7777"), 40);
            _lanHost = ButtonAt(_lanMenu.transform, "LAN 방 만들기", new Vector2(0, -70), new Vector2(620, 60), () => Connect(true));
            _lanJoin = ButtonAt(_lanMenu.transform, "주소로 참가", new Vector2(0, -70), new Vector2(620, 60), () => Connect(false));
            Label(_lanMenu.transform, "같은 네트워크에서 직접 연결합니다.\nLAN 테스트는 진행 저장과 훈련소 해금에 포함되지 않습니다.", new Vector2(0, -152), new Vector2(900, 65), 18).color = MenuMuted;
            _steamStatus = Label(_start.transform, "", new Vector2(0, -275), new Vector2(1050, 65), 17);
            _steamStatus.color = MenuMuted; _steamStatus.supportRichText = false;
            _cancelJoin = ButtonAt(_start.transform, "접속 취소", new Vector2(0, -218), new Vector2(300, 42), () => RaidSteamRuntime.Instance?.CancelJoin());
        }

        private Button MenuCard(Transform parent, float x, string number, string title, string description, string action,
            UnityEngine.Events.UnityAction click, out Text detail, out Text actionText)
        {
            var card = Box(parent, title, new Vector2(x, -5), new Vector2(350, 292), new Color(.065f, .105f, .15f));
            var button = card.gameObject.AddComponent<Button>(); button.targetGraphic = card.GetComponent<Image>();
            var colors = button.colors; colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f); colors.pressedColor = new Color(.7f, .8f, .9f); colors.disabledColor = new Color(.55f, .55f, .55f); button.colors = colors;
            RaidUIView.Bind(this, title, click, button);
            Box(card, "Accent", new Vector2(0, 144), new Vector2(350, 3), MenuGold).GetComponent<Image>().raycastTarget = false;
            Label(card, number, new Vector2(-125, 104), new Vector2(52, 30), 17).color = MenuGold;
            Label(card, title, new Vector2(0, 50), new Vector2(310, 54), 34).alignment = TextAnchor.MiddleLeft;
            detail = Label(card, description, new Vector2(0, -21), new Vector2(310, 72), 18); detail.color = MenuMuted; detail.alignment = TextAnchor.MiddleLeft;
            Box(card, "Divider", new Vector2(0, -82), new Vector2(310, 1), new Color(.22f, .28f, .34f)).GetComponent<Image>().raycastTarget = false;
            actionText = Label(card, action, new Vector2(0, -112), new Vector2(310, 40), 19); actionText.color = MenuGold; actionText.alignment = TextAnchor.MiddleLeft;
            return button;
        }

        private void ContinueFromMenu()
        { var c = CampaignRuntime.Instance; var run = c?.Settlement?.run ?? c?.Saved; if (run != null) c.Continue(run.transport == "steam"); }
        private void ShowMenuPage(MainMenuPage page)
        {
            _menuPage = page;
            _homeMenu.SetActive(page == MainMenuPage.Home); _createMenu.SetActive(page == MainMenuPage.Create);
            _joinMenu.SetActive(page == MainMenuPage.Join); _lanMenu.SetActive(page == MainMenuPage.LanHost || page == MainMenuPage.LanJoin);
            _lanHost.gameObject.SetActive(page == MainMenuPage.LanHost); _lanJoin.gameObject.SetActive(page == MainMenuPage.LanJoin);
            LocalInputBindings.QuarantineHeld();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }
        private void BackMainMenu()
        {
            if (RaidSteamRuntime.Instance?.Busy == true) return;
            ShowMenuPage(_menuPage == MainMenuPage.LanHost ? MainMenuPage.Create : _menuPage == MainMenuPage.LanJoin ? MainMenuPage.Join : MainMenuPage.Home);
        }
        internal bool TryBackMainMenu()
        {
            if (_session.Mode != RaidSessionMode.Menu || _menuPage == MainMenuPage.Home) return false;
            BackMainMenu(); return true;
        }
        private void UpdateMainMenu(bool starting, bool busy)
        {
            var size = ((RectTransform)_overlay.transform).rect.size;
            if (!RaidUIStyle.Authored(_menuLayout)) _menuLayout.localScale = Vector3.one * Mathf.Max(.1f, Mathf.Min(1, (size.x - 40) / 1200, (size.y - 24) / 840));
            _menuBack.gameObject.SetActive(starting && _menuPage != MainMenuPage.Home); _menuBack.interactable = !busy;
            _menuTitle.text = !starting ? "파티 대기실" : _menuPage == MainMenuPage.Home ? "협동 레이드" : _menuPage == MainMenuPage.Create ? "방 만들기" : _menuPage == MainMenuPage.Join ? "들어가기" : _menuPage == MainMenuPage.Saves ? "저장 관리" : _menuPage == MainMenuPage.LanHost ? "LAN 방 만들기" : "LAN으로 들어가기";
            _menuSubtitle.text = !starting ? "동료와 준비를 마치고 출발하세요" : _menuPage == MainMenuPage.Home ? "네 명의 동료, 하나의 파티.  여섯 전장을 함께 돌파하세요." : _menuPage == MainMenuPage.Create ? "새 원정의 시작 방식을 선택하세요" : _menuPage == MainMenuPage.Join ? "친구가 기다리는 파티로" : _menuPage == MainMenuPage.Saves ? "지난 원정을 이어가거나 진행 기록을 확인하세요" : "직접 연결 · 테스트 세션";
            _hostEntry.interactable = _joinEntry.interactable = !busy;
            _friends.interactable = RaidSteamRuntime.Instance?.Available == true && !busy;
            if (_difficultyToggle != null)
            {
                var run = CampaignRuntime.Instance;
                _difficultyToggle.interactable = !busy && run?.CanSelectDifficulty == true;
                var label = _difficultyToggle.GetComponentInChildren<Text>();
                if (label != null) label.text = "Stage 1 난이도: " + (run?.SelectedDifficulty == CampaignCatalog.Hard ? "하드" : "노멀");
            }
            bool unlocked = CampaignRuntime.Instance?.TrainingUnlocked == true;
            _training.interactable = unlocked && !busy;
            _trainingAccess.text = unlocked ? "12직업과 스킬을 자유롭게 시험하세요.\n진행도와 보상에 영향을 주지 않습니다." : "정규 전투에 한 번 참여하면 해금됩니다.\n먼저 방을 만들거나 파티에 참가하세요.";
            _trainingAction.text = unlocked ? "자유 훈련 시작  >" : "잠김  ·  첫 출전 후 이용 가능";
            RaidUIStyle.Color(_trainingAction, unlocked ? MenuGold : MenuMuted); RaidUIStyle.State(_trainingAction, unlocked, !unlocked);
            _steamStatus.gameObject.SetActive(starting && (_menuPage == MainMenuPage.Create || _menuPage == MainMenuPage.Join || busy));
            _status.gameObject.SetActive(!starting || _menuPage == MainMenuPage.LanHost || _menuPage == MainMenuPage.LanJoin);
            if (!RaidUIStyle.Authored(_status)) _status.rectTransform.anchoredPosition = new Vector2(0, starting ? -285 : 166);
        }
    }
}
