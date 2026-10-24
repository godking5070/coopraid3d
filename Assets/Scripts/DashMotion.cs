using System;

namespace RaidRogue
{
    // One shared tuning block on PlayerController, independent of job and GCD.
    [Serializable]
    public sealed class DashSettings
    {
        public float distance = 4f;
        public float duration = 0.2f;
        public float cooldown = 5f;
        public int maximumCharges = 1;
        public bool preserveCast;
    }

    // Pure state used by the actual movement controller and standalone regression checks.
    public sealed class DashMotion
    {
        // Only authoritative snapshots are restored; clients never send these values to the host.
        [Serializable]
        public struct Snapshot
        {
            public bool Active;
            public int Charges, MaximumCharges; public float Recharge;
            public float Cooldown, CooldownTotal, Time, Distance, Speed, X, Z;
        }
        public Snapshot Capture() => new Snapshot { Active = Active, Cooldown = (float)CooldownRemaining,
            CooldownTotal = (float)CooldownDuration, Charges = Charges, MaximumCharges = MaximumCharges, Recharge = (float)_recharge, Time = (float)_timeRemaining, Distance = (float)_distanceRemaining,
            Speed = (float)_speed, X = (float)DirectionX, Z = (float)DirectionZ };
        public void Restore(Snapshot state)
        {
            Active = state.Active; MaximumCharges = Math.Max(1,state.MaximumCharges); Charges = state.MaximumCharges > 0 ? Math.Max(0,Math.Min(MaximumCharges,state.Charges)) : state.Cooldown > 0 ? 0 : 1; _recharge = state.MaximumCharges > 0 ? Math.Max(0,state.Recharge) : state.Cooldown; CooldownDuration = state.CooldownTotal;
            _timeRemaining = state.Time; _distanceRemaining = state.Distance; _speed = state.Speed;
            DirectionX = state.X; DirectionZ = state.Z;
        }

        public bool Active { get; private set; }
        public int Charges { get; private set; } = 1;
        public int MaximumCharges { get; private set; } = 1;
        private double _recharge;
        public double RechargeRemaining => _recharge;
        public double CooldownRemaining => Charges > 0 ? 0 : _recharge;
        public double CooldownDuration { get; private set; }
        private double _timeRemaining;
        private double _distanceRemaining;
        private double _speed;
        public double DirectionX { get; private set; }
        public double DirectionZ { get; private set; }

        public bool TryStart(DashSettings settings, bool allowed, double moveX, double moveZ, double facingX, double facingZ)
        {
            if (!allowed || Active || settings == null || settings.maximumCharges < 1 || settings.maximumCharges > 10 ||
                !PositiveFinite(settings.distance) || !PositiveFinite(settings.duration) ||
                float.IsNaN(settings.cooldown) || float.IsInfinity(settings.cooldown) || settings.cooldown < 0) return false;
            if (_recharge <= 0 && MaximumCharges != settings.maximumCharges) { MaximumCharges = settings.maximumCharges; Charges = MaximumCharges; }
            if (Charges < 1) return false;

            double x = moveX, z = moveZ;
            if (x * x + z * z < 0.0001) { x = facingX; z = facingZ; }
            double length = Math.Sqrt(x * x + z * z);
            if (length < 0.0001 || double.IsNaN(length) || double.IsInfinity(length)) { x = 0; z = 1; length = 1; }
            DirectionX = x / length;
            DirectionZ = z / length;
            _distanceRemaining = settings.distance;
            _timeRemaining = settings.duration;
            _speed = _distanceRemaining / _timeRemaining;
            CooldownDuration = settings.cooldown; Charges--;
            if (_recharge <= 0) _recharge = settings.cooldown;
            if (settings.cooldown == 0) Charges = MaximumCharges;
            Active = true;
            return true;
        }

        public void TickCooldown(double deltaTime)
        {
            if(deltaTime<0||double.IsNaN(deltaTime)||double.IsInfinity(deltaTime)||Charges>=MaximumCharges)return;
            _recharge-=deltaTime;
            while(_recharge<=0&&Charges<MaximumCharges){Charges++;_recharge+=CooldownDuration;if(CooldownDuration<=0){Charges=MaximumCharges;break;}}
            if(Charges==MaximumCharges)_recharge=0;
        }

