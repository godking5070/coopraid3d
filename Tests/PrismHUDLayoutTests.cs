using System;
using System.Linq;
using RaidRogue;
using UnityEngine;

// Runs the actual optional layout component without opening Unity or touching user saves.
namespace UnityEngine
{
    public class MonoBehaviour { }
    public class RectTransform { public string authoredPose; }
    public sealed class DisallowMultipleComponent : Attribute { }
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string value) { } }
}

internal static class PrismHUDLayoutTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }
    public static void Main()
    {
        var layout = new AstellaPrismHUDLayout();
        Check(layout.FileName == "hud-layout-prism-v4.json", "Prism defaults use a separate local file.");
        Check(!layout.MappedEntries.Any(), "Empty mappings allow legacy discovery to supply optional panels.");
        foreach (string invalid in new[] { null, "", " ", "hud-layout.json", "HUD-LAYOUT.JSON", "../hud.json", "a/b.json", "a\\b.json", "C:\\hud.json", "hud.txt", new string('x', 129) + ".json" })
            Check(AstellaPrismHUDLayout.ResolveFileName(invalid) == AstellaPrismHUDLayout.DefaultFileName,
                "Invalid paths and the legacy save name cannot replace the Prism layout file.");
        foreach (string valid in new[] { "hud-layout-prism-v5.json", "private-layout.JSON", "ui.2026.json" })
            Check(AstellaPrismHUDLayout.ResolveFileName(valid) == valid, "Explicit local JSON filenames are retained.");
        Check(AstellaPrismHUDLayout.ResolveFileName("  private.json  ") == "private.json", "Filename surrounding whitespace is ignored.");

        var authored = new RectTransform { authoredPose = "authored anchors/position/scale" };
        string[] ids = { "skills.left", "skills.right", "identity", "ultimate", "hp", "ultimate.resource", "potion", "revival", "boss", "boss.cast", "party", "minimap", "enemies", "metrics" };
        layout.entries = ids.Select(id => new AstellaPrismHUDLayout.Entry { id = id, name = "Prism " + id, rect = authored }).ToArray();
        var mapped = layout.MappedEntries.ToArray();
        Check(mapped.Length == ids.Length, "All existing stable layout IDs remain available.");
        for (int i = 0; i < ids.Length; i++)
        {
            Check(mapped[i].id == ids[i], "Mapping does not rename a saved layout ID.");
            Check(ReferenceEquals(mapped[i].rect, authored), "Mapping retains the explicit RectTransform rather than discovering a legacy source.");
        }
        Check(authored.authoredPose == "authored anchors/position/scale", "Resolving mapping references does not rewrite authored geometry.");
        layout.entries = new[] {
            null,
            new AstellaPrismHUDLayout.Entry { id = "", rect = authored },
            new AstellaPrismHUDLayout.Entry { id = " ", rect = authored },
            new AstellaPrismHUDLayout.Entry { id = new string('x', 81), rect = authored },
            new AstellaPrismHUDLayout.Entry { id = "hp", rect = null },
            new AstellaPrismHUDLayout.Entry { id = "hp", name = "Prism health", rect = authored }
        };
        Check(layout.MappedEntries.Count() == 1, "Broken optional entries are ignored without suppressing fallback discovery.");
        Check(layout.MappedEntries.Single().name == "Prism health", "Explicit display labels are retained.");
        layout.entries = null;
        Check(!layout.MappedEntries.Any(), "Null serialized entry arrays are safe.");
        Console.WriteLine("PASS: " + _checks + " Prism HUD mapping/file-isolation checks; managed component only, no Unity layout or user saves.");
    }
}
