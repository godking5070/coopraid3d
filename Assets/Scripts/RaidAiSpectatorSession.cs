using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    // Marks an AI-controlled combatant, not the observing user. It is intentionally
    // separate from PartyBotAI because replacement AI already uses real job skills.
    public sealed class AiSpectatorActor : MonoBehaviour { }

    public sealed partial class RaidNetworkSession
    {
        public bool AiSpectatorActive => Mode == RaidSessionMode.AiSpectator;
        public string AiSpectatorError { get; private set; }
        public int AiSpectatorStartingStage { get; private set; }
        public bool AiSpectatorContinuous { get; private set; }
        public bool AiSpectatorAutoRetry { get; set; }
        public int AiSpectatorMaxRetries { get; set; } = 2;
        public bool AiSpectatorStopOnClear { get; set; }
        public int AiSpectatorRetries { get; private set; }
        public int AiSpectatorDamagePercent { get; private set; } = AiSpectatorDamageRules.DefaultPercent;
        public int AiSpectatorAttemptStartPercent { get; private set; } = AiSpectatorDamageRules.DefaultPercent;
        public int AiSpectatorAttemptEndPercent { get; private set; } = AiSpectatorDamageRules.DefaultPercent;
        public bool AiSpectatorDamageChangedDuringAttempt { get; private set; }
        private readonly List<string> _aiSpectatorDamageHistory = new List<string>();
        private bool _aiSpectatorSessionStopped;
        private bool _aiSpectatorWipeRequested;
        public IReadOnlyList<string> AiSpectatorDamageHistory => _aiSpectatorDamageHistory;
        public Transform AiSpectatorLeader => _avatars.TryGetValue(_localId,out var avatar) ? avatar.transform : null;

        public bool CanTestAiSpectatorWipe
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var stage = StageManager.Instance;
                return Debug.isDebugBuild && AiSpectatorActive && IsHost && !Ending && !_aiSpectatorWipeRequested &&
                    BattlePhase == RaidBattlePhase.Combat && !StageManager.CombatSuspended &&
                    CampaignRuntime.Instance?.IsolatedTestProfile == true && _avatars.Count == 4 &&
                    stage != null && stage.boss != null && stage.boss.currentHp > 0;
#else
                return false;
#endif
            }
        }

        public bool RequestAiSpectatorWipeTest()
        {
            if (!CanTestAiSpectatorWipe) return false;
            // Resolve with the usual wipe check in LateUpdate, after all combat Updates.
            _aiSpectatorWipeRequested = true;
            return true;
        }

        public bool SetAiSpectatorDamagePercent(int percent)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!AiSpectatorActive || !Debug.isDebugBuild || percent < 0 || percent > AiSpectatorDamageRules.MaximumPercent) return false;
            if (percent == AiSpectatorDamagePercent) return true;
            int previous = AiSpectatorDamagePercent;
            AiSpectatorDamagePercent = percent;
            if (BattlePhase == RaidBattlePhase.Combat) AiSpectatorDamageChangedDuringAttempt = true;
            string entry = "t=" + Time.unscaledTime.ToString("F1") + "s stage=" + StageManager.Instance?.currentStage +
                " phase=" + BattlePhase + " AI damage " + previous + "% -> " + percent + "%";
            _aiSpectatorDamageHistory.Add(entry);
            Debug.Log("[AI Spectator] " + entry);
            return true;
#else
            return false;
