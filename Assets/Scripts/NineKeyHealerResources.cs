using System;
using System.Linq;

namespace RaidRogue
{
    [Serializable] public sealed class HealerResourceSnapshot
    {
        public int version=1,lastIngredient=2;
        public int[] ingredients=Array.Empty<int>();
        public bool verseReserved;
        public HealerResourceSnapshot Copy(){var c=(HealerResourceSnapshot)MemberwiseClone();c.ingredients=(int[])ingredients.Clone();return c;}
    }
    // The framework resource is the only spendable balance. The array describes
    // its ingredient composition; a reserved verse has already left that balance.
    public sealed class NineKeyHealerResources
    {
        private readonly CharacterRuntimeState _actor;
        private readonly RosterJob _job;
        private HealerResourceSnapshot _data=new HealerResourceSnapshot();
        private int Route=>Array.FindIndex(_actor.Definition.routes,r=>r.id==_actor.RouteId)+1;
        public bool Alchemy=>_job.index==5&&Route==2;
        public HealerResourceSnapshot Snapshot=>_data.Copy();
        public NineKeyHealerResources(CharacterRuntimeState actor,RosterJob job)
        {
            if(!NineKeyHealerData.Supports(job))throw new ArgumentException("healer job");
            _actor=actor;_job=job;
            if(Alchemy)_data.ingredients=Enumerable.Repeat(2,(int)_actor.Resource(job.Resource)).ToArray();
        }
        private bool AddIngredient(int color)
        {
            if(_data.ingredients.Length<3)
            {
                if(!_actor.Gain(_job.Resource,1))return false;
                _data.ingredients=_data.ingredients.Concat(new[]{color}).ToArray();return true;
            }
            int opposite=Array.FindIndex(_data.ingredients,v=>v!=color);
            if(opposite<0)return false;
            _data.ingredients[opposite]=color;return true;
        }
        // Called only after the actor claims the accepted, valid direct finisher.
        // Effective healing and hit count never enter this calculation.
        public void CompleteCombo(int chain)
        {
            if(chain!=1&&chain!=2)return;
            if(Alchemy)
            {
                int color=chain==1?1:2;
                if(AddIngredient(color))_data.lastIngredient=color;
            }
            else _actor.Gain(_job.Resource,_job.index==5?20:1);
            _actor.Gain(NineKeyHealerData.Gauge(_job),10);
        }
        public bool Supply()
        {
            if(_job.index!=5)return false;
            if(Alchemy)return AddIngredient(_data.lastIngredient);
            float before=_actor.Resource(_job.Resource);
            return _actor.Gain(_job.Resource,20)&&_actor.Resource(_job.Resource)>before;
        }
        public bool Prescribe(out int red)
        {
            red=0;
            if(!Alchemy||_data.ingredients.Length!=3||!_actor.Spend(_job.Resource,3))return false;
            red=_data.ingredients.Count(c=>c==1);_data.ingredients=Array.Empty<int>();return true;
        }
        public bool BeginVerse()
        {
            if(_job.index!=4||Route!=3||_data.verseReserved||!_actor.Spend(_job.Resource,1))return false;
            _data.verseReserved=true;return true;
        }
        public bool CompleteVerse()
        {if(!_data.verseReserved)return false;_data.verseReserved=false;return true;}
        public void CancelVerse()
        {if(!_data.verseReserved)return;_data.verseReserved=false;_actor.Gain(_job.Resource,1);}
        public bool Restore(HealerResourceSnapshot value)
        {
            if(value==null||value.version!=1||value.ingredients==null||value.ingredients.Length>3||value.ingredients.Any(c=>c!=1&&c!=2)||value.lastIngredient<1||value.lastIngredient>2)return false;
            if(Alchemy?value.ingredients.Length!=(int)_actor.Resource(_job.Resource):value.ingredients.Length!=0)return false;
            if(value.verseReserved&&(_job.index!=4||Route!=3))return false;
            _data=value.Copy();return true;
        }
    }
}
