using System;
using System.Linq;
using System.Collections.Generic;
using RaidRogue;
sealed class GameObject
{
    public CharacterActor actor;public NineKeyDamageKit damage;
    public T GetComponent<T>() where T:class=>typeof(T)==typeof(CharacterActor)?actor as T:damage as T;
    public T AddComponent<T>() where T:class,new(){var v=new T();if(v is NineKeyDamageKit k){damage=k;k.go=this;k.Initialize();}return v;}
}
sealed class Transform {public int position;}
static class RaidCombatAuthority {public static bool Replica;}
sealed class CampaignRuntime {public static CampaignRuntime Instance=new CampaignRuntime();public bool BuildLocked=true,Restoring=true,ResumingSettlement;}
static class CharacterCatalog {public static CharacterDefinition current;public static CharacterDefinition Find(string id)=>current?.id==id?current:null;}
enum PlayerRole {Tank}
sealed class PlayerController
{
 public float maxHp,currentHp,moveSpeed,combatRadius;public string playerRoleDisplayName;public PlayerController cardManager;
 public void ApplyRoleStatsAndVisuals(PlayerRole role,bool reset){}public void RestoreCampaignCards(CampaignCardStack[] cards){}
}
sealed class PartyBotAI {public float currentHp,maxHp;}
sealed class NetworkRaidAvatar {public int Id;public bool IsLocal;public void Configure(int id,bool local,float radius){}}
static class Mathf {public static float Clamp01(float f)=>Math.Max(0,Math.Min(1,f));}
sealed class CharacterPartyStats {public void ResetFactors(){}}
sealed class CombatStatusEffects {public void Clear(){}}
sealed class AegisPaladin {public void ReleaseRouteEffects(){} public void RecalculateStats(){}public static void Ensure(GameObject go){}}
sealed class CharacterKit {public void Clear(){}public void Recalculate(){}public static void Ensure(GameObject go){}}
sealed class NineKeyTankKit {public static bool Supports(CharacterDefinition d)=>false;public static NineKeyTankKit Ensure(GameObject go)=>new NineKeyTankKit();public void Clear(){}public void Recalculate(){}}
sealed class NineKeyHealerKit {public static bool Supports(CharacterDefinition d)=>false;public static NineKeyHealerKit Ensure(GameObject go)=>new NineKeyHealerKit();public void Clear(){}}
sealed class NineKeyDamageKit
{
 public GameObject go;CharacterActor _actor;public int clears;public string core,route;
 public static bool Supports(CharacterDefinition d)=>NineKeyDamageData.Supports(CharacterRosterData.Find(d?.id));
 public static NineKeyDamageKit Ensure(GameObject go)=>go.GetComponent<NineKeyDamageKit>()??go.AddComponent<NineKeyDamageKit>();
 T GetComponent<T>() where T:class=>go.GetComponent<T>();
 public void Initialize()=>Awake();bool Execute(CharacterSkillDefinition skill,Transform target)=>true;
 /* ACTUAL_DAMAGE_AWAKE */
 public void Clear(){clears++;core=_actor._state.Snapshot.coreTrait;route=_actor._state.Snapshot.routeId;}
}
sealed class CharacterActor
{
 public GameObject gameObject;public CharacterRuntimeState _state;public int _role,observations;
 public Dictionary<string,Func<CharacterSkillDefinition,Transform,bool>> handlers=new Dictionary<string,Func<CharacterSkillDefinition,Transform,bool>>();
 public PlayerController _player;public Transform transform=new Transform();int _previous;object _singleCastTarget;bool _identityHeld;
 T GetComponent<T>() where T:class=>gameObject.GetComponent<T>();void ObserveResources(){observations++;}
 public void RegisterHandler(string key,Func<CharacterSkillDefinition,Transform,bool> handler)=>handlers[key]=handler;
 /* ACTUAL_CAMPAIGN_RESTORE */
}
static class DamageCampaignRestoreTests
{
 static int checks;static void Check(bool condition,string why){checks++;if(!condition)throw new Exception(why);}
 static void Main()
 {
  foreach(var job in CharacterRosterData.Jobs.Where(j=>j.index>=6&&j.index<=11))for(int r=1;r<=3;r++)
  {
   var d=NineKeyDamageData.Build(job);CharacterCatalog.current=d;
   var saved=new CharacterRuntimeState(d);saved.ChooseRoute(d.routes[r-1].id,true);
   var slot=new CampaignPartySlot{character=saved.Snapshot};
   var go=new GameObject();var actor=new CharacterActor{gameObject=go};go.actor=actor;
   Check(actor.RestoreCampaignBuild(slot),"fresh actor restores DPS saved build");
   Check(go.damage!=null,"fresh restoration creates DPS kit");
   Check(actor.handlers.ContainsKey(NineKeyDamageData.Handler),"real Awake registers damage execution handler");
   Check(actor.handlers[NineKeyDamageData.Handler](d.skills.First(),null),"restored handler callable");
   Check(go.damage.core==saved.Snapshot.coreTrait&&go.damage.route==saved.Snapshot.routeId,"kit clear sees restored build");
   var original=go.damage;Check(actor.RestoreCampaignBuild(slot)&&go.damage==original,"repeat restore reuses kit");
   saved.ChangeLoadout(NineKeyDamageData.Id(job,"loadout.core"+r),NineKeyDamageData.Core(job,r,1),true);
   int before=go.damage.clears;Check(actor.RestoreCampaignState(saved.Snapshot),"stage boundary state restored");
   Check(go.damage.clears==before+1&&go.damage.core==saved.Snapshot.coreTrait,"boundary clears kit after runtime restore");
   RaidCombatAuthority.Replica=true;Check(!actor.RestoreCampaignBuild(slot),"replica cannot restore campaign build");RaidCombatAuthority.Replica=false;
   CampaignRuntime.Instance.BuildLocked=false;Check(!actor.RestoreCampaignBuild(slot),"restore requires campaign lock");CampaignRuntime.Instance.BuildLocked=true;
  }
  Console.WriteLine("Damage campaign restore checks PASS: "+checks);
 }
}
