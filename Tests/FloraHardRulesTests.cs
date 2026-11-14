using System;
using RaidRogue;

internal static class FloraHardRulesTests
{
    private static int assertions;
    private static void Check(bool condition,string message)
    { assertions++;if(!condition)throw new Exception(message); }
    private static FloraHardSpecial Due(bool transformed,bool core,bool vines,bool garden,bool final,float hp) =>
        FloraHardRules.Due(transformed,core,vines,garden,final,hp,.4f,.5f,.25f);
    private static void Main()
    {
        Check(Math.Abs(FloraHardRules.Tempo(3.6f,1.2f)-3f)<.001f,"tempo once");
        Check(Math.Abs(FloraHardRules.Tempo(2.4f,1.2f)-2f)<.001f,"two-second followup");
        Check(FloraHardRules.Tempo(3,0)<3.001f,"tempo floor");
        Check(FloraHardRules.CanTraverse(3.6f,7.02f,2),"slowest movement can cross sweep midpoint");
        Check(!FloraHardRules.CanTraverse(16,7.02f,2),"full-half crossing is unsafe");
        Check(Due(false,false,true,false,false,.2f)==FloraHardSpecial.Transformation,"simultaneous thresholds start transformation");
        Check(Due(true,false,true,false,false,.2f)==FloraHardSpecial.RootCores,"cores after transformation");
        Check(Due(true,true,true,false,false,.2f)==FloraHardSpecial.FirstGarden,"first garden before finale");
        Check(Due(true,true,true,true,false,.2f)==FloraHardSpecial.FinalGarden,"final after first garden");
        Check(Due(true,true,true,true,true,.2f)==FloraHardSpecial.None,"one-shot finale");
        Check(Due(false,false,false,false,false,1)==FloraHardSpecial.None,"retry begins phase one");
        Check(Due(true,true,false,false,false,.2f)==FloraHardSpecial.None,"no premature finale while first garden pending");
        Console.WriteLine("PASS: "+assertions+" Flora hard priority, tempo, and movement assertions.");
    }
}
