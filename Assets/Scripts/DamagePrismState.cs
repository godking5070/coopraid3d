using System;
using System.Linq;
using System.Globalization;

namespace RaidRogue
{
    public sealed class DamagePrismCommand
    {
        public int mirrorId;
        public DamagePoint point,normal;
        public bool Valid=>mirrorId>=0&&point.Valid&&normal.Valid&&
            Math.Abs((double)normal.x*normal.x+(double)normal.z*normal.z-1)<.002;
        public string Encode()=>mirrorId.ToString(CultureInfo.InvariantCulture)+"|"+
            point.x.ToString("R",CultureInfo.InvariantCulture)+"|"+point.z.ToString("R",CultureInfo.InvariantCulture)+"|"+
            normal.x.ToString("R",CultureInfo.InvariantCulture)+"|"+normal.z.ToString("R",CultureInfo.InvariantCulture);
        public static bool Decode(string text,out DamagePrismCommand command)
        {
            command=null;if(string.IsNullOrEmpty(text)||text.Length>160)return false;
            var fields=text.Split('|');var culture=CultureInfo.InvariantCulture;
            if(fields.Length!=5||!int.TryParse(fields[0],NumberStyles.None,culture,out int id)||
                !float.TryParse(fields[1],NumberStyles.Float,culture,out float x)||!float.TryParse(fields[2],NumberStyles.Float,culture,out float z)||
                !float.TryParse(fields[3],NumberStyles.Float,culture,out float nx)||!float.TryParse(fields[4],NumberStyles.Float,culture,out float nz))return false;
            var result=new DamagePrismCommand{mirrorId=id,point=new DamagePoint(x,z),normal=new DamagePoint(nx,nz)};
            if(!result.Valid)return false;command=result;return true;
        }
    }
    [Serializable] public sealed class DamageMirror
    {
        public int id;
        public DamagePoint point,normal;
        public float remaining;
        public DamageMirror Copy()=>(DamageMirror)MemberwiseClone();
    }
    [Serializable] public sealed class DamagePrismSnapshot
    {
        public int nextId=1,firstId;
        public float adjustDelay;
        public DamageMirror[] mirrors=Array.Empty<DamageMirror>();
        public DamagePrismSnapshot Copy()=>new DamagePrismSnapshot{nextId=nextId,firstId=firstId,adjustDelay=adjustDelay,mirrors=mirrors.Select(m=>m.Copy()).ToArray()};
    }
    public sealed class DamagePrismState
    {
        private DamagePrismSnapshot _data=new DamagePrismSnapshot();
        public DamagePrismSnapshot Snapshot=>_data.Copy();
        public int ReplacedId(int maximum)=>_data.mirrors.Length>=maximum?_data.mirrors.OrderBy(m=>m.id).First().id:0;
        public bool Place(DamagePoint point,DamagePoint normal,int maximum,float duration)
        {
            if(!point.Valid||!Normalize(normal,out normal)||maximum<1||maximum>2||!DamageTime.Positive(duration)||_data.nextId==int.MaxValue)return false;
            int replace=ReplacedId(maximum);
            var mirror=new DamageMirror{id=_data.nextId++,point=point,normal=normal,remaining=duration};
            _data.mirrors=_data.mirrors.Where(m=>m.id!=replace).Concat(new[]{mirror}).ToArray();
            if(_data.firstId==0||_data.firstId==replace)_data.firstId=mirror.id;return true;
        }
        public bool SelectFirst(int id)
        {if(!_data.mirrors.Any(m=>m.id==id))return false;_data.firstId=id;return true;}
        public bool CanAdjust(int id)=>_data.adjustDelay<=0&&_data.mirrors.Any(m=>m.id==id);
        // One ordinary placement/adjustment per AI input. No free mirrors or
        // lifetime changes; host validation and terrain clipping remain outside.
        public DamagePrismCommand Plan(DamagePoint origin,DamagePoint target,int maximum)
        {
            if(maximum<1||maximum>2||!origin.Valid||!target.Valid||!Normalize(Subtract(target,origin),out var forward))return null;
            float distance=Length(Subtract(target,origin));if(distance<1||distance>20)return null;
            var side=new DamagePoint(-forward.z,forward.x);
            var firstPoint=Add(Add(origin,Scale(forward,distance*.3f)),Scale(side,2));
            var secondPoint=Add(Add(origin,Scale(forward,distance*.7f)),Scale(side,2));
            var mirrors=_data.mirrors.OrderBy(m=>m.id).ToArray();
            for(int n=0;n<maximum;n++)
            {
                var point=n==0?firstPoint:secondPoint;
                var from=n==0?origin:firstPoint;
                var to=n==0&&maximum==2?secondPoint:target;
                if(!Normalize(Subtract(point,from),out var incoming)||!Normalize(Subtract(to,point),out var outgoing)||
                    !Normalize(Subtract(incoming,outgoing),out var normal))return null;
                var existing=n<mirrors.Length?mirrors[n]:null;
                if(existing==null)return new DamagePrismCommand{point=point,normal=normal};
                if(Length(Subtract(existing.point,point))>.1f||Math.Abs(Dot(existing.normal,normal))<.999f)
                    return CanAdjust(existing.id)?new DamagePrismCommand{mirrorId=existing.id,point=point,normal=normal}:null;
            }
            // Adjust selects its mirror as the entry point. Restore first entry
            // after adjusting the second, without recreating either mirror.
            if(mirrors.Length>0&&_data.firstId!=mirrors[0].id&&CanAdjust(mirrors[0].id))
                return new DamagePrismCommand{mirrorId=mirrors[0].id,point=mirrors[0].point,normal=mirrors[0].normal};
            return null;
        }
        public DamagePrismState Preview(DamagePrismCommand command,int maximum,float duration)
        {
            if(command==null||!command.Valid)return null;
            var copy=new DamagePrismState();if(!copy.Restore(Snapshot,maximum))return null;
            if(command.mirrorId==0)
            {if(!copy.Place(command.point,command.normal,maximum,duration))return null;}
            else
            {if(!copy.Adjust(command.mirrorId,command.point,command.normal,.25f)||!copy.SelectFirst(command.mirrorId))return null;}
            return copy;
        }
        public bool Adjust(int id,DamagePoint point,DamagePoint normal,float delay)
        {
            var mirror=_data.mirrors.FirstOrDefault(m=>m.id==id);
            if(mirror==null||_data.adjustDelay>0||!point.Valid||!Normalize(normal,out normal)||!DamageTime.Nonnegative(delay))return false;
            mirror.point=point;mirror.normal=normal;_data.adjustDelay=delay;return true;
        }
        public void Tick(float delta)
        {
            if(!DamageTime.Positive(delta))return;
            _data.adjustDelay=Math.Max(0,_data.adjustDelay-delta);
            foreach(var mirror in _data.mirrors)mirror.remaining=Math.Max(0,mirror.remaining-delta);
            _data.mirrors=_data.mirrors.Where(m=>m.remaining>0).ToArray();
            if(!_data.mirrors.Any(m=>m.id==_data.firstId))_data.firstId=_data.mirrors.FirstOrDefault()?.id??0;
        }
        // Deterministic horizontal geometry for both preview and host verdict.
        // The adapter separately rejects obstructed/illegal placements and casts
        // world collision rays along each resulting segment.
        public DamagePoint[] Trace(DamagePoint origin,float range,float mirrorHalfWidth=1)
        {
            var first=_data.mirrors.FirstOrDefault(m=>m.id==_data.firstId);
            if(first==null||!origin.Valid||!DamageTime.Positive(range)||!DamageTime.Positive(mirrorHalfWidth))return Array.Empty<DamagePoint>();
            var initial=Subtract(first.point,origin);float distance=Length(initial);
            if(distance>range||!Normalize(initial,out var direction))return Array.Empty<DamagePoint>();
            float remaining=range-distance;direction=Reflect(direction,first.normal);
            var result=new System.Collections.Generic.List<DamagePoint>{origin,first.point};
            var second=_data.mirrors.FirstOrDefault(m=>m.id!=first.id);
            DamagePoint from=first.point;
            if(second!=null)
            {
                var tangent=new DamagePoint(-second.normal.z,second.normal.x);
                float denominator=Dot(direction,second.normal);
                if(Math.Abs(denominator)>.0001f)
                {
                    float t=Dot(Subtract(second.point,from),second.normal)/denominator;
                    var intersection=Add(from,Scale(direction,t));
                    if(t>.001f&&t<=remaining&&Math.Abs(Dot(Subtract(intersection,second.point),tangent))<=mirrorHalfWidth)
                    {result.Add(intersection);remaining-=t;from=intersection;direction=Reflect(direction,second.normal);}
                }
            }
            result.Add(Add(from,Scale(direction,remaining)));return result.ToArray();
        }
        // The first obstructed segment terminates the entire optical path: mirrors
        // behind it cannot reflect. The callback returns a distance, not a world point,
        // so the clipped endpoint stays on the same segment used by preview/damage.
        public static DamagePoint[] Clip(DamagePoint[] path,Func<DamagePoint,DamagePoint,float?> obstruction)
        {
            if(path==null||path.Length<2||path.Any(p=>!p.Valid)||obstruction==null)return Array.Empty<DamagePoint>();
            var result=new System.Collections.Generic.List<DamagePoint>{path[0]};
            for(int n=0;n<path.Length-1;n++)
            {
                var delta=Subtract(path[n+1],path[n]);float length=Length(delta);
                if(!DamageTime.Nonnegative(length))return Array.Empty<DamagePoint>();
                float? hit=obstruction(path[n],path[n+1]);
                if(hit.HasValue)
                {
                    if(!DamageTime.Nonnegative(hit.Value)||hit.Value>length)return Array.Empty<DamagePoint>();
                    result.Add(length>0?Add(path[n],Scale(delta,hit.Value/length)):path[n]);
                    return result.ToArray();
                }
                result.Add(path[n+1]);
            }
            return result.ToArray();
        }
        // One bool budget per manual finisher, even if multiple reflected segments
        // cross the same enemy. Initial path into the first mirror is not a bonus.
        public static bool Hits(DamagePoint[] path,DamagePoint target,float radius)
        {return ReflectionsToHit(path,target,radius)>0;}
        public static int ReflectionsToHit(DamagePoint[] path,DamagePoint target,float radius)
        {
            if(path==null||path.Length<3||!target.Valid||!DamageTime.Nonnegative(radius))return 0;
            int reflections=0;
            for(int n=1;n<path.Length-1;n++)
            {
                var delta=Subtract(path[n+1],path[n]);float square=Dot(delta,delta);
                float t=square>.000001f?Math.Max(0,Math.Min(1,Dot(Subtract(target,path[n]),delta)/square)):0;
                if(Length(Subtract(target,Add(path[n],Scale(delta,t))))<=radius)reflections=n;
            }
            return reflections;
        }
        public bool Restore(DamagePrismSnapshot v,int maximum)
        {
            if(v?.mirrors==null||maximum<1||maximum>2||v.nextId<1||v.mirrors.Length>maximum||!DamageTime.Nonnegative(v.adjustDelay)||
                v.mirrors.Any(m=>m==null||m.id<1||m.id>=v.nextId||!m.point.Valid||!m.normal.Valid||Math.Abs(Length(m.normal)-1)>.001f||!DamageTime.Positive(m.remaining))||
                v.mirrors.Select(m=>m.id).Distinct().Count()!=v.mirrors.Length||(v.mirrors.Length==0?v.firstId!=0:!v.mirrors.Any(m=>m.id==v.firstId)))return false;
            _data=v.Copy();return true;
        }
        private static DamagePoint Add(DamagePoint a,DamagePoint b)=>new DamagePoint(a.x+b.x,a.z+b.z);
        private static DamagePoint Subtract(DamagePoint a,DamagePoint b)=>new DamagePoint(a.x-b.x,a.z-b.z);
        private static DamagePoint Scale(DamagePoint a,float k)=>new DamagePoint(a.x*k,a.z*k);
        private static float Dot(DamagePoint a,DamagePoint b)=>a.x*b.x+a.z*b.z;
        private static float Length(DamagePoint a)=>(float)Math.Sqrt((double)a.x*a.x+(double)a.z*a.z);
        private static bool Normalize(DamagePoint a,out DamagePoint normal)
        {
            float length=Length(a);normal=default;if(!a.Valid||!DamageTime.Positive(length)||length<.0001f)return false;
            normal=Scale(a,1/length);return true;
        }
        private static DamagePoint Reflect(DamagePoint direction,DamagePoint normal)=>Subtract(direction,Scale(normal,2*Dot(direction,normal)));
    }
}
