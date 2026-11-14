#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    /// <summary>Separate class ultimate and primary-resource instruments, authored from the supplied v6/v8 layers.</summary>
    public static class AstellaClassHubsInstaller
    {
        private const string Folder = "Assets/UI/AstellaClassHubs";
        private const string MotionFolder = "Assets/UI/Resources/AstellaUltimateMotion";
        private const string Stamp = "Logs/ClassHubs-v6-v8.applied.txt";
        private static Font font;
        [Serializable] private sealed class Manifest { public HubSpec[] classes; }
        [Serializable] private sealed class HubSpec { public string jobId, slug; public JobSpec job; public UltimateSpec ultimate; }
        [Serializable] private sealed class JobSpec
        { public string kind, track, frame, fill, direction; public float[] fillBounds, value; public int fontSize; public bool showMaximum; public TokenSpec[] tokens; }
        [Serializable] private sealed class TokenSpec { public string active, empty; public float[] rect; }
        [Serializable] private sealed class UltimateSpec
        { public float[] chargeBounds, value; public StateSpec charging, ready, unavailable; public MotionSpec burst, loop; }
        [Serializable] private sealed class StateSpec { public string track, fill, frame, core, glow, badge, symbol; }
        [Serializable] private sealed class MotionSpec
        { public string[] sheetResourcePaths; public int frameCount, framesPerSheet, columns; public float framesPerSecond; }

        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            if (File.Exists(Stamp)) return;
            EditorApplication.update -= WhenIdle; EditorApplication.update += WhenIdle;
        }
        private static void WhenIdle()
        {
            if (File.Exists(Stamp)) { EditorApplication.update -= WhenIdle; return; }
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
            { EditorApplication.update -= WhenIdle; return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity") return;
            // The preceding requested authoring pass supplies the exact existing action/layout roots.
            if (!File.Exists("Logs/PlayerHUD-v3.applied.txt")) return;
            EditorApplication.update -= WhenIdle;
            try { Apply(); }
            catch (Exception error)
            {
                Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/ClassHubs-v6-v8.error.txt", error.ToString());
                Debug.LogError("Class hubs upgrade stopped; backups retained. " + error);
            }
        }

        [MenuItem("Tools/RaidRogue/UI/Apply Class Ultimate and Job Hubs")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open Astella SampleScene.");
            var live = scene.GetRootGameObjects().Where(g => g.activeSelf).SelectMany(g => g.GetComponentsInChildren<AstellaOrnateHUD>(true))
                .Where(h => h.gameObject.activeInHierarchy).ToArray();
            if (live.Length != 1) throw new InvalidOperationException("Expected one active main-scene HUD.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>("Assets/Resources/UI/RaidUIPresentation.asset");
            if (profile?.hudPrefab == null) throw new InvalidOperationException("Active HUD prefab missing.");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "/class-hubs-manifest.json"));
            if (manifest?.classes?.Length != 12) throw new InvalidOperationException("Twelve independent class pairs are required.");
            Import(); font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            string path = AssetDatabase.GetAssetPath(profile.hudPrefab);
            string backup = "Logs/UIRepairBackups/class-hubs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            foreach (string file in new[] { path, path + ".meta", scene.path, scene.path + ".meta" }) File.Copy(file, backup + "/" + Path.GetFileName(file));
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Build(contents.GetComponentInChildren<AstellaOrnateHUD>(true), manifest);
                RaidUIAuthoring.ValidateScripts(contents);
                if (PrefabUtility.SaveAsPrefabAsset(contents, path) == null) throw new IOException(path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssets();
            Build(live[0], manifest); Sync(live[0]);
            RaidUIAuthoring.Validate(profile);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Stamp, DateTime.Now.ToString("s") + "\n" + path + "\nBackup: " + backup +
                "\nTwelve class-specific ultimate + job instruments. Independent actual resources; v6 class sculpting and v8 state/animation/job layers. Side skill art remains pending.");
            Debug.Log("CLASS HUBS V6/V8 APPLIED: independent class ultimate charge and primary job resource, actual HP, continuous/count instruments and lazy motion sheets. Backup: " + backup);
        }

        private static void Import()
        {
            foreach (string folder in new[] { Folder, MotionFolder })
                foreach (string path in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    bool motion = path.Replace('\\', '/').Contains("/Resources/");
                    importer.textureType = motion ? TextureImporterType.Default : TextureImporterType.Sprite;
                    if (!motion)
                    {
                        importer.spriteImportMode = SpriteImportMode.Single;
                        var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings); settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
                    }
                    importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = 2048;
                    importer.SaveAndReimport();
                }
        }

        private static void Build(AstellaOrnateHUD hud, Manifest manifest)
        {
            if (hud?.playerRoot == null || hud.resourceRoot == null) throw new InvalidOperationException("Bound player HUD is incomplete.");
            var player = hud.playerRoot.transform;
            var existing = player.GetComponent<AstellaClassUltimateHUD>();
            if (existing != null)
            {
                var priorJob = player.GetComponent<AstellaClassJobHUD>();
                if (existing.views?.Length != 12 || priorJob?.views?.Length != 12 ||
                    existing.views.Any(v => v?.root == null || v.charging?.fillMask == null || v.ready?.fillMask == null || v.unavailable?.fillMask == null) ||
                    priorJob.views.Any(v => v?.root == null))
                    throw new InvalidOperationException("Partial class-hub hierarchy detected; restore the retained backup before retrying.");
                return;
            }
            var readiness = player.GetComponent<AstellaPlayerHUDV3>();
            if (readiness == null) throw new InvalidOperationException("Player HUD v3 readiness/data bridge missing.");
            readiness.ultimateArtwork = null; readiness.readyGlow = null; readiness.disabledOverlay = null; readiness.compactUltimateValue = false;
            var left = (RectTransform)player.Find("Left - 1234"); var right = (RectTransform)player.Find("Right - Q F R Space");
            var center = (RectTransform)player.Find("Center - V");
            Place(left, new Vector2(-222, 68), new Vector2(560, 128), .5f);
            Place(right, new Vector2(222, 68), new Vector2(560, 128), .5f);
            Place(center, new Vector2(0, 89.77778f), new Vector2(360, 280), 4f / 9f);
            CharacterSlot[] first = { CharacterSlot.Gcd1, CharacterSlot.Gcd2, CharacterSlot.OffGcd1, CharacterSlot.Counter };
            CharacterSlot[] second = { CharacterSlot.Job1, CharacterSlot.Movement, CharacterSlot.Identity, CharacterSlot.OffGcd2 };
            float[] positions = { -216, -72, 72, 216 };
            for (int i = 0; i < 4; i++)
            {
                Center((RectTransform)Slot(hud, first[i]).button.transform, new Vector2(positions[i], 0), new Vector2(128, 128));
                Center((RectTransform)Slot(hud, second[i]).button.transform, new Vector2(positions[i], 0), new Vector2(128, 128));
            }
            var hp = (RectTransform)hud.playerHpText.transform.parent;
            Place(hp, new Vector2(-222, 122), new Vector2(560, 60), .5f);
            hud.showOnlyCurrentHealth = true; hud.playerHpText.fontSize = 44; hud.playerHpText.resizeTextMaxSize = 44;
            var label = Label("Health label", hp, 10, -34, 200, 32, 24); label.text = "체력"; label.alignment = TextAnchor.MiddleLeft;
            // This older horizontal ultimate bar becomes a hidden value/data source, never the primary resource instrument.
            if (hud.secondaryResourceRoot != null)
            {
                foreach (var gauge in hud.secondaryResourceRoot.GetComponentsInChildren<AstellaPointedGauge>(true)) gauge.enabled = false;
                foreach (var graphic in hud.secondaryResourceRoot.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            }

            var jobRoot = (RectTransform)hud.resourceRoot.transform;
            foreach (var graphic in jobRoot.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            Place(jobRoot, new Vector2(222, 158), new Vector2(400, 240), .4f);
            var job = hud.playerRoot.AddComponent<AstellaClassJobHUD>(); job.hud = hud;
            job.views = manifest.classes.Select(c => BuildJob(jobRoot, c)).ToArray();

            var binding = Slot(hud, CharacterSlot.Ultimate); var button = binding.button;
            foreach (var graphic in button.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            Stretch((RectTransform)button.transform);
            var content = Rect("Class ultimate instruments", button.transform); Stretch(content);
            var hit = Image("Input target", content, AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/AstellaOrnate/Shapes/White.png"));
            hit.color = Color.clear; hit.raycastTarget = true; button.targetGraphic = hit; button.transition = Selectable.Transition.None;
            var ultimate = hud.playerRoot.AddComponent<AstellaClassUltimateHUD>(); ultimate.hud = hud; ultimate.readinessSource = readiness;
            ultimate.views = manifest.classes.Select(c => BuildUltimate(content, c)).ToArray();
            // Per-class layers own their state brightness; don't dim the hidden v3 circle as the new instrument.
            binding.artwork = null; binding.stateGroup = null;
            hud.combatLayoutVersion = Mathf.Max(8, hud.combatLayoutVersion);
            if (!AstellaSplitCombatLayout.IsApplied(hud)) throw new InvalidOperationException("Existing action/layout IDs must be preserved.");
        }

        private static AstellaClassJobHUD.View BuildJob(RectTransform parent, HubSpec hub)
        {
            var spec = hub.job;
            var root = Rect("Job instrument " + hub.slug, parent); Stretch(root);
            Image("Track", root, Sprite(spec.track));
            var view = new AstellaClassJobHUD.View { jobId = hub.jobId, root = root.gameObject, showMaximum = spec.showMaximum };
            if (spec.kind == "continuous")
            {
                var b = spec.fillBounds;
                var mask = Rect("Fixed texture reveal", root);
                Center(mask, new Vector2(b[0] + b[2] / 2 - 200, 120 - b[1] - b[3] / 2), new Vector2(b[2], b[3]));
                mask.gameObject.AddComponent<RectMask2D>();
                var art = Image("Resource fill", mask, Sprite(spec.fill));
                Center(art.rectTransform, new Vector2(200 - b[0] - b[2] / 2, b[1] + b[3] / 2 - 120), new Vector2(400, 240));
                view.fillMask = mask; view.fillArtwork = art.rectTransform;
                view.fillDirection = spec.direction == "left-to-right" ? AstellaClassJobHUD.FillDirection.LeftToRight :
                    spec.direction == "right-to-left" ? AstellaClassJobHUD.FillDirection.RightToLeft :
                    spec.direction == "top-to-bottom" ? AstellaClassJobHUD.FillDirection.TopToBottom : AstellaClassJobHUD.FillDirection.BottomToTop;
            }
            view.tokens = new GameObject[spec.tokens.Length]; view.emptyTokens = new GameObject[spec.tokens.Length];
            for (int i = 0; i < spec.tokens.Length; i++)
            {
                var token = spec.tokens[i]; var r = token.rect;
                view.emptyTokens[i] = Picture("Empty token " + i, root, Sprite(token.empty), r[0], r[1], r[2], r[3], 400, 240).gameObject;
                view.tokens[i] = Picture("Active token " + i, root, Sprite(token.active), r[0], r[1], r[2], r[3], 400, 240).gameObject;
            }
            Image("Frame", root, Sprite(spec.frame));
            view.valueText = Label("Actual resource", root, spec.value[0] - 76, spec.value[1] - spec.fontSize * .95f, 152, spec.fontSize * 1.3f, spec.fontSize);
            view.valueText.text = "—"; root.gameObject.SetActive(hub.slug == "aegis-paladin");
            return view;
        }

        private static AstellaClassUltimateHUD.ClassView BuildUltimate(RectTransform parent, HubSpec hub)
        {
            var root = Rect("Ultimate instrument " + hub.slug, parent); Stretch(root);
            var u = hub.ultimate; var b = u.chargeBounds;
            var view = new AstellaClassUltimateHUD.ClassView { jobId = hub.jobId, root = root.gameObject,
                chargeBounds = new Rect(b[0], b[1], b[2], b[3]), canvasSize = new Vector2(360, 280) };
            view.charging = BuildState(root, u.charging, b, "Charging");
            view.ready = BuildState(root, u.ready, b, "Ready");
            view.unavailable = BuildState(root, u.unavailable, b, "Unavailable");
            var motion = Rect("Motion overlay", root).gameObject.AddComponent<RawImage>(); motion.raycastTarget = false; motion.enabled = false;
            view.motion = motion;
            view.percentText = Label("Actual ultimate percent", root, u.value[0] - 60, u.value[1] - 25, 120, 34, 25); view.percentText.text = "—";
            var shadow = view.percentText.gameObject.AddComponent<Shadow>(); shadow.effectColor = Color.black; shadow.effectDistance = new Vector2(1, -1);
            view.keyText = Label("Actual ultimate key", root, 152, 3, 56, 36, 26); view.keyText.text = "V";
            view.chargeBurst = Motion(u.burst); view.readyLoop = Motion(u.loop);
            root.gameObject.SetActive(hub.slug == "aegis-paladin");
            return view;
        }
        private static AstellaClassUltimateHUD.StateView BuildState(RectTransform parent, StateSpec spec, float[] bounds, string name)
        {
            var root = Rect(name, parent); Stretch(root);
            Image("Track", root, Sprite(spec.track));
            float bottom = 280 - bounds[1] - bounds[3];
            var mask = Rect("Charge reveal", root);
            mask.anchorMin = new Vector2(0, bottom / 280); mask.anchorMax = new Vector2(1, (bottom + bounds[3]) / 280);
            mask.offsetMin = mask.offsetMax = Vector2.zero; mask.gameObject.AddComponent<RectMask2D>();
            var fill = Image("Fixed full-canvas fill", mask, Sprite(spec.fill));
            fill.rectTransform.anchorMin = fill.rectTransform.anchorMax = new Vector2(.5f, 0); fill.rectTransform.pivot = new Vector2(.5f, 0);
            fill.rectTransform.sizeDelta = new Vector2(360, 280); fill.rectTransform.anchoredPosition = new Vector2(0, -bottom);
            Image("Frame", root, Sprite(spec.frame)); Image("Core", root, Sprite(spec.core));
            if (!string.IsNullOrEmpty(spec.glow)) Image("Full charge static glow", root, Sprite(spec.glow));
            Picture("State key badge", root, Sprite(spec.badge), 152, 3, 56, 36, 360, 280);
            Picture("State symbol", root, Sprite(spec.symbol), 216, 8, 24, 24, 360, 280);
            root.gameObject.SetActive(name == "Charging");
            return new AstellaClassUltimateHUD.StateView { root = root.gameObject, fillMask = mask };
        }
        private static AstellaClassUltimateHUD.Motion Motion(MotionSpec spec) => new AstellaClassUltimateHUD.Motion
        { sheetResourcePaths = spec.sheetResourcePaths, frameCount = spec.frameCount, framesPerSecond = spec.framesPerSecond,
            framesPerSheet = spec.framesPerSheet, columns = spec.columns, frameSize = new Vector2(360, 280) };

        private static AstellaOrnateHUD.SlotBinding Slot(AstellaOrnateHUD hud, CharacterSlot slot) => hud.slots.First(s => !s.sharedResurrection && s.slot == slot);
        private static Sprite Sprite(string path)
        { var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Folder + "/" + path); if (sprite == null) throw new InvalidOperationException("Missing hub sprite: " + path); return sprite; }
        private static RectTransform Rect(string name, Transform parent)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var rect = (RectTransform)go.transform; Stretch(rect); return rect; }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
        private static void Center(RectTransform rect, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one; }
        private static void Place(RectTransform rect, Vector2 position, Vector2 size, float scale)
        { Center(rect, position, size); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.localScale = Vector3.one * scale; }
        private static Image Image(string name, Transform parent, Sprite sprite)
        { var image = Rect(name, parent).gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = Color.white; image.raycastTarget = false; return image; }
        private static Image Picture(string name, Transform parent, Sprite sprite, float x, float y, float w, float h, float pw, float ph)
        { var image = Image(name, parent, sprite); var r = image.rectTransform; r.anchorMin = new Vector2(x / pw, 1 - (y + h) / ph); r.anchorMax = new Vector2((x + w) / pw, 1 - y / ph); r.offsetMin = r.offsetMax = Vector2.zero; return image; }
        private static Text Label(string name, Transform parent, float x, float y, float w, float h, int size)
        {
            var r = Rect(name, parent); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            var text = r.gameObject.AddComponent<Text>(); text.font = font; text.fontSize = size; text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter; text.color = new Color32(242, 230, 204, 255); text.raycastTarget = false;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 10; text.resizeTextMaxSize = size; return text;
        }
        private static void Sync(AstellaOrnateHUD hud)
        {
            foreach (var r in new[] { hud.playerRoot.transform.Find("Left - 1234"), hud.playerRoot.transform.Find("Right - Q F R Space"),
                hud.playerRoot.transform.Find("Center - V"), hud.resourceRoot.transform, hud.playerHpText.transform.parent })
                foreach (string key in new[] { "m_AnchorMin", "m_AnchorMax", "m_Pivot", "m_AnchoredPosition", "m_SizeDelta", "m_LocalScale" }) Revert(r, key);
            foreach (var branch in new[] { hud.resourceRoot.transform, hud.secondaryResourceRoot?.transform, Slot(hud, CharacterSlot.Ultimate).button.transform })
                if (branch != null) foreach (var graphic in branch.GetComponentsInChildren<Graphic>(true)) Revert(graphic, "m_Enabled");
            Revert(hud, "showOnlyCurrentHealth");
            int index = Array.FindIndex(hud.slots, s => !s.sharedResurrection && s.slot == CharacterSlot.Ultimate);
            if (index >= 0) { Revert(hud, "slots.Array.data[" + index + "].artwork"); Revert(hud, "slots.Array.data[" + index + "].stateGroup"); }
        }
        private static void Revert(UnityEngine.Object target, string key)
        {
            if (target == null || !PrefabUtility.IsPartOfPrefabInstance(target) || PrefabUtility.GetCorrespondingObjectFromSource(target) == null) return;
            var property = new SerializedObject(target).FindProperty(key);
            if (property != null && property.prefabOverride) PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
        }
    }
}
#endif
