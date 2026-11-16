#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Astella.PrismV4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    /// <summary>Explicitly authors a compact 1920 layout into a new preserved copy.</summary>
    public static class AstellaPrism1920Layout
    {
        private const string Output = "Assets/UI/AstellaPrismV4/AppliedMain";
        private const string MainScene = "Assets/Scenes/SampleScene.unity";
        private const string ReportFolder = "C:/Astella/prism-v4-layout-1920-20261116";
        private const string LayoutFile = "hud-layout-prism-v4-1920.json";

        [Serializable] private sealed class RectEvidence
        {
            public string name, path;
            public Vector2 anchorMin, anchorMax, pivot, position, size;
            public Vector3 scale;
        }

        [Serializable] private sealed class AuthoringReport
        {
            public string generatedUtc, sourcePrefab, newPrefab, scene, backupFolder, layoutFile;
            public string sourceSha256Before, sourceSha256After, sourceMetaSha256Before, sourceMetaSha256After;
            public bool passed, sourceUnchanged, bindingsValidated, gameViewConfigured, presentationSavedInactive;
            public bool sceneWasAlreadyOpen, sceneWasDirty;
            public Vector2 requestedGameView = new Vector2(1920, 1080);
            public string gameViewMethod = "UnityEditor.GameView.SetCustomResolution(Vector2,string)";
            public string[] disabledPreviousHudInstances;
            public RectEvidence[] rectangles;
            public string error = "";
            public string boundary = "Editor authoring and asset-reference validation. Runtime rendering and input require Play verification.";
        }

        [MenuItem("Tools/Astella/Prism V4/Apply Compact 1920 Layout (Preserved Copy) %#F6")]
        public static void ApplyCompact1920Layout()
        {
            RequireOriginalEditor();
            var profile = RaidUIPresentation.Settings;
            if (profile == null || profile.hudPrefab == null)
                throw new InvalidOperationException("The active HUD prefab is missing.");

            string source = AssetDatabase.GetAssetPath(profile.hudPrefab);
            if (string.IsNullOrEmpty(source) || !File.Exists(source))
                throw new InvalidOperationException("The current HUD must be a saved prefab asset.");
            if (profile.hudPrefab.GetComponentInChildren<AstellaPrismHUDBridge>(true) == null)
                throw new InvalidOperationException("The active HUD is not the authored Prism view.");
            ResolveGameViewMethod(); // Verify this API before writing any copied asset.

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string folder = Output + "/Layout1920-" + stamp;
            string newPrefab = folder + "/RaidHUD.prefab";
            var report = new AuthoringReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"), sourcePrefab = source,
                newPrefab = newPrefab, scene = MainScene, layoutFile = LayoutFile,
                backupFolder = Path.Combine(ReportFolder, "Backup-" + stamp),
                sourceSha256Before = Digest(source), sourceMetaSha256Before = Digest(source + ".meta")
            };

            try
            {
                PreserveFiles(report, profile);
                EnsureFolder(Output);
                EnsureFolder(folder);
                if (!AssetDatabase.CopyAsset(source, newPrefab))
                    throw new IOException("Unable to copy the current HUD prefab.");

                var contents = PrefabUtility.LoadPrefabContents(newPrefab);
                try
                {
                    report.rectangles = Author(contents);
                    RaidUIAuthoring.ValidateScripts(contents);
                    if (PrefabUtility.SaveAsPrefabAsset(contents, newPrefab) == null)
                        throw new IOException("Unable to save the new compact HUD copy.");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }

                AssertSourceUnchanged(report);
                AstellaPrismHUDChecks.ValidatePrefab(newPrefab);
                report.bindingsValidated = true;
                ConfigureGameView();
                report.gameViewConfigured = true;
                Activate(report, profile);
                AssertSourceUnchanged(report);
                report.presentationSavedInactive = true;
                report.passed = true;
                WriteReport(report, stamp);
                Debug.Log("PRISM COMPACT 1920 APPLIED: " + newPrefab + " | Source preserved: " + source +
                    " | Evidence: " + Path.Combine(ReportFolder, "layout-authoring.json"));
            }
            catch (Exception error)
            {
                report.error = error.ToString();
                report.sourceSha256After = Digest(source);
                report.sourceMetaSha256After = Digest(source + ".meta");
                report.sourceUnchanged = report.sourceSha256Before == report.sourceSha256After &&
                    report.sourceMetaSha256Before == report.sourceMetaSha256After;
                WriteReport(report, stamp);
                throw;
            }
        }

        private static RectEvidence[] Author(GameObject root)
        {
            var bridge = root.GetComponentInChildren<AstellaPrismHUDBridge>(true);
            if (bridge == null || bridge.hud == null || bridge.playerView == null || bridge.bossView == null ||
                bridge.centerView?.button == null)
                throw new InvalidOperationException("The copied Prism HUD has incomplete view bindings.");
            var hud = bridge.hud;
            var layout = hud.GetComponent<AstellaPrismHUDLayout>();
            if (layout == null) throw new InvalidOperationException("The Prism edit-layout mapping is missing.");
            var display = root.transform.Find("Prism V4") as RectTransform;
            if (display == null) throw new InvalidOperationException("The Prism composition root is missing.");
            var canvas = root.GetComponent<Canvas>();
            var scaler = root.GetComponent<CanvasScaler>();
            if (canvas == null || scaler == null)
                throw new InvalidOperationException("The authored HUD requires its existing Canvas and CanvasScaler.");
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            // Retain the current Expand policy and its authored matching settings;
            // this layout pass does not change menu scaling at other aspect ratios.

            foreach (var fit in display.GetComponents<AstellaPrismCanvasFit>())
            {
                fit.enabled = false;
                fit.referenceSize = new Vector2(1920, 1080);
                fit.designScale = 1;
                fit.padding = Vector2.zero;
            }
            display.anchorMin = Vector2.zero;
            display.anchorMax = Vector2.one;
            display.pivot = new Vector2(.5f, .5f);
            display.offsetMin = display.offsetMax = Vector2.zero;
            display.localScale = Vector3.one;
            display.localRotation = Quaternion.identity;

            var dock = display.Find("BottomDock") as RectTransform;
            if (dock == null) dock = new GameObject("BottomDock", typeof(RectTransform)).GetComponent<RectTransform>();
            Anchored(dock, display, new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(0, 24), new Vector2(1672, 324), .5f);

            var player = (RectTransform)bridge.playerView.transform;
            Place(player, dock, 0, 0, 1672, 324);
            var hp = ParentOf(bridge.playerView.healthFill, "player health");
            var resource = ParentOf(bridge.playerView.jobResourceFill, "job resource");
            Place(hp, player, 64, 28, 620, 58);
            Place(resource, player, 986, 28, 620, 58);
            var left = Mapped(layout, "skills.left");
            var right = Mapped(layout, "skills.right");
            Place(left, player, 82, 120, 598, 136);
            Place(right, player, 1000, 120, 598, 136);

            var ultimate = (RectTransform)bridge.centerView.button.transform;
            Place(ultimate, dock, 700, 20, 272, 272);
            var potion = Array.Find(hud.slots ?? Array.Empty<AstellaOrnateHUD.SlotBinding>(),
                slot => slot != null && !slot.sharedResurrection && slot.slot == CharacterSlot.Potion);
            if (potion?.button == null) throw new InvalidOperationException("The original potion action is missing.");
            var potionRect = (RectTransform)potion.button.transform;
            Place(potionRect, player, 30, 260, 72, 64);

            RectTransform tooltip = null;
            if (hud.tooltipRoot != null)
            {
                tooltip = (RectTransform)hud.tooltipRoot.transform;
                Anchored(tooltip, display, new Vector2(.5f, 0), new Vector2(.5f, 0),
                    new Vector2(0, 204), new Vector2(640, 150), 1);
            }

            var boss = (RectTransform)bridge.bossView.transform;
            Anchored(boss, display, new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -36), new Vector2(1008, 200), .7f);
            var cast = ParentOf(bridge.bossView.castFill, "boss cast");
            var castPosition = cast.anchoredPosition;
            cast.anchoredPosition = new Vector2(248, castPosition.y);
            var bossPresenter = root.GetComponentInChildren<AstellaBossHUDV4>(true);
            if (bossPresenter?.targetRoot != null)
            {
                var target = (RectTransform)bossPresenter.targetRoot.transform;
                Place(target, boss, 248, 171, 512, 30);
                bossPresenter.repositionTargetWhenIdle = false;
                bossPresenter.scaleTargetWithComposition = false;
            }

            var party = Mapped(layout, "party");
            Anchored(party, display, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -100), new Vector2(340, 300), .75f);
            RectTransform revival = null;
            if (hud.partyRevivalRoot != null)
            {
                revival = (RectTransform)hud.partyRevivalRoot.transform;
                Anchored(revival, display, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(28, -340), new Vector2(200, 62), .75f);
            }

            MinimumFont(bridge.playerView.valueText, 32);
            MinimumFont(bridge.playerView.resourceText, 32);
            MinimumFont(bridge.playerView.nameText, 24);
            MinimumFont(hp.Find("Caption")?.GetComponent<Text>(), 26);
            foreach (var mirror in bridge.slots ?? Array.Empty<AstellaPrismHUDBridge.SlotMirror>())
            {
                if (mirror?.view == null) continue;
                MinimumFont(mirror.view.keyText, 26);
                MinimumFont(mirror.view.cooldownText, 32);
                MinimumFont(mirror.view.nameText, 24);
            }
            MinimumFont(bridge.centerView.keyText, 30);
            MinimumFont(bridge.centerView.valueText, 28);
            MinimumFont(bridge.centerView.cooldownText, 32);
            MinimumFont(potion.keycap, 26);
            MinimumFont(potion.cooldown, 32);

            layout.layoutFileName = LayoutFile;
            bridge.presentationRoot = display.gameObject;
            // The runtime bridge remains enabled on the original presenter, outside
            // this root, and decides when combat presentation may become visible.
            if (bridge.transform == display || bridge.transform.IsChildOf(display))
                throw new InvalidOperationException("The visibility bridge must remain outside the combat presentation root.");
            display.gameObject.SetActive(false);
            return new[] { display, dock, player, hp, resource, left, right, ultimate, potionRect,
                tooltip, boss, cast, party, revival }.Where(rect => rect != null).Select(Describe).ToArray();
        }

        private static void Activate(AuthoringReport report, RaidUIPresentation profile)
        {
            var scene = SceneManager.GetSceneByPath(MainScene);
            report.sceneWasAlreadyOpen = scene.IsValid() && scene.isLoaded;
            if (!report.sceneWasAlreadyOpen)
            {
                for (int index = 0; index < SceneManager.sceneCount; index++)
                    if (SceneManager.GetSceneAt(index).isDirty)
                        throw new InvalidOperationException("Save the currently edited scene before opening the main scene.");
                scene = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
            }
            report.sceneWasDirty = scene.isDirty;
            var previous = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<RaidUIRoot>(true))
                .Where(owner => owner.gameObject.activeInHierarchy).ToArray();
            report.disabledPreviousHudInstances = previous.Select(owner => owner.gameObject.name).ToArray();
            foreach (var owner in previous)
            {
                owner.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(owner.gameObject);
            }
            // Resolve after OpenScene, which can unload unused prefab references.
            var saved = AssetDatabase.LoadAssetAtPath<RaidUIRoot>(report.newPrefab);
            if (saved == null) throw new InvalidOperationException("The compact HUD asset could not be resolved.");
            var instance = PrefabUtility.InstantiatePrefab(saved.gameObject, scene) as GameObject;
            if (instance == null) throw new IOException("Unable to instantiate the compact HUD in the main scene.");
            instance.name = "Astella_PrismV4HUD_1920";
            instance.SetActive(true);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Unable to save the activated main scene.");
            profile = RaidUIPresentation.Settings;
            profile.hudPrefab = saved;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        private static void PreserveFiles(AuthoringReport report, RaidUIPresentation profile)
        {
            Directory.CreateDirectory(report.backupFolder);
            File.Copy(report.sourcePrefab, Path.Combine(report.backupFolder, "SourceRaidHUD.prefab"));
            File.Copy(report.sourcePrefab + ".meta", Path.Combine(report.backupFolder, "SourceRaidHUD.prefab.meta"));
            string profilePath = AssetDatabase.GetAssetPath(profile);
            File.Copy(profilePath, Path.Combine(report.backupFolder, "RaidUIPresentation.asset"));
            File.Copy(MainScene, Path.Combine(report.backupFolder, "SampleScene.unity"));
        }

        private static void RequireOriginalEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Apply the compact HUD outside Play and Prefab Mode.");
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'),
                @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Apply only from the original Astella Unity project.");
        }

        private static MethodInfo ResolveGameViewMethod()
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            var method = type?.GetMethod("SetCustomResolution", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Vector2), typeof(string) }, null);
            if (method == null) throw new MissingMethodException("Verified GameView.SetCustomResolution(Vector2,string) API is unavailable.");
            return method;
        }

        private static void ConfigureGameView()
        {
            var method = ResolveGameViewMethod();
            var view = EditorWindow.GetWindow(method.DeclaringType);
            method.Invoke(view, new object[] { new Vector2(1920, 1080), "Astella 1920x1080" });
            view.Repaint();
        }

        private static void AssertSourceUnchanged(AuthoringReport report)
        {
            report.sourceSha256After = Digest(report.sourcePrefab);
            report.sourceMetaSha256After = Digest(report.sourcePrefab + ".meta");
            report.sourceUnchanged = report.sourceSha256Before == report.sourceSha256After &&
                report.sourceMetaSha256Before == report.sourceMetaSha256After;
            if (!report.sourceUnchanged)
                throw new IOException("The source HUD prefab or GUID metadata changed; inspect the preserved backup.");
        }

        private static string Digest(string file)
        {
            if (!File.Exists(file)) return "<missing>";
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").ToLowerInvariant();
        }

        private static void WriteReport(AuthoringReport report, string stamp)
        {
            Directory.CreateDirectory(ReportFolder);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(ReportFolder, "layout-authoring-" + stamp + ".json"), json);
            File.WriteAllText(Path.Combine(ReportFolder, "layout-authoring.json"), json);
        }

        private static RectTransform ParentOf(Component graphic, string name)
        {
            if (graphic == null || graphic.transform.parent is not RectTransform parent)
                throw new InvalidOperationException("Missing layout parent for " + name);
            return parent;
        }

        private static RectTransform Mapped(AstellaPrismHUDLayout layout, string id)
        {
            var entry = layout.MappedEntries.FirstOrDefault(value => value.id == id);
            if (entry?.rect == null) throw new InvalidOperationException("Missing Prism layout entry: " + id);
            return entry.rect;
        }

        private static void Place(RectTransform rect, Transform parent, float x, float y, float width, float height)
            => Anchored(rect, parent, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(x, -y), new Vector2(width, height), 1);

        private static void Anchored(RectTransform rect, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size, float scale)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * scale;
            rect.localRotation = Quaternion.identity;
            var element = rect.GetComponent<LayoutElement>();
            if (element != null) element.ignoreLayout = true;
        }

        private static void MinimumFont(Text text, int minimum)
        {
            if (text == null) return;
            text.fontSize = Mathf.Max(minimum, text.fontSize);
            if (text.resizeTextForBestFit)
            {
                text.resizeTextMinSize = Mathf.Max(minimum, text.resizeTextMinSize);
                text.resizeTextMaxSize = Mathf.Max(text.fontSize, text.resizeTextMaxSize);
            }
        }

        private static RectEvidence Describe(RectTransform rect)
        {
            var names = new List<string>();
            for (var current = rect.transform; current != null; current = current.parent) names.Add(current.name);
            names.Reverse();
            return new RectEvidence
            {
                name = rect.name, path = string.Join("/", names), anchorMin = rect.anchorMin, anchorMax = rect.anchorMax,
                pivot = rect.pivot, position = rect.anchoredPosition, size = rect.sizeDelta, scale = rect.localScale
            };
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
#endif
