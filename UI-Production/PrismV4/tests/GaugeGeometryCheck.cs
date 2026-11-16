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

    private static Rect Bounds(List<Vector2> points)
    {
        float left = 1, bottom = 1, right = 0, top = 0;
        foreach (var point in points)
        {
            left = Math.Min(left, point.x); bottom = Math.Min(bottom, point.y);
            right = Math.Max(right, point.x); top = Math.Max(top, point.y);
        }
        return Rect.MinMaxRect(left, bottom, right, top);
    }

    private static void Near(float actual, float expected, string message)
    {
        Check(Math.Abs(actual - expected) < .00001, message + " actual=" + actual + " expected=" + expected);
    }

    private static void Main()
    {
        foreach (var field in typeof(SpriteMeshType).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            Console.WriteLine("SpriteMeshType " + field.Name + "=" + field.GetRawConstantValue());
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

        // Regression: Tight/atlas sprites expose trimmed OuterUV. The custom fill must
        // have the same padded visible bounds as an ordinary uGUI Image, not stretch
        // that small texture area across its entire 96px canvas.
        var sourceSizes = new[] {
            new Vector2(1280, 96), new Vector2(512, 48), new Vector2(1024, 96),
            new Vector2(320, 40), new Vector2(256, 128), new Vector2(256, 128)
        };
        var paddings = new[] {
            new Vector4(26, 28, 26, 28), new Vector4(14, 19, 14, 19), new Vector4(28, 17, 28, 17),
            new Vector4(13, 11, 13, 11), new Vector4(12, 7, 31, 19), Vector4.zero
        };
        var scratch = new List<Vector2>();
        var atlasUV = new Vector4(.125f, .25f, .625f, .875f);
        for (int i = 0; i < sourceSizes.Length; i++)
        {
            Vector2 sourceSize = sourceSizes[i];
            Vector4 padding = paddings[i];
            Rect visible = PrismV4Gauge.GetVisibleSpriteRect(padding, sourceSize);
            Rect fillBounds = i < bounds.Length ? bounds[i] : new Rect(0, 0, 1, 1);
            float tip = i < tips.Length ? tips[i] : 0;
            foreach (float ratio in new[] { 0f, .01f, .5f, .6f, 1f })
            {
                PrismV4Gauge.BuildClipPolygon(ratio, fillBounds, tip, PrismV4Gauge.Direction.LeftToRight, polygon);
                PrismV4Gauge.ClipToVisibleRect(polygon, visible, scratch);
                if (ratio == 0) Check(polygon.Count == 0, "Trimmed zero must remain empty.");
                foreach (var point in polygon)
                {
                    Check(point.x >= visible.xMin - .00001 && point.x <= visible.xMax + .00001 &&
                        point.y >= visible.yMin - .00001 && point.y <= visible.yMax + .00001,
                        "No fill vertex may expand outside the trimmed sprite's original padded region.");
                    Vector2 uv = PrismV4Gauge.MapSourcePointToUV(point, visible, atlasUV);
                    Check(uv.x >= atlasUV.x - .00001 && uv.x <= atlasUV.z + .00001 &&
                        uv.y >= atlasUV.y - .00001 && uv.y <= atlasUV.w + .00001,
                        "Clipped fill must never sample a neighboring atlas sprite.");
                }
                if (ratio == 1)
                {
                    Rect actual = Bounds(polygon);
                    // Independent equivalent of uGUI Image.GetDrawingDimensions(false).
                    Near(actual.xMin, padding.x / sourceSize.x, "Full left must match Image padding");
                    Near(actual.yMin, padding.y / sourceSize.y, "Full bottom must match Image padding");
                    Near(actual.xMax, (sourceSize.x - padding.z) / sourceSize.x, "Full right must match Image padding");
                    Near(actual.yMax, (sourceSize.y - padding.w) / sourceSize.y, "Full top must match Image padding");
                    Vector2 bottomLeft = PrismV4Gauge.MapSourcePointToUV(actual.min, visible, atlasUV);
                    Vector2 topRight = PrismV4Gauge.MapSourcePointToUV(actual.max, visible, atlasUV);
                    Near(bottomLeft.x, atlasUV.x, "Visible left maps to OuterUV left");
                    Near(bottomLeft.y, atlasUV.y, "Visible bottom maps to OuterUV bottom");
                    Near(topRight.x, atlasUV.z, "Visible right maps to OuterUV right");
                    Near(topRight.y, atlasUV.w, "Visible top maps to OuterUV top");
                    if (i == 0) Near(actual.height * 72, 30, "Boss visible texture must occupy 30px, not the entire 72px Image rect");
                }
                cases++;
            }
        }

        Rect aspect = PrismV4Gauge.GetAspectDrawingRect(new Rect(-200, -100, 400, 200), new Vector2(100, 100), new Vector2(.25f, .5f), true);
        Near(aspect.x, -150, "Image preserveAspect uses the authored horizontal pivot");
        Near(aspect.width, 200, "Square sprite stays square");
        Near(aspect.height, 200, "Square sprite retains height");
        cases++;
        aspect = PrismV4Gauge.GetAspectDrawingRect(new Rect(0, 0, 100, 200), new Vector2(400, 100), new Vector2(.5f, .25f), true);
        Near(aspect.y, 43.75f, "Image preserveAspect uses the authored vertical pivot");
        Near(aspect.height, 25, "Wide sprite retains its source aspect ratio");
        cases++;
        Console.WriteLine("PASS: " + cases + " actual C# geometry cases (32 original fill cases + 30 trimmed/atlas padding cases + 2 aspect/pivot cases).");
    }
}
