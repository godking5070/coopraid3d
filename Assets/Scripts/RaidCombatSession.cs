using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class RaidNetworkSession
    {
        public bool IsHostAuthority => IsHost;
        public DashSettings CommonDashSettings=>_config?.Dash;
        public void CommitSkillRelocation(Transform actor)
        {
            if(!IsHost||!_avatars.TryGetValue(CombatId(actor),out var avatar))return;
            var motor=_motors[avatar.Id];motor.Position=actor.position;motor.Yaw=actor.eulerAngles.y;motor.Dash.Cancel();
            avatar.MotionVersion++;foreach(var peer in _peers)if(peer.Id==avatar.Id)peer.Inputs.DiscardPending();
            if(avatar.IsLocal)_prediction.Clear();
        }
        public RaidBattlePhase BattlePhase { get; private set; } = RaidBattlePhase.Lobby;
        public int BattleEpoch { get; private set; }
        public float CountdownEnd { get; private set; }
        public float PhaseDeadline { get; private set; }
        public float ArrivalRevealAt { get; private set; }
        private readonly HashSet<int> _arrivalSkipVotes = new HashSet<int>();
        private bool _arrivalSkipCommitted;
        private void SetBattlePhase(RaidBattlePhase phase, string reason)
        {
            if (BattlePhase == phase) return;
            Debug.Log($"[Raid {SessionTag}] epoch={BattleEpoch} stage={StageManager.Instance?.currentStage} {BattlePhase}->{phase}: {reason}");
            BattlePhase = phase;
            StageManager.Instance?.UpdateStageHeaderUI();
            RaidSteamRuntime.Instance?.PublishPhase();
        }
        public float ServerTime => IsHost ? Time.unscaledTime : Time.unscaledTime + _serverOffset;
        public bool CombatRunning => !Ending && Multiplayer && BattlePhase == RaidBattlePhase.Combat;
        public bool CanJoinBattle => !Ending && BattlePhase == RaidBattlePhase.Lobby;
        public bool CanChooseRole => !Ending && BattlePhase == RaidBattlePhase.Lobby;
        public bool CanEditLobbyBuild => CanChooseRole && !CampaignBuildLocked && StageManager.Instance?.Progress.Stage == 0;
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
            bool forced = pc.IsBeingRescued || pc.IsBeingKnockedBack || pc.IsSkillRushing;
            bool portalWalking = RaidApproachField.Instance?.PortalActive == true && (BattlePhase == RaidBattlePhase.Reward || BattlePhase == RaidBattlePhase.Victory);
            bool locked = pc.IsDead || pc.IsStunned || pc.IsMovementLocked || pc.GetComponent<CharacterKit>()?.DashBlocked==true || forced || (BattlePhase != RaidBattlePhase.Combat && BattlePhase != RaidBattlePhase.Lobby && BattlePhase != RaidBattlePhase.Approach && !portalWalking);
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
            else {if(pc.GetComponent<CharacterKit>()?.MovementBlocked==true){input.X=input.Z=0;}if(BattlePhase==RaidBattlePhase.Lobby||BattlePhase==RaidBattlePhase.Approach||portalWalking)input.Dash=false;motor.Step(input, pc.transform, _config, !locked);}
            avatar.Display(motor.Position, motor.Yaw, motor.Dash.Active);
            pc.SetNetworkMoving((before - motor.Position).sqrMagnitude > .000001f);
            if (!wasDashing && motor.Dash.Active) { pc.Potion?.EndRevivalProtection(); var character = CharacterActor.Ensure(pc.gameObject); if (!character.MovementSettings(pc.dashSettings).preserveCast) { pc.GetComponent<BattleResurrection>()?.Cancel(); character.InterruptCast(); } }
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
            if (Ending || !HasLocalAvatar || !ControlsReady) return;
            var request = new RaidActionRequest { actor = _localId, epoch = BattleEpoch, sequence = ++_actionSequence, lease = _localLease, skill = skill, target = target };
            if (skill == 5) StampRescueRequest(request, target == 0 ? _player.transform : CombatTarget(target));
            if (IsHost) ExecuteAction(_localId, request);
            else Send(_server, new RaidPacket { Kind = RaidPacketKind.CombatRequest, Text = JsonUtility.ToJson(request) }, true);
        }
        public void RequestCharacterAction(int kind,string job,int slot,int target=0,string rule="",string choice="")
        {
            if(Ending||!HasLocalAvatar||!ControlsReady)return;
            var request=new RaidActionRequest{actor=_localId,epoch=BattleEpoch,sequence=++_actionSequence,lease=_localLease,skill=kind,target=target,jobId=job,slot=slot,ruleId=rule,choiceId=choice,
                rewardKey=kind==11&&rule!="route"?CharacterActor.Ensure(_player.gameObject).ChoiceKey(rule):""};
            if(IsHost)ExecuteAction(_localId,request);
            else Send(_server,new RaidPacket{Kind=RaidPacketKind.CombatRequest,Text=JsonUtility.ToJson(request)},true);
        }
        private void ExecuteAction(int owner, RaidActionRequest request, bool automatic = false)
        {
            if (Ending || request == null || !_avatars.TryGetValue(owner, out var avatar) || avatar.IsBot) return;
            var ownership = _recovery.Actor(owner);
            if (!automatic && (avatar.Automated || request.lease != (ownership?.Lease ?? 0) || !_actionGate.Accept(owner, BattleEpoch, request))) return;
            var character=CharacterActor.Ensure(avatar.gameObject);
            if(request.skill==12)
            {
                var definition=CharacterCatalog.Find(request.jobId);
                if(!CanEditLobbyBuild||definition==null||!definition.playable||definition.legacyRole!=0){ActionFailure(owner,"최초 대기실에서 직업을 선택하세요. 이어하기의 빌드는 고정입니다.");return;}
                int selectedRole=CharacterRosterData.Role(definition);
                if(!_roster.SelectRole(owner,selectedRole)){ActionFailure(owner,"이미 다른 플레이어가 선택한 역할입니다");return;}
                var member=_roster.Members.Find(m=>m.Id==owner);avatar.SetIdentity(selectedRole,member.Name);
                character.UseLegacyRole();
                if(!character.AssignDefinition(definition)){ActionFailure(owner,"직업 실행기 연결 실패");return;}
                member.Ready=false;SyncLobbyAvatar(member);avatar.RefreshJobLabel();ReconcileBots();return;
            }
            if(request.skill>=10 && request.skill<=13 && (request.jobId!=character.Definition.id || request.skill==10&&!Enum.IsDefined(typeof(CharacterSlot),request.slot))) { ActionFailure(owner,"직업 또는 슬롯이 유효하지 않습니다");return; }
            if(request.skill==13){avatar.GetComponent<CharacterKit>()?.CancelChannel();return;}
            if (request.skill == 14)
            {
                if ((BattlePhase != RaidBattlePhase.Arrival && BattlePhase != RaidBattlePhase.BossClear) || _arrivalSkipCommitted) return;
                _arrivalSkipVotes.Add(owner);
                int participants = 0;
                bool everyone = true;
                foreach (var participant in _avatars.Values)
                    if (!participant.IsBot && !participant.Automated && participant.gameObject.activeInHierarchy)
                    { participants++; if (!_arrivalSkipVotes.Contains(participant.Id)) everyone = false; }
                if (everyone && participants > 0)
                {
                    _arrivalSkipCommitted = true;
                    if (BattlePhase == RaidBattlePhase.Arrival)
                    {
                        StageManager.Instance.RevealPreparedBoss();
                        StageManager.Instance.EndStagePresentation();
                        ArrivalRevealAt = ServerTime;
                        PhaseDeadline = ServerTime + Mathf.Max(0, RaidApproachField.Instance.Arrival(StageManager.Instance.currentStage).afterCutsceneSeconds);
                    }
                    else { StageManager.Instance.EndStagePresentation(); PhaseDeadline = ServerTime; }
                }
                return;
            }
            if (request.skill == 15)
            {
                var field = RaidApproachField.Instance;
                if (avatar.IsBot || avatar.Automated || field?.PortalActive != true ||
                    !field.NearPortal(avatar.transform) && avatar.GetComponent<PlayerController>()?.IsDead != true ||
                    BattlePhase != RaidBattlePhase.Reward && BattlePhase != RaidBattlePhase.Victory)
                { ActionFailure(owner, "호스트가 활성화된 중앙 포탈에 가까이 있어야 합니다"); return; }
                if (BattlePhase == RaidBattlePhase.Reward && !_roster.Members.TrueForAll(m => RewardComplete(m.Id)))
                { ActionFailure(owner, "모든 보상과 스킬 선택을 완료하세요"); return; }
                if (CampaignRuntime.Instance?.HasSaveFailure == true)
                { ActionFailure(owner, "저장 실패를 복구하는 중입니다. 잠시 후 다시 시도하세요."); return; }
                ReturnVictoryPartyToLobby();
                return;
            }
            if(request.skill==11)
            {
                if (request.ruleId == "route" && !CanEditLobbyBuild) { ActionFailure(owner, "출발 후 확정한 기원 루트는 변경할 수 없습니다."); return; }
                if(request.ruleId!="route"&&request.rewardKey!=character.ChoiceKey(request.ruleId)){ActionFailure(owner,"이전 런/스테이지의 스킬 보상 요청입니다");return;}
                if(!character.Choose(request.ruleId,request.choiceId,out string reason,request.ruleId=="route"?null:request.rewardKey))ActionFailure(owner,reason);
                else if(request.ruleId=="route"){var member=_roster.Members.Find(m=>m.Id==owner);if(member!=null){member.Ready=false;SyncLobbyAvatar(member);}}
                FinishRewardsIfReady();
                return;
            }
            if (request.skill == 7) { avatar.CombatTarget = CombatTarget(request.target); return; }
            if (request.skill == 9)
            {
                if (BattlePhase == RaidBattlePhase.Victory)
                {
                    if (owner == _localId) StageManager.Instance?.CloseVictoryForPortal();
                    else ActionFailure(owner, "호스트만 파티를 대기실로 복귀시킬 수 있습니다.");
                    return;
                }
                if (owner != _localId) { ActionFailure(owner, "현재 보스 시작/재도전은 호스트만 할 수 있습니다"); return; }
                if (BattlePhase == RaidBattlePhase.Lobby && !CanStartLobby) { ActionFailure(owner, "모든 참가자의 접속·역할·준비가 완료되어야 합니다"); return; }
                if (BattlePhase == RaidBattlePhase.Lobby && !RaidLobbyRoom.HostAtDoor) { ActionFailure(owner, "호스트가 대기실 북쪽 문 앞에 있어야 합니다."); return; }
                if (BattlePhase == RaidBattlePhase.Lobby || BattlePhase == RaidBattlePhase.Wiped) BeginNetworkEncounter(StageManager.Instance.currentStage, BattlePhase == RaidBattlePhase.Wiped);
                return;
            }
            if (request.skill == 8)
            {
                if (BattlePhase != RaidBattlePhase.Reward || !_rewards.Choose(request.epoch, owner, request.target)) { ActionFailure(owner, "유효하지 않거나 이미 선택한 보상입니다"); return; }
                CampaignRuntime.Instance?.CardChosen(avatar.GetComponent<PlayerController>(), request.target, _rewards.Cards(owner));
                avatar.GetComponent<PlayerController>().cardManager.ApplyNetworkCard((RoguelikeCardId)request.target);
                CampaignRuntime.Instance?.SyncSettlement();
                FinishRewardsIfReady(); return;
            }
            if (BattlePhase != RaidBattlePhase.Combat) { ActionFailure(owner, "현재 전투 중이 아닙니다"); return; }
            var pc = avatar.GetComponent<PlayerController>();
            if (request.skill == 5)
            {
                var rescueTarget = request.target == 0 && pc != null && pc.IsDead ? pc.transform : CombatTarget(request.target);
                IrisRescue.TryUse(avatar.transform, rescueTarget, request.attemptId, request.deathGeneration, out string result);
                ActionNotice(owner, result); return;
            }
            if (pc == null || pc.IsDead || pc.IsStunned || pc.IsBeingRescued || pc.IsBeingKnockedBack) { ActionFailure(owner, "현재 행동할 수 없습니다"); return; }
            Transform target = CombatTarget(request.target);
            if (request.target != 0 && target == null) { ActionFailure(owner, "대상이 더 이상 존재하지 않습니다"); return; }
            avatar.CombatTarget = target;
            RaidCombatAuthority.ExecutingActor = owner;
            try
            {
                var combat = pc.combatController;
                if(request.skill==10) { if(!character.Execute((CharacterSlot)request.slot,target,out string failure))ActionFailure(owner,failure);return; }
                if (request.skill <= 4)
                {
                    if(character.Definition.legacyRole==0) { if(!character.Execute(CharacterSlots.LegacySlot(request.skill),target,out string failure))ActionFailure(owner,failure);return; }
                    if (!combat.ValidateCombatSkill(request.skill, out string reason)) { ActionFailure(owner, reason); return; }
                    if (request.skill == 1) combat.TryUseSkill1_FlankCombo();
                    if (request.skill == 2) combat.TryUseSkill2_RearCombo();
                    if (request.skill == 3) combat.TryUseSkill3_PersonalDefensive_oGCD();
                    if (request.skill == 4) combat.TryUseSkill4_GaleThrust_oGCD();
                }
                else if (request.skill == 6 && !pc.Potion.TryUse()) ActionFailure(owner, "포션을 사용할 수 없습니다: HP·쿨타임·아이템 차단 상태를 확인하세요");
            }
            finally { RaidCombatAuthority.ExecutingActor = 0; }
        }
        private void ActionFailure(int owner, string reason)
            => ActionNotice(owner, reason);
        private void ActionNotice(int owner, string reason)
        {
            if (owner == _localId) { Status = reason; _player.combatController.ReportNetworkFailure(reason); }
            else { var peer = _peers.Find(p => p.Id == owner); if (peer != null) Send(peer.Connection, new RaidPacket { Kind = RaidPacketKind.Notice, Text = reason }, true); }
        }
        private void StampRescueRequest(RaidActionRequest request, Transform target)
        {
            request.attemptId = StageManager.Instance.AttemptId;
            request.deathGeneration = target != null ? target.GetComponent<PartyLife>()?.DeathGeneration ?? 0 : 0;
        }
        public void BeginNetworkEncounter(int stage, bool retry)
        {
            if (Ending || !IsHost || (BattlePhase != RaidBattlePhase.Lobby && BattlePhase != RaidBattlePhase.Wiped && BattlePhase != RaidBattlePhase.Reward)) return;
            if (BattlePhase == RaidBattlePhase.Lobby && (!CanStartLobby || !RaidLobbyRoom.HostAtDoor)) return;
            if(BattlePhase==RaidBattlePhase.Lobby)
                foreach(var avatar in _avatars.Values)if(avatar.Automated&&!avatar.IsBot)
                {
                    var actor=CharacterActor.Ensure(avatar.gameObject);
                    // A disconnected player who had not chosen an origin must not block departure.
                    if(!actor.DepartureReady)actor.Choose("route",actor.Definition.routes[0].id,out _);
                }
            if (BattlePhase == RaidBattlePhase.Lobby && CampaignRuntime.Instance?.ResumingSettlement == true)
            {
                RestoreNetworkSettlement();
                return;
            }
            bool resume = CampaignRuntime.Instance?.Resume != null;
            if (BattlePhase == RaidBattlePhase.Lobby && !resume && StageManager.Instance.Progress.Stage == 0 && CampaignRuntime.Instance?.PrepareFreshRun() == false)
            { Status = CampaignRuntime.Instance.Notice; return; }
            if (!(resume ? StageManager.Instance.PrepareCampaignEncounter() : StageManager.Instance.PrepareNetworkEncounter(stage, retry))) return;
            BattleEpoch++; CountdownEnd = PhaseDeadline = ArrivalRevealAt = 0; _arrivalSkipVotes.Clear(); _arrivalSkipCommitted = false;
            SetBattlePhase(RaidBattlePhase.Loading, retry ? "retry" : "prepare stage");
            BeginLoadingBarrier();
            MenuOpen = false;
            ResetNetworkMotors();
            CampaignRuntime.Instance?.Prepared(retry, StageManager.Instance.LastPreparationNewRun);
            if (Ending) return;
            if (resume) { BeginCampaignReservations(); CampaignRuntime.Instance.RestoreFinished(); }
        }
        private void RestoreNetworkSettlement()
        {
            var campaign = CampaignRuntime.Instance;
            var stage = StageManager.Instance;
            if (!IsHost || campaign?.Resume == null || !stage.RestoreSettlementFromLobby()) return;
            BattleEpoch++;
            _rewards.Begin(BattleEpoch);
            if (stage.currentStage < stage.maxStage)
                foreach (var member in _roster.Members.OrderBy(m => m.Role))
                {
                    if (!_avatars.TryGetValue(member.Id, out var avatar)) continue;
                    var offer = campaign.SettlementOffer(avatar.transform);
                    if (offer == null)
                    {
                        offer = avatar.GetComponent<PlayerController>().cardManager.GenerateNetworkOffer();
                        campaign.RecordSettlementOffer(avatar.transform, offer);
                    }
                    _rewards.Offer(member.Id, offer);
                    var slot = campaign.Resume.party.FirstOrDefault(p => p.role == member.Role);
                    if (slot != null && campaign.Resume.rewards.Any(r => r.key == CharacterRewardKey.Stage(campaign.Resume.runId, campaign.Resume.chapterId, stage.currentStage) + "/card/" + slot.slotId))
                        _rewards.RestoreChosen(member.Id);
                }
            MenuOpen = false;
            ResetNetworkMotors();
            PhaseDeadline = ServerTime + stage.FlowSettings.rewardSeconds;
            campaign.RestoreFinished();
            SetBattlePhase(RaidBattlePhase.Reward, "restored pending boss clear settlement");
            FinishRewardsIfReady();
        }
        public void RequestVictoryLobbyReturn()
        {
            if (Ending) return;
            if (Mode == RaidSessionMode.Single)
            {
                if (StageManager.Instance?.Progress.Phase == RaidBattlePhase.Victory)
                    StageManager.Instance.CloseVictoryForPortal();
                return;
            }
            if (BattlePhase == RaidBattlePhase.Victory)
            {
                if (IsHost) RequestAction(9);
                else StageManager.Instance?.CloseVictoryForPortal();
                return;
            }
            else Status = "파티를 대기실로 복귀시키려면 호스트가 최종 결과에서 복귀를 선택해야 합니다.";
        }
        private void ReturnVictoryPartyToLobby()
        {
            if (!IsHost || BattlePhase != RaidBattlePhase.Victory && BattlePhase != RaidBattlePhase.Reward) return;
            bool final = BattlePhase == RaidBattlePhase.Victory;
            if (final ? StageManager.Instance?.ReturnToLobby() != true : StageManager.Instance?.ReturnToLobbyIntermission() != true) return;
            if (final) _campaignResumed = false;
            _rewards.Begin(BattleEpoch + 1);
            foreach (var member in _roster.Members)
            { member.Ready = false; member.Loaded = true; SyncLobbyAvatar(member); }
            foreach (var pair in _avatars)
            {
                var avatar = pair.Value;
                avatar.MovementLocked = false; avatar.CombatTarget = null;
                var motor = _motors[pair.Key]; motor.Position = avatar.transform.position;
                motor.Yaw = avatar.transform.eulerAngles.y; motor.Dash.ResetForRetry();
                avatar.MotionVersion++;
            }
            foreach (var peer in _peers) peer.Inputs.DiscardPending();
            _prediction.Clear(); ClearReplicaMechanics();
            BattleEpoch++; CountdownEnd = PhaseDeadline = 0;
            SetBattlePhase(RaidBattlePhase.Lobby, final ? "completed party returned to lobby" : "settled party returned through portal");
            MenuOpen = false;
        }
        private void ResetNetworkMotors()
        {
            foreach (var pair in _avatars)
            {
                var motor = _motors[pair.Key]; motor.Position = pair.Value.transform.position; motor.Yaw = pair.Value.transform.eulerAngles.y; motor.Dash.ResetForRetry();
                pair.Value.MotionVersion++; pair.Value.MovementLocked = true;
                pair.Value.GetComponent<PlayerController>()?.RefillMovementCharges();
            }
            foreach (var peer in _peers) peer.Inputs.DiscardPending();
            _prediction.Clear();
        }
        public void NetworkEncounterOutcome(bool cleared)
        {
            if (!IsHost || BattlePhase != RaidBattlePhase.Combat) return;
            foreach (var avatar in _avatars.Values) { avatar.MovementLocked = true; _motors[avatar.Id].Dash.Cancel(); }
            if (cleared)
            {
                var defeat = RaidApproachField.Instance.Defeat(StageManager.Instance.currentStage);
                _arrivalSkipVotes.Clear(); _arrivalSkipCommitted = false;
                ArrivalRevealAt = ServerTime + defeat.DepartureAt;
                PhaseDeadline = ServerTime + defeat.Duration;
            }
            else PhaseDeadline = 0;
            SetBattlePhase(cleared ? RaidBattlePhase.BossClear : RaidBattlePhase.Wiped, "authoritative outcome");
        }
        private void FinishNetworkClearPresentation()
        {
            if (BattlePhase != RaidBattlePhase.BossClear) return;
            if (ServerTime >= ArrivalRevealAt) RaidApproachField.Instance?.SetBossVisible(false);
            if (ServerTime < PhaseDeadline) return;
            var stage = StageManager.Instance;
            stage.EndBossDefeatCutscene();
            if (CampaignRuntime.Instance?.MarkClearPresentationFinished() == false) return;
            if (!stage.FinishNetworkClear()) return;
            if (stage.Progress.Phase==RaidBattlePhase.Victory)
            { CampaignRuntime.Instance?.Completed(); PhaseDeadline = 0; SetBattlePhase(RaidBattlePhase.Victory, "final clear"); return; }
            _rewards.Begin(BattleEpoch);
            if(stage.currentStage<stage.maxStage)foreach (var member in _roster.Members.OrderBy(m => m.Role))
                if (_avatars.TryGetValue(member.Id, out var actor))
                {
                    var offer = actor.GetComponent<PlayerController>().cardManager.GenerateNetworkOffer();
                    CampaignRuntime.Instance?.RecordSettlementOffer(actor.transform, offer);
                    _rewards.Offer(member.Id, offer);
                }
            PhaseDeadline = ServerTime + stage.FlowSettings.rewardSeconds;
            SetBattlePhase(RaidBattlePhase.Reward, "offers generated once");
        }
        private void UpdateRewardDeadline()
        {
            if (BattlePhase != RaidBattlePhase.Reward || ServerTime < PhaseDeadline) return;
            foreach (var member in _roster.Members)
            {
                int[] cards = _rewards.Cards(member.Id);
                if (cards.Length>0&&_rewards.Choose(BattleEpoch, member.Id, cards[0]))
                {
                    CampaignRuntime.Instance?.CardChosen(_avatars[member.Id].GetComponent<PlayerController>(), cards[0], cards);
                    _avatars[member.Id].GetComponent<PlayerController>().cardManager.ApplyNetworkCard((RoguelikeCardId)cards[0]);
                    var slot = _recovery.Actor(member.Id); if (slot != null) _recovery.AutoSelected(slot, cards[0]);
                    BroadcastPartyNotice(member.Name + " 카드 선택 시간 초과 · 첫 번째 카드 자동 선택");
                }
                AutoChooseSkillRewards(member.Id);
            }
            FinishRewardsIfReady();
        }
        private void FinishRewardsIfReady()
        {
            if (IsHost && BattlePhase == RaidBattlePhase.Reward && CampaignRuntime.Instance?.SyncSettlement() == false) return;
            if(Ending||!IsHost||BattlePhase!=RaidBattlePhase.Reward||!_roster.Members.TrueForAll(m=>RewardComplete(m.Id)))return;
            var stage=StageManager.Instance;
            if(stage.currentStage>=stage.maxStage)
            {if(stage.Progress.FinishFinalRewards(stage.Progress.Revision,stage.maxStage)){CampaignRuntime.Instance?.Completed();PhaseDeadline=0;SetBattlePhase(RaidBattlePhase.Victory,"final skill choices complete");}return;}
            if (RaidApproachField.Instance?.PortalActive == true) return;
            if (CampaignRuntime.Instance?.CommitNextStageBoundary(stage.currentStage + 1) == false) return;
            PhaseDeadline = 0;
            RaidApproachField.Instance?.SetPortalActive(true);
            Status = "보상 선택 완료 · 중앙 귀환 포탈에서 대기실로 돌아가세요.";
        }
        private bool CardComplete(int id)=>StageManager.Instance.currentStage>=StageManager.Instance.maxStage||_rewards.Chosen(id);
        private bool RewardComplete(int id)=>CardComplete(id)&&(!_avatars.TryGetValue(id,out var avatar)||CharacterActor.Ensure(avatar.gameObject).PendingChoices.Length==0);
        private void AutoChooseSkillRewards(int id)
        {
            if(!IsHost||BattlePhase!=RaidBattlePhase.Reward||!_avatars.TryGetValue(id,out var avatar))return;
            var actor=CharacterActor.Ensure(avatar.gameObject);
            foreach(var rule in actor.PendingChoices)
                if(actor.Choose(rule.id,rule.choices[0],out _,actor.ChoiceKey(rule.id),true))BroadcastPartyNotice(avatar.DisplayName+" 스킬 자동 선택: "+actor.Definition.Skill(rule.choices[0]).name);
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
            _rewards.Remove(id); _actionGate.Remove(id); // Advance only after peer/avatar removal has completed.
        }
        private void LateUpdate()
        {
            if (Ending || !Multiplayer) return;
            UpdateRecoveryHost();
            SendCampaignReceipts();
            if (IsHost) { FinishNetworkClearPresentation(); UpdateRewardDeadline(); }
            if (Mode == RaidSessionMode.Disconnected) return;
            UpdateLoadingBarrier();
            if (Ending || StageManager.Instance?.boss == null) return;
            Time.timeScale = 1; // Local menus never stop the authoritative simulation.
            if (IsHost && BattlePhase == RaidBattlePhase.Approach && RaidApproachField.Instance?.HumanEntered() == true)
            {
                if (StageManager.Instance.BeginPreparedArrival())
                {
                    var arrival = RaidApproachField.Instance.Arrival(StageManager.Instance.currentStage);
                    _arrivalSkipVotes.Clear(); _arrivalSkipCommitted = false;
                    ArrivalRevealAt = ServerTime + arrival.RevealAt;
                    PhaseDeadline = ServerTime + arrival.Duration + Mathf.Max(0, arrival.afterCutsceneSeconds);
                    SetBattlePhase(RaidBattlePhase.Arrival, "human entered; party summoned");
                }
            }
            if (BattlePhase == RaidBattlePhase.Arrival && HasLocalAvatar && ControlsReady && !LocalGameplayInput.ModalCovered && LocalInputBindings.Pressed("Interact")) RequestAction(14);
            if (BattlePhase == RaidBattlePhase.BossClear && HasLocalAvatar && ControlsReady && !LocalGameplayInput.ModalCovered && LocalInputBindings.Pressed("Interact")) RequestAction(14);
            if ((BattlePhase == RaidBattlePhase.Reward || BattlePhase == RaidBattlePhase.Victory) &&
                (RaidApproachField.Instance?.NearPortal(_player.transform) == true ||
                    _player.IsDead && RaidApproachField.Instance?.PortalActive == true) &&
                !LocalGameplayInput.ModalCovered && LocalInputBindings.Pressed("Interact")) RequestAction(15);
            if (IsHost && BattlePhase == RaidBattlePhase.Arrival)
            {
                if (ServerTime >= ArrivalRevealAt) StageManager.Instance.RevealPreparedBoss();
                if (ServerTime >= PhaseDeadline && StageManager.Instance.BeginPreparedCountdown())
                {
                    CountdownEnd = ServerTime + StageManager.Instance.FlowSettings.countdownSeconds;
                    PhaseDeadline = CountdownEnd;
                    SetBattlePhase(RaidBattlePhase.Countdown, "arrival complete");
                }
            }
            if (IsHost && BattlePhase == RaidBattlePhase.Countdown && ServerTime >= CountdownEnd)
            {
                SetBattlePhase(RaidBattlePhase.Combat, "all loaded; countdown complete"); StageManager.Instance.StartStage(StageManager.Instance.currentStage);
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
                var frame = CaptureCombatFrame(); _latestCombatFrame = frame; StageManager.Instance.RestoreIris(frame.irisCoins, frame.attemptId, frame.totalRevives);
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
                    deathGeneration = avatar.GetComponent<PartyLife>()?.DeathGeneration ?? 0, rewardChosen = RewardComplete(avatar.Id), cardChosen=CardComplete(avatar.Id), offer = _rewards.Cards(avatar.Id) };
                var pc = avatar.GetComponent<PlayerController>();
                if (pc != null) pc.CaptureNetworkCombat(actor); else avatar.GetComponent<PartyBotAI>()?.CaptureNetworkCombat(actor);
                avatar.GetComponent<CommonPotion>()?.CaptureNetworkCombat(actor); avatar.GetComponent<BattleResurrection>()?.CaptureNetworkCombat(actor);
                if (avatar.IsBot) avatar.CombatTarget = CombatTarget(actor.target);
                var character = CharacterActor.Ensure(avatar.gameObject); actor.character = character.Snapshot;
                actor.jobState=avatar.GetComponent<AegisPaladin>()?.IsActive==true?JsonUtility.ToJson(avatar.GetComponent<AegisPaladin>().DamageState.Capture()):"";
                if(avatar.GetComponent<CharacterKit>()?.Active==true)actor.jobState=avatar.GetComponent<CharacterKit>().Capture();
                actor.debuffs=avatar.GetComponent<CombatStatusEffects>()?.Capture();
                if(character.Definition.legacyRole==0)
                {
                    actor.casting=actor.character.castSkillId.Length>0; actor.castName=actor.casting?character.Definition.Skill(actor.character.castSkillId).name:"";
                    actor.castRemaining=actor.character.castRemaining;actor.castTotal=actor.character.castTotal;
                }
                CaptureRecovery(actor); avatar.State = actor; actors.Add(actor);
            }
            var adds = new List<RaidCombatEnemy>();
            if (stage.boss.addSpawner != null) foreach (var add in stage.boss.addSpawner.activeAdds)
                if (add != null && !add.isDead) adds.Add(add.CaptureNetworkCombat(CombatId(add.transform)));
            var ledger = CombatMetricsManager.Instance.Ledger;
            var checkpoint = CampaignRuntime.Instance?.Saved;
            bool currentSave = checkpoint != null && checkpoint.runId == stage.RunId;
            return new RaidCombatFrame { campaignResume = _campaignResumed, campaignSavedStage = currentSave ? checkpoint.nextStage : 0,
                campaignEligible = UsesSteam && CampaignRuntime.Instance?.RunStarted == true && CampaignRuntime.Instance.Eligible,
                campaignSavedUtc = currentSave ? checkpoint.savedUtc : "", campaignCompleted = currentSave && checkpoint.completed,
                campaignSaveFailed = CampaignRuntime.Instance?.HasSaveFailure == true, epoch = BattleEpoch, stage = stage.currentStage, irisCoins=stage.Iris.Coins,attemptId=stage.AttemptId,totalRevives=stage.TotalRevives,runId=stage.RunId,chapterId=stage.ChapterId, retries = ledger.RetryCount, phase = BattlePhase, time = ServerTime,
                countdownEnd = CountdownEnd, phaseDeadline = PhaseDeadline, arrivalRevealAt = ArrivalRevealAt,
                bossVisible = RaidApproachField.Instance?.BossVisible == true, portalActive = RaidApproachField.Instance?.PortalActive == true,
                runTime = stage.NetworkRunTime, party = actors.ToArray(), boss = stage.boss.CaptureNetworkCombat(), adds = adds.ToArray(), mechanics = CaptureMechanics(),
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
            _campaignResumed = frame.campaignResume;
            if (Mode == RaidSessionMode.Restoring && frame.recoveryPoses != null && frame.poseTick >= _lastSnapshot)
            {
                var snapshot = new RaidPacket { Kind = RaidPacketKind.Snapshot, Tick = frame.poseTick }; snapshot.Poses.AddRange(frame.recoveryPoses);
                ApplySnapshot(snapshot); _lastSnapshot = frame.poseTick;
            }
            if (frame.epoch != BattleEpoch)
            {
                _prediction.Clear(); ClearReplicaMechanics(); _loadFrame = Time.frameCount;
                StageManager.Instance.boss.SetupBossForStage(frame.stage); // Also reset collapse/casting on a same-stage retry.
                RaidApproachField.Instance?.PrepareReplicaStage();
            }
            if (frame.phase != BattlePhase && Mode != RaidSessionMode.Restoring) MenuOpen = false;
            BattleEpoch = frame.epoch; SetBattlePhase(frame.phase, "host snapshot"); CountdownEnd = frame.countdownEnd; PhaseDeadline = frame.phaseDeadline; ArrivalRevealAt = frame.arrivalRevealAt; _latestCombatFrame = frame; StageManager.Instance.RestoreIris(frame.irisCoins, frame.attemptId, frame.totalRevives);
            var stage = StageManager.Instance; stage.ApplyNetworkStage(frame);
            stage.boss.ApplyNetworkCombat(frame.boss, frame.time);
            ApplyNetworkAdds(frame.adds);
            foreach (var actor in frame.party)
            {
                if (!_avatars.TryGetValue(actor.id, out var avatar)) continue;
                avatar.Automated = actor.controlState != 0; avatar.State = actor; avatar.CombatTarget = CombatTarget(actor.target);
                avatar.GetComponent<PlayerController>()?.ApplyNetworkCombat(actor);
                if(!CharacterActor.Ensure(avatar.gameObject).Restore(actor.character)) { UnexpectedClientLoss("직업 상태 복구 실패: 같은 빌드인지 확인하세요"); return; }
                if(CharacterRosterData.Find(actor.character.jobId)!=null&&!CharacterKit.Ensure(avatar.gameObject).Restore(actor.jobState)){UnexpectedClientLoss("직업 효과 복구 실패");return;}
                if(actor.character.jobId==AegisPaladinData.JobId && (string.IsNullOrEmpty(actor.jobState)||!AegisPaladin.Ensure(avatar.gameObject).DamageState.Restore(JsonUtility.FromJson<AegisStateSnapshot>(actor.jobState))))
                {UnexpectedClientLoss("팔라딘 상태 복구 실패: 같은 빌드인지 확인하세요");return;}
                CombatStatusEffects.Ensure(avatar.gameObject).Restore(actor.debuffs);
                avatar.RefreshJobLabel();
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
            if (local != null && frame.phase == RaidBattlePhase.Reward && !local.cardChosen)
                cards.ShowNetworkOffer(IsHost ? _rewards.Cards(_localId) : local.offer, frame.epoch);
            else cards.CloseNetworkOffer();
            StageManager.Instance.PresentNetworkPhase(frame.phase, Mathf.CeilToInt(frame.countdownEnd - ServerTime));
        }
    }
}
