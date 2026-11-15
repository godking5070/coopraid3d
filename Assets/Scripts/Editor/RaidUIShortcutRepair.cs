#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    public static class RaidUIShortcutRepair
    {
        private const string ProfilePath = "Assets/Resources/UI/RaidUIPresentation.asset";

        [MenuItem("Tools/RaidRogue/UI/Repair Legacy Shortcut Scripts")]
        public static void Repair()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before repairing the saved UI.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (profile == null || profile.hudPrefab == null) throw new InvalidOperationException("Active HUD missing.");
            string source = AssetDatabase.GetAssetPath(profile.hudPrefab);
            // Read only: identify the exact legacy failure. All asset writes use Unity APIs.
            var missing = Regex.Matches(File.ReadAllText(source), @"m_Script: \{fileID: 0\}\s+m_Name:[^\r\n]*\s+m_EditorClassIdentifier: ([^\r\n]+)");
            if (missing.Count == 0) { RaidUIAuthoring.Validate(profile); Debug.Log("UI SHORTCUT REPAIR: active HUD is already valid; no changes."); return; }
            if (missing.Cast<Match>().Any(m => m.Groups[1].Value.Trim() != "Assembly-CSharp::RaidRogue.LocalShortcutText"))
                throw new InvalidOperationException("Unknown missing script found; preserving the HUD without changing it.");

            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(source);
                var nodes = contents.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject)
                    .Where(g => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(g) > 0).ToArray();
                if (nodes.Length != missing.Count || nodes.Any(g => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(g) != 1 || g.GetComponent<Text>() == null))
                    throw new InvalidOperationException("Missing scripts do not match the known shortcut-label defect; no repair applied.");
                foreach (var node in nodes)
                {
                    // Only the loaded COPY, after every missing slot was identified above.
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(node);
                    var label = node.GetComponent<LocalShortcutText>() ?? node.AddComponent<LocalShortcutText>();
                    label.SetTemplate(node.GetComponent<Text>().text);
                }
                // Old embedded presenter references may point at the former missing component.
                foreach (var binding in contents.GetComponent<RaidUIRoot>().presenters)
                    if (binding.typeName == typeof(LocalShortcutText).AssemblyQualifiedName)
                    {
                        var text = binding.references.FirstOrDefault(r => r.path == "_text")?.target as Text;
                        if (text != null) binding.embeddedPresenter = text.GetComponent<LocalShortcutText>();
                    }
                RaidUIAuthoring.ValidateScripts(contents);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                string folder = "Assets/UI/Generated/ShortcutRepair-" + stamp;
                AssetDatabase.CreateFolder("Assets/UI/Generated", "ShortcutRepair-" + stamp);
                string repairedPath = folder + "/RaidHUD.prefab";
                var repaired = PrefabUtility.SaveAsPrefabAsset(contents, repairedPath);
                AssetDatabase.ImportAsset(repairedPath, ImportAssetOptions.ForceUpdate);
                if (Regex.IsMatch(File.ReadAllText(repairedPath), @"m_Script: \{fileID: 0\}"))
                    throw new InvalidOperationException("Saved repair still has a missing script; active profile was preserved.");
                var candidate = Object.Instantiate(profile);
                try
                {
                    candidate.hudPrefab = repaired.GetComponent<RaidUIRoot>();
                    RaidUIAuthoring.Validate(candidate);
                    string backup = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "UIRepairBackups", stamp);
                    Directory.CreateDirectory(backup);
                    File.Copy(ProfilePath, Path.Combine(backup, "RaidUIPresentation.asset"));
                    File.Copy(ProfilePath + ".meta", Path.Combine(backup, "RaidUIPresentation.asset.meta"));
                    profile.hudPrefab = candidate.hudPrefab;
                    EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
                }
                finally { Object.DestroyImmediate(candidate); }
                Selection.activeObject = repaired;
                Debug.Log("UI SHORTCUT REPAIR PASS: " + nodes.Length + " labels restored in " + repairedPath + ". Original prefab/layout/assets kept; active profile points to the repaired copy.");
            }
            finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
        }
    }
}
#endif
