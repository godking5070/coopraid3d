using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Explicit command-only integration checks. No input injection, authored asset
    // changes or ordinary campaign/settings writes. Absent from release builds.
    public sealed class HeadlessResumeCheck : MonoBehaviour
    {
        [Serializable] public sealed class Scenario { public int version; public string chapterId; public string[] difficulties; public int[] stages; public Slot[] party; }
        [Serializable] public sealed class Slot { public int role,routeIndex,loadoutChoiceIndex; public string jobId; }
        [Serializable] public sealed class Member { public int role,id; public string job,route,core; public string[] auxiliary,selectedSkills; public bool bot,automated,ready; public CharacterStateSnapshot state; }
        [Serializable] public sealed class Result
        {
            public string mode,difficulty,reason,profileRoot,protocol,startedUtc,finishedUtc,runId,seed,checkpointPath,processSessionId=Guid.NewGuid().ToString("N"),attemptId;
            public bool success,headless=true,humanInputVerified,steamVerified,generatedFixture;
            public int expectedPlayers,connectedPlayers,targetStage,checkpointStage=1,stage,frames,gc0,gc1,gc2;
            public double seconds,meanFrameIntervalMs,maxFrameIntervalMs,peakAllocatedMB;
            public Member[] members; public string[] errors;
        }
        private readonly List<string> _errors=new List<string>();
        private string _mode,_caseDirectory,_reportPath,_difficulty;
        private int _role,_expected,_target,_timeout,_boundary;
        private int _startFrame,_gc0,_gc1,_gc2;
        private float _began,_maximumFrame,_nextReport;
        private double _peakMemory;
        private Scenario _scenario;
        private RaidNetworkSession _session;
        private bool _finished;
        private Result _result;
        private StreamWriter _tacticTrace;
        private float _nextTacticTrace;
        private Transform _traceWall;
        private static string Argument(string name,string fallback="")
        {
            string prefix="--astella-"+name+"=";
            return Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length)??fallback;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if(!Debug.isDebugBuild||Application.isEditor||!Application.isBatchMode||Argument("check").Length==0)return;
            try { CampaignStorage.RootFor(Application.persistentDataPath,Environment.GetCommandLineArgs(),true); }
            catch(Exception error){Debug.LogError("Headless check refused: "+error.Message);Application.Quit(2);return;}
            var host=new GameObject("Explicit isolated headless resume check");DontDestroyOnLoad(host);host.AddComponent<HeadlessResumeCheck>();
        }
        private void Awake(){Application.logMessageReceived+=OnLog;_began=Time.realtimeSinceStartup;_startFrame=Time.frameCount;_gc0=GC.CollectionCount(0);_gc1=GC.CollectionCount(1);_gc2=GC.CollectionCount(2);}
        private void OnDestroy(){Application.logMessageReceived-=OnLog;_tacticTrace?.Dispose();}
        private void OnLog(string text,string stack,LogType type)
        {if((type==LogType.Error||type==LogType.Exception||type==LogType.Assert)&&_errors.Count<16)_errors.Add(text);}
        private IEnumerator Start()
        {
            var execution=Run();
            while(true)
            {
                bool next;object current=null;
                try{next=execution.MoveNext();if(next)current=execution.Current;}
                catch(Exception error){Finish(false,error.ToString());yield break;}
                if(!next)yield break;
                yield return current;
            }
        }
        private IEnumerator Run()
        {
            _mode=Argument("check");_difficulty=Argument("difficulty",CampaignCatalog.Normal);
            _role=int.Parse(Argument("role","1"));_expected=int.Parse(Argument("players","4"));
            _target=int.Parse(Argument("through","1"));_timeout=int.Parse(Argument("timeout","900"));
            _boundary=int.Parse(Argument("boundary","1"));
            bool single=_mode=="single-save"||_mode=="single-resume";
            bool fixture=_mode=="single-fixture-save"||_mode=="single-fixture-resume";
            _caseDirectory=Path.GetFullPath(Argument("case-dir"));
            if(!Directory.Exists(_caseDirectory)||!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(_caseDirectory),"^case-[a-f0-9]{32}$"))throw new InvalidOperationException("An explicit unique case directory is required.");
            _reportPath=Path.Combine(_caseDirectory,"role-"+_role+".json");
            if(File.Exists(_reportPath)||(_mode!="ai"&&_mode!="lan-host"&&_mode!="lan-client"&&!single&&!fixture)||_role<1||_role>4||(_expected!=2&&_expected!=4)||(_target!=1&&_target!=6)||_timeout<15||_timeout>3600||(!fixture&&_boundary!=1)||(fixture&&_boundary!=4&&_boundary!=6))throw new InvalidOperationException("Invalid isolated integration request.");
            string scenarioPath=Path.GetFullPath(Argument("scenario"));
            if(new FileInfo(scenarioPath).Length>32768)throw new InvalidOperationException("Scenario is too large.");
            _scenario=JsonUtility.FromJson<Scenario>(File.ReadAllText(scenarioPath));
            if(_scenario==null||_scenario.version!=1||_scenario.chapterId!=CampaignCatalog.PartOne||_scenario.party?.Length!=4||!_scenario.party.Select(p=>p.role).SequenceEqual(new[]{1,2,3,4})||!CampaignCatalog.SupportedDifficulty(_difficulty))throw new InvalidOperationException("Invalid four-role Part 1 scenario.");
            for(int i=0;i<30&& (CampaignRuntime.Instance==null||RaidNetworkSession.Instance==null);i++)yield return null;
            yield return null;yield return null;
            _session=RaidNetworkSession.Instance;
            var campaign=CampaignRuntime.Instance;
            if(campaign==null||_session==null||!campaign.IsolatedTestProfile||RaidSteamSettings.Load().appId!=0)throw new InvalidOperationException("Explicit isolated test profile and disabled Steam are required.");
            string profile=Path.GetFullPath(CampaignStorage.RootFor(Application.persistentDataPath,Environment.GetCommandLineArgs(),true)).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(campaign.RunPath).StartsWith(profile,StringComparison.OrdinalIgnoreCase)||LocalSettingsRuntime.Instance==null||!Path.GetFullPath(LocalSettingsRuntime.Instance.FilePath).StartsWith(profile,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Campaign and local settings must use the isolated profile.");
            foreach(var slot in _scenario.party)
            {
                var definition=CharacterCatalog.Find(slot.jobId);
                if(definition==null||!definition.playable||!CharacterSlots.IsNineKey(definition)||CharacterRosterData.Role(definition)!=slot.role||slot.routeIndex<0||slot.routeIndex>=definition.routes.Length)throw new InvalidOperationException("Scenario catalog/role mismatch.");
            }
            _result=new Result{mode=_mode,difficulty=_difficulty,expectedPlayers=_expected,targetStage=_target,checkpointStage=_boundary,generatedFixture=fixture,profileRoot=profile,protocol=RaidWire.Version.ToString(),startedUtc=DateTime.UtcNow.ToString("O")};
            if(_mode=="ai")
            {
                _tacticTrace=new StreamWriter(Path.Combine(_caseDirectory,"tactics.csv"),false,new System.Text.UTF8Encoding(false));
                _tacticTrace.WriteLine("time,stage,phase,pattern,step,bossX,bossZ,role,x,z,hp,maxHp,shield,dead,cast,castRemaining,coins,gapX,gapZ");
            }
            Application.targetFrameRate=60;
            Debug.Log("[HeadlessResume] START mode="+_mode+" role="+_role+" report="+_reportPath+" profile="+profile);
            if(single||fixture)
            {
                var stage=StageManager.Instance;
                if(_target!=1)throw new InvalidOperationException("Application restart fixture checks the first regular stage boundary only.");
                if(_mode=="single-save"||_mode=="single-fixture-save")
                {
                    if(campaign.Saved!=null||campaign.Settlement!=null)throw new InvalidOperationException("Fresh save fixture requires an unused profile.");
                    campaign.SelectDifficulty(_difficulty);_session.SinglePlay();
                    var slot=_scenario.party[0];
                    if(!RoleSelectionManager.Instance.SelectRosterJob(slot.jobId))throw new InvalidOperationException("Regular single job selection rejected.");
                    var actor=CharacterActor.Ensure(stage.player.gameObject);
                    if(!actor.Choose("route",actor.Definition.routes[slot.routeIndex].id,out _))throw new InvalidOperationException("Regular single origin rejected.");
                    foreach(var rule in actor.Definition.loadouts.Where(r=>string.IsNullOrEmpty(r.routeId)||r.routeId==actor.RouteId))
                        if(!actor.Choose(rule.id,rule.choices.Last(),out _))throw new InvalidOperationException("Nondefault departure choice rejected.");
                    var auxiliary=actor.Definition.auxiliaryTraits.Reverse().Take(2).ToArray();
                    if(!actor.Choose("auxiliary.1",auxiliary[0].id,out _)||!actor.Choose("auxiliary.2",auxiliary[1].id,out _))throw new InvalidOperationException("Nondefault auxiliary selection rejected.");
                    string chosen=BuildKey(actor.Snapshot);
                    AegisRouteSelectionUI.Close();RoleSelectionManager.Instance.ConfirmAegisDeparture();stage.BeginSingleRun();
                    if(campaign.Saved==null||campaign.Saved.transport!="single"||campaign.Saved.nextStage!=1||BuildKey(actor.Snapshot)!=chosen)throw new InvalidOperationException("Regular departure checkpoint or chosen build was lost.");
                    if(fixture)WriteRestoreFixture(campaign,_boundary);
                }
                else
                {
                    if(!campaign.CanContinue||campaign.Saved?.transport!="single"||campaign.Saved.nextStage!=_boundary||campaign.Saved.difficultyId!=_difficulty)throw new InvalidOperationException("Prior process regular checkpoint is not resumable.");
                    string before=File.ReadAllText(campaign.RunPath),personal=JsonUtility.ToJson(campaign.Personal);
                    var prior=JsonUtility.FromJson<CampaignCheckpoint>(JsonUtility.ToJson(campaign.Saved));
                    var local=prior.party.Single(p=>!p.bot&&p.userId==campaign.Owner);
                    campaign.Continue(false);
                    var actor=CharacterActor.Ensure(stage.player.gameObject);
                    if(_session.Mode!=RaidSessionMode.Single||BuildKey(actor.Snapshot)!=BuildKey(local.character))throw new InvalidOperationException("Fresh process failed to restore the saved lobby build.");
                    // Door geometry/input is not under test. Place this isolated actor
                    // at the real door and invoke the production continuation entry.
                    stage.player.transform.position=RaidLobbyRoom.DoorPosition;
                    _session.ContinueSavedSingleFromDoor();
                    if(campaign.Resume!=null||stage.RunId!=prior.runId||stage.currentStage!=_boundary||stage.IrisCoins!=4||Mathf.Abs(stage.player.currentHp-stage.player.maxHp)>.01f||BuildKey(actor.Snapshot)!=BuildKey(local.character)||actor.Snapshot.skills.Any(s=>s.recharge!=0))throw new InvalidOperationException("Fresh process boundary restore did not complete correctly.");
                    VerifyRestoredParty(prior);
                    if(File.ReadAllText(campaign.RunPath)!=before||JsonUtility.ToJson(campaign.Personal)!=personal)throw new InvalidOperationException("Loading a boundary rewrote the checkpoint or personal rewards.");
                    if(fixture&&(_boundary==4&&!stage.boss.MarionetteEncounter||_boundary==6&&!stage.boss.JanusEncounter))throw new InvalidOperationException("Late checkpoint restored the wrong encounter.");
                }
                if(campaign.HasSaveFailure)throw new InvalidOperationException("Regular checkpoint write failed.");
                var persisted=JsonUtility.FromJson<CampaignCheckpoint>(File.ReadAllText(campaign.RunPath));
                string invalid=CampaignValidation.Checkpoint(persisted,campaign.Owner,CharacterCatalog.Find);
                if(invalid!=null||persisted.completed||persisted.nextStage!=_boundary||persisted.clears.Length!=_boundary-1)throw new InvalidOperationException("Regular boundary checkpoint invalid: "+invalid);
                File.Copy(campaign.RunPath,Path.Combine(_caseDirectory,"checkpoint.json"));
                Finish(true,fixture?"Generated regular stage-"+_boundary+" restore fixture: "+(_mode=="single-fixture-save"?"validated representative card/unlock/history data written in the isolated profile; no combat simulated.":"new process restored cards, unlocks, party, metrics and encounter without replaying personal rewards or rewriting disk. This is a generated-data restore check, not a combat clear."):
                    _mode=="single-save"?"Regular single first-boundary checkpoint with nondefault build written through production departure.":"Separate game process resumed the same regular checkpoint, build and run with fresh HP/cooldowns/four revivals. Door movement/input and later-stage rewards remain separate.");
            }
            else if(_mode=="ai")
            {
                if(!_session.BeginAiSpectator(1,_difficulty,_scenario.party.Select(p=>p.jobId).ToArray(),_target==6))throw new InvalidOperationException(_session.AiSpectatorError);
                _session.AiSpectatorStopOnClear=_target==1;
                foreach(var avatar in _session.Avatars.Values)
                {
                    var actor=CharacterActor.Ensure(avatar.gameObject);var slot=_scenario.party[avatar.Role-1];
                    foreach(var rule in actor.Definition.loadouts.Where(r=>string.IsNullOrEmpty(r.routeId)||r.routeId==actor.RouteId))
                    {
                        if(slot.loadoutChoiceIndex<0||slot.loadoutChoiceIndex>=rule.choices.Length||!actor.Choose(rule.id,rule.choices[slot.loadoutChoiceIndex],out string reason))throw new InvalidOperationException("AI fixed loadout rejected: "+rule.id);
                    }
                    if(!FixedBuild(actor,slot,false))throw new InvalidOperationException("AI fixed build mismatch.");
                }
                while(Time.realtimeSinceStartup-_began<_timeout)
                {
                    if(_errors.Count>0)throw new InvalidOperationException("Engine error during AI integration.");
                    if(campaign.HasSaveFailure)throw new InvalidOperationException("Campaign write failure: "+campaign.Notice);
                    if(_session.BattlePhase==RaidBattlePhase.Wiped)throw new InvalidOperationException("Unmodified AI party wiped. No balance or damage override applied.");
                    if(_target==1&&StageManager.Instance.currentStage==1&&_session.BattlePhase==RaidBattlePhase.Reward&&RaidApproachField.Instance?.PortalActive==true)break;
                    if(_target==6&&_session.BattlePhase==RaidBattlePhase.Victory&&campaign.Saved?.completed==true)break;
                    yield return null;
                }
                if(Time.realtimeSinceStartup-_began>=_timeout)throw new TimeoutException("AI stage/phase timeout: "+_session.BattlePhase);
                if(campaign.Saved==null||campaign.Saved.clears.Length<_target)throw new InvalidOperationException("Confirmed stage metrics/checkpoint missing.");
                string saved=File.ReadAllText(campaign.RunPath);
                string invalid=CampaignValidation.Checkpoint(JsonUtility.FromJson<CampaignCheckpoint>(saved),campaign.Owner,CharacterCatalog.Find);
                if(invalid!=null)throw new InvalidOperationException("Unity JSON checkpoint invalid: "+invalid);
                File.Copy(campaign.RunPath,Path.Combine(_caseDirectory,"checkpoint.json"));
                Finish(true,"Unmodified four-job AI combat and Unity JSON checkpoint confirmed through stage "+_target+"; human input, live transport and balance remain unverified.");
            }
            else
            {
                _session.StartConnection(_mode=="lan-host","Resume role "+_role,Argument("address","127.0.0.1:18777"));
                if(_mode=="lan-host"&&_session.Mode!=RaidSessionMode.Host)throw new InvalidOperationException("LAN listener failed: "+_session.Status);
                File.WriteAllText(Path.Combine(_caseDirectory,"role-"+_role+".started"),profile);
                float nextChoice=0;bool readyObserved=false;float stableSince=0;
                while(Time.realtimeSinceStartup-_began<_timeout)
                {
                    if(_errors.Count>0)throw new InvalidOperationException("Engine error during LAN integration.");
                    if(_session.UsesSteam||_session.Ending)throw new InvalidOperationException("Unexpected transport shutdown: "+_session.Status);
                    if(_session.HasLocalAvatar&&_session.ControlsReady&&Time.unscaledTime>=nextChoice)
                    {
                        nextChoice=Time.unscaledTime+.5f;
                        var avatar=_session.Avatars.Values.FirstOrDefault(a=>a.IsLocal);
                        if(avatar!=null)
                        {
                            var actor=CharacterActor.Ensure(avatar.gameObject);var slot=_scenario.party[_role-1];
                            if(actor.Definition.id!=slot.jobId)_session.RequestCharacterAction(12,slot.jobId,0);
                            else
                            {
                                var origin=actor.Definition.routes[slot.routeIndex];
                                if(actor.RouteId!=origin.id)actor.RequestChoice("route",origin.id);
                                else if(!FixedBuild(actor,slot,true)){}
                                else if(!_session.LocalReady)_session.ToggleReady();
                                else if(avatar.Role==_role)File.WriteAllText(Path.Combine(_caseDirectory,"role-"+_role+".configured"),profile);
                            }
                        }
                    }
                    bool synchronized=_session.ControlsReady&&_session.ConnectedPlayerCount==_expected&&_session.Avatars.Values.Where(a=>!a.IsBot).All(a=>a.Ready&&FixedBuild(CharacterActor.Ensure(a.gameObject),_scenario.party[a.Role-1],false));
                    if(synchronized){if(!readyObserved){readyObserved=true;stableSince=Time.unscaledTime;}if(Time.unscaledTime-stableSince>=2)break;}
                    else readyObserved=false;
                    yield return null;
                }
                if(Time.realtimeSinceStartup-_began>=_timeout)throw new TimeoutException("LAN four-role readiness/replication timeout: "+_session.Status);
                Finish(true,"Real UDP game sessions confirmed: connections, role/job/origin/loadout and ready-state replication. Combat, disconnect recovery and Steam remain separate.");
            }
            while(!File.Exists(Path.Combine(_caseDirectory,"STOP"))&&Time.realtimeSinceStartup-_began<_timeout+60)yield return null;
            if(_errors.Count>0){_result.success=false;_result.reason="Engine error after initial result.";SaveResult();}
            ExitCheck(_result.success?0:1);
        }
        private static string CardKey(CampaignCardStack[] cards)=>string.Join(",",cards.OrderBy(c=>c.id).Select(c=>c.id+"="+c.stacks));
        private static string EffectKey(JobBuildEffect[] effects)=>string.Join(",",effects.OrderBy(e=>e.id).Select(e=>e.id+"="+e.stacks+"@"+TraceNumber(e.magnitude)));
        private static void WriteRestoreFixture(CampaignRuntime campaign,int boundary)
        {
            if(!campaign.IsolatedTestProfile||(boundary!=4&&boundary!=6)||campaign.Saved?.nextStage!=1)throw new InvalidOperationException("Generated restore fixtures require an unused isolated departure checkpoint.");
            var data=JsonUtility.FromJson<CampaignCheckpoint>(JsonUtility.ToJson(campaign.Saved));int finished=boundary-1;
            foreach(var slot in data.party)
            {
                var state=new CharacterRuntimeState(CharacterCatalog.Find(slot.character.jobId));
                if(!state.Restore(slot.character))throw new InvalidOperationException("Fixture source actor is invalid.");
                for(int n=1;n<=finished;n++)
                {
                    if(!state.ClearedStage(data.runId,data.chapterId,n))throw new InvalidOperationException("Fixture stage identity is invalid.");
                    foreach(var rule in state.PendingChoices())if(!state.ChooseSkill(rule.id,rule.choices.Last(),state.RewardKey(rule),true))throw new InvalidOperationException("Fixture unlock is invalid.");
                }
                var jobCards=Enumerable.Range(7,24).Select(CampaignCardIds.Id).Where(id=>CampaignCardIds.Fits(id,slot.character.jobId)).ToArray();
                slot.cards=slot.bot?Array.Empty<CampaignCardStack>():new[]{new CampaignCardStack{id=CampaignCardIds.Id(6),stacks=finished-2},
                    new CampaignCardStack{id=jobCards[0],stacks=1},new CampaignCardStack{id=jobCards[1],stacks=1}};
                foreach(var effect in CampaignCardIds.Effects(slot.cards))state.SetBuildEffect(effect.id,effect.stacks,effect.magnitude);
                state.Reset(CharacterReset.NextStage);slot.character=state.Snapshot;
            }
            data.nextStage=boundary;data.attemptsStarted=boundary;data.runSeconds=finished;data.savedUtc=DateTimeOffset.UtcNow.ToString("O");
            data.processedStages=Enumerable.Range(1,finished).Select(n=>CharacterRewardKey.Stage(data.runId,data.chapterId,n)).ToArray();
            var owner=data.party.Single(p=>!p.bot);
            var ownerCards=owner.cards.Where(c=>CampaignCardIds.Index(c.id)>=7).Select(c=>c.id).ToArray();
            data.rewards=Enumerable.Range(1,finished).Select(n=>new CampaignRewardRecord{key=CharacterRewardKey.Stage(data.runId,data.chapterId,n)+"/card/"+owner.slotId,slotId=owner.slotId,
                offeredIds=new[]{n<=2?ownerCards[n-1]:CampaignCardIds.Id(6),CampaignCardIds.Id(0),CampaignCardIds.Id(1)},selectedId=n<=2?ownerCards[n-1]:CampaignCardIds.Id(6)}).ToArray();
            data.clears=Enumerable.Range(1,finished).Select(n=>new CampaignMetricRecord{stage=n,seconds=1,members=data.party.Select(p=>new CampaignMetricMember{slotId=p.slotId,name=p.name,role=p.role,jobId=p.character.jobId}).ToArray()}).ToArray();
            string invalid=CampaignValidation.Checkpoint(data,campaign.Owner,CharacterCatalog.Find);
            if(invalid!=null)throw new InvalidOperationException("Generated checkpoint rejected: "+invalid);
            // Fixture data never enters ordinary saves or changes the live campaign.
            // The next process must load it through the normal continuation API.
            if(!CampaignStorage.Save(campaign.RunPath,JsonUtility.ToJson(data,true),json=>CampaignValidation.Checkpoint(JsonUtility.FromJson<CampaignCheckpoint>(json),campaign.Owner,CharacterCatalog.Find)==null?CampaignFileState.Valid:CampaignFileState.Corrupt,out string error))throw new IOException(error);
        }
        private static void VerifyRestoredParty(CampaignCheckpoint prior)
        {
            var members=PartyLife.OrderedMembers();if(members.Count!=prior.party.Length)throw new InvalidOperationException("Restored party count differs.");
            foreach(var member in members)
            {
                var actor=CharacterActor.Ensure(member.gameObject);var live=actor.Snapshot;
                var saved=prior.party.Single(p=>p.role==CharacterRosterData.Role(actor.Definition));
                if(BuildKey(live)!=BuildKey(saved.character)||live.runId!=prior.runId||live.clearedStage!=prior.nextStage-1||
                    !new HashSet<string>(live.clearedEncounters).SetEquals(saved.character.clearedEncounters)||!new HashSet<string>(live.unlocked).SetEquals(saved.character.unlocked)||
                    !new HashSet<string>(live.chosenRules).SetEquals(saved.character.chosenRules)||!new HashSet<string>(live.automaticChoices).SetEquals(saved.character.automaticChoices)||
                    !live.selections.OrderBy(s=>s.slot).Select(s=>s.skillId).SequenceEqual(saved.character.selections.OrderBy(s=>s.slot).Select(s=>s.skillId))||
                    live.gcd!=0||live.castSkillId!=""||live.skills.Any(s=>s.recharge!=0||s.charges!=actor.RuntimeState.MaximumCharges(s.id))||
                    EffectKey(live.effects)!=EffectKey(CampaignCardIds.Effects(saved.cards))||live.resources.Any(r=>Mathf.Abs(r.value-saved.character.resources.Single(v=>v.id==r.id).value)>.001f))throw new InvalidOperationException("Restored actor progress/build differs: "+actor.Definition.id);
                float maximum=member.GetComponent<PlayerController>()?.maxHp??member.GetComponent<PartyBotAI>().maxHp;
                if(Mathf.Abs(member.GetComponent<PartyLife>().Hp-maximum)>.01f)throw new InvalidOperationException("Restored party member is not at full health.");
                if(member.GetComponent<PlayerController>() is PlayerController player&&CardKey(player.cardManager.CaptureCampaignCards())!=CardKey(saved.cards))throw new InvalidOperationException("Restored card stacks differ.");
            }
            if(CombatMetricsManager.Instance.Ledger.Stages.Count!=prior.clears.Length)throw new InvalidOperationException("Restored stage history differs.");
            if(CampaignRuntime.Instance.DifficultyId!=prior.difficultyId||CampaignRuntime.Instance.ReportWorking.randomState!=prior.randomState||Mathf.Abs(StageManager.Instance.TotalPlayTime-prior.runSeconds)>.01f)throw new InvalidOperationException("Restored difficulty, random state or run time differs.");
        }
        private bool FixedBuild(CharacterActor actor,Slot slot,bool request)
        {
            if(actor.Definition.id!=slot.jobId||actor.RouteId!=actor.Definition.routes[slot.routeIndex].id)return false;
            var snapshot=actor.Snapshot;
            foreach(var rule in actor.Definition.loadouts.Where(r=>string.IsNullOrEmpty(r.routeId)||r.routeId==actor.RouteId))
            {
                if(slot.loadoutChoiceIndex<0||slot.loadoutChoiceIndex>=rule.choices.Length)throw new InvalidOperationException("Invalid scenario choice.");
                string selected=(int)rule.slot==0?snapshot.coreTrait:snapshot.selections.First(s=>s.slot==rule.slot).skillId;
                if(selected!=rule.choices[slot.loadoutChoiceIndex]){if(request)actor.RequestChoice(rule.id,rule.choices[slot.loadoutChoiceIndex]);return false;}
            }
            return snapshot.auxiliaryTraits.Length==2&&snapshot.auxiliaryTraits.Distinct().Count()==2;
        }
        private static string BuildKey(CharacterStateSnapshot state)=>state.routeId+"/"+state.coreTrait+"/"+string.Join(",",state.auxiliaryTraits)+"/"+string.Join(",",state.selections.Where(s=>s.slot!=CharacterSlot.Ultimate).Select(s=>s.slot+"="+s.skillId));
        private void Update()
        {
            _maximumFrame=Mathf.Max(_maximumFrame,Time.unscaledDeltaTime);
            _peakMemory=Math.Max(_peakMemory,UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()/1048576.0);
            if(!_finished&&_tacticTrace!=null&&Time.realtimeSinceStartup>=_nextTacticTrace)
            {_nextTacticTrace=Time.realtimeSinceStartup+.25f;TraceTactics();}
            if(_session!=null&&!_finished&&Time.realtimeSinceStartup>=_nextReport)
            {_nextReport=Time.realtimeSinceStartup+10;_tacticTrace?.Flush();Debug.Log("[HeadlessResume] progress mode="+_mode+" stage="+StageManager.Instance?.currentStage+" phase="+_session.BattlePhase+" connected="+_session.ConnectedPlayerCount+" bossHp="+StageManager.Instance?.boss?.currentHp);}
        }
        private static string TraceNumber(float value)=>value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        private void TraceTactics()
        {
            var stage=StageManager.Instance;var boss=stage?.boss;
            if(_session==null||boss==null)return;
            // Read-only observation of the actual poses and inactive exit segment.
            // Never call destination selection here: that mutates the AI's cache.
            string gapX="",gapZ="";
            if(boss.GuardianNormal&&(boss.GuardianPatternNow==GuardianPattern.Lockdown||boss.GuardianPatternNow==GuardianPattern.FullLockdown))
            {
                if(_traceWall==null)_traceWall=GameObject.Find("Guardian hard · breakable ring wall")?.transform;
                if(_traceWall!=null)for(int i=0;i<_traceWall.childCount;i++)
                {var piece=_traceWall.GetChild(i);if(!piece.gameObject.activeSelf){gapX=TraceNumber(piece.position.x);gapZ=TraceNumber(piece.position.z);break;}}
            }
            foreach(var avatar in _session.Avatars.Values)
            {
                var pc=avatar.GetComponent<PlayerController>();var actor=avatar.GetComponent<CharacterActor>();
                if(pc==null||actor==null)continue;
                var runtime=actor.RuntimeState;
                _tacticTrace.WriteLine(string.Join(",",TraceNumber(RaidCombatAuthority.ServerTime),stage.currentStage.ToString(),_session.BattlePhase.ToString(),
                    boss.GuardianNormal?boss.GuardianPatternNow.ToString():"",boss.GuardianNormal?boss.GuardianStepNow.ToString():"",TraceNumber(boss.transform.position.x),TraceNumber(boss.transform.position.z),
                    avatar.Role.ToString(),TraceNumber(pc.transform.position.x),TraceNumber(pc.transform.position.z),TraceNumber(pc.currentHp),TraceNumber(pc.maxHp),TraceNumber(pc.currentShield),pc.IsDead?"1":"0",
                    runtime?.CastingSkillId??"",TraceNumber(runtime?.CastingRemaining??0),stage.IrisCoins.ToString(),gapX,gapZ));
            }
        }
        private void Finish(bool success,string reason)
        {
            if(_finished)return;_finished=true;
            _tacticTrace?.Flush();
            if(_result==null)_result=new Result{mode=_mode,difficulty=_difficulty};
            _result.success=success&&_errors.Count==0;_result.reason=reason;
            if(!_result.success&&!string.IsNullOrEmpty(_caseDirectory)&&CampaignRuntime.Instance?.ReportWorking!=null)
                File.WriteAllText(Path.Combine(_caseDirectory,"rejected-working-checkpoint.json"),JsonUtility.ToJson(CampaignRuntime.Instance.ReportWorking,true));
            SaveResult();
            Debug.Log("[HeadlessResume] "+(_result.success?"PASS ":"FAIL ")+reason);
            if(!_result.success)ExitCheck(1);
        }
        private void ExitCheck(int code)
        {
            // Use the production session cleanup before quitting our own isolated
            // process. This also satisfies CampaignUI's normal quit-confirmation gate.
            var session=RaidNetworkSession.Instance;
            if(CampaignRuntime.Instance?.IsolatedTestProfile==true&&session!=null&&!session.Ending)session.DisconnectConfirmed();
            Application.Quit(code);
        }
        private void SaveResult()
        {
            _result.seconds=Time.realtimeSinceStartup-_began;_result.frames=Time.frameCount-_startFrame;
            _result.finishedUtc=DateTime.UtcNow.ToString("O");_result.connectedPlayers=_session?.ConnectedPlayerCount??0;
            _result.stage=StageManager.Instance?.currentStage??0;_result.gc0=GC.CollectionCount(0)-_gc0;_result.gc1=GC.CollectionCount(1)-_gc1;_result.gc2=GC.CollectionCount(2)-_gc2;
            _result.meanFrameIntervalMs=_result.seconds*1000/Math.Max(1,_result.frames);_result.maxFrameIntervalMs=_maximumFrame*1000;_result.peakAllocatedMB=_peakMemory;
            _result.errors=_errors.ToArray();
            var saved=CampaignRuntime.Instance?.Saved;_result.runId=saved?.runId??"";_result.seed=saved!=null?saved.seed.ToString():"";_result.checkpointPath=CampaignRuntime.Instance?.RunPath??"";
            _result.attemptId=StageManager.Instance?.AttemptId??"";
            _result.members=_session?.Avatars.Values.OrderBy(a=>a.Role).Select(a=>{var actor=CharacterActor.Ensure(a.gameObject);var state=actor.Snapshot;return new Member{role=a.Role,id=a.Id,job=actor.Definition.id,route=actor.RouteId,core=state.coreTrait,auxiliary=state.auxiliaryTraits,selectedSkills=state.selections.Select(s=>s.slot+"="+s.skillId).ToArray(),bot=a.IsBot,automated=a.Automated,ready=a.Ready};}).ToArray();
            if(_session?.Mode==RaidSessionMode.Single)_result.members=PartyLife.OrderedMembers().Select(member=>{var actor=CharacterActor.Ensure(member.gameObject);var state=actor.Snapshot;return new Member{role=CharacterRosterData.Role(actor.Definition),id=RaidCombatAuthority.Id(member),job=actor.Definition.id,route=actor.RouteId,core=state.coreTrait,auxiliary=state.auxiliaryTraits,selectedSkills=state.selections.Select(s=>s.slot+"="+s.skillId).ToArray(),bot=member.GetComponent<PartyBotAI>()!=null,automated=member.GetComponent<PartyBotAI>()!=null,ready=actor.DepartureReady,state=state};}).OrderBy(m=>m.role).ToArray();
            if(!string.IsNullOrEmpty(_reportPath))File.WriteAllText(_reportPath,JsonUtility.ToJson(_result,true));
        }
    }
