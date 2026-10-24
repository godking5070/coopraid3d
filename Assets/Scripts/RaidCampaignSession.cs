using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class RaidNetworkSession
    {
        private bool _campaignResumed;
        private readonly Dictionary<string, CampaignClearReceipt> _campaignReceipts = new Dictionary<string, CampaignClearReceipt>();
        private readonly HashSet<string> _campaignAcks = new HashSet<string>();
        private float _nextCampaignReceipt;
        private bool CampaignRemoteContext => UsesSteam && !IsHostAuthority && (Mode == RaidSessionMode.Client || IsRecovering || Mode == RaidSessionMode.Connecting);
        public bool CampaignRemoteSaveFailed => CampaignRemoteContext && _latestCombatFrame?.campaignSaveFailed == true;
        public string CampaignExitSaveLabel
        {
            get
            {
                if (Multiplayer && !UsesSteam) return "LAN 테스트 · 이 세션의 영구 체크포인트 없음";
                if (!CampaignRemoteContext) return CampaignRuntime.Instance?.LastSaveLabel ?? "저장된 체크포인트 없음";
                var frame = _latestCombatFrame;
                if (frame == null) return "호스트의 저장 지점 정보 수신 대기 중";
                if (frame.campaignSavedStage == 0) return "호스트의 현재 런에 정상 저장된 체크포인트 없음";
                return "호스트의 마지막 수신 저장 정보 · " + CampaignCatalog.Name(frame.chapterId) +
                    (frame.campaignCompleted ? " · 완료된 런" : " · Stage " + frame.campaignSavedStage + " 시작") + " · " + frame.campaignSavedUtc;
            }
        }
        public bool CampaignBuildLocked => _campaignResumed && BattlePhase == RaidBattlePhase.Lobby;
        public bool AcceptsCampaignUser(ulong id) => !_campaignResumed || id == RaidSteamRuntime.Instance.UserId || _recovery.Find(id) != null;
        public bool CampaignUserConnected(NetworkRaidAvatar avatar) => avatar != null && !avatar.Automated && (avatar.IsLocal || _peers.Any(p => p.Id == avatar.Id && PeerControls(p)));
        internal void ContinueCampaignSingle()
        {
            var c = CampaignRuntime.Instance;
            if (Mode != RaidSessionMode.Menu || c?.Resume == null || c.Resume.transport != "single") return;
            Mode = RaidSessionMode.Single; MenuOpen = false;
            RoleSelectionManager.Instance?.CloseRoleSelectionModal();
            var saved = c.Resume;
            var local = Array.Find(saved.party, p => !p.bot && p.userId == c.Owner);
            _player.ApplyRoleStatsAndVisuals((PlayerRole)local.role, true);
            // The same role-completion policy as initial departure, with saved roles restored.
            var bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None).OrderBy(b => b.GetInstanceID()).ToArray();
            var botSlots = saved.party.Where(p => p.bot).ToArray();
            if (bots.Length != botSlots.Length) { FailSession("저장된 AI 파티 구성을 복구할 수 없습니다"); return; }
            for (int i = 0; i < bots.Length; i++) bots[i].ConfigureBotRoleDynamic((PartyBotRole)(botSlots[i].role - 1), SpawnPoint(botSlots[i].role));
            if (!CharacterActor.Ensure(_player.gameObject).RestoreCampaignBuild(local)) { FailSession("저장된 직업을 복구할 수 없습니다"); return; }
            c.Bind(_player.transform, local);
            for (int i = 0; i < bots.Length; i++)
            {
                if (!CharacterActor.Ensure(bots[i].gameObject).RestoreCampaignBuild(botSlots[i])) { FailSession("저장된 AI 직업을 복구할 수 없습니다"); return; }
                c.Bind(bots[i].transform, botSlots[i]);
            }
            StageManager.Instance.RestoreCampaignHeader(saved);
            if (!StageManager.Instance.PrepareCampaignEncounter()) { FailSession("체크포인트 준비 실패"); return; }
            if (Ending) return;
            c.RestoreFinished();
            StageManager.Instance.ContinueCampaignCountdown();
        }
        private void RestoreCampaignLobby()
        {
            var c = CampaignRuntime.Instance; var saved = c?.Resume;
            if (!IsHost || !UsesSteam) return;
            // A completed run cannot be continued. Its undelivered confirmations still reach
            // the original authenticated recipient in a later lobby hosted by the same account.
            if (c?.Personal != null) foreach (var receipt in c.Personal.deliveries) QueueCampaignReceipt(receipt);
            if (saved == null) return;
            if (saved.owner != CampaignRuntime.CurrentOwner || saved.transport != "steam") throw new InvalidOperationException("같은 Steam 호스트만 저장을 재개할 수 있습니다");
            _campaignResumed = true;
            // Discard only freshly-created menu previews; never transfer live actors/IDs from disk.
            foreach (int id in new List<int>(_avatars.Keys)) if (id != _localId) RemoveAvatar(id);
            foreach (var slot in saved.party.Where(p => !p.bot).OrderBy(p => p.userId == saved.owner ? 0 : 1))
            {
                bool local = slot.userId == saved.owner;
                var member = local ? _roster.Members.Find(m => m.Id == _localId) : _roster.Join(slot.name);
                if (member == null) throw new InvalidOperationException("저장된 파티 정원이 현재 빌드와 다릅니다");
                member.Role = slot.role; member.SteamId = CampaignIdentity.Steam(slot.userId); member.Ready = false; member.Loaded = true;
                if (!local) AddAvatar(member.Id, member.Role, member.Name, SpawnPoint(member.Role));
                var avatar = _avatars[member.Id]; avatar.SetIdentity(member.Role, member.Name); SyncLobbyAvatar(member);
                if (!CharacterActor.Ensure(avatar.gameObject).RestoreCampaignBuild(slot)) throw new InvalidOperationException("저장된 캐릭터 상태 복구 실패");
                avatar.RefreshJobLabel(); c.Bind(avatar.transform, slot);
                if (!local)
                {
                    RegisterRecoveryMember(member); _recovery.Reserve(member.Id, ServerTime);
                    // Waiting for original participants in the new lobby has no 60-second expiry.
                    // Existing live-disconnect policy starts when the party departs.
                    _recovery.Actor(member.Id).Deadline = float.MaxValue; avatar.Automated = true;
                }
            }
            ReconcileBots();
            foreach (var slot in saved.party.Where(p => p.bot))
            {
                var avatar = _avatars.Values.FirstOrDefault(a => a.IsBot && a.Role == slot.role);
                if (avatar == null || !CharacterActor.Ensure(avatar.gameObject).RestoreCampaignBuild(slot)) throw new InvalidOperationException("저장된 빈 역할 AI 복구 실패");
                c.Bind(avatar.transform, slot);
            }
            StageManager.Instance.RestoreCampaignHeader(saved); c.RestoreMetrics();
            foreach (var receipt in saved.receipts) QueueCampaignReceipt(receipt);
            MenuOpen = true; Status = "저장 이어하기 · 원래 참가자만 복귀 가능 · 직업/기원/카드 고정 · 미접속 자리는 AI 유지";
        }
        private void BeginCampaignReservations()
        {
            foreach (var slot in _recovery.All)
                if (slot.Automated && slot.Deadline == float.MaxValue) slot.Deadline = ServerTime + RecoverySettings.reservationSeconds;
        }
        internal void QueueCampaignReceipt(CampaignClearReceipt receipt)
        {
            if (!IsHost || !UsesSteam || !CampaignValidation.Receipt(receipt, receipt?.userId)) return;
            _campaignReceipts[receipt.userId + "/" + receipt.Key] = receipt;
            // Send confirmed clears immediately, including the terminal stage. The periodic
            // outbox remains responsible for retransmission until the recipient's disk ACK.
            var member = _roster.Members.Find(m => m.SteamId == CampaignIdentity.Steam(receipt.userId));
            var peer = member == null ? null : _peers.Find(p => p.Id == member.Id && PeerControls(p));
            if (peer != null) Send(peer.Connection, new RaidPacket { Kind = RaidPacketKind.CampaignClear, Text = JsonUtility.ToJson(receipt) }, true);
        }
        private void SendCampaignReceipts()
        {
            if (!IsHost || !UsesSteam || Time.unscaledTime < _nextCampaignReceipt) return;
            _nextCampaignReceipt = Time.unscaledTime + 2;
            foreach (var pair in _campaignReceipts)
            {
                if (_campaignAcks.Contains(pair.Key)) continue;
                ulong user = CampaignIdentity.Steam(pair.Value.userId);
                var member = _roster.Members.Find(m => m.SteamId == user);
                var peer = member == null ? null : _peers.Find(p => p.Id == member.Id && PeerControls(p));
                if (peer != null) Send(peer.Connection, new RaidPacket { Kind = RaidPacketKind.CampaignClear, Text = JsonUtility.ToJson(pair.Value) }, true);
            }
        }
        private void ReceiveCampaignReceipt(string json)
        {
            if (!UsesSteam || IsHost || Mode != RaidSessionMode.Client || !ControlsReady) return;
            try
            {
                var r = JsonUtility.FromJson<CampaignClearReceipt>(json);
                // Only the authenticated server sends this packet. A durable outbox may refer
                // to a previous run; recipient ID and stable receipt validation still apply.
                if (r == null || !CampaignValidation.Receipt(r, CampaignRuntime.CurrentOwner) || !r.eligible ||
                    !CampaignCatalog.Available(r.chapterId)) return;
                if (CampaignRuntime.Instance?.AcceptReceipt(r) == true)
                    Send(_server, new RaidPacket { Kind = RaidPacketKind.CampaignAck, Text = r.Key }, true);
            }
            catch (ArgumentException) { }
        }
        private void AcknowledgeCampaign(Peer peer, string key)
        {
            if (!IsHost || !UsesSteam || !PeerControls(peer)) return;
            string receiptKey = CampaignIdentity.ForSteam(_transport.RemoteSteamId(peer.Connection)) + "/" + key;
            if (_campaignReceipts.ContainsKey(receiptKey))
            {
                _campaignAcks.Add(receiptKey);
                CampaignRuntime.Instance?.AcknowledgeReceipt(CampaignIdentity.ForSteam(_transport.RemoteSteamId(peer.Connection)), key);
            }
        }
    }
}
