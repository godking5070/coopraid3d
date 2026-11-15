using System;
using RaidRogue;

static class AiSpectatorDamageTests
{
    static int checks;
    static void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
    static void Equal(float actual,float expected,string name)=>Check(Math.Abs(actual-expected)<.0001f,name);
    static void Main()
    {
        Equal(AiSpectatorDamageRules.Resolve(100,0,true,true),0,"0% retains a hit but removes HP damage");
        Equal(AiSpectatorDamageRules.Resolve(100,100,true,true),100,"100% baseline");
        Equal(AiSpectatorDamageRules.Resolve(100,120,true,true),120,"120% direct hit");
        Equal(AiSpectatorDamageRules.Resolve(100,500,true,true),500,"500% ceiling");
        Equal(AiSpectatorDamageRules.Resolve(80,120,true,true),96,"periodic tick uses current multiplier");
        Equal(AiSpectatorDamageRules.Resolve(80,120,true,true,true),80,"derived confirmed damage is not multiplied again");
        Equal(AiSpectatorDamageRules.Resolve(80,500,false,true),80,"ordinary play unchanged");
        Equal(AiSpectatorDamageRules.Resolve(80,500,true,false),80,"enemy or player source unchanged");
        Equal(AiSpectatorDamageRules.BaseFromConfirmed(120,120),100,"recorded hit stores base for later replay");
        Equal(AiSpectatorDamageRules.Resolve(AiSpectatorDamageRules.BaseFromConfirmed(120,120),80,true,true),80,"later replay uses its own current multiplier");
        Equal(AiSpectatorDamageRules.BaseFromConfirmed(0,0),0,"zero-damage recording safe");
        Check(AiSpectatorDamageRules.Step(100,10)==110&&AiSpectatorDamageRules.Step(110,10)==120,"percentage-point buttons");
        Check(AiSpectatorDamageRules.Step(0,-10)==0&&AiSpectatorDamageRules.Step(500,10)==500,"button limits");
        Check(AiSpectatorDamageRules.TryParse("0",out int low)&&low==0&&AiSpectatorDamageRules.TryParse("500",out int high)&&high==500,"valid boundary input");
        foreach(string invalid in new[]{"-1","501","NaN","1.2","","999999999999"})
            Check(!AiSpectatorDamageRules.TryParse(invalid,out _),"invalid input " + invalid);
        Console.WriteLine("PASS AI spectator damage: " + checks + " assertions.");
    }
}
