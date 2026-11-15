using UnityEngine;
namespace RaidRogue
{
    public sealed partial class RaidConnectionUI
    {
        private void BindAuthoredMenu()
        {
            RaidUIView.Bind(this, "12직업 · 직업 / 기원 선택", AegisRouteSelectionUI.OpenNetwork);
            RaidUIView.Bind(this, "준비", _session.ToggleReady);
            RaidUIView.Bind(this, "친구 초대", () => RaidSteamRuntime.Instance?.InviteFriends());
            RaidUIView.Bind(this, "전투 시작 (호스트)", () => _session.RequestAction(9));
            RaidUIView.Bind(this, "화면으로 돌아가기", () => _session.MenuOpen = false);
            RaidUIView.Bind(this, "방 나가기 · 시작 화면", _session.Disconnect);
            RaidUIView.Bind(this, "현재 방 유지", () => RaidSteamRuntime.Instance.AnswerInvite(false));
            RaidUIView.Bind(this, "나가고 초대 참가", () => RaidSteamRuntime.Instance.AnswerInvite(true));
            RaidUIView.Bind(this, "다시 연결", () => RaidSteamRuntime.Instance?.RequestRecovery());
            RaidUIView.Bind(this, "재접속 취소", _session.CancelRecovery);
            RaidUIView.Bind(this, "메인 메뉴로", () => ConfirmCampaignExit(false, _session.RecoveryMenu));
            RaidUIView.Bind(this, "접속 / 역할", () => _session.MenuOpen = true);
            RaidUIView.Bind(this, "메뉴 / 나가기", () => _session.MenuOpen = true);
            RaidUIView.Bind(this, "<  뒤로", BackMainMenu);
            RaidUIView.Bind(this, "방 만들기", () => ShowMenuPage(MainMenuPage.Create));
            RaidUIView.Bind(this, "들어가기", () => ShowMenuPage(MainMenuPage.Join));
            var retiredTraining = RaidUIView.For(this)?.actions.Find(x => x.id == "훈련소")?.button;
            if (retiredTraining != null)
            {
                RaidUIView.Bind(this, "훈련소", () => { });
                retiredTraining.interactable = false;
                retiredTraining.gameObject.SetActive(false);
            }
            RaidUIView.Bind(this, "이어하기  >", ContinueFromMenu);
            RaidUIView.Bind(this, "저장 관리", () => ShowMenuPage(MainMenuPage.Saves));
            RaidUIView.Bind(this, "설정", () => FindFirstObjectByType<LocalCameraSettingsUI>()?.Open());
            RaidUIView.Bind(this, "게임 종료", Application.Quit);
            RaidUIView.Bind(this, "혼자 시작  ·  AI 파티와 출발", () => RequestNewCampaign(_session.SinglePlay));
            RaidUIView.Bind(this, "Steam 친구 방 만들기", () => RequestNewCampaign(() => RaidSteamRuntime.Instance?.CreateRoom()));
            var difficultyAction = RaidUIView.For(this)?.actions.Find(x => x.id == "Stage 1 난이도: 노멀");
            _difficultyToggle = difficultyAction?.button != null
                ? RaidUIView.Bind(this, "Stage 1 난이도: 노멀", ToggleDifficulty)
                : _createMenu != null ? ButtonAt(_createMenu.transform, "Stage 1 난이도: 노멀", new Vector2(0, -208), new Vector2(680, 40), ToggleDifficulty) : null;
            RaidUIView.Bind(this, "LAN 직접 연결  >", () => ShowMenuPage(MainMenuPage.LanHost));
            RaidUIView.Bind(this, "Steam 친구 목록 열기", () => RaidSteamRuntime.Instance?.OpenFriends());
            RaidUIView.Bind(this, "LAN 주소로 들어가기  >", () => ShowMenuPage(MainMenuPage.LanJoin));
            RaidUIView.Bind(this, "끊긴 세션에 다시 연결", () => RaidSteamRuntime.Instance?.RequestRecovery());
            RaidUIView.Bind(this, "LAN 방 만들기", () => Connect(true));
            RaidUIView.Bind(this, "주소로 참가", () => Connect(false));
            RaidUIView.Bind(this, "접속 취소", () => RaidSteamRuntime.Instance?.CancelJoin());
            RaidUIView.Bind(this, "이어하기", ContinueFromMenu);
            RaidUIView.Bind(this, "저장 정보 확인", () => CampaignDialog(CampaignRuntime.Instance.SaveDescription, null));
            RaidUIView.Bind(this, "런 체크포인트 삭제", () => CampaignDialog("이 계정의 런 체크포인트와 최근 백업을 삭제합니다.\n개인 클리어 기록·챕터 해금·설정은 유지됩니다.\n\n" + CampaignRuntime.Instance.LastSaveLabel, () => CampaignRuntime.Instance.DeleteSelected(false)));
            RaidUIView.Bind(this, "개인 진행도 확인", ShowPersonalProgress);
            RaidUIView.Bind(this, "개인 진행도 삭제", () => CampaignDialog("이 계정의 개인 클리어·챕터 해금·미전달 클리어 확정 알림과 백업을 삭제합니다.\n런 체크포인트·설정·키 배치는 유지됩니다.\n기존 런의 완료 영수증을 다시 받으면 해당 기록은 복구될 수 있습니다.", () => CampaignRuntime.Instance.DeleteSelected(true)));
            RaidUIView.Bind(this, "저장 재시도", () => CampaignRuntime.Instance?.RetrySave());
            RaidUIView.Bind(this, "닫기 / 취소", CloseCampaignDialog);
            RaidUIView.Bind(this, "확인", () => {
                var action = _campaignAction; CloseCampaignDialog(); action?.Invoke();
            });
            for (int i = 0; i < 4; i++) { int role = i + 1; RaidUIView.Bind(this, CombatMetricsManager.RoleName(role), () => _session.SelectRole(role)); }
        }
    }
}
