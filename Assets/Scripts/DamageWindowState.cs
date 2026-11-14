using System;

namespace RaidRogue
{
    [Serializable] public sealed class DamageBurstSnapshot
    {
        public int bundles;
        public bool precise;
        public float remaining,inputDelay;
        public DamageBurstSnapshot Copy()=>(DamageBurstSnapshot)MemberwiseClone();
    }
    public sealed class DamageBurstState
    {
        private DamageBurstSnapshot _data=new DamageBurstSnapshot();
        public DamageBurstSnapshot Snapshot=>_data.Copy();
        public bool Active=>_data.bundles>0;
        public bool Begin(float duration,float inputDelay)
        {
            if(Active||!DamageTime.Positive(duration)||!DamageTime.Nonnegative(inputDelay))return false;
            // Caller fires the first bundle immediately. Only two future rights.
            _data=new DamageBurstSnapshot{bundles=2,remaining=duration,inputDelay=inputDelay};return true;
        }
        public void CompleteCombo(){if(Active)_data.precise=true;}
        public bool Fire(float delay,out bool strengthened,out bool final)
        {
            strengthened=final=false;if(!Active||_data.inputDelay>0||!DamageTime.Nonnegative(delay))return false;
            strengthened=_data.precise;_data.precise=false;_data.bundles--;final=_data.bundles==0;
            if(final)_data=new DamageBurstSnapshot();else _data.inputDelay=delay;
            return true;
        }
        public void Tick(float delta)
        {
            if(!Active||!DamageTime.Positive(delta))return;
            _data.remaining=Math.Max(0,_data.remaining-delta);_data.inputDelay=Math.Max(0,_data.inputDelay-delta);
            if(_data.remaining==0)_data=new DamageBurstSnapshot();
        }
        public bool Restore(DamageBurstSnapshot v)
        {
            if(v==null||v.bundles<0||v.bundles>2||!DamageTime.Nonnegative(v.remaining)||!DamageTime.Nonnegative(v.inputDelay)||
                (v.bundles==0?(v.precise||v.remaining!=0||v.inputDelay!=0):v.remaining<=0))return false;
            _data=v.Copy();return true;
        }
    }
    [Serializable] public sealed class DamageSeasonSnapshot
    {
        public int season;
        public float remaining=8,period=8;
        public bool delayed;
        public DamageSeasonSnapshot Copy()=>(DamageSeasonSnapshot)MemberwiseClone();
    }
    public sealed class DamageSeasonState
    {
        private DamageSeasonSnapshot _data=new DamageSeasonSnapshot();
        public DamageSeasonSnapshot Snapshot=>_data.Copy();
        public void Reset(float period)
        {if(DamageTime.Positive(period))_data=new DamageSeasonSnapshot{remaining=period,period=period};}
        public int Advance()
        {
            int fired=_data.season;_data.season=(_data.season+1)%4;_data.remaining=_data.period;_data.delayed=false;return fired;
        }
        public bool Delay(float seconds)
        {
            if(_data.delayed||!DamageTime.Positive(seconds)||!CharacterDefinitionValidation.Finite(_data.remaining+seconds))return false;
            _data.delayed=true;_data.remaining+=seconds;return true;
        }
        public void Tick(float delta)
        {
            if(!DamageTime.Positive(delta))return;
            if(delta<_data.remaining){_data.remaining-=delta;return;}
            double leftover=(double)delta-_data.remaining;
            double visits=Math.Floor(leftover/_data.period)+1;
            _data.season=(_data.season+(int)(visits%4))%4;
            _data.remaining=(float)(_data.period-leftover%_data.period);_data.delayed=false;
        }
        public bool Restore(DamageSeasonSnapshot v)
        {
            if(v==null||v.season<0||v.season>3||!DamageTime.Positive(v.period)||!DamageTime.Positive(v.remaining)||!v.delayed&&v.remaining>v.period)return false;
            _data=v.Copy();return true;
        }
    }
    [Serializable] public sealed class DamageInstantSnapshot
    {
        public int rights;
        public bool enabled;
        public float remaining,cooldown,toggleDelay;
        public DamageInstantSnapshot Copy()=>(DamageInstantSnapshot)MemberwiseClone();
    }
    public sealed class DamageInstantState
    {
        private DamageInstantSnapshot _data=new DamageInstantSnapshot();
        public DamageInstantSnapshot Snapshot=>_data.Copy();
        public bool Active=>_data.rights>0;
        public bool CanConsume=>Active&&_data.enabled;
        public bool Begin(int rights,float duration,float cooldown)
        {
            if(Active||_data.cooldown>0||rights<1||rights>8||!DamageTime.Positive(duration)||!DamageTime.Nonnegative(cooldown))return false;
            _data=new DamageInstantSnapshot{rights=rights,remaining=duration,cooldown=cooldown,enabled=true};return true;
        }
        // Its own input gate; preparation cooldown must never lock this switch.
        public bool Toggle(float delay)
        {
            if(!Active||_data.toggleDelay>0||!DamageTime.Nonnegative(delay))return false;
            _data.enabled=!_data.enabled;_data.toggleDelay=delay;return true;
        }
        public bool Consume(bool validSecondCast)
        {
            if(!validSecondCast||!CanConsume)return false;
            _data.rights--;if(_data.rights==0)ClearRights();return true;
        }
        private void ClearRights(){_data.rights=0;_data.enabled=false;_data.remaining=_data.toggleDelay=0;}
        public void Tick(float delta)
        {
            if(!DamageTime.Positive(delta))return;
            _data.cooldown=Math.Max(0,_data.cooldown-delta);_data.toggleDelay=Math.Max(0,_data.toggleDelay-delta);
            if(!Active)return;_data.remaining=Math.Max(0,_data.remaining-delta);if(_data.remaining==0)ClearRights();
        }
        public bool Restore(DamageInstantSnapshot v)
        {
            if(v==null||v.rights<0||v.rights>8||!DamageTime.Nonnegative(v.remaining)||!DamageTime.Nonnegative(v.cooldown)||!DamageTime.Nonnegative(v.toggleDelay)||
                (v.rights==0?(v.enabled||v.remaining!=0||v.toggleDelay!=0):v.remaining<=0))return false;
            _data=v.Copy();return true;
        }
    }
}
