using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class RaidNetworkSession
    {
        public readonly RaidRecoverySettings RecoverySettings = new RaidRecoverySettings();
        private RaidRecoverySlots _recovery;
        private bool _steamSession, _resumeRequested, _controlReady = true;
        private uint _localLease;
        private float _restoreStarted, _noticeUntil;
        private int _autoRevisionSeen;
        private readonly HashSet<int> _expiryNotices = new HashSet<int>();
        public string PartyNotice { get; private set; }
        public bool IsRecovering => Mode == RaidSessionMode.Disconnected || Mode == RaidSessionMode.Restoring || _resumeRequested;
        public bool ControlsReady => !Ending && _controlReady && Mode != RaidSessionMode.Disconnected && Mode != RaidSessionMode.Restoring;
        public bool ResumeRequested => _resumeRequested;
        public string VisiblePartyNotice => Time.unscaledTime < _noticeUntil ? PartyNotice : "";
        public bool KnowsSteamMember(ulong id) => _recovery.Find(id) != null;
        private readonly RaidAdmissionWait _admissionWait = new RaidAdmissionWait();
        public bool HasUnapprovedLobbyGuests(List<ulong> members)
        {
            _admissionWait.Retain(members); bool waiting = false;
            foreach (var id in members)
                waiting |= _admissionWait.Waiting(id, id == RaidSteamRuntime.Instance.UserId || _roster.Members.Exists(m => m.SteamId == id), ServerTime, StageManager.Instance.FlowSettings.lobbyApprovalSeconds);
            return waiting;
        }
        private void RegisterRecoveryMember(RaidRoster.Member member)
        { if (UsesSteam && member.Id != _localId) _recovery.Register(member.SteamId, member.Id); }
        private void DropPeer(Peer peer)
        {
            if (peer.Id == 0) return;
            Debug.Log($"[Raid {SessionTag}] epoch={BattleEpoch} actor={peer.Id} disconnected; voluntary={peer.Voluntary} phase={BattlePhase}");
            if (UsesSteam && !peer.Voluntary && _recovery.Actor(peer.Id) != null)
            {
                bool wasHuman = _recovery.Actor(peer.Id).State == RaidControlState.Connected;
                bool changed = _recovery.Reserve(peer.Id, ServerTime, BattlePhase == RaidBattlePhase.Reward ? BattleEpoch : -1);
                if (changed && _avatars.TryGetValue(peer.Id, out var avatar))
                {
                    avatar.Automated = true; avatar.MotionVersion++;
                    if (wasHuman)
                    {
                        _motors[peer.Id].Dash.Cancel(); // Keep remaining cooldown, HP, shields and all progression.
                        var pc = avatar.GetComponent<PlayerController>(); pc.combatController.InterruptForConnectionLoss();
                        pc.GetComponent<BattleResurrection>()?.Cancel(); pc.SetNetworkMoving(false);
                    }
                    _actionGate.Remove(peer.Id); peer.Inputs.DiscardPending();
                    var member = _roster.Members.Find(m => m.Id == peer.Id); member.Ready = false; member.Loaded = true; SyncLobbyAvatar(member);
                    if (wasHuman) BroadcastPartyNotice(avatar.DisplayName + " 연결 끊김 · AI 대체 중");
                }
                return;
            }
            _recovery.Remove(peer.Id); _roster.Remove(peer.Id); OnNetworkDeparture(peer.Id); RemoveAvatar(peer.Id);
            if (CanJoinBattle) ReconcileBots();
        }
        private bool TryResumePeer(Peer peer)
        {
            ulong steam = UsesSteam ? _transport.RemoteSteamId(peer.Connection) : 0;
            string failure = _recovery.BeginRestore(steam, ServerTime, out var slot);
            if (failure != null)
            {
                if (slot != null && slot.State != RaidControlState.PermanentAI)
                { Send(peer.Connection, new RaidPacket { Kind = RaidPacketKind.Notice, Text = "재접속:" + failure }, true); peer.CloseAt = Time.unscaledTime + .5f; }
                else Reject(peer, failure);
                return false;
            }
            if (!_avatars.TryGetValue(slot.Actor, out var avatar)) { Reject(peer, "복구할 캐릭터가 없습니다."); return false; }
            peer.Id = slot.Actor; peer.Lease = slot.Lease; peer.RecoveryFrame = true; peer.LastInput = Time.unscaledTime;
            avatar.Automated = true;
            var member = _roster.Members.Find(m => m.Id == peer.Id); member.Loaded = false; SyncLobbyAvatar(member);
            _actionGate.Remove(peer.Id);
            SendWelcome(peer, true); SendSnapshot(peer.Connection); SendCombatFrame(peer, CaptureCombatFrame());
            return true;
        }
        private void SendWelcome(Peer peer, bool recovering)
        {
            _config.Id = peer.Id; _config.Tick = peer.Lease; _config.Role = recovering ? 1 : 0;
            Send(peer.Connection, _config, true);
        }
        private bool PeerControls(Peer peer) => peer.CloseAt == 0 && peer.Id != 0 &&
            (_avatars.TryGetValue(peer.Id, out var avatar) && !avatar.Automated);
        private void AcceptRestore(Peer peer, RaidPacket packet)
        {
            if (!peer.RecoveryFrame || (uint)packet.Role != peer.Lease || packet.Id != BattleEpoch ||
                !peer.Barrier.Accept(packet.Tick, peer.Lease, BattleEpoch, (int)BattlePhase)) return;
            var slot = _recovery.Actor(peer.Id);
            if (slot == null || !_recovery.Complete(slot.SteamId, peer.Lease, ServerTime)) { if (slot != null && slot.State == RaidControlState.PermanentAI) Reject(peer, "복귀 가능 시간이 지났습니다."); return; }
            var avatar = _avatars[peer.Id]; avatar.Automated = false; avatar.MotionVersion++;
            peer.Inputs.DiscardPending(); _actionGate.Remove(peer.Id);
            avatar.GetComponent<PlayerController>().SetNetworkMoving(false);
            var member = _roster.Members.Find(m => m.Id == peer.Id); member.Loaded = true; SyncLobbyAvatar(member);
            Debug.Log($"[Raid {SessionTag}] epoch={BattleEpoch} actor={peer.Id} restored lease={peer.Lease} phase={BattlePhase}");
            BroadcastPartyNotice(avatar.DisplayName + " 복귀 성공");
            // The grant is part of a NEW full frame, after the AI-to-owner switch. Never restore client state on the host.
            SendCombatFrame(peer, CaptureCombatFrame());
        }
        private void UpdateRecoveryHost()
        {
            if (Ending || !IsHost) return;
            foreach (var slot in _recovery.All)
            {
                _recovery.Expire(slot, ServerTime);
                if (slot.State == RaidControlState.PermanentAI && _expiryNotices.Add(slot.Actor))
                {
                    BroadcastPartyNotice(_avatars[slot.Actor].DisplayName + " 복귀 시간 만료 · 이번 런 AI 전환");
                    var peer = _peers.Find(p => p.Id == slot.Actor); if (peer != null) Reject(peer, "복귀 가능 시간이 지났습니다.");
                    var member = _roster.Members.Find(m => m.Id == slot.Actor); member.Loaded = true; SyncLobbyAvatar(member);
                }
                if (BattlePhase != RaidBattlePhase.Reward || !slot.Automated || _rewards.Chosen(slot.Actor)) continue;
                _recovery.BeginReward(slot, BattleEpoch, ServerTime);
                int[] cards = _rewards.Cards(slot.Actor);
                if (cards.Length == 0 || !_recovery.RewardDue(slot, BattleEpoch, ServerTime) || !_rewards.Choose(BattleEpoch, slot.Actor, cards[0])) continue;
                _avatars[slot.Actor].GetComponent<PlayerController>().cardManager.ApplyNetworkCard((RoguelikeCardId)cards[0]);
                _recovery.AutoSelected(slot, cards[0]);
            }
            FinishRewardsIfReady();
        }
        private void CaptureRecovery(RaidCombatActor actor)
        {
            var slot = _recovery.Actor(actor.id); if (slot == null) return;
            actor.controlState = (int)slot.State; actor.returnDeadline = slot.Deadline;
            actor.autoRevision = slot.AutoRevision; actor.autoCards = slot.AutoCards.ToArray();
        }
        private RaidPose[] RecoveryPoses()
        { return BuildSnapshot().Poses.ToArray(); }
        private void ReceiveRecoveryFrame(RaidCombatFrame frame)
        {
            if (Mode != RaidSessionMode.Restoring || frame.ownerLease != _localLease || frame.recoveryPoses == null) return;
            if (!HasLocalAvatar || Array.Exists(frame.party, a => !_avatars.ContainsKey(a.id))) return;
            _lastEffect = Math.Max(_lastEffect, frame.effectWatermark);
            if (!frame.controlGranted)
            {
                Send(_server, new RaidPacket { Kind = RaidPacketKind.RestoreReady, Id = frame.epoch, Tick = frame.serial, Role = (int)_localLease }, true);
                return;
            }
            _lastSentTarget = int.MinValue; _controlReady = true; _resumeRequested = false; Mode = RaidSessionMode.Client; _prediction.Clear();
            MenuOpen = frame.phase == RaidBattlePhase.Lobby;
            _restoreStarted = 0; Status = "복귀 완료 · 현재 전투 상태로 연결되었습니다";
            RaidSteamRuntime.Instance?.ConnectionRestored();
            Send(_server, new RaidPacket { Kind = RaidPacketKind.RestoreComplete, Id = frame.epoch, Tick = frame.serial, Role = (int)_localLease }, true);
            var local = Array.Find(frame.party, a => a.id == _localId);
            if (local != null && local.autoRevision > _autoRevisionSeen && local.autoCards != null)
            {
                var names = new List<string>(); for (int i = Mathf.Clamp(_autoRevisionSeen, 0, local.autoCards.Length); i < local.autoCards.Length; i++) names.Add(_player.cardManager.NetworkCardName(local.autoCards[i]));
                ShowPartyNotice("연결 중 자동 선택된 카드: " + string.Join(", ", names)); _autoRevisionSeen = local.autoRevision;
            }
            else ShowPartyNotice("현재 게임으로 복귀했습니다");
        }
        public void BeginRecoveryLookup()
        {
            if (Ending) return;
            _steamSession = true; _controlReady = false; Mode = RaidSessionMode.Disconnected; MenuOpen = true;
            Status = "이전 세션을 확인하고 있습니다…";
        }
        public bool StartSteamRecovery(ulong host, string name)
        {
            if (Ending || (Mode != RaidSessionMode.Menu && Mode != RaidSessionMode.Disconnected) || _player == null) return false;
            _resumeRequested = true; _controlReady = false; _steamSession = true;
            _prediction.Clear(); _frameDirection = Vector3.zero; _dashQueued = false; _sequence = _lastSnapshot = _actionSequence = 0;
            _clockReady = false; _bestRtt = float.MaxValue;
            return OpenTransport(() => new RaidSteamTransport(false, host, _ => false), false, name, RaidSteamRuntime.Instance.UserId);
        }
        private void UnexpectedClientLoss(string reason)
        {
            if (Ending) return;
            if (!UsesSteam) { RequestReturn(reason); return; }
            if (Mode != RaidSessionMode.Disconnected) Debug.LogWarning($"[Raid {SessionTag}] epoch={BattleEpoch} phase={BattlePhase} connection lost: {reason}");
            DisposeDriver(); _controlReady = false; _resumeRequested = false; Mode = RaidSessionMode.Disconnected; MenuOpen = true;
            _prediction.Clear(); _frameDirection = Vector3.zero; _dashQueued = false; _returnRequested = false;
            QuarterViewCamera.LocalCamera?.ReleaseOrbit();
            Status = reason + $" · 호스트가 살아 있다면 자리 예약 {RecoverySettings.reservationSeconds:0}초 내에 복귀할 수 있습니다.";
            RaidSteamRuntime.Instance?.ConnectionLost();
        }
        public void RecoveryFailed(string reason, bool terminal)
        {
            if (terminal) { RaidSteamRuntime.Instance?.ForgetRecovery(); RequestReturn(reason); }
            else UnexpectedClientLoss(reason);
        }
        public void CancelRecovery()
        {
            // Cancelling an attempt does not turn it into a voluntary in-game departure.
            RaidSteamRuntime.Instance?.CancelRecoveryAttempt(); UnexpectedClientLoss("재접속 시도를 취소했습니다");
        }
        public void RecoveryMenu()
        {
            RaidSteamRuntime.Instance?.CancelRecoveryAttempt(); DisposeDriver(); _returnMessage = "이전 게임으로 복귀 버튼으로 다시 시도할 수 있습니다."; ReloadMenu();
        }
        private void ShowPartyNotice(string message) { PartyNotice = message; _noticeUntil = Time.unscaledTime + 7; }
        private void BroadcastPartyNotice(string message)
        {
            ShowPartyNotice(message);
            foreach (var peer in _peers) if (peer.Id != 0 && peer.CloseAt == 0) Send(peer.Connection, new RaidPacket { Kind = RaidPacketKind.PartyNotice, Text = message }, true);
        }
        public string ConnectionLabel(RaidCombatActor actor)
        {
            if (actor == null || actor.controlState == 0) return "";
            if (actor.controlState == (int)RaidControlState.PermanentAI) return "AI 대체 확정";
            return (actor.controlState == (int)RaidControlState.Restoring ? "복구 중 · AI 대체" : "연결 끊김 · AI 대체 중") + " " + Mathf.Max(0, Mathf.CeilToInt(actor.returnDeadline - ServerTime)) + "초";
        }
    }
}
