#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RaidRogue
{
    // Opt-in integration driver. Only a real original project (.git directory) may run it.
    // Editor SessionState selects the isolated campaign root BEFORE any Play-mode Awake.
    [InitializeOnLoad]
    public static class GuardianNormalDevelopmentChecks
    {
        [Serializable] public sealed class Request { public int role=3; public bool guided; }
        public static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName,".git","codex-backups","guardian-stage1-20261027");
        static GuardianNormalDevelopmentChecks()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += change => {
                if(change==PlayModeStateChange.EnteredEditMode)SessionState.EraseString("GuardianNormal.TestProfile");
            };
        }
        [MenuItem("Tools/RaidRogue/Guardian/Use Isolated Save For Next Manual Play")]
        private static void UseManualTestProfile()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string project = Directory.GetParent(Application.dataPath).FullName;
            if (!Directory.Exists(Path.Combine(project, ".git")))
            { Debug.LogError("Guardian manual test profile is available only in the original project."); return; }
            string profile = "manual-guardian-" + DateTime.Now.ToString("MMddHHmmss");
            SessionState.SetString("GuardianNormal.TestProfile", profile);
            Debug.Log("[Guardian] Next manual Play uses isolated test save: " + profile);
        }
        private static void Poll()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)return;
            string path=Path.Combine(Folder,"run-guardian-check.json");
            if(!File.Exists(path))return;
            string json=File.ReadAllText(path);File.Move(path,path+".consumed-"+DateTime.Now.Ticks);
            SessionState.SetString("GuardianNormal.Request",json);
            SessionState.SetString("GuardianNormal.TestProfile","guardian-"+DateTime.Now.ToString("MMddHHmmss"));
            EditorApplication.EnterPlaymode();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string profile = SessionState.GetString("GuardianNormal.TestProfile", "");
            if (profile.Length == 0 || profile.StartsWith("manual-", StringComparison.Ordinal)) return;
            new GameObject("Guardian isolated integration driver").AddComponent<GuardianNormalCheckDriver>();
        }
    }
    public sealed class GuardianNormalCheckDriver : MonoBehaviour
    {
        private BossController boss; private PlayerController player; private CharacterActor actor;
        private string report; private int role; private float began; private bool drive;
        private GuardianPattern observedPattern; private GuardianStep observedStep;
        private float observedAt; private Vector3 lockedForward;
        private void Log(string text){File.AppendAllText(report,text+Environment.NewLine);Debug.Log("[GuardianCheck] "+text);}
        private IEnumerator Start()
        {
            var request=JsonUtility.FromJson<GuardianNormalDevelopmentChecks.Request>(SessionState.GetString("GuardianNormal.Request","{}"));role=request.role;
            report=Path.Combine(GuardianNormalDevelopmentChecks.Folder,"play-"+DateTime.Now.ToString("HHmmss")+"-role"+role+".txt");
            yield return null; yield return null;
            var stage=StageManager.Instance; boss=stage.boss;player=stage.player;
            RaidNetworkSession.Instance.SinglePlay();
            var job=Array.Find(CharacterRosterData.Jobs,j=>j.Role==role);
            if(!RoleSelectionManager.Instance.SelectRosterJob(job.Id)){Log("FAIL select role");Finish();yield break;}
            actor=CharacterActor.Ensure(player.gameObject);
            if(actor.Definition.routes.Length>0)actor.Choose("route",actor.Definition.routes[0].id,out _);
            RoleSelectionManager.Instance.ConfirmAegisDeparture();
            Log("mode="+(request.guided?"DEVELOPMENT forced patterns":"NORMAL scheduler, scripted player movement/skills")+" role="+role+" job="+job.Id+" save="+CampaignRuntime.Instance.RunPath);
            if(!CampaignRuntime.Instance.RunPath.Contains("test-profiles"))throw new InvalidOperationException("Test save isolation failed");
            stage.BeginSingleRun();
            float timeout=Time.realtimeSinceStartup+45;
            while(StageManager.CombatSuspended && Time.realtimeSinceStartup<timeout)
            {
                if(RaidApproachField.Instance?.Walking==true)
                    player.transform.position=Vector3.MoveTowards(player.transform.position,new Vector3(0,1,RaidApproachField.Instance.EntryZ+1),player.moveSpeed*Time.unscaledDeltaTime);
                yield return null;
            }
            if(StageManager.CombatSuspended){Log("FAIL combat did not start "+stage.Progress.Phase);Finish();yield break;}
            Log("PASS approach/arrival/countdown; party="+PartyLife.OrderedMembers().Count+" radius="+RaidApproachField.Instance.FieldRadius+
                " hp="+boss.currentHp+" max="+boss.maxHp);
            Time.timeScale=1; began=Time.time;drive=true;
            if(request.guided)
            {
                CampaignRuntime.Instance.MarkDevelopmentUse();
                yield return null;
                foreach(var pattern in GuardianNormalState.Introduction)
                {
                    if(StageManager.CombatSuspended)break;
                    typeof(BossController).GetMethod("BeginGuardian",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(boss,new object[]{pattern,RaidCombatAuthority.ServerTime});
                    var forward=boss.transform.forward;float start=Time.time;
                    while(boss.GuardianStepNow!=GuardianStep.Rest&&!StageManager.CombatSuspended){if(pattern==GuardianPattern.Sweep||pattern==GuardianPattern.GateCharge||pattern==GuardianPattern.ShieldPush)if(Vector3.Angle(forward,boss.transform.forward)>.1f)Log("FAIL locked direction");yield return null;}
                    Log("pattern="+pattern+" elapsed="+(Time.time-start).ToString("F2")+" living="+PartyLife.OrderedMembers().FindAll(CombatRange.LivingPartyMember).Count+" humanHp="+player.currentHp);
                    yield return new WaitForSeconds(1);
                }
                drive=false;
                if (!StageManager.CombatSuspended)
                {
                    yield return ForcedHit(GuardianPattern.ShieldPush);
                    yield return ForcedHit(GuardianPattern.LineFracture);
                }
                Log("GUIDED complete combatSeconds="+(Time.time-began).ToString("F2"));
            }
            else
            {
                while(!StageManager.CombatSuspended && Time.time-began<420)yield return null;
                Log("NORMAL combatSeconds="+(Time.time-began).ToString("F2")+" bossHp="+boss.currentHp+" humanHp="+player.currentHp+" phase="+stage.Progress.Phase);
            }
            drive=false;Time.timeScale=1;
            Log("pattern counts/hits: "+boss.GuardianRunReport);
            float settleUntil=Time.realtimeSinceStartup+20;
            while(boss.currentHp<=0 && stage.Progress.Phase==RaidBattlePhase.BossClear && Time.realtimeSinceStartup<settleUntil)yield return null;
            Log("post cutscene phase="+stage.Progress.Phase+" portal="+(RaidApproachField.Instance?.PortalActive==true));
            Log("remaining aoe="+FindObjectsByType<AoETelegraph>(FindObjectsSortMode.None).Length);
            Finish();
        }
        private IEnumerator ForcedHit(GuardianPattern pattern)
        {
            player.currentHp=player.maxHp; // Development command in isolated profile, never normal combat.
            typeof(BossController).GetMethod("BeginGuardian",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(boss,new object[]{pattern,RaidCombatAuthority.ServerTime});
            if(pattern==GuardianPattern.LineFracture)
            {
                while(boss.GuardianStepNow!=GuardianStep.Locked && !StageManager.CombatSuspended)yield return null;
            }
            var aoe=boss.ActiveTelegraph;
            if(aoe==null){Log("FAIL forced hit no telegraph "+pattern);yield break;}
            float distance=Mathf.Min(6,aoe.outerRadius*.5f);
            var point=aoe.transform.position+aoe.transform.forward*distance;point.y=player.transform.position.y;
            player.transform.position=point;
            float before=player.currentHp;
            while(boss.GuardianStepNow!=GuardianStep.Rest && !StageManager.CombatSuspended)yield return null;
            float after=player.currentHp;
            yield return new WaitForSeconds(.5f);
            float displacement=BattleResurrection.FlatDistance(point,player.transform.position);
            Log("DEV forced "+pattern+" hp="+before.ToString("F1")+"->"+after.ToString("F1")+
                " movement="+displacement.ToString("F2")+" hit="+(after<before));
        }
        private void Update()
        {
            if(!drive||boss==null||player.IsDead||StageManager.CombatSuspended)return;
            if (boss.GuardianPatternNow!=observedPattern || boss.GuardianStepNow!=observedStep)
            {
                if (observedStep==GuardianStep.Telegraph && observedPattern!=GuardianPattern.None)
                    Log("TIMING "+observedPattern+" telegraph="+(RaidCombatAuthority.ServerTime-observedAt).ToString("F2"));
                observedPattern=boss.GuardianPatternNow; observedStep=boss.GuardianStepNow;
                observedAt=RaidCombatAuthority.ServerTime;lockedForward=boss.transform.forward;
            }
            if ((observedPattern==GuardianPattern.Sweep||observedPattern==GuardianPattern.ShieldPush||
                observedPattern==GuardianPattern.LineFracture&&observedStep==GuardianStep.Locked) &&
                Vector3.Angle(lockedForward,boss.transform.forward)>.2f)Log("FAIL direction shifted "+observedPattern);
            Vector3 desired;
            bool mechanic=boss.GuardianDestination(player.transform,role,out desired);
            if(!mechanic)desired=CombatRange.AttackPosition(player.transform,boss.transform,role==1?boss.transform.forward:-boss.transform.forward,actor.PrimaryRange);
            // Explicit scripted input substitute, bounded by the selected character's actual speed.
            if(player.CanMoveVoluntarily)player.transform.position=Vector3.MoveTowards(player.transform.position,desired,player.moveSpeed*Time.deltaTime);
            Vector3 facing=boss.transform.position-player.transform.position;facing.y=0;
            if(facing.sqrMagnitude>.01f)player.transform.rotation=Quaternion.RotateTowards(player.transform.rotation,Quaternion.LookRotation(facing),360*Time.deltaTime);
            if(player.HpRatio<.45f)actor.Execute(CharacterSlot.Potion,player.transform,out _);
            if(role==2){var lowest=PartyAiTactics.LowestHealth(out float ratio);if(lowest!=null&&ratio<.8f){actor.Execute(CharacterSlot.Job1,lowest,out _);actor.Execute(CharacterSlot.Job2,lowest,out _);}}
            if(boss.GuardianPatternNow==GuardianPattern.Punishment&&role==1)actor.Execute(CharacterSlot.Job1,player.transform,out _);
            if(boss.GuardianStepNow==GuardianStep.CounterWindow)actor.Execute(CharacterSlot.Counter,boss.transform,out _);
            if(!mechanic||boss.GuardianPatternNow==GuardianPattern.Resonance){actor.Execute(CharacterSlot.Gcd1,boss.transform,out _);actor.Execute(CharacterSlot.Gcd2,boss.transform,out _);actor.Execute(CharacterSlot.OffGcd1,boss.transform,out _);}
        }
        private void Finish(){Time.timeScale=1;EditorApplication.ExitPlaymode();}
    }
}
#endif
