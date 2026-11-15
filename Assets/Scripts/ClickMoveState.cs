using System;

namespace RaidRogue
{
    // Client intent only: the existing motor retains collision, speed and host authority.
    public sealed class ClickMoveState
    {
        public bool Active {get;private set;}
        public double X {get;private set;}
        public double Z {get;private set;}
        private bool _dragging,_releaseRequired;
        private double _progressX,_progressZ,_progressTime;
        public void Cancel(){Active=false;}
        public void Suspend(bool held)
        {Active=false;_dragging=false;_releaseRequired=held;}
        public bool Pointer(bool pressed,bool held,bool blocked)
        {
            if(!held){_dragging=false;_releaseRequired=false;}
            if(blocked){Suspend(held);return false;}
            if(_releaseRequired)return false;
            if(pressed&&held)_dragging=true;
            return held&&_dragging;
        }
        public bool SetDestination(double x,double z,double currentX,double currentZ,double now)
        {
            if(!Finite(x)||!Finite(z)||!Finite(currentX)||!Finite(currentZ)||!Finite(now))return false;
            if(!Active){_progressX=currentX;_progressZ=currentZ;_progressTime=now;}
            X=x;Z=z;Active=true;return true;
        }
        public void Direction(double x,double z,double speed,double step,double now,out double dx,out double dz)
        {
            dx=dz=0;
            if(!Active)return;
            if(!Finite(x)||!Finite(z)||!Finite(speed)||!Finite(step)||!Finite(now)||speed<=0||step<=0){Cancel();return;}
            double distance=Math.Sqrt((X-x)*(X-x)+(Z-z)*(Z-z));
            if(distance<=.12){Cancel();return;}
            double progress=(x-_progressX)*(x-_progressX)+(z-_progressZ)*(z-_progressZ);
            if(progress>=.0016){_progressX=x;_progressZ=z;_progressTime=now;}
            else if(now-_progressTime>=.45){Cancel();return;} // Wall/arena edge: do not retain a stale order forever.
            double scale=Math.Min(1,distance/(speed*step))/distance;
            dx=(X-x)*scale;dz=(Z-z)*scale;
        }
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
}
