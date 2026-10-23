using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidRogue
{
    public sealed partial class RaidNetworkSession
    {
        private float _loadingDeadline, _nextLoadedAck;
        private int _loadFrame;
        public bool LocalReady => _avatars.TryGetValue(_localId, out var avatar) && avatar.Ready;
        public bool CanStartLobby => !Ending && IsHost && CanChooseRole && _roster.Members.TrueForAll(m => (_recovery.Actor(m.Id)?.Automated ?? false) || (m.Ready && m.Loaded && _avatars.TryGetValue(m.Id,out var a) && CharacterActor.Ensure(a.gameObject).DepartureReady)) && _peers.TrueForAll(p => p.Id != 0 && p.CloseAt == 0) &&
            (!UsesSteam || !HasUnapprovedLobbyGuests(RaidSteamRuntime.Instance.Members()));
        private string HelloTag => SceneManager.GetActiveScene().name + (UsesSteam ? "|" + RaidSteamRuntime.Instance.Build + "|" + RaidSteamRuntime.Instance.LobbyId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
        public void ToggleReady()
        {
            if (!HasLocalAvatar || !ControlsReady || !CanChooseRole) return;
            if (IsHost) SetReady(_localId, !LocalReady);
            else Send(_server, new RaidPacket { Kind = RaidPacketKind.Ready, Id = LocalReady ? 0 : 1 }, true);
        }
        private void SetReady(int id, bool ready)
        {
            if(!IsHost||!CanChooseRole)return;
            if(ready&&_avatars.TryGetValue(id,out var avatar)&&!CharacterActor.Ensure(avatar.gameObject).DepartureReady){ActionFailure(id,"출발 전에 이지스 팔라딘의 기원을 선택하세요");return;}
            if (!_roster.SetReady(id, ready)) return;
            SyncLobbyAvatar(_roster.Members.Find(m => m.Id == id));
        }
        private void SetLoaded(int id, int epoch)
        {
            if (!IsHost || epoch != BattleEpoch || (BattlePhase != RaidBattlePhase.Loading && BattlePhase != RaidBattlePhase.Lobby)) return;
            var member = _roster.Members.Find(m => m.Id == id);
            if (member != null) { member.Loaded = true; SyncLobbyAvatar(member); }
        }
        private void SyncLobbyAvatar(RaidRoster.Member member)
        {
            if (member != null && _avatars.TryGetValue(member.Id, out var avatar))
            { avatar.SteamId = member.SteamId; avatar.Ready = member.Ready; avatar.Loaded = member.Loaded; }
        }
        private void BeginLoadingBarrier()
        {
            _loadFrame = Time.frameCount; _loadingDeadline = Time.unscaledTime + StageManager.Instance.FlowSettings.loadingSeconds; PhaseDeadline = _loadingDeadline;
            foreach (var member in _roster.Members) { member.Loaded = _recovery.Actor(member.Id)?.Automated ?? false; SyncLobbyAvatar(member); }
            RaidSteamRuntime.Instance?.PublishPhase();
        }
        public string FlowStatus
        {
            get
            {
                if (Ending) return Status;
                if (Mode == RaidSessionMode.Connecting || IsRecovering) return Status;
                if (!Multiplayer) return "";
                if (BattlePhase == RaidBattlePhase.Loading) return $"참가자 로딩 대기 · {Mathf.Max(0, Mathf.CeilToInt(PhaseDeadline - ServerTime))}초 후 시간 초과 처리";
                if (BattlePhase == RaidBattlePhase.Countdown) return $"전투 시작까지 {Mathf.Max(0, Mathf.CeilToInt(CountdownEnd - ServerTime))}초";
                if (BattlePhase == RaidBattlePhase.Reward) return $"카드 선택 대기 · {Mathf.Max(0, Mathf.CeilToInt(PhaseDeadline - ServerTime))}초 후 미선택자의 첫 카드 자동 선택";
                if (BattlePhase == RaidBattlePhase.Wiped) return IsHost ? "전멸 · 현재 보스 재도전 또는 메뉴 복귀" : "전멸 · 호스트 재도전 대기";
                if (BattlePhase == RaidBattlePhase.Victory) return "최종 클리어 · 전투 결과 확인 후 메뉴에서 새 런 시작";
                if (BattlePhase == RaidBattlePhase.Lobby) return $"역할 선택 및 준비 대기 · 미접속 로비 인원은 {StageManager.Instance.FlowSettings.lobbyApprovalSeconds:0}초 후 시작 대기에서 제외";
                return "";
            }
        }
        private void UpdateLoadingBarrier()
        {
            if (Ending || (BattlePhase != RaidBattlePhase.Loading && BattlePhase != RaidBattlePhase.Lobby)) return;
            bool built = Time.frameCount > _loadFrame + 1 && _avatars.Count > 0 &&
                (BattlePhase != RaidBattlePhase.Lobby || _avatars.Count == 4) && HasLocalAvatar && StageManager.Instance?.boss != null;
            foreach (var avatar in _avatars.Values)
                if (avatar.GetComponent<PlayerController>() == null && avatar.GetComponent<PartyBotAI>() == null) built = false;
            if (IsHost)
            {
                // Timeout must run even when local runtime construction failed.
                if (!built && _loadingDeadline > 0 && Time.unscaledTime >= _loadingDeadline)
                { FailSession("호스트 전장 구성 시간이 초과되었습니다. 원본 씬 자동 구성과 Console을 확인하세요."); return; }
                if (built) SetLoaded(_localId, BattleEpoch);
                foreach (var peer in _peers)
                {
                    bool timedOut = BattlePhase == RaidBattlePhase.Loading ? Time.unscaledTime >= _loadingDeadline : Time.unscaledTime - peer.ConnectedAt >= StageManager.Instance.FlowSettings.loadingSeconds;
                    if (peer.Id == 0 || peer.CloseAt != 0 || !timedOut || _roster.Members.Exists(m => m.Id == peer.Id && m.Loaded)) continue;
                    string reason = "전장 로딩 시간이 초과되었습니다. 다시 연결하거나 메뉴로 돌아가세요.";
                    if (UsesSteam)
                    { Send(peer.Connection, new RaidPacket { Kind = RaidPacketKind.Notice, Text = "재접속:" + reason }, true); peer.CloseAt = Time.unscaledTime + .5f; }
                    else Reject(peer, reason);
                    Debug.LogWarning($"[Raid {SessionTag}] epoch={BattleEpoch} actor={peer.Id} loading timeout");
                }
                if (built && BattlePhase == RaidBattlePhase.Loading && _roster.AllLoaded && _peers.TrueForAll(p => p.CloseAt == 0))
                {
                    if (!StageManager.Instance.BeginPreparedCountdown()) { FailSession("준비 상태가 일치하지 않습니다."); return; }
                    CountdownEnd = ServerTime + StageManager.Instance.FlowSettings.countdownSeconds; PhaseDeadline = CountdownEnd;
                    SetBattlePhase(RaidBattlePhase.Countdown, "loading barrier complete");
                }
            }
            else if (built && ControlsReady && _latestCombatFrame != null && _latestCombatFrame.epoch == BattleEpoch && System.Array.TrueForAll(_latestCombatFrame.party, a => _avatars.ContainsKey(a.id)) && Time.unscaledTime >= _nextLoadedAck)
            {
                _nextLoadedAck = Time.unscaledTime + .5f;
                Send(_server, new RaidPacket { Kind = RaidPacketKind.SceneReady, Id = BattleEpoch }, true);
            }
        }
        public List<ulong> ConnectedSteamIds()
        {
            var result = new List<ulong>(); foreach (var member in _roster.Members) if (member.SteamId != 0) result.Add(member.SteamId); return result;
        }
        public void EndSteamSession(string reason)
        {
            Status = reason;
            if (UsesSteam || Mode == RaidSessionMode.Closing) { RaidSteamRuntime.Instance?.ForgetRecovery(); RequestReturn(reason); }
        }
    }
}
