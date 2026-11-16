using System;

namespace RaidRogue
{
    public static class CameraControlMath
    {
        // Stable party order; a removed/dead subject falls back to the first living member.
        public static int SpectatorIndex(int current, int count, int direction)
        {
            if (count <= 0) return -1;
            if (current < 0 || current >= count) return 0;
            return (current + (direction < 0 ? count - 1 : 1)) % count;
        }
        public static double MovementYaw(bool is2D, double yaw3D) => is2D ? 0 : yaw3D;
        public static void MovementXZ(double horizontal, double vertical, double yawDegrees, out double x, out double z)
        {
            double length = Math.Sqrt(horizontal * horizontal + vertical * vertical);
            if (length > 1) { horizontal /= length; vertical /= length; }
            double angle = yawDegrees * Math.PI / 180;
            x = horizontal * Math.Cos(angle) + vertical * Math.Sin(angle);
            z = vertical * Math.Cos(angle) - horizontal * Math.Sin(angle);
        }

        public static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
        public static double WrapYaw(double yaw) => (yaw % 360 + 360) % 360;
        public static double ClampPitch(double pitch) => Clamp(pitch, 20, 80);
        public static double Zoom(double value, double wheel, double sensitivity, double min, double max) =>
            Clamp(value - wheel * sensitivity, min, max);
        public static double ArenaOrthoSize(double radius, double aspect, double padding) =>
            (Math.Max(1, radius) + Math.Max(0, padding)) / Math.Min(1, Math.Max(0.1, aspect));
        public static double FollowBlend(double zoomFraction)
        {
            double t = Clamp(zoomFraction, 0, 1);
            return t * t * (3 - 2 * t);
        }
        public static double ClampFocusAxis(double focus, double arenaRadius, double visibleHalfSpan)
        {
            double limit = Math.Max(0, arenaRadius - visibleHalfSpan);
            return Clamp(focus, -limit, limit);
        }

        // Fit a subject in camera-local coordinates without changing the user's zoom preference.
        // The inset leaves space for the boss HUD and hotbar. Radius also protects the near plane.
        public static double PerspectiveSubjectDistance(double right, double up, double forward,
            double radius, double verticalFov, double aspect, double nearPlane)
        {
            double extent = Math.Max(0, radius);
            double tanY = Math.Tan(Clamp(verticalFov, 5, 170) * Math.PI / 360);
            double horizontal = (Math.Abs(right) + extent) / (tanY * Math.Max(.1, aspect) * .8);
            double vertical = (Math.Abs(up) + extent) / (tanY * .55);
            return Math.Max(Math.Max(horizontal, vertical), Math.Max(.01, nearPlane)) + extent - forward;
        }

        public static bool SubjectInViewport(double x, double y, double depth, double margin = .08) =>
            depth > 0 && x >= margin && x <= 1 - margin && y >= margin && y <= 1 - margin;
    }

    // A new press is required after UI, focus loss, view changes or explicit cancellation.
    public sealed class CameraDragState
    {
        public bool Active { get; private set; }
        private bool _waitForRelease;
        public void Step(bool pressed, bool held, bool blocked, bool is2D, bool overUI)
        {
            if (_waitForRelease)
            {
                // Focus regain can synthesize a new press for a physically held button.
                if (!held && !blocked) _waitForRelease = false;
                return;
            }
            if (Active)
            {
                if (blocked || is2D) Cancel();
                else if (!held) Active = false;
            }
            else if (pressed && held && !blocked && !is2D && !overUI) Active = true;
        }
        public void Cancel(bool waitForRelease = true) { Active = false; _waitForRelease = waitForRelease; }
    }
}
