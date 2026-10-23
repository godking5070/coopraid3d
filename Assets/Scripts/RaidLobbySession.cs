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
        public bool CanStartLobby => IsHost && CanChooseRole && _roster.Members.TrueForAll(m => (_recovery.Actor(m.Id)?.Automated ?? false) || (m.Ready && m.Loaded)) && _peers.TrueForAll(p => p.Id != 0 && p.CloseAt == 0) &&
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
            if (!IsHost || !CanChooseRole || !_roster.SetReady(id, ready)) return;
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
            _loadFrame = Time.frameCount; _loadingDeadline = Time.unscaledTime + 30;
            foreach (var member in _roster.Members) { member.Loaded = _recovery.Actor(member.Id)?.Automated ?? false; SyncLobbyAvatar(member); }
            RaidSteamRuntime.Instance?.PublishPhase();
        }
        private void UpdateLoadingBarrier()
        {
            if (BattlePhase != RaidBattlePhase.Loading && BattlePhase != RaidBattlePhase.Lobby) return;
            // Runtime-built scene and all four actors must exist, and their Start callbacks must have run.
            if (Time.frameCount <= _loadFrame + 1 || _avatars.Count == 0 || (BattlePhase == RaidBattlePhase.Lobby && _avatars.Count != 4) || !HasLocalAvatar || StageManager.Instance?.boss == null) return;
            foreach (var avatar in _avatars.Values)
                if (avatar.GetComponent<PlayerController>() == null && avatar.GetComponent<PartyBotAI>() == null) return;
            if (IsHost)
            {
                SetLoaded(_localId, BattleEpoch);
                if (BattlePhase != RaidBattlePhase.Loading) return;
                if (_roster.AllLoaded)
                { BattlePhase = RaidBattlePhase.Countdown; CountdownEnd = ServerTime + 3; return; }
                if (Time.unscaledTime > _loadingDeadline)
                    foreach (var peer in _peers)
                        if (peer.CloseAt == 0 && !_roster.Members.Exists(m => m.Id == peer.Id && m.Loaded)) Reject(peer, "전투 준비 시간이 초과되었습니다. 다시 참가하세요.");
            }
            else if (ControlsReady && _latestCombatFrame != null && _latestCombatFrame.epoch == BattleEpoch && System.Array.TrueForAll(_latestCombatFrame.party, a => _avatars.ContainsKey(a.id)) && Time.unscaledTime >= _nextLoadedAck)
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
