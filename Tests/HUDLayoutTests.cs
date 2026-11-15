using System;
using RaidRogue;

public static class HUDLayoutTests
{
    private static int _checks;
    private static void Check(bool condition,string name){_checks++;if(!condition)throw new Exception(name);}
    private static void Near(float a,float b,string name)=>Check(Math.Abs(a-b)<.001f,name);
    private static LocalHUDLayoutEntry Entry()=>new LocalHUDLayoutEntry{id="skills.left",x=-.2f,y=.4f,scale=1};
    private static LocalHUDLayoutData Profile(LocalHUDLayoutEntry e)=>new LocalHUDLayoutData{entries=new[]{e}};
    public static void Main()
    {
        Check(new LocalHUDLayoutData().Valid(),"empty profile restores authored defaults");
        var e=Entry();Check(Profile(e).Valid(),"valid layout");
        var copy=e.Copy();copy.x=.8f;Near(e.x,-.2f,"draft does not mutate saved entry");
        Check(new LocalHUDLayoutData{version=2}.Valid()==false,"future version rejected");
        Check(new LocalHUDLayoutData{entries=null}.Valid()==false,"null list rejected");
        Check(new LocalHUDLayoutData{entries=new[]{e,e.Copy()}}.Valid()==false,"duplicate IDs rejected");
        Check(Profile(null).Valid()==false,"null entry rejected");
        foreach(string id in new[]{null,"","  ",new string('a',81)}){e=Entry();e.id=id;Check(!Profile(e).Valid(),"invalid ID");}
        e=Entry();e.id="future.optional-panel";Check(Profile(e).Valid(),"unknown stable IDs preserved for future HUD elements");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,4.01f,-4.01f})
        {e=Entry();e.x=invalid;Check(!Profile(e).Valid(),"invalid x");e=Entry();e.y=invalid;Check(!Profile(e).Valid(),"invalid y");}
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,.49f,2.01f})
        {e=Entry();e.scale=invalid;Check(!Profile(e).Valid(),"invalid scale");}
        foreach(float scale in new[]{.5f,1,2}){e=Entry();e.scale=scale;Check(Profile(e).Valid(),"scale boundary");}
        var many=new LocalHUDLayoutEntry[65];for(int i=0;i<many.Length;i++){many[i]=Entry();many[i].id="panel."+i;}
        Check(!new LocalHUDLayoutData{entries=many}.Valid(),"oversized profile rejected");
        Near(HUDLayoutRules.Scale(.1f),.5f,"minimum scale");Near(HUDLayoutRules.Scale(3),2,"maximum scale");
        Near(HUDLayoutRules.Scale(float.NaN),1,"invalid scale fallback");
        Near(HUDLayoutRules.Snap(12,true),16,"grid positive midpoint");Near(HUDLayoutRules.Snap(-12,true),-16,"grid negative midpoint");
        Near(HUDLayoutRules.Snap(13,false),13,"free positioning");
        Near(HUDLayoutRules.Normalize(100,0),0,"zero-size parent");Near(HUDLayoutRules.Normalize(float.NaN,100),0,"invalid position normalization");
        foreach(float width in new[]{800f,1280,1920,2560})
        foreach(float x in new[]{-400f,-31,0,130.3f,350})
        {float normalized=HUDLayoutRules.Normalize(x,width);Near(normalized*width,x,"normalized position round trip");Near(normalized*(width*2),x*2,"resolution proportional position");}
        foreach(float viewport in new[]{640f,1280,1920})
        foreach(float extent in new[]{20f,100,500})
        for(float x=-1000;x<=3000;x+=100)
        {
            float clamped=HUDLayoutRules.ClampCenter(x,extent,viewport);
            Check(clamped-extent*.5f>=7.99f&&clamped+extent*.5f<=viewport-7.99f,"HUD stays reachable within viewport");
            Near(HUDLayoutRules.ClampCenter(clamped,extent,viewport),clamped,"screen clamp idempotent");
        }
        Near(HUDLayoutRules.ClampCenter(-500,2000,1280),640,"oversized panel centered");
        Console.WriteLine("PASS: "+_checks+" HUD layout validation, draft isolation, scale, grid, resolution and screen-boundary checks. Drag/rendering require Unity verification.");
    }
}
