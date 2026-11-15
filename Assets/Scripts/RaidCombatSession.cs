using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class RaidNetworkSession
    {
        public bool IsHostAuthority => IsHost;
        public RaidBattlePhase BattlePhase { get; private set; } = RaidBattlePhase.Lobby;
        public int BattleEpoch { get; private set; }
        public float CountdownEnd { get; private set; }
        public float ServerTime => IsHost ? Time.unscaledTime : Time.unscaledTime + _serverOffset;
        public bool CombatRunning => Multiplayer && BattlePhase == RaidBattlePhase.Combat;
        public bool CanJoinBattle => BattlePhase == RaidBattlePhase.Lobby;
        public bool CanChooseRole => BattlePhase == RaidBattlePhase.Lobby;
        private readonly RaidActionGate _actionGate = new RaidActionGate();
        private readonly RaidRewardRound _rewards = new RaidRewardRound();
        private readonly RaidFrameAssembler _combatAssembler = new RaidFrameAssembler();
        private readonly Dictionary<int, BossAddMinion> _combatAdds = new Dictionary<int, BossAddMinion>();
        private readonly Dictionary<int, AoETelegraph> _replicaTelegraphs = new Dictionary<int, AoETelegraph>();
        private readonly Dictionary<int, LineRenderer> _replicaLines = new Dictionary<int, LineRenderer>();
        private readonly Dictionary<int, AttachedPartyMechanicMarker> _replicaMarkers = new Dictionary<int, AttachedPartyMechanicMarker>();
        private readonly List<PartyBotAI> _standbyBots = new List<PartyBotAI>();
        private int _nextEnemyId = -100, _lastSentTarget;
        private uint _actionSequence, _combatFrameId, _effectId, _lastEffect;
        private float _nextCombatFrame, _nextPing, _serverOffset, _bestRtt = float.MaxValue;
        private RaidCombatFrame _latestCombatFrame;
        private bool _clockReady;
        public RaidNetworkMotor MotorFor(Transform actor) => actor != null && actor.GetComponent<NetworkRaidAvatar>() is NetworkRaidAvatar avatar && _motors.TryGetValue(avatar.Id, out var motor) ? motor : null;
        public int CombatId(Transform actor)
        {
            if (actor == null) return 0;
            var avatar = actor.GetComponent<NetworkRaidAvatar>(); if (avatar != null) return avatar.Id;
            if (actor.GetComponent<BossController>() != null) return -1;
            var add = actor.GetComponent<BossAddMinion>();
            if (add != null)
            {
                foreach (var pair in _combatAdds) if (pair.Value == add) return pair.Key;
                if (IsHost) { int id = _nextEnemyId--; _combatAdds[id] = add; return id; }
            }
            return 0;
        }
        public Transform CombatTarget(int id)
        {
            if (id == -1) return StageManager.Instance?.boss?.transform;
            if (_avatars.TryGetValue(id, out var avatar)) return avatar.transform;
            return _combatAdds.TryGetValue(id, out var add) && add != null ? add.transform : null;
        }
        private void EnsureCombatActor(NetworkRaidAvatar avatar)
        {
            if (avatar.GetComponent<PartyBotAI>() != null) return;
            var pc = avatar.GetComponent<PlayerController>() ?? avatar.gameObject.AddComponent<PlayerController>();
            var combat = avatar.GetComponent<PlayerCombatController>() ?? avatar.gameObject.AddComponent<PlayerCombatController>();
            pc.combatController = combat; combat.playerController = pc; pc.currentRole = (PlayerRole)avatar.Role;
            pc.targetBoss = StageManager.Instance.boss; pc.stageManager = StageManager.Instance;
            if (!avatar.IsLocal)
            {
                pc.cardManager = avatar.GetComponent<RoguelikeCardManager>() ?? avatar.gameObject.AddComponent<RoguelikeCardManager>();
                pc.cardManager.player = pc; pc.cardManager.boss = pc.targetBoss;
            }
            pc.EnsureCombatInitialized(); combat.EnsureCombatInitialized();
            pc.SetNetworkDisplayName(avatar.DisplayName + " · " + avatar.RoleName, NetworkRaidAvatar.RoleColor(avatar.Role));
            avatar.CombatTarget = pc.targetBoss != null ? pc.targetBoss.transform : null;
        }
        private void StepHostAvatar(NetworkRaidAvatar avatar, RaidMoveInput input)
        {
            var pc = avatar.GetComponent<PlayerController>(); var motor = _motors[avatar.Id];
            if (pc == null) return;
            bool forced = pc.IsBeingRescued || pc.IsBeingKnockedBack;
            bool locked = pc.IsDead || pc.IsStunned || pc.IsMovementLocked || forced || (BattlePhase != RaidBattlePhase.Combat && BattlePhase != RaidBattlePhase.Lobby);
            if (locked != avatar.MovementLocked || (forced && !avatar.ForcedLast)) avatar.MotionVersion++;
            avatar.ForcedLast = forced; avatar.MovementLocked = locked;
            if (input.MotionVersion != avatar.MotionVersion) { input.X = input.Z = 0; input.Dash = false; }
            Vector3 before = pc.transform.position;
            bool wasDashing = motor.Dash.Active;
            _config.Speed = pc.NetworkMoveSpeed; motor.Yaw = pc.transform.eulerAngles.y;
            if (forced && !pc.IsDead && BattlePhase == RaidBattlePhase.Combat)
            {
                motor.Dash.Cancel(); pc.StepNetworkForcedMovement(RaidWire.Step); motor.Position = pc.transform.position;
                motor.Dash.TickCooldown(RaidWire.Step);
            }
            else motor.Step(input, pc.transform, _config, !locked);
            avatar.Display(motor.Position, motor.Yaw, motor.Dash.Active);
            pc.SetNetworkMoving((before - motor.Position).sqrMagnitude > .000001f);
            if (!wasDashing && motor.Dash.Active) { pc.Potion?.EndRevivalProtection(); pc.GetComponent<BattleResurrection>()?.Cancel(); }
            if (pc.IsMovingXZ) pc.targetBoss?.TryInterceptPlayerMovement(pc, before, motor.Position);
        }
        private void ReconcileBots()
        {
            if (!IsHost || !CanJoinBattle) return;
            foreach (var member in _roster.Members) if (_avatars.ContainsKey(10000 + member.Role)) RemoveAvatar(10000 + member.Role);
            for (int role = 1; role <= 4; role++)
            {
                if (_roster.Members.Exists(m => m.Role == role) || _avatars.ContainsKey(10000 + role)) continue;
                var bot = _standbyBots.Find(b => b != null && !b.gameObject.activeSelf);
                if (bot == null) continue;
                bot.gameObject.SetActive(true); bot.targetBoss = StageManager.Instance.boss; bot.humanPlayer = _player;
                bot.ConfigureBotRoleDynamic((PartyBotRole)(role - 1), SpawnPoint(role));
                var avatar = bot.GetComponent<NetworkRaidAvatar>() ?? bot.gameObject.AddComponent<NetworkRaidAvatar>();
                avatar.Configure(10000 + role, false, bot.combatRadius); avatar.IsBot = true; avatar.SetIdentity(role, "AI " + CombatMetricsManager.RoleName(role));
                _avatars[avatar.Id] = avatar; _motors[avatar.Id] = new RaidNetworkMotor { Position = bot.transform.position, Yaw = bot.transform.eulerAngles.y };
            }
        }
        public void RequestAction(int skill, int target = 0)
        {
            if (!HasLocalAvatar || !ControlsReady) return;
            var request = new RaidActionRequest { actor = _localId, epoch = BattleEpoch, sequence = ++_actionSequence, lease = _localLease, skill = skill, target = target };
            if (IsHost) ExecuteAction(_localId, request);
            else Send(_server, new RaidPacket { Kind = RaidPacketKind.CombatRequest, Text = JsonUtility.ToJson(request) }, true);
        }
        private void ExecuteAction(int owner, RaidActionRequest request, bool automatic = false)
        {
            if (request == null || !_avatars.TryGetValue(owner, out var avatar) || avatar.IsBot) return;
            var ownership = _recovery.Actor(owner);
            if (!automatic && (avatar.Automated || request.lease != (ownership?.Lease ?? 0) || !_actionGate.Accept(owner, BattleEpoch, request))) return;
            if (request.skill == 7) { avatar.CombatTarget = CombatTarget(request.target); return; }
            if (request.skill == 9)
            {
                if (owner != _localId) { ActionFailure(owner, "현재 보스 시작/재도전은 호스트만 할 수 있습니다"); return; }
                if (BattlePhase == RaidBattlePhase.Lobby && !CanStartLobby) { ActionFailure(owner, "모든 참가자의 접속·역할·준비가 완료되어야 합니다"); return; }
                if (BattlePhase == RaidBattlePhase.Lobby || BattlePhase == RaidBattlePhase.Wiped) BeginNetworkEncounter(StageManager.Instance.currentStage, BattlePhase == RaidBattlePhase.Wiped);
                return;
            }
            if (request.skill == 8)
            {
                if (BattlePhase != RaidBattlePhase.Reward || !_rewards.Choose(request.epoch, owner, request.target)) { ActionFailure(owner, "유효하지 않거나 이미 선택한 보상입니다"); return; }
                avatar.GetComponent<PlayerController>().cardManager.ApplyNetworkCard((RoguelikeCardId)request.target);
                FinishRewardsIfReady(); return;
            }
            if (BattlePhase != RaidBattlePhase.Combat) { ActionFailure(owner, "현재 전투 중이 아닙니다"); return; }
            var pc = avatar.GetComponent<PlayerController>();
            if (pc == null || pc.IsDead || pc.IsStunned || pc.IsBeingRescued || pc.IsBeingKnockedBack) { ActionFailure(owner, "현재 행동할 수 없습니다"); return; }
            Transform target = CombatTarget(request.target);
            if (request.target != 0 && target == null) { ActionFailure(owner, "대상이 더 이상 존재하지 않습니다"); return; }
            avatar.CombatTarget = target;
            RaidCombatAuthority.ExecutingActor = owner;
            try
            {
                var combat = pc.combatController;
                if (request.skill <= 4)
                {
                    if (!combat.ValidateCombatSkill(request.skill, out string reason)) { ActionFailure(owner, reason); return; }
                    if (request.skill == 1) combat.TryUseSkill1_FlankCombo();
                    if (request.skill == 2) combat.TryUseSkill2_RearCombo();
                    if (request.skill == 3) combat.TryUseSkill3_PersonalDefensive_oGCD();
                    if (request.skill == 4) combat.TryUseSkill4_GaleThrust_oGCD();
                }
                else if (request.skill == 5 && !pc.GetComponent<BattleResurrection>().TryBegin(target)) ActionFailure(owner, "부활 실패: 사망한 아군·20m 사거리·쿨타임·이동 상태를 확인하세요");
                else if (request.skill == 6 && !pc.Potion.TryUse()) ActionFailure(owner, "포션을 사용할 수 없습니다: HP·쿨타임·아이템 차단 상태를 확인하세요");
            }
            finally { RaidCombatAuthority.ExecutingActor = 0; }
        }
        private void ActionFailure(int owner, string reason)
        {
            if (owner == _localId) { Status = reason; _player.combatController.ReportNetworkFailure(reason); }
            else { var peer = _peers.Find(p => p.Id == owner); if (peer != null) Send(peer.Connection, new RaidPacket { Kind = RaidPacketKind.Notice, Text = reason }, true); }
        }
        public void BeginNetworkEncounter(int stage, bool retry)
        {
            if (!IsHost || (BattlePhase != RaidBattlePhase.Lobby && BattlePhase != RaidBattlePhase.Wiped && BattlePhase != RaidBattlePhase.Reward)) return;
            if (BattlePhase == RaidBattlePhase.Lobby && !CanStartLobby) return;
            BattleEpoch++; BattlePhase = RaidBattlePhase.Loading; CountdownEnd = 0;
            BeginLoadingBarrier();
            MenuOpen = false;
            StageManager.Instance.PrepareNetworkEncounter(stage, retry);
            ResetNetworkMotors();
        }
        private void ResetNetworkMotors()
        {
            foreach (var pair in _avatars)
            {
                var motor = _motors[pair.Key]; motor.Position = pair.Value.transform.position; motor.Yaw = pair.Value.transform.eulerAngles.y; motor.Dash.ResetForRetry();
                pair.Value.MotionVersion++; pair.Value.MovementLocked = true;
            }
            foreach (var peer in _peers) peer.Inputs.DiscardPending();
            _prediction.Clear();
        }
        public void NetworkEncounterOutcome(bool cleared)
        {
            if (!IsHost || BattlePhase != RaidBattlePhase.Combat) return;
            foreach (var avatar in _avatars.Values) { avatar.MovementLocked = true; _motors[avatar.Id].Dash.Cancel(); }
            if (!cleared) { BattlePhase = RaidBattlePhase.Wiped; return; }
            var stage = StageManager.Instance;
            if (stage.currentStage >= stage.maxStage) { BattlePhase = RaidBattlePhase.Victory; return; }
            BattlePhase = RaidBattlePhase.Reward; _rewards.Begin(BattleEpoch);
            foreach (var member in _roster.Members)
                if (_avatars.TryGetValue(member.Id, out var actor)) _rewards.Offer(member.Id, actor.GetComponent<PlayerController>().cardManager.GenerateNetworkOffer());
        }
        private void FinishRewardsIfReady()
        {
            if (IsHost && BattlePhase == RaidBattlePhase.Reward && _rewards.Complete) BeginNetworkEncounter(StageManager.Instance.currentStage + 1, false);
        }
        private void OnNetworkDeparture(int id)
        {
            if (_avatars.TryGetValue(id, out var avatar))
            {
                avatar.gameObject.SetActive(false);
                foreach (var actor in _avatars.Values) if (actor.CombatTarget == avatar.transform) actor.CombatTarget = null;
                StageManager.Instance?.boss?.OnPartyMemberDied(avatar.transform);
                foreach (var cast in FindObjectsByType<BattleResurrection>(FindObjectsSortMode.None)) cast.CancelIfTarget(avatar.transform);
                foreach (var effect in FindObjectsByType<HealingSanctuaryZone>(FindObjectsSortMode.None)) effect.CancelIfSource(avatar.transform);
            }
            _rewards.Remove(id); _actionGate.Remove(id); FinishRewardsIfReady();
        }
        private void LateUpdate()
        {
            if (!Multiplayer) return;
            UpdateRecoveryHost();
            if (Mode == RaidSessionMode.Disconnected) return;
            UpdateLoadingBarrier();
            Time.timeScale = 1; // Local menus never stop the authoritative simulation.
            if (IsHost && BattlePhase == RaidBattlePhase.Countdown && ServerTime >= CountdownEnd)
            {
                BattlePhase = RaidBattlePhase.Combat; StageManager.Instance.StartStage(StageManager.Instance.currentStage);
                foreach (var avatar in _avatars.Values) avatar.MovementLocked = false;
            }
            if (HasLocalAvatar && ControlsReady)
            {
                int selected = CombatId(TargetingManager.Instance?.CurrentTarget);
                if (selected != _lastSentTarget) { _lastSentTarget = selected; RequestAction(7, selected); }
            }
            if (!IsHost && Time.unscaledTime >= _nextPing)
            { _nextPing = Time.unscaledTime + 1; Send(_server, new RaidPacket { Kind = RaidPacketKind.Ping, Speed = Time.unscaledTime }, false); }
            if (IsHost && Time.unscaledTime >= _nextCombatFrame)
            {
                _nextCombatFrame = Time.unscaledTime + .1f;
                var frame = CaptureCombatFrame(); _latestCombatFrame = frame;
                foreach (var peer in _peers) if (peer.Id != 0 && peer.CloseAt == 0) SendCombatFrame(peer, frame, false);
                PresentBattleState(frame);
            }
        }
        private RaidCombatFrame CaptureCombatFrame()
        {
            var stage = StageManager.Instance;
            var actors = new List<RaidCombatActor>();
            foreach (var avatar in _avatars.Values)
            {
                var actor = new RaidCombatActor { id = avatar.Id, role = avatar.Role, name = avatar.DisplayName, bot = avatar.IsBot, target = CombatId(avatar.CombatTarget),
                    rewardChosen = _rewards.Chosen(avatar.Id), offer = _rewards.Cards(avatar.Id) };
                var pc = avatar.GetComponent<PlayerController>();
                if (pc != null) pc.CaptureNetworkCombat(actor); else avatar.GetComponent<PartyBotAI>()?.CaptureNetworkCombat(actor);
                avatar.GetComponent<CommonPotion>()?.CaptureNetworkCombat(actor); avatar.GetComponent<BattleResurrection>()?.CaptureNetworkCombat(actor);
                if (avatar.IsBot) avatar.CombatTarget = CombatTarget(actor.target);
                CaptureRecovery(actor); avatar.State = actor; actors.Add(actor);
            }
            var adds = new List<RaidCombatEnemy>();
            if (stage.boss.addSpawner != null) foreach (var add in stage.boss.addSpawner.activeAdds)
                if (add != null && !add.isDead) adds.Add(add.CaptureNetworkCombat(CombatId(add.transform)));
            var ledger = CombatMetricsManager.Instance.Ledger;
            return new RaidCombatFrame { epoch = BattleEpoch, stage = stage.currentStage, retries = ledger.RetryCount, phase = BattlePhase, time = ServerTime,
                countdownEnd = CountdownEnd, runTime = stage.NetworkRunTime, party = actors.ToArray(), boss = stage.boss.CaptureNetworkCombat(), adds = adds.ToArray(), mechanics = CaptureMechanics(),
                current = CombatMetricsManager.CaptureNetworkResult(ledger.Snapshot()), clears = CombatMetricsManager.CaptureNetworkResults(ledger.Stages), failures = CombatMetricsManager.CaptureNetworkResults(ledger.FailedAttempts) };
        }
        private void SendCombatFrame(Peer peer, RaidCombatFrame frame, bool immediate = true)
        {
            // Full reliable recovery baselines are capped at 2 Hz; normal combat stays at 10 Hz.
            if (peer.RecoveryFrame && !immediate && Time.unscaledTime < peer.NextRecoveryFrame) return;
            if (peer.RecoveryFrame) peer.NextRecoveryFrame = Time.unscaledTime + .5f;
            // Offers are private; the global completion flags remain visible to everyone.
            foreach (var actor in frame.party) actor.offer = actor.id == peer.Id ? _rewards.Cards(actor.id) : Array.Empty<int>();
            uint id = ++_combatFrameId;
            frame.serial = id; frame.ownerLease = peer.Lease; frame.effectWatermark = _effectId;
            frame.controlGranted = PeerControls(peer); frame.recoveryPoses = peer.RecoveryFrame ? RecoveryPoses() : null; frame.poseTick = _tick + 1;
            if (peer.RecoveryFrame) peer.Barrier.SentFrame(id, peer.Lease, frame.epoch, (int)frame.phase);
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(frame));
            int count = (bytes.Length + RaidFrameAssembler.ChunkBytes - 1) / RaidFrameAssembler.ChunkBytes;
            if (count > RaidFrameAssembler.MaxChunks) { ActionFailure(peer.Id, "전투 상태가 전송 한도를 초과했습니다"); return; }
            for (int i = 0; i < count; i++)
            {
                var chunk = new byte[Math.Min(RaidFrameAssembler.ChunkBytes, bytes.Length - i * RaidFrameAssembler.ChunkBytes)];
                Array.Copy(bytes, i * RaidFrameAssembler.ChunkBytes, chunk, 0, chunk.Length);
                Send(peer.Connection, new RaidPacket { Kind = RaidPacketKind.CombatFrame, Tick = id, Id = i, Role = count, Bytes = chunk }, peer.RecoveryFrame);
            }
        }
        private void ReceiveCombatPacket(RaidPacket packet)
        {
            if (packet.Kind == RaidPacketKind.Pong)
            {
                float rtt = Time.unscaledTime - packet.Speed;
                if (rtt >= 0 && rtt < 2 && rtt <= _bestRtt + .03f) { _bestRtt = Mathf.Min(_bestRtt, rtt); _serverOffset = packet.Radius + rtt * .5f - Time.unscaledTime; _clockReady = true; }
            }
            else if (packet.Kind == RaidPacketKind.CombatFrame)
            {
                byte[] bytes = _combatAssembler.Add(packet.Tick, packet.Id, packet.Role, packet.Bytes);
                if (bytes == null) return;
                var frame = JsonUtility.FromJson<RaidCombatFrame>(Encoding.UTF8.GetString(bytes));
                if (frame == null || frame.ownerLease != _localLease || frame.epoch < BattleEpoch || frame.party == null || frame.party.Length > 4) return;
                if (!_clockReady) { _serverOffset = frame.time - Time.unscaledTime; _clockReady = true; }
                ApplyCombatFrame(frame);
            }
            else if (packet.Kind == RaidPacketKind.CombatEffect)
            {
                var effect = JsonUtility.FromJson<RaidCombatEffect>(Encoding.UTF8.GetString(packet.Bytes));
                if (ControlsReady && effect != null && effect.id > _lastEffect && effect.epoch == BattleEpoch) { _lastEffect = effect.id; PresentNetworkEffect(effect); }
            }
        }
        private void ApplyCombatFrame(RaidCombatFrame frame)
        {
            if (Mode == RaidSessionMode.Restoring && frame.recoveryPoses != null && frame.poseTick >= _lastSnapshot)
            {
                var snapshot = new RaidPacket { Kind = RaidPacketKind.Snapshot, Tick = frame.poseTick }; snapshot.Poses.AddRange(frame.recoveryPoses);
                ApplySnapshot(snapshot); _lastSnapshot = frame.poseTick;
            }
            if (frame.epoch != BattleEpoch) { _prediction.Clear(); ClearReplicaMechanics(); _loadFrame = Time.frameCount; }
            if (frame.phase != BattlePhase && Mode != RaidSessionMode.Restoring) MenuOpen = false;
            BattleEpoch = frame.epoch; BattlePhase = frame.phase; CountdownEnd = frame.countdownEnd; _latestCombatFrame = frame;
            var stage = StageManager.Instance; stage.ApplyNetworkStage(frame);
            stage.boss.ApplyNetworkCombat(frame.boss, frame.time);
            ApplyNetworkAdds(frame.adds);
            foreach (var actor in frame.party)
            {
                if (!_avatars.TryGetValue(actor.id, out var avatar)) continue;
                avatar.Automated = actor.controlState != 0; avatar.State = actor; avatar.CombatTarget = CombatTarget(actor.target);
                avatar.GetComponent<PlayerController>()?.ApplyNetworkCombat(actor);
                avatar.GetComponent<PlayerController>()?.cardManager?.ApplyNetworkCardStacks(actor.cards);
            }
            CombatMetricsManager.Instance.ApplyNetworkMetrics(frame);
            ApplyReplicaMechanics(frame.mechanics);
            ReceiveRecoveryFrame(frame);
            if (ControlsReady) PresentBattleState(frame);
        }
        private void PresentBattleState(RaidCombatFrame frame)
        {
            var cards = _player.cardManager;
            RaidCombatActor local = Array.Find(frame.party, a => a.id == _localId);
            if (local != null && frame.phase == RaidBattlePhase.Reward && !local.rewardChosen)
                cards.ShowNetworkOffer(IsHost ? _rewards.Cards(_localId) : local.offer, frame.epoch);
            else cards.CloseNetworkOffer();
            StageManager.Instance.PresentNetworkPhase(frame.phase, Mathf.CeilToInt(frame.countdownEnd - ServerTime));
        }
    }
}
