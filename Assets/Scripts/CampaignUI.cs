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
            _start.GetComponent<RectTransform>().anchoredPosition = new Vector2(-230, 0);
            _campaignMenu = Box(overlay, "CampaignSaveMenu", new Vector2(550, -40), new Vector2(440, 510), new Color(.06f, .10f, .16f)).gameObject;
            _campaignSummary = Label(_campaignMenu.transform, "", new Vector2(0, 165), new Vector2(414, 150), 18);
            _campaignContinue = ButtonAt(_campaignMenu.transform, "이어하기", new Vector2(0, 52), new Vector2(390, 46), () => {
                var c = CampaignRuntime.Instance; if (c?.Saved != null) c.Continue(c.Saved.transport == "steam");
            });
            ButtonAt(_campaignMenu.transform, "저장 정보 확인", new Vector2(0, -2), new Vector2(390, 46), () => CampaignDialog(CampaignRuntime.Instance.SaveDescription, null));
            ButtonAt(_campaignMenu.transform, "런 체크포인트 삭제", new Vector2(0, -56), new Vector2(390, 46), () => CampaignDialog("이 계정의 런 체크포인트와 최근 백업을 삭제합니다.\n개인 클리어 기록·챕터 해금·설정은 유지됩니다.\n\n" + CampaignRuntime.Instance.LastSaveLabel, () => CampaignRuntime.Instance.DeleteSelected(false)));
            ButtonAt(_campaignMenu.transform, "개인 진행도 / 챕터 해금", new Vector2(0, -110), new Vector2(390, 46), ShowPersonalProgress);
            ButtonAt(_campaignMenu.transform, "개인 진행도 삭제", new Vector2(0, -164), new Vector2(390, 46), () => CampaignDialog("이 계정의 개인 스테이지 클리어·챕터 해금과 백업을 삭제합니다.\n런 체크포인트·설정·키 배치는 유지됩니다.\n기존 런의 완료 영수증을 다시 받으면 해당 기록은 복구될 수 있습니다.", () => CampaignRuntime.Instance.DeleteSelected(true)));
            Label(_campaignMenu.transform, "1부/2부 정식 콘텐츠는 현재 빌드에 미포함", new Vector2(0, -218), new Vector2(414, 40), 16);
            _campaignNotice = Label(overlay, "", new Vector2(0, -380), new Vector2(1400, 75), 16);
            _campaignHudNotice = Label(canvas, "", new Vector2(0, 180), new Vector2(1000, 90), 19);
            _campaignHudNotice.color = new Color(1, .7f, .35f);
            _campaignRetry = ButtonAt(overlay, "저장 재시도", new Vector2(0, -435), new Vector2(260, 40), () => CampaignRuntime.Instance?.RetrySave());
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
            string text = "개인 계정: " + c.Owner + "\n확정된 스테이지 클리어: " + c.Personal.clears.Length + "건\n1부 완료: " + (c.Personal.PartOneComplete ? "완료" : "미완료") + "\n2부: " + (c.Personal.PartTwoUnlocked ? "영구 해금됨" : "1부 필수 6스테이지와 최종 클리어 필요") +
                "\n\n현재 실행 가능한 콘텐츠는 4인·3스테이지 프로토타입입니다.\n프로토타입 클리어는 1부 완료로 인정하지 않습니다.\n정식 1부(4인·6스테이지), 2부(8인·8스테이지) 데이터가 없어 출발할 수 없습니다.\n" + CampaignCatalog.DepartureRequirement(CampaignCatalog.PartTwo) + "\n훈련장·개발 기능은 개인 클리어와 저장에서 제외됩니다.";
            CampaignDialog(text, null);
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
                "\n\n현재 전투와 완료하지 않은 보상 선택은 저장되지 않습니다.\n이어하기는 마지막 저장 스테이지 시작부터 재개합니다." +
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
            _campaignMenu.SetActive(_session.Mode == RaidSessionMode.Menu);
            _campaignSummary.text = c.LastSaveLabel + "\n" + (c.Saved?.completed == true ? "완료된 런 · 새 게임으로 출발하세요" : c.Saved == null ? "첫 출발 및 보상 선택 완료 후 자동 저장" : c.Saved.transport == "steam" ? "같은 호스트 · 새 Steam 로비로 재개" : "싱글플레이 · 저장 구성으로 재개");
            _campaignContinue.interactable = c.CanContinue && RaidSteamRuntime.Instance?.Busy != true && (c.Saved.transport != "steam" || RaidSteamRuntime.Instance?.Available == true);
            _campaignNotice.text = c.Notice;
            _campaignHudNotice.gameObject.SetActive(c.HasSaveFailure && !_session.MenuOpen && _session.Mode != RaidSessionMode.Training);
            _campaignHudNotice.text = "저장 실패 · 플레이는 계속되며 이전 저장을 유지합니다.\n메뉴에서 원인 확인 / 저장 재시도 가능";
            if (_campaignHudNotice.gameObject.activeSelf) _campaignHudNotice.transform.SetAsLastSibling();
            _campaignRetry.gameObject.SetActive(c.HasSaveFailure);
            if (_campaignDialog.activeSelf) _campaignDialog.transform.SetAsLastSibling();
        }
        private void DestroyCampaignUI()
        {
            Application.wantsToQuit -= WantsToQuit; CampaignDialogOpen = false;
            if (_campaignDialog != null) Destroy(_campaignDialog);
            if (_campaignHudNotice != null) Destroy(_campaignHudNotice.gameObject);
        }
    }
}
