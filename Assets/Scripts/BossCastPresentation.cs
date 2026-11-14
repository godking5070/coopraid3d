using System;

namespace RaidRogue
{
    public static class BossCastPresentation
    {
        private static readonly char[] GuideSeparators={'·','/','→','\n','\r'};
        // Combat names keep the action title; directional sequences and solution notes are training-only.
        public static string Name(string name,bool training)
        {
            if(string.IsNullOrEmpty(name))return "";
            if(training)return name;
            int split=name.IndexOfAny(GuideSeparators);
            return split<0?name:name.Substring(0,split).TrimEnd();
        }
    }
}
