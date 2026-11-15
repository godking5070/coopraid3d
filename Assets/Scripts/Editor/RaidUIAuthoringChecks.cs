#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    public static class RaidUIAuthoringChecks
    {
        private static int _checks;

        [MenuItem("Tools/RaidRogue/UI/Run Prefab Safety Checks")]
        public static void Run()
        {
            RaidUIBindingChecks.Run();
            _checks = 0;
            string folder = "Assets/__RaidUIAuthoringCheck_" + Guid.NewGuid().ToString("N");
            GameObject host = null, loaded = null;
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            try
            {
                host = new GameObject("Prefab safety fixture", typeof(RectTransform));
                host.SetActive(false);
                var bindings = host.AddComponent<RaidUIView>();
                var slots = new GameObject[4];
                for (int i = 0; i < slots.Length; i++)
                {
                    var slot = new GameObject("Slot " + i, typeof(RectTransform), typeof(Image), typeof(Button), typeof(CharacterSlotHover), typeof(RaidUIRepeatedItem));
                    slot.transform.SetParent(host.transform, false);
                    var rect = (RectTransform)slot.transform;
                    rect.anchoredPosition = new Vector2(70 * i, 29);
                    rect.sizeDelta = new Vector2(60, 60);
                    var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
                    label.transform.SetParent(slot.transform, false);
                    var item = slot.GetComponent<RaidUIRepeatedItem>();
                    item.button = slot.GetComponent<Button>(); item.button.targetGraphic = slot.GetComponent<Image>();
                    item.label = label.GetComponent<Text>();
                    label.AddComponent<LocalShortcutText>().SetTemplate("[1] {key:Dash}");
                    bindings.references.Add(new RaidUIView.Reference { path = "button" + i, target = item.button });
                    bindings.references.Add(new RaidUIView.Reference { path = "rect" + i, target = rect });
                    bindings.references.Add(new RaidUIView.Reference { path = "object" + i, target = slot });
                    slots[i] = slot;
                }
                RaidUIAuthoring.ValidateScripts(host);
                var prefab = PrefabUtility.SaveAsPrefabAsset(slots[0], folder + "/Slot.prefab");
                Check(prefab != null && prefab.GetComponent<CharacterSlotHover>() != null, "hover survives prefab serialization");
                RaidUIAuthoring.ValidateScripts(prefab);
                for (int i = 0; i < slots.Length; i++)
                {
                    RaidUIAuthoring.Replace(host, slots[i], prefab);
                    Check(host.transform.childCount == 4, "parent retains exactly four children");
                    var button = bindings.references[i * 3].target as Button;
                    Check(button != null && button.transform.parent == host.transform, "button mapped to new instance");
                    Check(bindings.references[i * 3 + 1].target == button.transform && bindings.references[i * 3 + 2].target == button.gameObject, "explicit transform and GameObject fields mapped");
                    Check(button.transform.GetSiblingIndex() == i && ((RectTransform)button.transform).anchoredPosition == new Vector2(70 * i, 29), "sibling order and authored position preserved");
                    Check(button.GetComponent<RaidUIRepeatedItem>().button == button && button.targetGraphic.gameObject == button.gameObject, "script references retain ownership");
                    Check(button.GetComponent<CharacterSlotHover>() != null, "hover remains attached to its own slot");
                    foreach (var transform in host.GetComponentsInChildren<Transform>(true))
                    foreach (var component in transform.GetComponents<Component>())
                        Check(component != null && component.gameObject == transform.gameObject && component.transform == transform, "native component ownership unchanged");
                }
                string path = folder + "/HUD.prefab";
                Check(PrefabUtility.SaveAsPrefabAsset(host, path) != null, "nested prefab saves after replacements");
                loaded = PrefabUtility.LoadPrefabContents(path);
                RaidUIAuthoring.ValidateScripts(loaded);
                var restored = loaded.GetComponent<RaidUIView>();
                Check(restored.references.Count == 12 && loaded.transform.childCount == 4, "saved bindings and children survive reload");
                foreach (var reference in restored.references) Check(reference.target != null, "saved reference survives reload");
                foreach (var shortcut in loaded.GetComponentsInChildren<LocalShortcutText>(true))
                {
                    Check(shortcut.Template == "[1] {key:Dash}", "shortcut template survives prefab serialization");
                    Check(AssetDatabase.Contains(MonoScript.FromMonoBehaviour(shortcut)), "shortcut MonoScript is a persistent asset");
                }
                Debug.Log("UI PREFAB SAFETY CHECKS PASS: " + _checks + " assertions, plus 15 binding assertions. Four consecutive replacements, native ownership, script assets and prefab save/reload.");
            }
            finally
            {
                if (loaded != null) PrefabUtility.UnloadPrefabContents(loaded);
                if (host != null) Object.DestroyImmediate(host);
                // Only this invocation's GUID-named test assets; never the user's editable UI.
                AssetDatabase.DeleteAsset(folder);
            }
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch entry point requires -batchmode.");
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        private static void Check(bool condition, string name)
        { if (!condition) throw new InvalidOperationException("UI prefab check failed: " + name); _checks++; }
    }
}
#endif
