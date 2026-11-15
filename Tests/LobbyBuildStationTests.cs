using System;
namespace RaidRogue
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 up=>new Vector3(0,1,0);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static bool operator ==(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
        public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);
        public override bool Equals(object value)=>value is Vector3 other&&this==other;
        public override int GetHashCode()=>x.GetHashCode()^y.GetHashCode()^z.GetHashCode();
        public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
    }
    public sealed class Transform { public Vector3 position; }
    public sealed class Player {public readonly Transform transform=new Transform();}
    public sealed class RaidLobbyLayout
    {
        public Vector3 buildStationPosition=new Vector3(-6,0,12),doorPosition=new Vector3(0,1,14.5f);
        public float buildStationRange=3;
        /* ACTUAL_STATION_POSITION */
    }
    public enum RaidSessionMode {Menu,Single,Host,Client,Disconnected}
    public enum RaidBattlePhase {Lobby,Loading,Combat,Reward,Wiped}
    public sealed class Progress {public RaidBattlePhase Phase;}
    public sealed class StageManager
    {
        public static StageManager Instance;public readonly Progress Progress=new Progress();public bool CanSelectInitialRole=true;
    }
    public sealed class RaidNetworkSession
    {
        public static RaidNetworkSession Instance;public static bool Multiplayer;
        public RaidSessionMode Mode=RaidSessionMode.Single;public bool Ending,MenuOpen,ControlsReady=true,CampaignBuildLocked,CanEditLobbyBuild=true;
        public RaidBattlePhase BattlePhase;
    }
    public static class CharacterTrainingSession {public static bool Active;}
    public sealed class CampaignRuntime {public static CampaignRuntime Instance;public bool BuildLocked;}
    public sealed class Definition {public int legacyRole;public object[] routes={new object()};public object[] loadouts={new object()};}
    public sealed class CharacterActor {public Definition Definition=new Definition();public bool CanChangeLoadout=true;}
    public static class LocalInputBindings {public static int quarantines;public static void QuarantineHeld(){quarantines++;}}
    public static class LocalGameplayInput {public static int suppressed;public static void SuppressPointerThisFrame(){suppressed++;}}
    public sealed class RaidLobbyRoom
    {
        public static RaidLobbyRoom Instance;
        public bool _showing=true;public RaidNetworkSession _session;public Player _player=new Player();
        public StageManager _stage;public RaidLobbyLayout _layout=new RaidLobbyLayout();
        /* ACTUAL_ROOM_ACTIVE */
        /* ACTUAL_STATION_ACCESS */
    }
    public sealed class AegisRouteSelectionUI
    {
        public static AegisRouteSelectionUI _instance;
        public CharacterActor _actor;public bool _routesOnly,_jobs=true;public string _pendingJob;public int refreshes;
        public static bool IsOpen;
        public static void Open(CharacterActor actor,bool network){_instance=new AegisRouteSelectionUI{_actor=actor};IsOpen=true;}
        public static void Close(){IsOpen=false;}
        public void RefreshChoices(){refreshes++;}
        public void GoBack()=>Back();
        /* ACTUAL_ROUTE_PERMISSION */
        /* ACTUAL_ROUTE_OPEN */
        /* ACTUAL_ROUTE_BACK */
    }
    public sealed class CharacterFrameworkUI
    {
        public CharacterActor actor=new CharacterActor();public bool _stationRequested;public float _next=1;
        public object _loadoutPanel;public int refreshes;
        private CharacterActor Actor()=>actor;
        private void RefreshLoadout(CharacterActor value){refreshes++;}
        /* ACTUAL_LOADOUT_OPEN */
    }
}
internal static class LobbyBuildStationTests
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    public static int Main()
    {
        try
        {
            RunScenarios();
            Console.WriteLine("PASS: "+checks+" lobby build station access, route permission and selection entry checks using production methods.");
            Console.WriteLine("Boundary: scene objects, rendering, input dispatch and live host execution are not run.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static void RunScenarios()
    {
        Check(!RaidRogue.RaidLobbyRoom.CanUseBuildStation,"no room denies station");
        var stage=new RaidRogue.StageManager();RaidRogue.StageManager.Instance=stage;
        var session=new RaidRogue.RaidNetworkSession();RaidRogue.RaidNetworkSession.Instance=session;
        var room=new RaidRogue.RaidLobbyRoom{_session=session,_stage=stage};RaidRogue.RaidLobbyRoom.Instance=room;
        Check(room._layout.BuildStationPosition==new RaidRogue.Vector3(-6,0,12),"station is beside north entrance");
        Check(RaidRogue.Vector3.Distance(room._layout.BuildStationPosition+RaidRogue.Vector3.up,room._layout.doorPosition)>6.4f,"station and departure interaction ranges do not overlap at default positions");
        room._layout.buildStationPosition=new RaidRogue.Vector3(-7,0,-5);
        Check(room._layout.BuildStationPosition==new RaidRogue.Vector3(-6,0,12),"old runtime station default migrates beside entrance");
        room._layout.buildStationPosition=new RaidRogue.Vector3(8,0,10);
        Check(room._layout.BuildStationPosition==new RaidRogue.Vector3(8,0,10),"custom authored station placement preserved");
        room._layout.buildStationPosition=new RaidRogue.Vector3(-6,0,12);
        room._player.transform.position=new RaidRogue.Vector3(-6,1,12);
        Check(RaidRogue.RaidLobbyRoom.CanUseBuildStation,"nearby player in single lobby can use station");
        room._player.transform.position=new RaidRogue.Vector3(-3,1,12);
        Check(!RaidRogue.RaidLobbyRoom.CanUseBuildStation,"range boundary denies access");
        room._player.transform.position=new RaidRogue.Vector3(-3.1f,1,12);
        Check(RaidRogue.RaidLobbyRoom.CanUseBuildStation,"inside range allows access");
        room._showing=false;Check(!RaidRogue.RaidLobbyRoom.CanUseBuildStation,"hidden room denies access");room._showing=true;
        session.MenuOpen=true;Check(!RaidRogue.RaidLobbyRoom.CanUseBuildStation,"menu overlay denies station");session.MenuOpen=false;
        session.Ending=true;Check(!RaidRogue.RaidLobbyRoom.CanUseBuildStation,"ending session denies station");session.Ending=false;
        session.Mode=RaidRogue.RaidSessionMode.Menu;Check(!RaidRogue.RaidLobbyRoom.CanUseBuildStation,"main menu backdrop is not a usable lobby");session.Mode=RaidRogue.RaidSessionMode.Single;
        RaidRogue.CharacterTrainingSession.Active=true;Check(!RaidRogue.RaidLobbyRoom.CanUseBuildStation,"separate training mode denies station");RaidRogue.CharacterTrainingSession.Active=false;
        foreach(var phase in new[]{RaidRogue.RaidBattlePhase.Loading,RaidRogue.RaidBattlePhase.Combat,RaidRogue.RaidBattlePhase.Reward,RaidRogue.RaidBattlePhase.Wiped})
        {stage.Progress.Phase=phase;Check(!RaidRogue.RaidLobbyRoom.CanUseBuildStation,"single non-lobby denies: "+phase);}
        stage.Progress.Phase=RaidRogue.RaidBattlePhase.Lobby;
        RaidRogue.RaidNetworkSession.Multiplayer=true;
        foreach(var mode in new[]{RaidRogue.RaidSessionMode.Host,RaidRogue.RaidSessionMode.Client})
        {
            session.Mode=mode;Check(RaidRogue.RaidLobbyRoom.CanUseBuildStation,"loaded network lobby can use station: "+mode);
            session.ControlsReady=false;Check(!RaidRogue.RaidLobbyRoom.CanUseBuildStation,"unloaded network actor denied");session.ControlsReady=true;
            session.BattlePhase=RaidRogue.RaidBattlePhase.Combat;Check(!RaidRogue.RaidLobbyRoom.CanUseBuildStation,"network combat overrides stale local lobby");session.BattlePhase=RaidRogue.RaidBattlePhase.Lobby;
        }
        RaidRogue.RaidNetworkSession.Multiplayer=false;session.Mode=RaidRogue.RaidSessionMode.Single;
        var actor=new RaidRogue.CharacterActor();
        Check(RaidRogue.AegisRouteSelectionUI.CanEditCurrentRoute(actor,false),"initial route edit permitted");
        stage.CanSelectInitialRole=false;Check(!RaidRogue.AegisRouteSelectionUI.CanEditCurrentRoute(actor,false),"route fixed after first departure");stage.CanSelectInitialRole=true;
        RaidRogue.CampaignRuntime.Instance=new RaidRogue.CampaignRuntime{BuildLocked=true};
        Check(!RaidRogue.AegisRouteSelectionUI.CanEditCurrentRoute(actor,false),"saved campaign route remains locked");RaidRogue.CampaignRuntime.Instance.BuildLocked=false;
        session.CampaignBuildLocked=true;Check(!RaidRogue.AegisRouteSelectionUI.CanEditCurrentRoute(actor,true),"network saved build remains locked");session.CampaignBuildLocked=false;
        session.CanEditLobbyBuild=false;Check(!RaidRogue.AegisRouteSelectionUI.CanEditCurrentRoute(actor,true),"network route permission belongs to existing lobby gate");session.CanEditLobbyBuild=true;
        actor.Definition.legacyRole=1;Check(!RaidRogue.AegisRouteSelectionUI.CanEditCurrentRoute(actor,false),"legacy actor must select a roster job first");actor.Definition.legacyRole=0;
        Check(!RaidRogue.AegisRouteSelectionUI.CanEditCurrentRoute(null,false),"missing local actor denied");
        Check(RaidRogue.AegisRouteSelectionUI.OpenCurrentRoutes(actor,false),"route-only picker opens");
        Check(RaidRogue.AegisRouteSelectionUI._instance._actor==actor&&RaidRogue.AegisRouteSelectionUI._instance._routesOnly&&!RaidRogue.AegisRouteSelectionUI._instance._jobs,"station keeps current job and opens its routes");
        RaidRogue.AegisRouteSelectionUI._instance.GoBack();Check(!RaidRogue.AegisRouteSelectionUI.IsOpen,"back from route-only picker closes instead of selecting another job");
        stage.CanSelectInitialRole=false;Check(!RaidRogue.AegisRouteSelectionUI.OpenCurrentRoutes(actor,false)&&!RaidRogue.AegisRouteSelectionUI.IsOpen,"blocked route cannot reopen picker");stage.CanSelectInitialRole=true;
        room._player.transform.position=new RaidRogue.Vector3(-6,1,12);
        var editor=new RaidRogue.CharacterFrameworkUI();
        Check(editor.OpenLoadoutAtStation()&&editor._stationRequested&&editor.refreshes==1&&editor._next==0,"interaction opens current build window immediately");
        Check(RaidRogue.LocalInputBindings.quarantines==1&&RaidRogue.LocalGameplayInput.suppressed==1,"opening consumes held input and pointer frame");
        room._player.transform.position=new RaidRogue.Vector3(0,1,0);
        Check(!new RaidRogue.CharacterFrameworkUI().OpenLoadoutAtStation(),"remote station open denied");
        room._player.transform.position=new RaidRogue.Vector3(-6,1,12);
        editor=new RaidRogue.CharacterFrameworkUI();editor.actor.CanChangeLoadout=false;
        Check(!editor.OpenLoadoutAtStation()&&!editor._stationRequested,"existing loadout restrictions preserved");
        editor.actor.CanChangeLoadout=true;editor.actor.Definition.loadouts=new object[0];
        Check(!editor.OpenLoadoutAtStation(),"unsupported legacy loadout is not opened");
    }
}
