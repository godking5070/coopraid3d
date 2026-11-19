using System;

namespace Astella.TwoD
{
    public struct ArenaPoint
    {
        public float X, Y;
        public ArenaPoint(float x, float y) { X = x; Y = y; }
    }

    // World-space geometry shared by 2D telegraphs and their damage checks.
    // Actor bodies are disks. Tangency counts as a hit, including shape endpoints.
    public static class Astella2DCombatRules
    {
        private const double Roundoff = 1e-12;

        // Radial projection into an origin-centered ellipse, preserving direction.
        // Invalid coordinates or nonpositive axes produce the finite safe origin.
        public static ArenaPoint ClampToEllipse(float x, float y, float radiusX, float radiusY)
        {
            if (!Finite(x) || !Finite(y) || !Positive(radiusX) || !Positive(radiusY))
                return new ArenaPoint(0, 0);
            double nx = (double)x / radiusX, ny = (double)y / radiusY;
            double scale = Math.Sqrt(nx * nx + ny * ny);
            return scale <= 1 ? new ArenaPoint(x, y) : new ArenaPoint((float)(x / scale), (float)(y / scale));
        }

        public static bool Circle(float px, float py, float cx, float cy, float radius, float actorRadius = 0f)
        {
            if (!Point(px, py) || !Point(cx, cy) || !Nonnegative(radius) || !Nonnegative(actorRadius)) return false;
            double x = (double)px - cx, y = (double)py - cy;
            double reach = (double)radius + actorRadius;
            return WithinSquared(x * x + y * y, reach * reach);
        }

        public static bool Donut(float px, float py, float cx, float cy, float inner, float outer, float actorRadius = 0f)
        {
            if (!Point(px, py) || !Point(cx, cy) || !Nonnegative(inner) || !Nonnegative(outer) ||
                !Nonnegative(actorRadius) || inner > outer) return false;
            double x = (double)px - cx, y = (double)py - cy;
            double distance = Math.Sqrt(x * x + y * y);
            return Within(distance, (double)outer + actorRadius) && Within(inner, distance + actorRadius);
        }

        // A line telegraph is a capsule: a segment with circular caps of halfWidth.
        // A zero-length segment is a disk, and zero width is a valid thin segment.
        public static bool Line(float px, float py, float ax, float ay, float bx, float by, float halfWidth, float actorRadius = 0f)
        {
            if (!Point(px, py) || !Point(ax, ay) || !Point(bx, by) || !Nonnegative(halfWidth) || !Nonnegative(actorRadius)) return false;
            double reach = (double)halfWidth + actorRadius;
            return WithinSquared(SegmentDistanceSquared(px, py, ax, ay, bx, by), reach * reach);
        }

        // Exact disk-versus-filled-sector intersection. Expanding only the angle and
        // radius would incorrectly hit disks outside the sector's outer corners.
        public static bool Cone(float px, float py, float cx, float cy, float dx, float dy, float radius,
            float halfAngleDegrees, float actorRadius = 0f)
        {
            if (!Point(px, py) || !Point(cx, cy) || !Point(dx, dy) || !Nonnegative(radius) ||
                !Nonnegative(actorRadius) || !Nonnegative(halfAngleDegrees) || halfAngleDegrees > 180) return false;
            if (radius == 0 || halfAngleDegrees == 180) return Circle(px, py, cx, cy, radius, actorRadius);
            double directionLength = Math.Sqrt((double)dx * dx + (double)dy * dy);
            if (directionLength == 0) return false;
            double ux = dx / directionLength, uy = dy / directionLength;
            double x = (double)px - cx, y = (double)py - cy;
            double distance = Math.Sqrt(x * x + y * y);
            if (!Within(distance, (double)radius + actorRadius)) return false;
            if (distance == 0) return true;
            if (halfAngleDegrees == 0)
                return WithinSquared(SegmentDistanceSquared(x, y, 0, 0, ux * radius, uy * radius),
                    (double)actorRadius * actorRadius);
            double angle = halfAngleDegrees * (Math.PI / 180);
            double cosine = Math.Cos(angle), sine = Math.Sin(angle);
            double projection = x * ux + y * uy;
            if (projection >= distance * cosine) return true;
            double ax = (ux * cosine - uy * sine) * radius;
            double ay = (ux * sine + uy * cosine) * radius;
            double bx = (ux * cosine + uy * sine) * radius;
            double by = (-ux * sine + uy * cosine) * radius;
            double closest = Math.Min(SegmentDistanceSquared(x, y, 0, 0, ax, ay),
                SegmentDistanceSquared(x, y, 0, 0, bx, by));
            return WithinSquared(closest, (double)actorRadius * actorRadius);
        }

        private static double SegmentDistanceSquared(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, lengthSquared = dx * dx + dy * dy;
            double t = lengthSquared == 0 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / lengthSquared));
            double x = px - (ax + t * dx), y = py - (ay + t * dy);
            return x * x + y * y;
        }

        // Double intermediates prevent overflow for every finite float input. The
        // tiny relative allowance includes analytical boundaries after trig rounding.
        private static bool WithinSquared(double distanceSquared, double limitSquared) =>
            distanceSquared <= limitSquared + Roundoff * Math.Max(1, Math.Max(distanceSquared, limitSquared));
        private static bool Within(double distance, double limit) =>
            distance <= limit + Roundoff * Math.Max(1, Math.Max(distance, limit));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Point(float x, float y) => Finite(x) && Finite(y);
        private static bool Nonnegative(float value) => Finite(value) && value >= 0;
        private static bool Positive(float value) => Finite(value) && value > 0;
    }
}
