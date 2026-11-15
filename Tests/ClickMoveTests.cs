using System;
using RaidRogue;

static class ClickMoveTests
{
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static void Near(double value,double expected,string reason)=>Check(Math.Abs(value-expected)<.000001,reason);
    static void Main()
    {
        var state=new ClickMoveState();
        Check(!state.Pointer(false,true,false),"holding without a fresh press cannot start a gesture");
        Check(state.Pointer(true,true,false),"right press starts a gesture");
        Check(state.SetDestination(6,0,0,0,0),"valid floor destination");
        state.Direction(0,0,6,.1,0,out double x,out double z);Near(x,1,"world direction");Near(z,0,"no lateral drift");
        Check(!state.Pointer(false,false,false)&&state.Active,"release retains the destination");
        state.Direction(5.8,0,6,.1,.1,out x,out z);Near(x*6*.1,.2,"last step cannot overshoot");
        state.Direction(6,0,6,.1,.2,out x,out z);Check(!state.Active&&x==0&&z==0,"arrival stops");
        state.Pointer(true,true,true);Check(!state.Pointer(false,true,false),"UI press cannot turn into world drag");
        state.Pointer(false,false,false);Check(state.Pointer(true,true,false),"fresh world press after UI is allowed");
        state.SetDestination(10,10,0,0,0);state.Suspend(true);
        Check(!state.Active&&!state.Pointer(false,true,false),"skill/menu/focus/keyboard cancellation requires release");
        state.Pointer(false,false,false);Check(!state.Pointer(false,true,false),"old held input cannot reissue move");
        state.Pointer(false,false,false);Check(state.Pointer(true,true,false),"new click resumes");
        state.SetDestination(2,3,0,0,0);state.SetDestination(7,9,0,0,.05);
        Near(state.X,7,"held movement refreshes goal");Near(state.Z,9,"held movement refreshes both axes");
        Check(!state.SetDestination(double.NaN,0,0,0,0)&&!state.SetDestination(0,double.PositiveInfinity,0,0,0),"invalid cursor rays rejected");
        state.Direction(0,0,6,.02,.46,out x,out z);Check(!state.Active&&x==0,"blocked wall cannot retain a move order");
        state.SetDestination(1,0,0,0,0);state.Direction(0,0,6,0,0,out x,out z);Check(!state.Active,"paused simulation cannot issue stale movement");
        foreach(int fps in new[]{30,60,144})foreach(double speed in new[]{3d,6d,12d})
        {
            var travel=new ClickMoveState();travel.SetDestination(9,12,0,0,0);
            double px=0,pz=0,step=1d/fps;
            for(int i=0;i<fps*8&&travel.Active;i++)
            {
                travel.Direction(px,pz,speed,step,i*step,out x,out z);
                Check(Math.Sqrt(x*x+z*z)<=1.000001,"speed never exceeds existing motor input bound");
                px+=x*speed*step;pz+=z*speed*step;
                Check(px<=9.000001&&pz<=12.000001,"no overshoot at supported frame rates");
            }
            Check(!travel.Active&&Math.Sqrt((px-9)*(px-9)+(pz-12)*(pz-12))<=.120001,"arrives at destination independent of frame rate");
        }
        var settings=new LocalSettingsData();Check(settings.movementMode==0,"existing settings stay keyboard mode");
        settings.movementMode=1;settings.Normalize();Check(settings.movementMode==1,"right-click preference persists normalization");
        var draft=settings.Copy();draft.movementMode=0;Check(settings.movementMode==1,"draft cancel cannot mutate applied preference");
        foreach(int bad in new[]{-1,2,int.MinValue,int.MaxValue})
        {draft.movementMode=bad;draft.Normalize();Check(draft.movementMode==0,"invalid saved preference falls back safely");}
        Console.WriteLine("PASS: "+checks+" click movement lifecycle, travel and settings checks.");
    }
}
