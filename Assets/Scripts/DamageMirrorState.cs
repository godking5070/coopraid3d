using System;
using System.Linq;

namespace RaidRogue
{
    [Serializable] public sealed class DamageReturnSnapshot
    {
        public DamagePoint point;
        public float remaining,cooldown;
        public DamageReturnSnapshot Copy()=>(DamageReturnSnapshot)MemberwiseClone();
    }
    public sealed class DamageReturnState
    {
        private DamageReturnSnapshot _data=new DamageReturnSnapshot();
        public DamageReturnSnapshot Snapshot=>_data.Copy();
        public bool Active=>_data.remaining>0;
        public bool Store(DamagePoint point,float duration,float cooldown)
        {
            if(Active||_data.cooldown>0||!point.Valid||!DamageTime.Positive(duration)||!DamageTime.Positive(cooldown))return false;
            _data=new DamageReturnSnapshot{point=point,remaining=duration,cooldown=cooldown};return true;
        }
        public bool Consume(bool destinationAccepted)
        {if(!Active||!destinationAccepted)return false;_data.remaining=0;return true;}
        public void Tick(float delta)
        {
            if(!DamageTime.Positive(delta))return;
            _data.remaining=Math.Max(0,_data.remaining-delta);_data.cooldown=Math.Max(0,_data.cooldown-delta);
        }
        public bool Restore(DamageReturnSnapshot value)
        {
            if(value==null||!value.point.Valid||!DamageTime.Nonnegative(value.remaining)||!DamageTime.Nonnegative(value.cooldown))return false;
            _data=value.Copy();return true;
        }
    }
    [Serializable] public sealed class DamageRecordedAttack
    {
        public string key="";
        public float remaining;
        public DamageRecordedAttack Copy()=>(DamageRecordedAttack)MemberwiseClone();
    }
    [Serializable] public sealed class DamageMemorySnapshot
    {
        public bool locked;
        public DamageRecordedAttack[] records=Array.Empty<DamageRecordedAttack>();
        public DamageMemorySnapshot Copy()=>new DamageMemorySnapshot{locked=locked,records=records.Select(r=>r.Copy()).ToArray()};
    }
    public sealed class DamageMemoryState
    {
        private DamageMemorySnapshot _data=new DamageMemorySnapshot();
        public DamageMemorySnapshot Snapshot=>_data.Copy();
        private static bool Allowed(string key)=>key=="g1"||key=="g20"||key=="g21"||key=="g22";
        // Accept only the actual final hit of manual 1/2. A replay supplies neither
        // a final-damage number nor a new record, resource or recursive copy.
        public bool Record(string key,bool manualFinisherHit,int maximum,float duration)
        {
            if(_data.locked||!manualFinisherHit||!Allowed(key)||maximum<1||maximum>2||!DamageTime.Positive(duration))return false;
            _data.records=_data.records.Concat(new[]{new DamageRecordedAttack{key=key,remaining=duration}}).Reverse().Take(maximum).Reverse().ToArray();return true;
        }
        public void ToggleLock()=>_data.locked=!_data.locked;
        public bool CanReplay(int count)=>count>=1&&count<=2&&_data.records.Length>=count;
        public bool Take(int count,out string[] keys)
        {
            keys=Array.Empty<string>();if(!CanReplay(count))return false;
            keys=_data.records.Take(count).Select(r=>r.key).ToArray();
            _data.records=_data.records.Skip(count).ToArray();_data.locked=false;return true;
        }
        public void Tick(float delta)
        {
            if(!DamageTime.Positive(delta))return;
            foreach(var record in _data.records)record.remaining=Math.Max(0,record.remaining-delta);
            _data.records=_data.records.Where(r=>r.remaining>0).ToArray();
        }
        public bool Restore(DamageMemorySnapshot v,int maximum)
        {
            if(v?.records==null||maximum<1||maximum>2||v.records.Length>maximum||v.records.Any(r=>r==null||!Allowed(r.key)||!DamageTime.Positive(r.remaining)))return false;
            _data=v.Copy();return true;
        }
    }
    [Serializable] public sealed class DamageProjectionSnapshot
    {
        public float remaining;
        public bool usedInCombo,empoweredExecution;
        public DamageProjectionSnapshot Copy()=>(DamageProjectionSnapshot)MemberwiseClone();
    }
    public sealed class DamageProjectionState
    {
        private DamageProjectionSnapshot _data=new DamageProjectionSnapshot();
        public DamageProjectionSnapshot Snapshot=>_data.Copy();
        public bool Active=>_data.remaining>0;
        public bool Begin(float duration)
        {
            if(Active||!DamageTime.Positive(duration))return false;
            _data.remaining=duration;return true;
        }
        // Starting projection is not a replay. The current combo index remains
        // in CharacterRuntimeState and only future launched steps use projection.
        public bool LaunchSecond()
        {
            if(!Active)return false;_data.usedInCombo=true;return true;
        }
        public bool CompleteSecond(bool validFinalHit)
        {
            bool direct=!_data.usedInCombo;
            if(direct&&validFinalHit)_data.empoweredExecution=true; // bounded 1
            _data.remaining=0;_data.usedInCombo=false;
            return direct&&validFinalHit;
        }
        public bool Execute(out bool empowered)
        {
            empowered=false;if(Active)return false;
            empowered=_data.empoweredExecution;_data.empoweredExecution=false;return true;
        }
        public void Tick(float delta)
        {
            if(DamageTime.Positive(delta))_data.remaining=Math.Max(0,_data.remaining-delta);
            // usedInCombo deliberately survives expiry until the combo ends.
        }
        public bool Restore(DamageProjectionSnapshot v)
        {if(v==null||!DamageTime.Nonnegative(v.remaining))return false;_data=v.Copy();return true;}
    }
}
