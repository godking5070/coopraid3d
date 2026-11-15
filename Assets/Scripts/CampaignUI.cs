using System;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    public sealed partial class RaidConnectionUI
    {
        private GameObject _campaignMenu, _campaignDialog;
        private Text _campaignSummary, _campaignMessage, _campaignNotice;
        private Text _campaignHudNotice;
        private Button _campaignContinue, _campaignConfirm, _campaignRetry;
        private Action _campaignAction;
        public static bool CampaignDialogOpen { get; private set; }
        private bool _quitApproved;
        private void BuildCampaignUI(Transform canvas, Transform overlay)
        {
            _campaignMenu = Box(overlay, "CampaignSaveMenu", new Vector2(0, -15), new Vector2(960, 380), new Color(.06f, .10f, .16f)).gameObject;
            _campaignSummary = Label(_campaignMenu.transform, "", new Vector2(-230, 0), new Vector2(420, 320), 20);
            _campaignSummary.alignment = TextAnchor.MiddleLeft;
            _campaignContinue = ButtonAt(_campaignMenu.transform, "이어하기", new Vector2(235, 130), new Vector2(390, 48), ContinueFromMenu);
            ButtonAt(_campaignMenu.transform, "저장 정보 확인", new Vector2(235, 65), new Vector2(390, 48), () => CampaignDialog(CampaignRuntime.Instance.SaveDescription, null));
            ButtonAt(_campaignMenu.transform, "런 체크포인트 삭제", new Vector2(235, 0), new Vector2(390, 48), () => CampaignDialog("이 계정의 런 체크포인트와 최근 백업을 삭제합니다.\n개인 클리어 기록·훈련소 해금·설정은 유지됩니다.\n\n" + CampaignRuntime.Instance.LastSaveLabel, () => CampaignRuntime.Instance.DeleteSelected(false)));
            ButtonAt(_campaignMenu.transform, "개인 진행도 확인", new Vector2(235, -65), new Vector2(390, 48), ShowPersonalProgress);
            ButtonAt(_campaignMenu.transform, "개인 진행도 삭제", new Vector2(235, -130), new Vector2(390, 48), () => CampaignDialog("이 계정의 개인 클리어·훈련소/챕터 해금·미전달 클리어 확정 알림과 백업을 삭제합니다.\n훈련소는 다음 정규 전투 참여 시 다시 해금됩니다.\n런 체크포인트·설정·키 배치는 유지됩니다.\n기존 런의 완료 영수증을 다시 받으면 해당 기록은 복구될 수 있습니다.", () => CampaignRuntime.Instance.DeleteSelected(true)));
            _campaignMenu.SetActive(false);
            _campaignNotice = Label(overlay, "", new Vector2(0, -350), new Vector2(1120, 62), 16);
            _campaignNotice.supportRichText = false;
            _campaignHudNotice = Label(canvas, "", new Vector2(0, 180), new Vector2(1000, 90), 19);
            _campaignHudNotice.color = new Color(1, .7f, .35f);
            _campaignRetry = ButtonAt(overlay, "저장 재시도", new Vector2(0, -398), new Vector2(260, 36), () => CampaignRuntime.Instance?.RetrySave());
            var dialog = Box(canvas, "CampaignConfirmation", Vector2.zero, Vector2.zero, new Color(.02f, .03f, .05f, .99f));
            dialog.anchorMin = Vector2.zero; dialog.anchorMax = Vector2.one; dialog.offsetMin = dialog.offsetMax = Vector2.zero;
            _campaignDialog = dialog.gameObject;
            _campaignMessage = Label(dialog, "", new Vector2(0, 70), new Vector2(1250, 470), 22);
            _campaignMessage.supportRichText = false;
            ButtonAt(dialog, "닫기 / 취소", new Vector2(-220, -230), new Vector2(390, 52), CloseCampaignDialog);
            _campaignConfirm = ButtonAt(dialog, "확인", new Vector2(220, -230), new Vector2(390, 52), () => {
                var action = _campaignAction; CloseCampaignDialog(); action?.Invoke();
            });
            _campaignDialog.SetActive(false); Application.wantsToQuit += WantsToQuit;
        }
        private void ShowPersonalProgress()
        {
            var c = CampaignRuntime.Instance;
            string text = "개인 계정: " + c.Owner + "\n확정된 스테이지 클리어: " + c.Personal.clears.Length + "건\n1부 완료: " + (c.Personal.PartOneComplete ? "완료" : "미완료") + "\n2부: " + (c.Personal.PartTwoUnlocked ? "후속 해금 조건 충족 · 현재 진입 불가" : "준비 중 · 1부 완료 기록 필요") +
                "\n\n1부 4인·6스테이지 진행을 지원합니다.\nStage 4~6은 기존 Stage 1~3 보스 구성을 재사용합니다.\n2부는 준비 중이며 진입할 수 없습니다.\n구버전 프로토타입 기록은 1부 완료로 인정하지 않습니다.\n훈련장·개발 기능은 개인 클리어와 저장에서 제외됩니다.";
            CampaignDialog(text + "\n훈련소: " + (c.TrainingUnlocked ? "해금됨" : "첫 정규 전투 참여 후 해금"), null);
        }
        private void RequestNewCampaign(Action start)
        {
            var c = CampaignRuntime.Instance;
            if (c?.Saved == null) { start(); return; }
            CampaignDialog("새 런을 시작합니다.\n현재 런 체크포인트는 새 런의 첫 출발 경계에서 교체됩니다.\n개인 진행도·챕터 해금·설정은 유지됩니다.\n\n기존: " + c.LastSaveLabel, start);
        }
        internal void ConfirmCampaignExit(bool quit, Action returnAction = null)
        {
            var c = CampaignRuntime.Instance;
            CampaignDialog((quit ? "게임을 종료합니다." : "현재 세션을 종료하고 메인 메뉴로 돌아갑니다.") + "\n\n" + _session.CampaignExitSaveLabel +
                "\n\n현재 전투와 완료하지 않은 보상 선택은 저장되지 않습니다.\n이어하기는 마지막 저장 스테이지 시작부터 새 도전으로 재개하며 공유 부활은 4회입니다.\n진행 중 세션 재접속은 현재 상태와 남은 부활을 유지합니다." +
                (_session.Mode == RaidSessionMode.Client ? "\n런 체크포인트는 호스트만 보관합니다. 내 개인 클리어 기록은 내 계정에 저장됩니다." : "") +
                (RaidNetworkSession.Multiplayer && !_session.UsesSteam ? "\nLAN 테스트 세션은 영구 저장 대상이 아닙니다. 표시된 이전 저장은 이 세션의 진행이 아닐 수 있습니다." : "") +
                (_session.CampaignRemoteSaveFailed ? "\n호스트의 저장 실패가 보고되었습니다. 호스트가 저장을 재시도해야 합니다." : "") +
                (c?.HasSaveFailure == true ? "\n주의: 저장 실패가 있습니다. 취소 후 저장 재시도를 확인하세요." : ""), () => {
                    if (quit) { _quitApproved = true; Application.Quit(); }
                    else if (returnAction != null) returnAction();
                    else _session.DisconnectConfirmed();
                });
        }
        private bool WantsToQuit()
        {
            if (_quitApproved || _session == null || _session.Ending || _session.Mode == RaidSessionMode.Menu || _session.Mode == RaidSessionMode.Training) return true;
            ConfirmCampaignExit(true); return false;
        }
        private void CampaignDialog(string message, Action action)
        {
            _campaignAction = action; _campaignMessage.text = message; _campaignConfirm.gameObject.SetActive(action != null);
            CampaignDialogOpen = true; _campaignDialog.SetActive(true); _session.MenuOpen = true;
            LocalInputBindings.QuarantineHeld();
        }
        internal void CloseCampaignDialog()
        { _campaignAction = null; CampaignDialogOpen = false; _campaignDialog.SetActive(false); LocalInputBindings.QuarantineHeld(); }
        private void UpdateCampaignUI()
        {
            var c = CampaignRuntime.Instance; if (c == null || _campaignMenu == null) return;
            _campaignMenu.SetActive(_session.Mode == RaidSessionMode.Menu && _menuPage == MainMenuPage.Saves);
            string summary = c.Saved == null ? "저장된 원정이 없습니다" : c.Saved.completed ? "1부 원정 완료" : "1부  ·  Stage " + c.Saved.nextStage + " / 6";
            if (c.Settlement != null) summary = "1부 · Stage " + c.Settlement.stage + " 처치 후 정산 대기";
            _homeSave.text = summary + (c.Saved != null && !c.Saved.completed ? "  ·  마지막 출발 지점부터 재개" : "  ·  방 만들기에서 새롭게 출발하세요");
            if (c.Settlement != null) _homeSave.text = summary + " · 미완료 보상부터 재개";
            string date = c.Saved != null && DateTimeOffset.TryParse(c.Saved.savedUtc, out var savedAt) ? savedAt.ToLocalTime().ToString("yyyy.MM.dd  HH:mm") : "";
            _campaignSummary.text = summary + "\n\n" + (c.Saved?.completed == true ? "새 원정을 시작할 수 있습니다." : c.Saved == null ? "첫 출발과 스테이지 사이에\n자동으로 저장됩니다." : "노말  ·  " + (c.Saved.transport == "steam" ? "Steam 파티" : "싱글 / AI 파티") + "\n" + date + "\n\n직업과 파티 구성은\n저장 정보에서 확인하세요.");
            if (c.Settlement != null) _campaignSummary.text = c.SaveDescription + "\n\n이어하기 후 북쪽 문에서 미완료 보상을 정산합니다.";
            var resumable = c.Settlement?.run ?? c.Saved;
            _campaignContinue.interactable = c.CanContinue && RaidSteamRuntime.Instance?.Busy != true &&
                (resumable?.transport != "steam" || RaidSteamRuntime.Instance?.Available == true);
            _homeContinue.interactable = _campaignContinue.interactable;
            _homeContinue.gameObject.SetActive(RaidSteamRuntime.Instance?.Busy != true);
            _campaignNotice.text = c.Notice;
            _campaignHudNotice.gameObject.SetActive(c.HasSaveFailure && !_session.MenuOpen && _session.Mode != RaidSessionMode.Training);
            _campaignHudNotice.text = "Save failed: " + c.SaveFailureDetail + "\nOpen the menu to retry saving.";
            if (_campaignHudNotice.gameObject.activeSelf) RaidUIOrder.Raise(_campaignHudNotice.transform);
            _campaignRetry.gameObject.SetActive(c.HasSaveFailure);
            if (_campaignDialog.activeSelf) RaidUIOrder.Raise(_campaignDialog.transform);
        }
        private void DestroyCampaignUI()
        {
            Application.wantsToQuit -= WantsToQuit; CampaignDialogOpen = false;
            if (_campaignDialog != null) Destroy(_campaignDialog);
            if (_campaignHudNotice != null) Destroy(_campaignHudNotice.gameObject);
        }
    }
}
