using System;
using System.Linq;

namespace RaidRogue
{
    [Serializable] public sealed class DamageArrowPair
    {
        public int id,target,curveStep,straightStep;
        public float remaining;
        public DamagePoint origin;
        public float range=20;
        public string curveSkill="",straightSkill="";
        public DamageArrowPair Copy()=>(DamageArrowPair)MemberwiseClone();
    }
    [Serializable] public sealed class DamageArrowSnapshot
    {
        public int nextId=1,convergence;
        public DamageArrowPair[] pending=Array.Empty<DamageArrowPair>();
        public DamageArrowSnapshot Copy()=>new DamageArrowSnapshot{nextId=nextId,convergence=convergence,pending=pending.Select(p=>p.Copy()).ToArray()};
    }
    public readonly struct DamageArrowImpact
    {
        public readonly DamageArrowPair Pair;
        public readonly bool CurveHit,StraightHit,Converged;
        public DamageArrowImpact(DamageArrowPair pair,bool curve,bool straight){Pair=pair;CurveHit=curve;StraightHit=straight;Converged=curve&&straight&&pair.straightStep>0;}
    }
    // Delayed, manually fired attacks. Store the launch step rather than consulting
    // the actor's later combo index. Automatic bonus damage never enters this queue.
    public sealed class DamageArrowState
    {
        private DamageArrowSnapshot _data=new DamageArrowSnapshot();
        public DamageArrowSnapshot Snapshot=>_data.Copy();
        public bool LaunchCurve(int target,int step,string skill,float delay,DamagePoint origin=default,float range=20)
        {
            if(target==0||step<1||step>3||string.IsNullOrEmpty(skill)||!DamageTime.Positive(delay)||!origin.Valid||!DamageTime.Positive(range)||range>50||_data.pending.Length>=16||_data.nextId==int.MaxValue)return false;
            var pair=new DamageArrowPair{id=_data.nextId++,target=target,curveStep=step,curveSkill=skill,remaining=delay,origin=origin,range=range};
            _data.pending=_data.pending.Concat(new[]{pair}).ToArray();return true;
        }
        public bool ReserveStraight(int target,int step,string skill,float window)
        {
            if(target==0||step<1||step>3||string.IsNullOrEmpty(skill)||!DamageTime.Positive(window))return false;
            var pair=_data.pending.Where(p=>p.target==target&&p.straightStep==0&&p.remaining<=window).OrderBy(p=>p.remaining).ThenBy(p=>p.id).FirstOrDefault();
            if(pair==null)return false;pair.straightStep=step;pair.straightSkill=skill;return true;
        }
        // Hit callback resolves one attack against Pair.target using its saved skill
        // and step. Remove all due entries before invoking callbacks (reentrant safe).
        public DamageArrowImpact[] Tick(float delta,Func<DamageArrowPair,bool,bool> hit)
        {
            if(!DamageTime.Positive(delta)||hit==null)return Array.Empty<DamageArrowImpact>();
            foreach(var pair in _data.pending)pair.remaining=Math.Max(0,pair.remaining-delta);
            var due=_data.pending.Where(p=>p.remaining==0).OrderBy(p=>p.id).ToArray();
            _data.pending=_data.pending.Where(p=>p.remaining>0).ToArray();
            var results=new DamageArrowImpact[due.Length];
            for(int n=0;n<due.Length;n++)
            {
                var pair=due[n];bool curve=hit(pair.Copy(),false),straight=pair.straightStep>0&&hit(pair.Copy(),true);
                results[n]=new DamageArrowImpact(pair.Copy(),curve,straight);
                if(results[n].Converged)_data.convergence=Math.Min(3,_data.convergence+1);
            }
            return results;
        }
        public int ConsumeConvergence(int maximum=3)
        {
            if(maximum<1||maximum>3)return 0;
            int value=Math.Min(maximum,_data.convergence);_data.convergence-=value;return value;
        }
        public bool Restore(DamageArrowSnapshot v)
        {
            if(v?.pending==null||v.pending.Length>16||v.nextId<1||v.convergence<0||v.convergence>3||
                v.pending.Any(p=>p==null||p.id<1||p.id>=v.nextId||p.target==0||p.curveStep<1||p.curveStep>3||p.straightStep<0||p.straightStep>3||
                    string.IsNullOrEmpty(p.curveSkill)||p.straightSkill==null||(p.straightStep>0)!=(p.straightSkill.Length>0)||!DamageTime.Positive(p.remaining)||!p.origin.Valid||!DamageTime.Positive(p.range)||p.range>50)||
                v.pending.Select(p=>p.id).Distinct().Count()!=v.pending.Length)return false;
            _data=v.Copy();return true;
        }
    }
}
