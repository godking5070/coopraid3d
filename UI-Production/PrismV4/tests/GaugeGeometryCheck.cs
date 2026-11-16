using System;
using System.Collections.Generic;
using Astella.PrismV4;
using UnityEngine;

// Calls the actual renderer geometry path without opening Unity or constructing a Canvas.
internal static class GaugeGeometryCheck
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static float MaxAlong(List<Vector2> points, bool vertical)
    {
        float maximum = 0;
        foreach (var point in points) maximum = Math.Max(maximum, vertical ? point.y : point.x);
        return maximum;
    }

    private static double Area(List<Vector2> points)
    {
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var next = points[(i + 1) % points.Count];
            area += (double)point.x * next.y - (double)next.x * point.y;
        }
        return Math.Abs(area) * .5;
    }

    private static void Main()
    {
        // Independent expected boss/cast/player/party geometry from Source/art-manifest.json.
        var bounds = new[] {
            new Rect(27f / 1280, 29f / 96, 1226f / 1280, 38f / 96),
            new Rect(15f / 512, 20f / 48, 482f / 512, 8f / 48),
            new Rect(29f / 1024, 18f / 96, 966f / 1024, 60f / 96),
            new Rect(14f / 320, 12f / 40, 292f / 320, 16f / 40)
        };
        var tips = new[] { 22f / 1280, 7f / 512, 36f / 1024, 10f / 320 };
        var polygon = new List<Vector2>();
        int cases = 0;
        for (int i = 0; i < bounds.Length; i++)
        {
            double previousArea = -1;
            foreach (float ratio in new[] { 0f, .001f, .01f, .5f, .6f, .99f, 1f })
            {
                PrismV4Gauge.BuildClipPolygon(ratio, bounds[i], tips[i], PrismV4Gauge.Direction.LeftToRight, polygon);
                if (ratio == 0) Check(polygon.Count == 0, "0% must hide active fill.");
                else if (ratio == 1)
                    Check(polygon.Count == 4 && Math.Abs(Area(polygon) - 1) < .00001,
                        "100% must retain the full source UV rectangle including both alpha tips.");
                else
                    Check(Math.Abs(MaxAlong(polygon, false) - (bounds[i].xMin + bounds[i].width * ratio)) < .00001,
                        "The moving front must match the ratio in original source coordinates.");

                double area = Area(polygon);
                Check(area + 1e-7 >= previousArea, "Reveal area must increase monotonically.");
                previousArea = area;
                foreach (var point in polygon)
                    Check(point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1,
                        "Geometry/UV coordinates must remain inside the full sprite rectangle.");
                cases++;
            }
        }

        foreach (float ratio in new[] { 0f, .5f, 1f })
        {
            PrismV4Gauge.BuildClipPolygon(ratio, new Rect(0, 0, 1, 1), 0, PrismV4Gauge.Direction.BottomToTop, polygon);
            Check(ratio == 0 ? polygon.Count == 0 : Math.Abs(MaxAlong(polygon, true) - ratio) < .00001,
                "Central ultimate must reveal vertically and remain independent of the job-resource gauge.");
            cases++;
        }

        PrismV4Gauge.BuildClipPolygon(float.NaN, new Rect(0, 0, 1, 1), 0, PrismV4Gauge.Direction.LeftToRight, polygon);
        Check(polygon.Count == 0, "NaN must not produce a full or corrupt fill.");
        cases++;
        Console.WriteLine("PASS: " + cases + " actual C# geometry cases (boss/cast/player/party, 0/.5/1, monotonic reveal, fixed source UV and vertical ultimate).");
    }
}