#endif
        }
        internal void AiSpectatorRecordAttemptStart()
        {
            if (!AiSpectatorActive) return;
            _aiSpectatorWipeRequested = false;
            AiSpectatorAttemptStartPercent = AiSpectatorDamagePercent;
            AiSpectatorDamageChangedDuringAttempt = false;
            string entry = "attempt start t=" + Time.unscaledTime.ToString("F1") + "s stage=" + StageManager.Instance.currentStage +
                " damage=" + AiSpectatorDamagePercent + "%";
            _aiSpectatorDamageHistory.Add(entry);
            Debug.Log("[AI Spectator] " + entry);
        }
        internal void AiSpectatorRecordAttemptEnd(bool cleared)
        {
            if (!AiSpectatorActive) return;
            AiSpectatorAttemptEndPercent = AiSpectatorDamagePercent;
            string entry = "attempt " + (cleared ? "clear" : "wipe") + " t=" + Time.unscaledTime.ToString("F1") +
                "s stage=" + StageManager.Instance.currentStage + " start=" + AiSpectatorAttemptStartPercent +
                "% end=" + AiSpectatorAttemptEndPercent + "% changed=" + AiSpectatorDamageChangedDuringAttempt;
            _aiSpectatorDamageHistory.Add(entry);
            Debug.Log("[AI Spectator] " + entry);
        }
        internal void AiSpectatorRecordSessionStop()
        {
            if (!AiSpectatorActive || _aiSpectatorSessionStopped) return;
            _aiSpectatorSessionStopped = true;
            AiSpectatorAttemptEndPercent = AiSpectatorDamagePercent;
            string entry = "session end t=" + Time.unscaledTime.ToString("F1") + "s damage=" +
                AiSpectatorDamagePercent + "%";
            _aiSpectatorDamageHistory.Add(entry);
            Debug.Log("[AI Spectator] " + entry);
        }

        public bool BeginAiSpectator(int stage,string difficulty,string[] jobs,bool continuous)
        {
            AiSpectatorError = "";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Debug.isDebugBuild || Ending || Mode != RaidSessionMode.Menu || StageManager.Instance == null ||
                _player == null || RaidSteamRuntime.Instance?.Busy == true)
                return AiSpectatorReject("개발용 시작 화면에서만 AI 관전 테스트를 시작할 수 있습니다.");
            if (stage < 1 || stage > 6 || (difficulty != CampaignCatalog.Normal && difficulty != CampaignCatalog.Hard) ||
                jobs == null || jobs.Length != 4)
                return AiSpectatorReject("스테이지·난이도·4인 직업 구성을 확인하세요.");
            for (int role=1; role<=4; role++)
            {
                var definition=CharacterCatalog.Find(jobs[role-1]);
                if (definition==null || !definition.playable || CharacterRosterData.Role(definition)!=role)
                    return AiSpectatorReject("역할마다 구현된 직업을 하나씩 선택하세요: 1탱·1힐·근딜·원딜.");
            }
            var campaign=CampaignRuntime.Instance;
            if (campaign == null || !campaign.IsolatedTestProfile)
                return AiSpectatorReject("전용 테스트 저장 경로가 확인되지 않았습니다. --raid-test-profile 또는 에디터 테스트 프로필을 먼저 설정하세요.");
            if (campaign.HasSaveFailure || campaign.Settlement != null ||
                campaign.Saved != null && !campaign.Saved.completed || !campaign.PrepareFreshRun())
                return AiSpectatorReject("테스트 프로필에 완료되지 않은 런이 있습니다. 별도 테스트 프로필을 사용하세요.");
            campaign.SelectDifficulty(difficulty);
            DisableBots();
            _roster.Members.Clear(); _avatars.Clear(); _motors.Clear();
            Mode=RaidSessionMode.AiSpectator;_controlReady=true;MenuOpen=false;
            _config=new RaidPacket{Kind=RaidPacketKind.Welcome,Speed=_player.moveSpeed,Radius=_player.combatRadius,
                Arena=_player.arenaBoundaryRadius,TurnSpeed=_player.rotationSmoothSpeed,
                Dash=new DashSettings{distance=_player.dashSettings.distance,duration=_player.dashSettings.duration,
                    cooldown=_player.dashSettings.cooldown,maximumCharges=_player.dashSettings.maximumCharges,
                    preserveCast=_player.dashSettings.preserveCast}};
            for (int role=1; role<=4; role++)
            {
                var definition=CharacterCatalog.Find(jobs[role-1]);
                var member=_roster.Join("AI "+definition.name);if(role==1)_localId=member.Id;
                AddAvatar(member.Id,role,member.Name,SpawnPoint(role));
                var avatar=_avatars[member.Id];avatar.Automated=true;avatar.IsBot=false;
                if(avatar.GetComponent<AiSpectatorActor>()==null)avatar.gameObject.AddComponent<AiSpectatorActor>();
                var pc=avatar.GetComponent<PlayerController>();pc.currentRole=(PlayerRole)role;
                var actor=CharacterActor.Ensure(avatar.gameObject);
                if(!actor.AssignDefinition(definition))return AiSpectatorReject("AI 직업 연결에 실패했습니다: "+definition.name);
                if(definition.routes.Length>0&&!actor.Choose("route",definition.routes[0].id,out _))
                    return AiSpectatorReject("AI 기원 선택에 실패했습니다: "+definition.name);
                member.Ready=member.Loaded=true;SyncLobbyAvatar(member);
            }
            AiSpectatorStartingStage=stage;AiSpectatorContinuous=continuous;
            AiSpectatorDamagePercent=AiSpectatorDamageRules.DefaultPercent;
            AiSpectatorAttemptStartPercent=AiSpectatorAttemptEndPercent=AiSpectatorDamagePercent;
            AiSpectatorDamageChangedDuringAttempt=false;_aiSpectatorDamageHistory.Clear();
            _aiSpectatorSessionStopped=false;_aiSpectatorWipeRequested=false;
            StageManager.Instance.currentStage=stage;
            AiSpectatorAutoRetry=false;AiSpectatorMaxRetries=2;AiSpectatorStopOnClear=!continuous;AiSpectatorRetries=0;
            if(stage>1)campaign.MarkDevelopmentUse(); // Forced entry is not a normal saved run.
            Status="개발 테스트 / AI 4인 관전 · 테스트 저장";
            if(GetComponent<AiSpectatorDevelopment>()==null)gameObject.AddComponent<AiSpectatorDevelopment>();
            Debug.Log("[AI Spectator] stage="+stage+" difficulty="+difficulty+" continuous="+continuous+
                " jobs="+string.Join(",",jobs)+" save="+campaign.RunPath);
            return true;
