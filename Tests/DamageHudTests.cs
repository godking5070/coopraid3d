using System;
using System.Collections.Generic;

namespace RaidRogue
{
    enum SkillTargetRule { None, Self, LivingEnemy, LivingAlly, DeadAlly }
    enum CharacterSlot { Gcd1, Gcd2, Counter, OffGcd2, Ultimate }
    sealed class Transform
    {
        public bool Enemy,Alive=true;public float Distance;public BossController Boss;
        public PartyLife Life=new PartyLife();
        public T GetComponent<T>() where T:class => typeof(T)==typeof(BossController)?Boss as T:Life as T;
    }
    sealed class BossController { public Transform transform; }
    sealed class PartyLife
    { public bool IsDead;public static List<Transform> OrderedMembers()=>new List<Transform>(); }
    static class CombatRange
    {
        public static bool LivingEnemy(Transform t)=>t!=null&&t.Alive&&t.Enemy;
        public static bool LivingPartyMember(Transform t)=>t!=null&&t.Alive&&!t.Enemy;
        public static bool InRange(Transform source,Transform target,float range)=>target!=null&&target.Distance<=range;
    }
    sealed class StageManager { public static StageManager Instance=new StageManager();public BossController boss; }
    sealed class CharacterSkillDefinition { public SkillTargetRule target;public float range=20; }
    sealed class NineKeyDamageKit
    {
        public bool Active=true;public readonly Dictionary<CharacterSlot,string> Blocked=new Dictionary<CharacterSlot,string>();
        public string Unavailable(CharacterSlot slot)=>Blocked.TryGetValue(slot,out var reason)?reason:"";
    }
    sealed class CharacterActor
    {
        public Transform transform=new Transform();public NineKeyDamageKit Damage=new NineKeyDamageKit();
        public T GetComponent<T>() where T:class =>Damage as T;
        public float EffectiveRange(CharacterSkillDefinition skill)=>skill.range;
    }
    sealed class TargetingManager
    {
        public static TargetingManager Instance=new TargetingManager();
        public Transform _attackTarget,_supportTarget,CurrentTarget;public bool _hasSupportSelection;
        public static bool Visible=true;private static bool BossCanBeTargeted()=>Visible;
        /* ACTUAL_TARGET_METHODS */
    }
    static class CharacterFrameworkUI
    {
        /* ACTUAL_DAMAGE_HUD_METHOD */
    }
    static class DamageHudTests
    {
        static int checks;
        static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        static void Main()
        {
            var actor=new CharacterActor();var tm=TargetingManager.Instance;
            var enemy=new Transform{Enemy=true};var ally=new Transform();
            var offensive=new CharacterSkillDefinition{target=SkillTargetRule.LivingEnemy};
            var support=new CharacterSkillDefinition{target=SkillTargetRule.LivingAlly};
            string Show(CharacterSkillDefinition skill,CharacterSlot slot=CharacterSlot.Counter)=>CharacterFrameworkUI.DamageSlotUnavailable(actor,skill,slot);
            tm._attackTarget=enemy;tm._supportTarget=ally;tm._hasSupportSelection=true;tm.CurrentTarget=ally;
            Check(Show(offensive)=="","friendly current target keeps offensive button available");
            Check(Show(offensive,CharacterSlot.Ultimate)=="","ultimate retains remembered enemy");
            tm.CurrentTarget=enemy;Check(Show(support)=="","enemy current target keeps support button available");
            ally.Alive=false;Check(Show(support).Length>0,"dead explicit support is not silently replaced with self");
            Check(Show(offensive)=="","invalid support does not block offense");
            tm._supportTarget=null;Check(Show(support).Length>0,"removed explicit support remains invalid");
            tm._hasSupportSelection=false;Check(Show(support)=="","unselected support defaults to self");
            enemy.Distance=30;Check(Show(offensive)=="사거리 밖","range follows selected offensive target");enemy.Distance=0;
            var boss=new BossController{transform=new Transform{Enemy=true}};boss.transform.Boss=boss;StageManager.Instance.boss=boss;
            tm._attackTarget=null;Check(Show(offensive)=="","no attack selection uses execution's visible boss fallback");
            TargetingManager.Visible=false;Check(Show(offensive).Length>0,"hidden boss fallback is unavailable");TargetingManager.Visible=true;
            tm._attackTarget=enemy;enemy.Alive=false;Check(Show(offensive).Length>0,"dead explicit enemy does not switch silently to fallback");
            foreach(var slot in new[]{CharacterSlot.Gcd1,CharacterSlot.Gcd2,CharacterSlot.OffGcd2})
            {
                actor.Damage.Blocked[slot]="내부 사용 제한";
                Check(Show(offensive,slot)=="내부 사용 제한","internal ammo/reload/tactical restriction takes precedence over target failure");
                actor.Damage.Blocked.Remove(slot);
            }
            enemy.Alive=true;Check(Show(offensive,CharacterSlot.Gcd1)=="","cleared internal restriction restores button availability");
            tm._hasSupportSelection=true;tm._supportTarget=null;
            Check(Show(new CharacterSkillDefinition{target=SkillTargetRule.Self})=="","personal defense ignores lost support");
            Check(Show(new CharacterSkillDefinition{target=SkillTargetRule.None})=="","untargeted route input ignores lost support");
            TargetingManager.Instance=null;Check(Show(support)=="","missing targeting component uses the same self fallback as RequestSlot");
            Check(Show(offensive).Length>0,"missing targeting component does not invent offensive target");
            actor.Damage.Active=false;Check(Show(offensive)=="","non-DPS UI retains existing policy");
            Console.WriteLine("PASS actual DPS HUD availability and target routing: "+checks+"; native UI rendering remains unverified.");
        }
    }
}
