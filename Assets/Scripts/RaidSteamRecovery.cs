using System.Globalization;
using Steamworks;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class RaidSteamRuntime
    {
        private const string RecoveryKey = "Raid.Steam.PreviousLobby";
        private bool _recovering, _recoveryConnecting, _readOnlyRecovery;
        public ulong PreviousLobby => RaidSteamPolicy.TryId(PlayerPrefs.GetString(RecoveryKey, ""), out ulong id) ? id : 0;
        public bool CanRecover => Available && PreviousLobby != 0 && !Busy;
        public void RememberRecovery()
        {
            if (LobbyId == 0 || HostId == UserId) return;
            PlayerPrefs.SetString(RecoveryKey, LobbyId.ToString(CultureInfo.InvariantCulture)); PlayerPrefs.Save();
        }
        public void ForgetRecovery() { PlayerPrefs.DeleteKey(RecoveryKey); PlayerPrefs.Save(); }
        public void ConnectionLost()
        {
            _recovering = _recoveryConnecting = false; _invites.Finish(); _waitingHost = false;
            Status = "연결 끊김 · 기존 캐릭터는 호스트 AI가 임시 제어합니다";
        }
        public void ConnectionRestored()
        {
            _recovering = _recoveryConnecting = false; _invites.Finish();
            Status = "Steam Relay 재접속 및 현재 상태 복구 완료"; RememberRecovery();
        }
        public void CancelRecoveryAttempt()
        { _recovering = _recoveryConnecting = false; ++_generation; }
        public void RequestRecovery()
        {
            if (!CanRecover || Session == null || (Session.Mode != RaidSessionMode.Menu && Session.Mode != RaidSessionMode.Disconnected)) return;
            // A running raid keeps its lobby closed. Steam permits metadata lookup without membership;
            // only the host's authenticated transport identity/slot ledger can authorize a return.
            ulong lobby = PreviousLobby; Session.BeginRecoveryLookup();
            if (!new CSteamID(lobby).IsLobby()) { RecoveryError("저장된 로비 정보가 올바르지 않습니다.", true); return; }
            LobbyId = lobby; HostId = 0; _recovering = _readOnlyRecovery = true; _recoveryConnecting = false;
            _deadline = Time.unscaledTime + 30; Status = "이전 로비와 호스트 확인 중…";
            QuarterViewCamera.LocalCamera?.ReleaseOrbit();
            if (!SteamMatchmaking.RequestLobbyData(new CSteamID(lobby))) RecoveryError("이전 로비 정보를 요청할 수 없습니다. Steam 연결을 확인하세요.", false);
        }
        private void RecoveryMetadata(bool success)
        {
            if (!_recovering || _recoveryConnecting || Session == null) return;
            if (!success) { RecoveryError("이전 방이 사라졌거나 세션이 종료되었습니다.", true); return; }
            var data = Metadata();
            if (data["game"] != RaidSteamPolicy.Game || data["build"] != Build || data["protocol"] != RaidWire.Version.ToString(CultureInfo.InvariantCulture))
            { RecoveryError("이전 게임과 빌드·프로토콜이 다릅니다. 동일한 빌드로 실행하세요.", true); return; }
            if (data["phase"] == "closed" || !RaidSteamPolicy.TryId(data["host"], out ulong host) || host == UserId)
            { RecoveryError("원래 호스트가 종료했습니다. 호스트 이전은 지원하지 않습니다.", true); return; }
            HostId = host; _recoveryConnecting = true; Status = "Steam 호스트 인증 및 예약 자리 확인 중…";
            if (!Session.StartSteamRecovery(host, Persona(UserId))) RecoveryError("호스트에 다시 연결하지 못했습니다.", false);
        }
        private void RecoveryError(string reason, bool terminal)
        {
            _recovering = _recoveryConnecting = false; Status = reason;
            if (terminal) ForgetRecovery();
            Session?.RecoveryFailed(reason, terminal);
        }
    }
}
