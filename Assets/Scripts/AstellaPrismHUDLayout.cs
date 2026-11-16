using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RaidRogue
{
    // Optional authored layout references. This component never changes transforms.
    [DisallowMultipleComponent]
    public sealed class AstellaPrismHUDLayout : MonoBehaviour
    {
        public const string DefaultFileName = "hud-layout-prism-v4.json";
        [Serializable] public sealed class Entry
        {
            public string id, name;
            public RectTransform rect;
        }

        public Entry[] entries = Array.Empty<Entry>();
        [Tooltip("Local layout file for this design. The legacy HUD layout stays separate.")]
        public string layoutFileName = DefaultFileName;

        public string FileName => ResolveFileName(layoutFileName);
        public IEnumerable<Entry> MappedEntries
        {
            get
            {
                foreach (var entry in entries ?? Array.Empty<Entry>())
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.id) && entry.id.Length <= 80 && entry.rect != null)
                        yield return entry;
            }
        }

        public static string ResolveFileName(string candidate)
        {
            string name = candidate?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 128 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "hud-layout.json", StringComparison.OrdinalIgnoreCase)) return DefaultFileName;
            return name;
        }
    }
}