#endif
    // Development presentation and controls. The combat still runs in RaidNetworkSession.
    public sealed class AiSpectatorDevelopment : MonoBehaviour
    {
        public Rect menuRect=new Rect(24,90,430,480);
        public Rect statusRect=new Rect(12,12,410,540);
        private readonly List<CharacterDefinition>[] _jobs={new List<CharacterDefinition>(),new List<CharacterDefinition>(),
            new List<CharacterDefinition>(),new List<CharacterDefinition>()};
        private readonly int[] _choice=new int[4];
        private int _stage=1,_cameraMember;
        private bool _hard,_continuous=true,_open,_autoRetry;
        private bool _statusMinimized;
        private Vector2 _statusScroll;
        private int _maxRetries=2;
        private string _message="";
        private string _damageInput="100";
        private RaidNetworkSession _session;
        private float _lastProgressAt,_lastBossHp;
        private int _lastPhase=-1;

        private void Awake(){_session=GetComponent<RaidNetworkSession>();}
        private void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            foreach(var job in CharacterRosterData.Jobs)
            {
                var definition=CharacterCatalog.Find(job.Id);
                if(definition!=null&&definition.playable)_jobs[CharacterRosterData.Role(definition)-1].Add(definition);
            }
            var aegis=CharacterCatalog.Find(AegisPaladinData.JobId);
            if(aegis!=null&&aegis.playable&&!_jobs[0].Contains(aegis))_jobs[0].Insert(0,aegis);
#endif
        }
        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(_session==null||!_session.AiSpectatorActive)return;
            var camera=QuarterViewCamera.LocalCamera;
            if(camera!=null&&!LocalGameplayInput.ModalCovered)
            {
                if(LocalInputBindings.Pressed("SpectatePrevious"))Cycle(-1);
                if(LocalInputBindings.Pressed("SpectateNext"))Cycle(1);
                if(LocalInputBindings.Pressed("SpectateBoss"))camera.SetDevelopmentView(AiSpectatorView.Boss);
                if(LocalInputBindings.Pressed("SpectateArena"))camera.SetDevelopmentView(AiSpectatorView.Arena);
                if(LocalInputBindings.Pressed("SpectateFree"))camera.SetDevelopmentView(AiSpectatorView.Free);
            }
            if(camera!=null&&camera.DevelopmentView==AiSpectatorView.Party&&
                !LocalGameplayInput.DevelopmentReportCovered&&!CombatRange.LivingPartyMember(camera.SpectatorTarget))Cycle(1);
            var boss=StageManager.Instance?.boss;
            int phase=(int)_session.BattlePhase;
            float hp=boss!=null?boss.currentHp:0;
            if(phase!=_lastPhase||Mathf.Abs(hp-_lastBossHp)>1)
            {_lastPhase=phase;_lastBossHp=hp;_lastProgressAt=Time.unscaledTime;}
            if(_session.BattlePhase==RaidBattlePhase.Combat&&_session.AiSpectatorDamagePercent>0&&Time.unscaledTime-_lastProgressAt>90)
            {_message="진행 정체: 90초 동안 보스 HP/전투 단계 변화 없음. 로그와 타깃·경로를 확인하세요.";
             Debug.LogWarning("[AI Spectator] "+_message);_lastProgressAt=Time.unscaledTime;}
            if(_session.BattlePhase==RaidBattlePhase.Reward&&!_session.AiSpectatorStopOnClear&&Time.unscaledTime-_lastProgressAt>30)
            {
                var leader=_session.AiSpectatorLeader;
                var pc=leader!=null?leader.GetComponent<PlayerController>():null;
                Debug.LogWarning("[AI Spectator] reward portal wait: position="+leader?.position.ToString("F3")+
                    " stage="+StageManager.Instance?.currentStage+" phase="+_session.BattlePhase+
                    " stopOnClear="+_session.AiSpectatorStopOnClear+
                    " saveFailure="+CampaignRuntime.Instance?.HasSaveFailure+
                    " settlement="+(CampaignRuntime.Instance?.Settlement!=null)+
                    " portal="+RaidApproachField.Instance?.PortalActive+" near="+RaidApproachField.Instance?.NearPortal(leader)+
                    " dead="+pc?.IsDead+" stun="+pc?.IsStunned+" root="+pc?.IsMovementLocked+
                    " rescue="+pc?.IsBeingRescued+" knockback="+pc?.IsBeingKnockedBack+" rush="+pc?.IsSkillRushing+
                    " motionLock="+leader?.GetComponent<NetworkRaidAvatar>()?.MovementLocked);
                _lastProgressAt=Time.unscaledTime;
            }