#else
            return AiSpectatorReject("개발 빌드에서만 사용할 수 있습니다.");
#endif
        }
        private bool AiSpectatorReject(string message){AiSpectatorError=message;Status=message;return false;}

        internal void AiSpectatorRescueDead()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var stage = StageManager.Instance;
            if (!AiSpectatorActive || Ending || !IsHost || BattlePhase != RaidBattlePhase.Combat ||
                stage == null || StageManager.CombatSuspended || stage.boss == null || stage.boss.currentHp <= 0)
            { _aiSpectatorWipeRequested = false; return; }
            if (_aiSpectatorWipeRequested)
            {
                _aiSpectatorWipeRequested = false;
                CampaignRuntime.Instance?.MarkDevelopmentUse();
                CombatMetricsManager.Event("개발 테스트 · AI 파티 강제 전멸");
                foreach (var avatar in _avatars.Values)
                    if (avatar != null) PartyLife.Ensure(avatar.gameObject).Die();
                Debug.Log("[AI Spectator] forced party wipe test stage=" + stage.currentStage + " attempt=" + stage.AttemptId);
                // Preserve coins; skip automatic rescue once so StageManager resolves the real wipe.
                return;
            }
            foreach (var avatar in _avatars.Values)
            {
                if (stage.IrisCoins <= 0) break;
                var life = avatar.GetComponent<PartyLife>();
                if (life?.IsDead != true) continue;
                if (IrisRescue.TryUse(avatar.transform, avatar.transform, stage.AttemptId, life.DeathGeneration))
                    Debug.Log("[AI Spectator] Iris rescue actor=" + avatar.Id + " coins=" + stage.IrisCoins);
            }
#endif
        }

        internal void AiSpectatorTick()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(!AiSpectatorActive || Ending || _avatars.Count!=4)return;
            var leader=_avatars[_localId];
            if(BattlePhase==RaidBattlePhase.Lobby && CanStartLobby)
                BeginNetworkEncounter(StageManager.Instance.currentStage,false);
            else if(BattlePhase==RaidBattlePhase.Wiped && AiSpectatorAutoRetry && AiSpectatorRetries<Mathf.Clamp(AiSpectatorMaxRetries,0,20))
            {AiSpectatorRetries++;AutomaticAction(leader,9,leader.transform);}
            else if(BattlePhase==RaidBattlePhase.Reward)
            {
                foreach(var member in _roster.Members)
                {
                    int[] offered=_rewards.Cards(member.Id);
                    if(offered.Length>0 && _rewards.Choose(BattleEpoch,member.Id,offered[0]))
                    {
                        var pc=_avatars[member.Id].GetComponent<PlayerController>();
                        CampaignRuntime.Instance?.CardChosen(pc,offered[0],offered);
                        pc.cardManager.ApplyNetworkCard((RoguelikeCardId)offered[0]);
                        Debug.Log("[AI Spectator] role="+member.Role+" card="+offered[0]);
                    }
                    AutoChooseSkillRewards(member.Id);
                }
                FinishRewardsIfReady();
                // A dead host cannot walk to the portal. Use the same exception as
                // the host action gate; that gate still validates rewards and saves.
                var field = RaidApproachField.Instance;
                if(!AiSpectatorStopOnClear && field?.PortalActive==true &&
                    (field.NearPortal(leader.transform) || leader.GetComponent<PlayerController>()?.IsDead==true))
                    AutomaticAction(leader,15,leader.transform);
            }
#endif
        }
    }

    // The original caster is passed through projectiles and periodic effects to the existing HP sinks.
    public static class AiSpectatorDamage
    {
        public static bool ZeroHpDamageMode(Transform source)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return RaidNetworkSession.Instance?.AiSpectatorActive == true &&
                RaidNetworkSession.Instance.AiSpectatorDamagePercent == 0 && source != null &&
                source.GetComponent<AiSpectatorActor>() != null;
#else
            return false;
#endif
        }
        public static float AtHp(Transform source, float amount, bool alreadyScaled = false)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var session = RaidNetworkSession.Instance;
            if (session?.AiSpectatorActive == true && source != null)
                return AiSpectatorDamageRules.Resolve(amount,session.AiSpectatorDamagePercent,true,
                    source.GetComponent<AiSpectatorActor>() != null,alreadyScaled);
#endif
            return amount;
        }
        public static float BaseFromConfirmed(Transform source, float amount)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var session = RaidNetworkSession.Instance;
            if (session?.AiSpectatorActive == true && source != null && source.GetComponent<AiSpectatorActor>() != null)
                return AiSpectatorDamageRules.BaseFromConfirmed(amount, session.AiSpectatorDamagePercent);
#endif
            return amount;
        }
    }
}
