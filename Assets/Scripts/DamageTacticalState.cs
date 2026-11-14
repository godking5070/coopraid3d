using System;
using System.Linq;

namespace RaidRogue
{
    [Serializable] public sealed class DamageWeaknessSnapshot
    {
        public int[] targets=Array.Empty<int>();
        public DamageWeaknessSnapshot Copy()=>new DamageWeaknessSnapshot{targets=(int[])targets.Clone()};
    }
    // A bounded pool of opportunities belonging to the enemy that granted them.
    // Position is checked on the finisher; spending does not check position again.
    public sealed class DamageWeaknessState
    {
        private DamageWeaknessSnapshot _data=new DamageWeaknessSnapshot();
        public DamageWeaknessSnapshot Snapshot=>_data.Copy();
        public int Count(int target)=>_data.targets.Count(t=>t==target);
        public bool Grant(int target,bool flankOrRear,bool directional,int maximum)
        {
            if(target==0||directional&&!flankOrRear||maximum<1||maximum>3||_data.targets.Length>=maximum)return false;
            _data.targets=_data.targets.Concat(new[]{target}).ToArray();return true;
        }
        public bool Consume(int target)
        {
            int index=Array.IndexOf(_data.targets,target);if(index<0)return false;
            _data.targets=_data.targets.Where((t,n)=>n!=index).ToArray();return true;
        }
        public void RemoveEnemy(int target)=>_data.targets=_data.targets.Where(t=>t!=target).ToArray();
        public void Prune(Func<int,bool> livingEnemy)
        {
            if(livingEnemy==null)return;
            foreach(int target in _data.targets.Distinct().ToArray())
                if(!livingEnemy(target))RemoveEnemy(target);
        }
        public bool Restore(DamageWeaknessSnapshot v,int maximum)
        {
            if(v?.targets==null||maximum<1||maximum>3||v.targets.Length>maximum||v.targets.Any(t=>t==0))return false;
            _data=v.Copy();return true;
        }
    }
    public enum DamageSealTrigger { CastEnd=1, PatternMoveEnd=2 }
    [Serializable] public sealed class DamageSealSnapshot
    {
        public int target;
        public DamageSealTrigger trigger;
        public float remaining;
        public DamageSealSnapshot Copy()=>(DamageSealSnapshot)MemberwiseClone();
    }
    public readonly struct DamageSealResolution
    {
        public readonly int Target;
        public readonly DamageSealTrigger Trigger;
        public readonly bool Enhanced;
        public DamageSealResolution(int target,DamageSealTrigger trigger,bool enhanced){Target=target;Trigger=trigger;Enhanced=enhanced;}
    }
    public sealed class DamageSealState
    {
        private DamageSealSnapshot _data=new DamageSealSnapshot();
        public DamageSealSnapshot Snapshot=>_data.Copy();
        public bool Active=>_data.target!=0;
        public bool Attach(int target,DamageSealTrigger trigger,float duration)
        {
            if(Active||target==0||!ValidTrigger(trigger)||!DamageTime.Positive(duration))return false;
            _data=new DamageSealSnapshot{target=target,trigger=trigger,remaining=duration};return true;
        }
        // Adapter emits semantic boss events. Tank-follow walking is NOT a
        // PatternMoveEnd event. A successful counter IS a CastEnd event.
        public bool OnActionEnded(int target,DamageSealTrigger trigger,out DamageSealResolution result)
        {
            result=default;if(!Active||target!=_data.target||trigger!=_data.trigger)return false;
            result=Resolve(true);return true;
        }
        public bool Tick(float delta,out DamageSealResolution result)
        {
            result=default;if(!Active||!DamageTime.Positive(delta))return false;
            _data.remaining=Math.Max(0,_data.remaining-delta);if(_data.remaining>0)return false;
            result=Resolve(false);return true;
        }
        private DamageSealResolution Resolve(bool enhanced)
        {
            var result=new DamageSealResolution(_data.target,_data.trigger,enhanced);
            _data=new DamageSealSnapshot();return result; // consume before callbacks
        }
        public bool Restore(DamageSealSnapshot v)
        {
            if(v==null||!DamageTime.Nonnegative(v.remaining)||
                (v.target==0?(v.remaining!=0||v.trigger!=0):(v.remaining<=0||!ValidTrigger(v.trigger))))return false;
            _data=v.Copy();return true;
        }
        private static bool ValidTrigger(DamageSealTrigger t)=>t==DamageSealTrigger.CastEnd||t==DamageSealTrigger.PatternMoveEnd;
    }
    [Serializable] public sealed class DamageGrenadeSnapshot
    {
        public int target;
        public float remaining,lifetime,adjustment;
        public DamageGrenadeSnapshot Copy()=>(DamageGrenadeSnapshot)MemberwiseClone();
    }
    public sealed class DamageGrenadeState
    {
        private DamageGrenadeSnapshot _data=new DamageGrenadeSnapshot();
        public DamageGrenadeSnapshot Snapshot=>_data.Copy();
        public bool Active=>_data.target!=0;
        public bool Attach(int target,float timer,float maximumLifetime,float adjustment)
        {
            if(Active||target==0||!DamageTime.Positive(timer)||!DamageTime.Positive(maximumLifetime)||timer>maximumLifetime||!DamageTime.Positive(adjustment))return false;
            _data=new DamageGrenadeSnapshot{target=target,remaining=timer,lifetime=maximumLifetime,adjustment=adjustment};return true;
        }
        public float Predicted(int chain)=>!Active?0:chain==1?Math.Max(0,_data.remaining-_data.adjustment):chain==2?Math.Min(_data.lifetime,_data.remaining+_data.adjustment):_data.remaining;
        // No manual R detonation API. Only a valid finisher changes this timer.
        public bool CompleteCombo(int chain,out int detonatedTarget)
        {
            detonatedTarget=0;if(!Active||(chain!=1&&chain!=2))return false;
            _data.remaining=Predicted(chain);if(_data.remaining==0)detonatedTarget=Resolve();return true;
        }
        public bool Tick(float delta,out int detonatedTarget)
        {
            detonatedTarget=0;if(!Active||!DamageTime.Positive(delta))return false;
            _data.lifetime=Math.Max(0,_data.lifetime-delta);_data.remaining=Math.Max(0,Math.Min(_data.remaining-delta,_data.lifetime));
            if(_data.remaining>0)return false;
            detonatedTarget=Resolve();return true;
        }
        private int Resolve(){int target=_data.target;_data=new DamageGrenadeSnapshot();return target;}
        public bool Restore(DamageGrenadeSnapshot v)
        {
            if(v==null||!DamageTime.Nonnegative(v.remaining)||!DamageTime.Nonnegative(v.lifetime)||!DamageTime.Nonnegative(v.adjustment)||
                (v.target==0?(v.remaining!=0||v.lifetime!=0||v.adjustment!=0):(v.remaining<=0||v.lifetime<v.remaining||v.adjustment<=0)))return false;
            _data=v.Copy();return true;
        }
    }
}