        public double Step(double deltaTime)
        {
            if (!Active || deltaTime <= 0) return 0;
            double elapsed = Math.Min(deltaTime, _timeRemaining);
            double distance = Math.Min(_distanceRemaining, elapsed * _speed);
            _timeRemaining -= elapsed;
            _distanceRemaining -= distance;
            if (_timeRemaining <= 1e-8 || _distanceRemaining <= 1e-8) Active = false;
            return distance;
        }

        // Interruptions never refund or restart the cooldown.
        public void Cancel() { Active = false; _timeRemaining = _distanceRemaining = 0; }
        public void ResetForRetry() { Cancel(); _recharge = CooldownDuration = 0; Charges = MaximumCharges; }
        // Counter rewards restore all charges without changing a dash already in progress.
        public void RefillCharges(DashSettings settings)
        {
            if(settings==null||settings.maximumCharges<1||settings.maximumCharges>10||settings.cooldown<0||float.IsNaN(settings.cooldown)||float.IsInfinity(settings.cooldown))return;
            MaximumCharges=Charges=settings.maximumCharges;_recharge=0;CooldownDuration=settings.cooldown;
        }
        private static bool PositiveFinite(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
    }

    public sealed class DashPressGate
    {
        private bool _wasHeld;
        public bool Consume(bool pressedThisFrame, bool held, bool allowed)
        {
            bool consume = pressedThisFrame && !_wasHeld && allowed;
            _wasHeld = held; // Sample even during pause, menus, cooldown and forced movement; never queue a dash.
            return consume;
        }
        public void RequireRelease() => _wasHeld = true;
    }

    public static class DashGeometry
    {
        // Clip along the requested path, rather than projecting its endpoint sideways around the arena.
        // Uses the existing arena's center-position boundary, shared with ordinary WASD movement.
        public static double ArenaTravel(double x, double z, double dx, double dz, double distance, double radius)
        {
            if (distance <= 0 || radius <= 0) return 0;
            double a = dx * dx + dz * dz;
            if (a < 1e-12) return 0;
            double dot = x * dx + z * dz;
            double c = x * x + z * z - radius * radius;
            // Vector3 positions/directions are floats. A clipped endpoint can round a few microns
            // outside the circle; it must still be possible to walk/dash back into the arena.
            if (c > 0.0001 * Math.Max(1, radius)) return 0;
            c = Math.Min(0, c);
            double discriminant = dot * dot - a * c;
            if (discriminant < 0) return 0;
            double exit = (-dot + Math.Sqrt(discriminant)) / a;
            return Math.Max(0, Math.Min(distance, exit));
        }

        // Sweep through the SAME strip used by the original point test: t in [0.05, 0.98],
        // perpendicular distance <= radius. No expanded endpoint caps or stale previous-frame path.
        public static bool CrossesTether(double fromX, double fromZ, double toX, double toZ,
            double bossX, double bossZ, double tankX, double tankZ, double radius)
        {
            double ax = tankX - bossX, az = tankZ - bossZ;
            double length = Math.Sqrt(ax * ax + az * az);
            if (length < 0.01 || radius < 0) return false;
            ax /= length; az /= length;
            double px = fromX - bossX, pz = fromZ - bossZ;
            double sx = toX - fromX, sz = toZ - fromZ;
            double enter = 0, exit = 1;
            return Clip(px * ax + pz * az, sx * ax + sz * az, length * 0.05, length * 0.98, ref enter, ref exit) &&
                   Clip(-px * az + pz * ax, -sx * az + sz * ax, -radius, radius, ref enter, ref exit);
        }

        private static bool Clip(double start, double delta, double min, double max, ref double enter, ref double exit)
        {
            if (Math.Abs(delta) < 1e-12) return start >= min && start <= max;
            double a = (min - start) / delta, b = (max - start) / delta;
            if (a > b) { double swap = a; a = b; b = swap; }
            enter = Math.Max(enter, a); exit = Math.Min(exit, b);
            return enter <= exit;
        }
    }
}
