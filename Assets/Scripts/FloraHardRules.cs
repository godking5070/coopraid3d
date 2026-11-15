namespace RaidRogue
{
    public enum FloraHardSpecial { None, Transformation, RootCores, FirstGarden, FinalGarden }

    public static class FloraHardRules
    {
        public static float Tempo(float baseSeconds,float multiplier) => baseSeconds/(multiplier<1?1:multiplier);
        public static bool CanTraverse(float meters,float metersPerSecond,float seconds) => metersPerSecond*seconds>=meters;
        public static FloraHardSpecial Due(bool transformed,bool coreDone,bool vinesIntroduced,bool firstGardenDone,
            bool finalGardenDone,float hpRatio,float transformationThreshold,float firstGardenThreshold,float finalGardenThreshold)
        {
            if(!transformed && hpRatio<=transformationThreshold)return FloraHardSpecial.Transformation;
            if(transformed && !coreDone)return FloraHardSpecial.RootCores;
            if(vinesIntroduced && !firstGardenDone && hpRatio<=firstGardenThreshold)return FloraHardSpecial.FirstGarden;
            if(coreDone && firstGardenDone && !finalGardenDone && hpRatio<=finalGardenThreshold)return FloraHardSpecial.FinalGarden;
            return FloraHardSpecial.None;
        }
    }
}