#endif
        }
        private void Cycle(int direction)
        {
            var members=PartyLife.OrderedMembers();
            if(members.Count==0){QuarterViewCamera.LocalCamera?.SetDevelopmentView(AiSpectatorView.Arena);return;}
            for(int offset=1;offset<=members.Count;offset++)
            {
                int next=(_cameraMember+direction*offset+members.Count*4)%members.Count;
                if(!CombatRange.LivingPartyMember(members[next]))continue;
                _cameraMember=next;QuarterViewCamera.LocalCamera?.SetDevelopmentView(AiSpectatorView.Party,members[next]);return;
            }
            QuarterViewCamera.LocalCamera?.SetDevelopmentView(AiSpectatorView.Arena);
        }
        private void Begin()
        {
            if(_session==null)return;
            var jobs=new string[4];for(int i=0;i<4;i++)
            {if(_jobs[i].Count==0){_message="선택할 수 있는 직업 데이터가 없습니다.";return;}
             jobs[i]=_jobs[i][_choice[i]%_jobs[i].Count].id;}
#if UNITY_EDITOR
            string profile="ai-spectator-"+DateTime.UtcNow.ToString("MMddHHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
            UnityEditor.SessionState.SetString("GuardianNormal.TestProfile",profile);
            CampaignRuntime.Instance?.ReloadAccount();
#endif
            if(_session.BeginAiSpectator(_stage,_hard?CampaignCatalog.Hard:CampaignCatalog.Normal,jobs,_continuous))
            {_session.AiSpectatorAutoRetry=_autoRetry;_session.AiSpectatorMaxRetries=_maxRetries;
             _open=false;_message="";Cycle(1);}
            else _message=_session.AiSpectatorError;
        }
        private void OnGUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(LocalGameplayInput.DevelopmentReportCovered)return;
            if(!Debug.isDebugBuild||_session==null)return;
            if(_session.Mode==RaidSessionMode.Menu)
            {
                if(!_open){if(GUI.Button(new Rect(20,Screen.height-62,210,42),"AI 4인 관전 테스트"))_open=true;return;}
                menuRect=GUI.Window(9843,menuRect,DrawMenu,"개발 테스트 / AI 4인 관전");return;
            }
            if(_session.AiSpectatorActive)
            {
                var window=statusRect;
                if(_statusMinimized)window.height=90;
                window=GUI.Window(9844,window,DrawStatus,"개발 테스트 / AI 4인 관전");
                // Preserve the expanded size and keep dragging available in both modes.
                if(_statusMinimized)statusRect.position=window.position;
                else statusRect=window;
            }
#endif
        }
        private void DrawMenu(int id)
        {
            GUILayout.Label("테스트 프로필에서만 시작됩니다. 관전자는 파티 인원이 아닙니다.");
            GUILayout.BeginHorizontal();if(GUILayout.Button("◀ Stage"))_stage=Mathf.Max(1,_stage-1);
            GUILayout.Label(_stage.ToString(),GUILayout.Width(35));if(GUILayout.Button("Stage ▶"))_stage=Mathf.Min(6,_stage+1);GUILayout.EndHorizontal();
            if(GUILayout.Button("난이도: "+(_hard?"하드":"노멀")))_hard=!_hard;
            for(int role=0;role<4;role++)
            {int r=role;if(_jobs[r].Count==0)continue;
             if(GUILayout.Button((r==0?"탱커":r==1?"힐러":r==2?"딜러 1":"딜러 2")+": "+_jobs[r][_choice[r]%_jobs[r].Count].name))
                 _choice[r]=(_choice[r]+1)%_jobs[r].Count;}
            _continuous=GUILayout.Toggle(_continuous,"연속 진행 (끔: 현재 스테이지 클리어 시 멈춤)");
            _autoRetry=GUILayout.Toggle(_autoRetry,"전멸 시 자동 재도전 (기본 꺼짐)");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("재도전 최대 -"))_maxRetries=Mathf.Max(0,_maxRetries-1);
            GUILayout.Label(_maxRetries.ToString(),GUILayout.Width(30));
            if(GUILayout.Button("재도전 최대 +"))_maxRetries=Mathf.Min(20,_maxRetries+1);
            GUILayout.EndHorizontal();
              GUILayout.Label(_stage>5?"Stage 6 노멀·하드는 야누스 전용 전투입니다. 후반 강제 진입은 개발 프리셋입니다.":"후반 강제 진입은 개발 프리셋이며 정상 런 검증이 아닙니다.");
              if(GUILayout.Button("관전 시작"))
              Begin();
            if(GUILayout.Button("닫기"))_open=false;
            if(_message.Length>0)GUILayout.Label(_message);
            GUI.DragWindow(new Rect(0,0,menuRect.width,24));
        }
        private void DrawStatus(int id)
        {
            if(GUI.Button(new Rect(statusRect.width-76,2,70,20),_statusMinimized?"최대화":"최소화"))
            {
                _statusMinimized=!_statusMinimized;
                GUIUtility.ExitGUI(); // Rebuild the window at its new size on the next GUI event.
            }
            var stage=StageManager.Instance;var boss=stage?.boss;
            GUILayout.Label("Stage "+stage?.currentStage+" / "+CampaignRuntime.Instance?.DifficultyId+
                " · "+_session.BattlePhase+" · "+(CampaignRuntime.Instance?.IsolatedTestProfile==true?"테스트 저장":"저장 경계 오류"));
            if(_statusMinimized)
            {
                GUILayout.Label("보스 HP "+(boss!=null?Mathf.CeilToInt(boss.currentHp)+"/"+Mathf.CeilToInt(boss.maxHp):"—"));
                GUI.DragWindow(new Rect(0,0,statusRect.width-80,24));
                return;
            }
            _statusScroll=GUILayout.BeginScrollView(_statusScroll);
            foreach(var member in PartyLife.OrderedMembers())
            {var pc=member!=null?member.GetComponent<PlayerController>():null;
             if(pc!=null)GUILayout.Label((CharacterActor.Ensure(member.gameObject).Definition?.name??member.name)+
                 " HP "+Mathf.CeilToInt(pc.currentHp)+"/"+Mathf.CeilToInt(pc.maxHp)+(pc.IsDead?" · 사망":""));}
            GUILayout.Label("보스 HP "+(boss!=null?Mathf.CeilToInt(boss.currentHp)+"/"+Mathf.CeilToInt(boss.maxHp):"—")+
                " · "+(boss?.CurrentCastSkillName??""));
            GUILayout.Label("공용 부활 "+(stage?.IrisCoins??0)+"/4 · 재도전 "+_session.AiSpectatorRetries+"/"+_session.AiSpectatorMaxRetries);
            int damage=_session.AiSpectatorDamagePercent;
            GUILayout.Label("AI 공격 피해 ×"+(damage/100f).ToString("F2")+" / "+damage+"%"+
                (damage==100?"":" · 기본 밸런스 결과 아님"));
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("-10%p"))ChangeDamage(AiSpectatorDamageRules.Step(damage,-10));
            if(GUILayout.Button("+10%p"))ChangeDamage(AiSpectatorDamageRules.Step(damage,10));
            if(GUILayout.Button("100%로 초기화"))ChangeDamage(100);
            GUILayout.EndHorizontal();
            int slider=Mathf.RoundToInt(GUILayout.HorizontalSlider(_session.AiSpectatorDamagePercent,0,500));
            if(slider!=_session.AiSpectatorDamagePercent)ChangeDamage(slider);
            GUILayout.BeginHorizontal();
            _damageInput=GUILayout.TextField(_damageInput,GUILayout.Width(70));
            if(GUILayout.Button("% 적용"))
            {if(AiSpectatorDamageRules.TryParse(_damageInput,out int parsed))ChangeDamage(parsed);
             else _message="AI 공격 피해는 0~500 사이의 정수 %로 입력하세요.";}
            GUILayout.EndHorizontal();
            GUILayout.Label("기믹 오브젝트 파괴 속도도 바뀝니다. 0%는 공격 판정만 유지합니다.");
            GUILayout.Label("도전 시작 "+_session.AiSpectatorAttemptStartPercent+"% · 변경 "+
                (_session.AiSpectatorDamageChangedDuringAttempt?"있음":"없음")+" · 종료 "+_session.AiSpectatorAttemptEndPercent+"%");
            GUILayout.BeginHorizontal();if(GUILayout.Button("◀"))Cycle(-1);if(GUILayout.Button("다음 AI"))Cycle(1);
            if(GUILayout.Button("보스"))QuarterViewCamera.LocalCamera?.SetDevelopmentView(AiSpectatorView.Boss);
            if(GUILayout.Button("전장"))QuarterViewCamera.LocalCamera?.SetDevelopmentView(AiSpectatorView.Arena);
            if(GUILayout.Button("자유"))QuarterViewCamera.LocalCamera?.SetDevelopmentView(AiSpectatorView.Free);GUILayout.EndHorizontal();
            _session.AiSpectatorAutoRetry=GUILayout.Toggle(_session.AiSpectatorAutoRetry,"전멸 시 자동 재도전");
            GUILayout.BeginHorizontal();if(GUILayout.Button("최대 -"))_session.AiSpectatorMaxRetries=Mathf.Max(0,_session.AiSpectatorMaxRetries-1);
            if(GUILayout.Button("최대 +"))_session.AiSpectatorMaxRetries=Mathf.Min(20,_session.AiSpectatorMaxRetries+1);GUILayout.EndHorizontal();
            _session.AiSpectatorStopOnClear=GUILayout.Toggle(_session.AiSpectatorStopOnClear,"클리어 시 멈춤");
            bool controlsEnabled=GUI.enabled;
            GUI.enabled=controlsEnabled && _session.CanTestAiSpectatorWipe;
            if(GUILayout.Button("파티 전멸 테스트 (코인 부활 건너뜀)"))
                _message=_session.RequestAiSpectatorWipeTest()?"전멸 테스트 요청 · 기존 전멸·재도전 흐름을 확인하세요.":"전투 중인 개발 AI 관전에서만 사용할 수 있습니다.";
            GUI.enabled=controlsEnabled;
            if(GUILayout.Button("관전 테스트 종료"))_session.DisconnectConfirmed();
            if(_message.Length>0)GUILayout.Label(_message);
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0,0,statusRect.width-80,24));
        }
        private void ChangeDamage(int percent)
        {
            if(_session.SetAiSpectatorDamagePercent(percent)){_damageInput=percent.ToString();_message="";}
            else _message="개발 AI 관전 세션에서 0~500%만 적용할 수 있습니다.";
        }
    }
}
