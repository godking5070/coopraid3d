using System;
using Astella.TwoD;

internal static class Astella2DGeometryTests
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    private static void Near(float actual, float expected, string message) => Check(Math.Abs(actual - expected) < .00001f, message);

    private static void Ellipse()
    {
        var inside = Astella2DCombatRules.ClampToEllipse(2, 1, 4, 2);
        Near(inside.X, 2, "ellipse preserves an interior X");
        Near(inside.Y, 1, "ellipse preserves an interior Y");
        var axis = Astella2DCombatRules.ClampToEllipse(10, 0, 4, 2);
        Near(axis.X, 4, "ellipse clips horizontal extent");
        Near(axis.Y, 0, "ellipse does not rotate an axial move");
        var diagonal = Astella2DCombatRules.ClampToEllipse(8, 4, 4, 2);
        Near(diagonal.X, (float)Math.Sqrt(8), "ellipse radial projection has expected X");
        Near(diagonal.Y, (float)Math.Sqrt(2), "ellipse radial projection has expected Y");
        var invalid = Astella2DCombatRules.ClampToEllipse(float.NaN, 3, 4, 2);
        Check(invalid.X == 0 && invalid.Y == 0, "invalid movement returns a finite origin");
        invalid = Astella2DCombatRules.ClampToEllipse(2, 3, 0, 2);
        Check(invalid.X == 0 && invalid.Y == 0, "invalid arena axes return a safe origin");
        var huge = Astella2DCombatRules.ClampToEllipse(float.MaxValue, float.MaxValue, 4, 2);
        Check(!float.IsNaN(huge.X) && !float.IsInfinity(huge.X) && huge.X > 0 && huge.X <= 4 && huge.Y <= 2,
            "large finite movement does not overflow or flip direction");
        for (int degrees = 0; degrees < 360; degrees += 15)
        {
            double angle = degrees * Math.PI / 180;
            var p = Astella2DCombatRules.ClampToEllipse((float)(20 * Math.Cos(angle)), (float)(20 * Math.Sin(angle)), 8, 4);
            double normalized = (double)p.X * p.X / 64 + (double)p.Y * p.Y / 16;
            Check(Math.Abs(normalized - 1) < .000001, "every radial direction reaches the same ellipse boundary");
        }
    }

    private static void CirclesAndDonuts()
    {
        Check(Astella2DCombatRules.Circle(3, 4, 0, 0, 5), "circle includes its boundary");
        Check(!Astella2DCombatRules.Circle(5.001f, 0, 0, 0, 5), "circle excludes points outside");
        Check(Astella2DCombatRules.Circle(6, 0, 0, 0, 5, 1), "actor touching outer circle is hit");
        Check(!Astella2DCombatRules.Circle(6.001f, 0, 0, 0, 5, 1), "actor beyond circle tangency is safe");
        Check(Astella2DCombatRules.Circle(3, 4, 3, 4, 0), "zero-radius circle retains its center");
        Check(!Astella2DCombatRules.Circle(0, 0, 0, 0, -1), "negative radius is invalid");
        Check(!Astella2DCombatRules.Circle(float.NaN, 0, 0, 0, 5), "NaN position is invalid");
        Check(!Astella2DCombatRules.Circle(0, 0, 0, 0, float.PositiveInfinity), "infinite area is invalid");
        Check(!Astella2DCombatRules.Donut(0, 0, 0, 0, 3, 5, 2.99f), "actor wholly inside the donut hole is safe");
        Check(Astella2DCombatRules.Donut(0, 0, 0, 0, 3, 5, 3), "actor touching hole boundary is hit");
        Check(Astella2DCombatRules.Donut(2, 0, 0, 0, 3, 5, 1), "disk from the hole may overlap inner rim");
        Check(!Astella2DCombatRules.Donut(2, 0, 0, 0, 3, 5, .99f), "disk short of inner rim is safe");
        Check(Astella2DCombatRules.Donut(6, 0, 0, 0, 3, 5, 1), "outer donut tangency counts");
        Check(!Astella2DCombatRules.Donut(6.001f, 0, 0, 0, 3, 5, 1), "beyond outer donut rim is safe");
        Check(Astella2DCombatRules.Donut(3, 0, 0, 0, 3, 5), "inner donut point boundary counts");
        Check(Astella2DCombatRules.Donut(5, 0, 0, 0, 3, 5), "outer donut point boundary counts");
        Check(!Astella2DCombatRules.Donut(4, 0, 0, 0, 5, 3), "reversed donut radii are invalid");
        Check(!Astella2DCombatRules.Donut(4, 0, 0, 0, 3, 5, -1), "negative actor body is invalid");
    }

    private static void Lines()
    {
        Check(Astella2DCombatRules.Line(2, 1, 0, 0, 4, 0, 1), "line includes its side boundary");
        Check(Astella2DCombatRules.Line(5, 0, 0, 0, 4, 0, 1), "capsule includes the end cap");
        Check(!Astella2DCombatRules.Line(5.001f, 0, 0, 0, 4, 0, 1), "capsule excludes beyond end cap");
        Check(Astella2DCombatRules.Line(7, 4, 0, 0, 4, 0, 3, 2), "body tangency at diagonal endpoint counts");
        Check(Astella2DCombatRules.Line(2, 2, 0, 0, 4, 0, 1, 1), "actor body expands line reach");
        Check(!Astella2DCombatRules.Line(2, 2.001f, 0, 0, 4, 0, 1, 1), "line body expansion has a finite boundary");
        Check(Astella2DCombatRules.Line(1, 1, 0, 0, 4, 4, 0), "thin diagonal line includes collinear point");
        Check(Astella2DCombatRules.Line(3, 4, 0, 0, 0, 0, 2, 3), "zero-length line is a disk");
        Check(!Astella2DCombatRules.Line(3, 4.01f, 0, 0, 0, 0, 2, 3), "zero-length line has no infinite reach");
        Check(!Astella2DCombatRules.Line(0, 0, float.NaN, 0, 4, 0, 1), "NaN line endpoint is invalid");
        Check(!Astella2DCombatRules.Line(0, 0, 0, 0, 4, 0, -1), "negative line half-width is invalid");
    }

    private static void Cones()
    {
        Check(Astella2DCombatRules.Cone(0, 0, 0, 0, 1, 0, 5, 45), "sector includes its apex");
        Check(Astella2DCombatRules.Cone(3, 3, 0, 0, 1, 0, 5, 45), "sector includes angular boundary");
        Check(!Astella2DCombatRules.Cone(3, 3.01f, 0, 0, 1, 0, 5, 45), "point outside angular boundary is safe");
        Check(Astella2DCombatRules.Cone(6, 0, 0, 0, 1, 0, 5, 45, 1), "actor touching the sector arc is hit");
        Check(!Astella2DCombatRules.Cone(6.001f, 0, 0, 0, 1, 0, 5, 45, 1), "actor beyond the sector arc is safe");
        Check(Astella2DCombatRules.Cone(3, 4, 0, 0, 1, 0, 5, 45, 1), "actor body crossing a radial edge is hit");
        Check(!Astella2DCombatRules.Cone(3, 4, 0, 0, 1, 0, 5, 45, .7f), "actor short of radial edge is safe");
        Check(!Astella2DCombatRules.Cone(4, 5, 0, 0, 1, 0, 5, 45, 1.5f),
            "separate angle/radius expansions must not invent hits outside an outer corner");
        Check(Astella2DCombatRules.Cone(4, 5, 0, 0, 1, 0, 5, 45, 1.54f), "actor actually reaching outer corner is hit");
        Check(Astella2DCombatRules.Cone(-1, 0, 0, 0, 1, 0, 5, 45, 1), "actor behind cone may touch apex");
        Check(!Astella2DCombatRules.Cone(-1, 0, 0, 0, 1, 0, 5, 45, .99f), "backside actor short of apex is safe");
        Check(!Astella2DCombatRules.Cone(1, 0, 0, 0, 0, 0, 5, 45), "missing facing does not create an arbitrary cone");
        Check(Astella2DCombatRules.Cone(2, 0, 0, 0, 1, 0, 4, 0), "zero-angle cone is a finite segment");
        Check(!Astella2DCombatRules.Cone(1000000, 1, 0, 0, 1, 0, 2000000, 0),
            "long zero-angle cones do not gain an artificial angular thickness");
        Check(!Astella2DCombatRules.Cone(5, 1, 0, 0, 1, 0, 4, 0, 1.1f), "collapsed cone still checks true endpoint distance");
        Check(Astella2DCombatRules.Cone(0, -4, 0, 0, 0, 0, 5, 180), "180-degree half angle is a full circle");
        Check(Astella2DCombatRules.Cone(2, 7, 2, 3, 0, 4, 4, 30), "translated cone normalizes its direction");
        Check(Astella2DCombatRules.Cone(-1, 2, 0, 0, 1, 0, 5, 120), "wide sectors include their rear-side interior");
        Check(!Astella2DCombatRules.Cone(-1, 1, 0, 0, 1, 0, 5, 120), "wide sectors still preserve their excluded rear wedge");
        Check(!Astella2DCombatRules.Cone(0, 0, 0, 0, 1, 0, 5, float.NaN), "NaN cone angle is invalid");
        Check(!Astella2DCombatRules.Cone(0, 0, 0, 0, 1, 0, 5, 181), "half-angle greater than 180 is invalid");
        Check(!Astella2DCombatRules.Cone(0, 0, 0, 0, float.PositiveInfinity, 0, 5, 45), "infinite facing is invalid");
    }

    public static void Main()
    {
        Ellipse(); CirclesAndDonuts(); Lines(); Cones();
        Console.WriteLine("PASS: Astella 2D geometry (" + checks + " checks). No Unity play or network verification performed.");
    }
}
