using System;
using RaidRogue;
using UnityEngine;
using UnityEngine.UI;

// Runs the actual icon component with small managed stand-ins, not Unity rendering.
namespace UnityEngine
{
    public class MonoBehaviour { }
    public class Sprite { }
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
}
namespace UnityEngine.UI
{
    public class Image { public Sprite sprite; public bool enabled = true; }
}
namespace RaidRogue
{
    public class RaidUIPresentation
    {
        public static RaidUIPresentation Settings;
        public Sprite fallback;
        public int lookups;
        public Sprite Icon(string id) { lookups++; return fallback; }
    }
}
public static class UIIconTests
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    public static void Main()
    {
        var authored = new Sprite();
        var icon = new RaidUIIcon { image = new Image(), overrideSprite = authored };
        icon.Show(null);
        Check(icon.image.enabled && icon.image.sprite == authored, "First null ID must resolve the authored override.");

        var missing = new RaidUIIcon { image = new Image() };
        missing.Show(null);
        Check(!missing.image.enabled, "An unresolved first null ID must hide the empty image.");

        var fallback = new Sprite();
        RaidUIPresentation.Settings = new RaidUIPresentation { fallback = fallback };
        var delayed = new RaidUIIcon();
        delayed.Show("skill.one");
        delayed.image = new Image();
        delayed.Show("skill.one");
        Check(delayed.image.sprite == fallback, "An early call without an image must not poison the ID cache.");
        int lookups = RaidUIPresentation.Settings.lookups;
        delayed.Show("skill.one");
        Check(RaidUIPresentation.Settings.lookups == lookups, "An unchanged resolved icon must retain its lookup cache.");

        delayed.image = new Image();
        delayed.Show("skill.one");
        Check(delayed.image.sprite == fallback, "Replacing the image must resolve the same ID again.");
        delayed.overrideSprite = authored;
        delayed.Show("skill.one");
        Check(delayed.image.sprite == authored, "Changing an override must refresh even with the same ID.");
        delayed.overrideSprite = null;
        delayed.Show("skill.one");
        Check(delayed.image.sprite == fallback, "Removing an override must restore the configured fallback.");

        var nullFallback = new RaidUIIcon { image = new Image() };
        nullFallback.Show(null);
        Check(nullFallback.image.enabled && nullFallback.image.sprite == fallback, "First null ID must resolve the fallback.");
        Console.WriteLine("PASS: UI icon checks " + checks);
    }
}
