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
    }

    // Pure state used by the actual movement controller and standalone regression checks.
    public sealed class DashMotion
    {
        public bool Active { get; private set; }
        public double CooldownRemaining { get; private set; }
        public double CooldownDuration { get; private set; }
        private double _timeRemaining;
        private double _distanceRemaining;
        private double _speed;
        public double DirectionX { get; private set; }
        public double DirectionZ { get; private set; }

        public bool TryStart(DashSettings settings, bool allowed, double moveX, double moveZ, double facingX, double facingZ)
        {
            if (!allowed || Active || CooldownRemaining > 0 || settings == null ||
                !PositiveFinite(settings.distance) || !PositiveFinite(settings.duration) ||
                float.IsNaN(settings.cooldown) || float.IsInfinity(settings.cooldown) || settings.cooldown < 0) return false;

            double x = moveX, z = moveZ;
            if (x * x + z * z < 0.0001) { x = facingX; z = facingZ; }
            double length = Math.Sqrt(x * x + z * z);
            if (length < 0.0001 || double.IsNaN(length) || double.IsInfinity(length)) { x = 0; z = 1; length = 1; }
            DirectionX = x / length;
            DirectionZ = z / length;
            _distanceRemaining = settings.distance;
            _timeRemaining = settings.duration;
            _speed = _distanceRemaining / _timeRemaining;
            CooldownDuration = CooldownRemaining = settings.cooldown;
            Active = true;
            return true;
        }

        public void TickCooldown(double deltaTime) => CooldownRemaining = Math.Max(0, CooldownRemaining - Math.Max(0, deltaTime));

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
