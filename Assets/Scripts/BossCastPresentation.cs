using System;

namespace RaidRogue
{
    public static class BossCastPresentation
    {
        private static readonly char[] GuideSeparators={'·','/','→','\n','\r'};
        // Combat names keep the action title without directional sequences or solution notes.
        public static string Name(string name)
        {
            if(string.IsNullOrEmpty(name))return "";
            int split=name.IndexOfAny(GuideSeparators);
            return split<0?name:name.Substring(0,split).TrimEnd();
        }
    }
}
