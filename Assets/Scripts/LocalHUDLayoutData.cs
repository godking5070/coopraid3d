using System;
using System.Collections.Generic;

namespace RaidRogue
{
    [Serializable] public sealed class LocalHUDLayoutEntry
    {
        public string id;
        public float x,y,scale=1;
        public LocalHUDLayoutEntry Copy()=>(LocalHUDLayoutEntry)MemberwiseClone();
    }
    [Serializable] public sealed class LocalHUDLayoutData
    {
        public int version=1;
        public LocalHUDLayoutEntry[] entries=Array.Empty<LocalHUDLayoutEntry>();
        public bool Valid()
        {
            if(version!=1||entries==null||entries.Length>64)return false;
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var e in entries)
                if(e==null||string.IsNullOrWhiteSpace(e.id)||e.id.Length>80||!ids.Add(e.id)||
                    !HUDLayoutRules.Finite(e.x)||!HUDLayoutRules.Finite(e.y)||Math.Abs(e.x)>4||Math.Abs(e.y)>4||
                    !HUDLayoutRules.Finite(e.scale)||e.scale<.5f||e.scale>2)return false;
            return true;
        }
    }
    public static class HUDLayoutRules
    {
        public static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        public static float Scale(float value)=>!Finite(value)?1:Math.Max(.5f,Math.Min(2,value));
        public static float Snap(float value,bool enabled)=>enabled?(float)Math.Round(value/8,MidpointRounding.AwayFromZero)*8:value;
        public static float Normalize(float value,float parentSize)=>parentSize>0&&Finite(value)&&Finite(parentSize)?value/parentSize:0;
        public static float ClampCenter(float center,float extent,float viewport)
        {
            if(extent>=viewport-16)return viewport*.5f;
            float half=Math.Max(0,extent)*.5f;
            return Math.Max(8+half,Math.Min(viewport-8-half,center));
        }
    }
}
