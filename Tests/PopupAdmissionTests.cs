using System;
using System.Collections.Generic;
using System.Globalization;
namespace RaidRogue
{
    struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public float sqrMagnitude=>x*x+y*y+z*z;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    struct Color { }
    static class Time { public static int frameCount; }
    static class Random { public static float Range(float a,float b)=>a; }
    static class ViewModeManager { public static bool Is2DViewActive; }
    static class RaidCombatAuthority { public static bool Host=true; }
    class RaidNetworkSession
    {
        public static readonly RaidNetworkSession Instance=new RaidNetworkSession();
        public int broadcasts;
        public bool lastImportant;
        public void BroadcastEffect(Vector3 a,Vector3 b,Color c,string text,bool large,int kind,bool important=false){broadcasts++;lastImportant=important;}
    }
    class Transform { public Vector3 position; }
    class GameObject
    {
        public Transform transform=new Transform();
        public GameObject(string name){}
        public FloatingTextPopup AddComponent<T>()=>new FloatingTextPopup();
    }
    class FloatingTextPopup
    {
        public Transform transform=new Transform();
        private float _elapsed;
        /* ACTUAL_STATE */
        /* ACTUAL_SPAWN */
        /* ACTUAL_PLACE_NOTICE */
        private void Initialize(string message,Color color,bool crit){}
        private void PlaceDamage(Vector3 origin){_damageOrigin=origin;DamagePopups.Add(this);}
        public void Disable(){OnDisable();}
        public void Age(float elapsed){_elapsed=elapsed;}
        public int NoticeLane=>_noticeLane;
        public static void Reset(){DamagePopups.Clear();NoticePopups.Clear();_spawnFrame=-1;Time.frameCount++;}
    }
    static class PopupAdmissionTests
    {
        static int checks;
        static void Check(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
        static FloatingTextPopup Spawn(string text)=>FloatingTextPopup.Spawn(new Vector3(),text,new Color(),false);
        static void Main()
        {
            FloatingTextPopup.Reset();
            for(int i=0;i<12;i++)Check(Spawn("100")!=null,"numeric frame budget");
            Check(Spawn("100")==null,"numeric burst bounded");
            Check(Spawn("COUNTER!")!=null,"damage burst must not consume notice budget");
            Time.frameCount++;
            int sentBefore=RaidNetworkSession.Instance.broadcasts;
            Check(Spawn("100")==null,"nearby occupied lanes remain bounded across frames");
            Check(RaidNetworkSession.Instance.broadcasts==sentBefore,"suppressed overflow not broadcast");
            for(int frame=0;frame<3;frame++){Time.frameCount++;for(int i=0;i<12;i++)Check(FloatingTextPopup.Spawn(new Vector3((frame+1)*10,0,0),"100",new Color(),false)!=null,"fill distant active damage");}
            Time.frameCount++;
            Check(Spawn("100")==null,"active damage bounded");
            Check(Spawn("RESCUE")!=null,"active damage cap must not consume notice slots");
            FloatingTextPopup.Reset();
            var first=Spawn("buff");int sent=RaidNetworkSession.Instance.broadcasts;
            Check(Spawn("buff")==null,"same nearby pulse coalesced");
            Check(RaidNetworkSession.Instance.broadcasts==sent,"duplicate not broadcast");
            first.Age(.36f);Check(Spawn("buff")!=null,"later pulse can be shown");
            first.Disable();first.Disable();Check(Spawn("different")!=null,"cleanup is idempotent");
            FloatingTextPopup.Reset();
            var party=FloatingTextPopup.SpawnPartyNotice(new Vector3(),"party",new Color());
            Check(party!=null,"first party pulse");
            Check(FloatingTextPopup.SpawnPartyNotice(new Vector3(100,0,0),"party",new Color())==null,"party pulse coalesces across owners");
            FloatingTextPopup.Reset();
            var notices=new List<FloatingTextPopup>();
            for(int i=0;i<3;i++)notices.Add(Spawn("n"+i));
            Check(Spawn("overflow")==null,"notice per-frame cap");
            Time.frameCount++;
            for(int i=3;i<6;i++)notices.Add(Spawn("n"+i));
            Time.frameCount++;Check(Spawn("overflow")==null,"notice active cap");
            var critical=FloatingTextPopup.Spawn(new Vector3(),"COUNTER",new Color(),true,true);
            Check(critical!=null&&critical.NoticeLane==0,"critical has reserved top lane when ordinary slots full");
            Check(RaidNetworkSession.Instance.lastImportant,"priority sent to replicas");
            Check(FloatingTextPopup.Spawn(new Vector3(),"COUNTER",new Color(),true,true)==null,"critical duplicate coalesced");
            var second=FloatingTextPopup.Spawn(new Vector3(),"DANGER",new Color(),true,true);
            Check(second!=null&&second.NoticeLane==1,"second critical lane independent");
            Check(FloatingTextPopup.Spawn(new Vector3(),"third",new Color(),true,true)==null,"critical burst bounded");
            Time.frameCount++;
            Check(FloatingTextPopup.Spawn(new Vector3(),"third",new Color(),true,true)==null,"critical active cap bounded");
            critical.Disable();
            Check(FloatingTextPopup.Spawn(new Vector3(),"replacement critical",new Color(),true,true)?.NoticeLane==0,"released critical lane reused");
            notices[0].Disable();Check(Spawn("replacement")!=null,"released notice slot reused");
            Check(notices[1].NoticeLane>=2,"ordinary notices do not occupy critical lanes");
            FloatingTextPopup.Reset();
            var hit=Spawn("100");
            for(int i=1;i<12;i++)Spawn("100");
            Time.frameCount++;hit.Disable();
            Check(Spawn("100")!=null,"released nearby damage capacity reused");
            FloatingTextPopup.Reset();
            for(int i=0;i<3;i++)Spawn("buff"+i);
            Check(FloatingTextPopup.Spawn(new Vector3(),"urgent",new Color(),true,true)!=null,"ordinary frame cap cannot consume critical budget");
            FloatingTextPopup.Reset();RaidCombatAuthority.Host=false;
            int beforeReplica=RaidNetworkSession.Instance.broadcasts;
            Check(FloatingTextPopup.Spawn(new Vector3(),"replicated critical",new Color(),true,true)?.NoticeLane==0,"replica retains critical presentation lane");
            Check(RaidNetworkSession.Instance.broadcasts==beforeReplica,"replica presentation never rebroadcasts");
            RaidCombatAuthority.Host=true;
            Console.WriteLine("PASS: popup admission checks "+checks);
        }
    }
}
