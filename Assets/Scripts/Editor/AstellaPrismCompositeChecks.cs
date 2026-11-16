#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Astella.PrismV4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Sprites;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    /// <summary>Pixel regression against ordinary uGUI Image, including real imported sprite trim/padding.</summary>
    public static class AstellaPrismCompositeChecks
    {
        private const string Folder = "C:/Astella/prism-v4-layout-1920-20261116";
        private const string SpriteFolder = "Assets/UI/AstellaPrismV4/Sprites/";
        private const int Width = 1920, Height = 1080, RenderLayer = 31, Margin = 3;

        [Serializable] public sealed class Result
        {
            public string gauge, importedMeshType;
            public float ratio;
            public Vector2 originalSpriteSize;
            public Vector4 actualSpritePadding, actualOuterUV;
            public int referenceFillPixels, activeFillPixels, spillPixels, fullFillMismatchPixels, compositeMismatchPixels;
            public int maximumFullChannelDelta;
            public bool zeroEmpty, nonzeroVisible, fillContained, fullMatchesNormalImage, compositeMatchesNormalImage;
            public int allowedAntialiasNeighborPixels = 1;
        }

        [Serializable] public sealed class Report
        {
            public int version = 1, width = Width, height = Height;
            public string generatedUtc, graphicsDevice, renderPath, isolationImage, compositeImage;
            public bool passed, captured, originalActiveSceneRestored, originalSceneDirtyUnchanged;
            public Result[] cases;
            public string[] failures, warnings;
            public string boundary = "Actual 1920x1080 temporary-scene GPU/uGUI pixel comparison. No main HUD, scene save, gameplay, input or layout validation.";
            public string comparison = "Three custom ratios 0/0.5/1 versus an ordinary Image using the same full fill PNG, Rect and pixel grid. Isolated fill containment plus full frame/track/fill composite equality.";
        }

        private sealed class Spec
        {
            public string name, frame, track, fill;
            public Vector2 canvas;
            public Rect bounds;
            public float tip;
        }

        private sealed class Sample
        {
            public Spec spec;
            public Result result;
            public Image fill, track, frame;
            public Rect screenRect;
            public bool reference;
        }

        [MenuItem("Tools/Astella/Prism V4/Run Composite GPU Regression %#F5")]
        public static void RunCompositeGpuRegression()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Run Composite GPU Regression in Edit Mode after compilation.");
            Directory.CreateDirectory(Folder);
            string stem = "composite-gpu-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            var report = new Report {
                generatedUtc = DateTime.UtcNow.ToString("o"), graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
                isolationImage = Path.Combine(Folder, stem + "-fill-isolation.png"),
                compositeImage = Path.Combine(Folder, stem + "-composite.png")
            };
            var failures = new List<string>();
            var warnings = new List<string>();
            var samples = new List<Sample>();
            var originalScene = SceneManager.GetActiveScene();
            bool originalDirty = originalScene.IsValid() && originalScene.isDirty;
            Scene scene = default(Scene);
            RenderTexture target = null;
            Texture2D isolation = null, composite = null;
            RenderTexture previousTarget = RenderTexture.active;
            Application.LogCallback onLog = (message, stack, type) => {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failures.Add(message);
                else if (type == LogType.Warning) warnings.Add(message);
            };
            Application.logMessageReceived += onLog;
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("A real graphics device is required; do not use -nographics.");
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                scene.name = "Prism isolated composite GPU regression";
                var cameraObject = NewObject("Composite regression camera", scene, typeof(Camera));
                var camera = cameraObject.GetComponent<Camera>();
                camera.cameraType = CameraType.Game;
                camera.enabled = false;
                camera.scene = scene;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.orthographic = true;
                camera.orthographicSize = Height * .5f;
                camera.aspect = (float)Width / Height;
                camera.transform.position = new Vector3(0, 0, -10);
                camera.nearClipPlane = .1f; camera.farClipPlane = 30;
                camera.cullingMask = 1 << RenderLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(13, 16, 33, 255);
                camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
                var canvasObject = NewObject("Composite regression Canvas", scene, typeof(RectTransform), typeof(Canvas));
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
                var canvasRect = (RectTransform)canvas.transform;
                canvasRect.position = Vector3.zero; canvasRect.rotation = Quaternion.identity;
                canvasRect.localScale = Vector3.one; canvasRect.sizeDelta = new Vector2(Width, Height);
                Label(canvas.transform, "Prism V4 / GPU composite and imported-sprite containment", 20, 18, 1800, 42, 27);
                string[] headers = { "Custom 0%", "Custom 50%", "Custom 100%", "Normal Image 100%" };
                for (int column = 0; column < 4; column++) Label(canvas.transform, headers[column], 210 + column * 450, 82, 320, 36, 24);
                Spec[] specs = Specs();
                for (int row = 0; row < specs.Length; row++)
                {
                    Label(canvas.transform, specs[row].name, 16, 138 + row * 170, 180, 54, 22);
                    for (int column = 0; column < 4; column++)
                        samples.Add(CreateSample(canvas.transform, specs[row], 210 + column * 450, 150 + row * 170,
                            column == 0 ? 0 : column == 1 ? .5f : 1, column == 3));
                }
                Label(canvas.transform, "Same PNG + same Rect: custom fill must remain inside the normal Image silhouette. No active game HUD is instantiated.", 20, 1008, 1860, 44, 21);
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) {
                    name = "Prism composite GPU regression", antiAliasing = 1
                };
                target.Create(); camera.targetTexture = target;

                // First expose the fill itself. Frame overlays must not be able to conceal a spill.
                foreach (var sample in samples) { sample.track.enabled = false; sample.frame.enabled = false; }
                isolation = Capture(camera, target, samples, report);
                Color32[] isolatedPixels = isolation.GetPixels32();
                ValidateIsolatedPixels(isolatedPixels, samples, failures);
                File.WriteAllBytes(report.isolationImage, isolation.EncodeToPNG());

                // Then prove the complete frame + track + fill stack remains aligned at full charge.
                foreach (var sample in samples) { sample.track.enabled = true; sample.frame.enabled = true; }
                composite = Capture(camera, target, samples, report);
                ValidateCompositePixels(composite.GetPixels32(), samples, failures);
                File.WriteAllBytes(report.compositeImage, composite.EncodeToPNG());
                report.captured = true;
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                Application.logMessageReceived -= onLog;
                RenderTexture.active = previousTarget;
                if (isolation != null) Object.DestroyImmediate(isolation);
                if (composite != null) Object.DestroyImmediate(composite);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                report.originalActiveSceneRestored = SceneManager.GetActiveScene() == originalScene;
                report.originalSceneDirtyUnchanged = !originalScene.IsValid() || originalScene.isDirty == originalDirty;
                if (!report.originalActiveSceneRestored || !report.originalSceneDirtyUnchanged)
                    failures.Add("Original active/dirty scene state was not preserved.");
                var results = new List<Result>();
                foreach (var sample in samples) if (!sample.reference) results.Add(sample.result);
                report.cases = results.ToArray(); report.failures = failures.ToArray(); report.warnings = warnings.ToArray();
                report.passed = report.captured && results.Count == 15 && failures.Count == 0;
                File.WriteAllText(Path.Combine(Folder, stem + ".json"), JsonUtility.ToJson(report, true) + "\n");
            }
            if (!report.passed) throw new InvalidOperationException("Prism composite GPU regression failed: " + Path.Combine(Folder, stem + ".json"));
            Debug.Log("PRISM COMPOSITE GPU PASS: 15 custom cases against 5 native Image references; actual pixel containment and full composite equality. Evidence: " + Path.Combine(Folder, stem + ".json"));
        }

        private static Texture2D Capture(Camera camera, RenderTexture target, List<Sample> samples, Report report)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var sample in samples)
            {
                sample.fill.Rebuild(CanvasUpdate.PreRender);
                if (sample.track.enabled) sample.track.Rebuild(CanvasUpdate.PreRender);
                if (sample.frame.enabled) sample.frame.Rebuild(CanvasUpdate.PreRender);
            }
            Canvas.ForceUpdateCanvases();
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                var additional = camera.GetUniversalAdditionalCameraData();
                additional.renderPostProcessing = false; additional.renderShadows = false; additional.volumeLayerMask = 0;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                report.renderPath = "UniversalRenderPipeline.SingleCameraRequest";
            }
            else { camera.Render(); report.renderPath = "Camera.Render (built-in pipeline)"; }
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); image.Apply();
            return image;
        }

        private static void ValidateIsolatedPixels(Color32[] pixels, List<Sample> samples, List<string> failures)
        {
            Color32 background = pixels[0];
            foreach (var sample in samples)
            {
                if (sample.reference) continue;
                Sample reference = samples.Find(candidate => candidate.reference && candidate.spec == sample.spec);
                int width = Mathf.RoundToInt(sample.screenRect.width) + Margin * 2;
                int height = Mathf.RoundToInt(sample.screenRect.height) + Margin * 2;
                var referenceMask = new bool[width * height];
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    bool visible = Delta(Pixel(pixels, reference, x - Margin, y - Margin), background) > 3;
                    referenceMask[y * width + x] = visible;
                    if (visible) sample.result.referenceFillPixels++;
                }
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    Color32 actual = Pixel(pixels, sample, x - Margin, y - Margin);
                    Color32 expected = Pixel(pixels, reference, x - Margin, y - Margin);
                    bool visible = Delta(actual, background) > 3;
                    if (visible)
                    {
                        sample.result.activeFillPixels++;
                        if (!ReferenceNear(referenceMask, width, height, x, y)) sample.result.spillPixels++;
                    }
                    if (sample.result.ratio == 1)
                    {
                        int delta = Delta(actual, expected);
                        sample.result.maximumFullChannelDelta = Math.Max(sample.result.maximumFullChannelDelta, delta);
                        if (delta > 3) sample.result.fullFillMismatchPixels++;
                    }
                }
                var result = sample.result;
                int allowedMismatch = Math.Max(2, result.referenceFillPixels / 1000);
                result.zeroEmpty = result.ratio != 0 || result.activeFillPixels == 0;
                result.nonzeroVisible = result.ratio == 0 || result.activeFillPixels > Math.Max(3, result.referenceFillPixels / 20);
                result.fillContained = result.spillPixels == 0;
                result.fullMatchesNormalImage = result.ratio != 1 || result.fullFillMismatchPixels <= allowedMismatch;
                if (result.referenceFillPixels < 20) failures.Add(result.gauge + ": native Image reference is blank or too small.");
                if (!result.zeroEmpty || !result.nonzeroVisible || !result.fillContained || !result.fullMatchesNormalImage)
                    failures.Add(result.gauge + " ratio=" + result.ratio + ": isolated pixel regression; spill=" + result.spillPixels + ", fullMismatch=" + result.fullFillMismatchPixels + ".");
            }
        }

        private static void ValidateCompositePixels(Color32[] pixels, List<Sample> samples, List<string> failures)
        {
            foreach (var sample in samples)
            {
                if (sample.reference) continue;
                var result = sample.result;
                result.compositeMatchesNormalImage = result.ratio != 1;
                if (result.ratio != 1) continue;
                Sample reference = samples.Find(candidate => candidate.reference && candidate.spec == sample.spec);
                int width = Mathf.RoundToInt(sample.screenRect.width), height = Mathf.RoundToInt(sample.screenRect.height);
                for (int y = -Margin; y < height + Margin; y++) for (int x = -Margin; x < width + Margin; x++)
                    if (Delta(Pixel(pixels, sample, x, y), Pixel(pixels, reference, x, y)) > 3) result.compositeMismatchPixels++;
                result.compositeMatchesNormalImage = result.compositeMismatchPixels <= Math.Max(2, result.referenceFillPixels / 1000);
                if (!result.compositeMatchesNormalImage)
                    failures.Add(result.gauge + ": full frame/track/fill composite differs from native Image at " + result.compositeMismatchPixels + " pixels.");
            }
        }

        private static bool ReferenceNear(bool[] mask, int width, int height, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                if (x + dx >= 0 && x + dx < width && y + dy >= 0 && y + dy < height && mask[(y + dy) * width + x + dx]) return true;
            return false;
        }

        private static int Delta(Color32 a, Color32 b) => Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));
        private static Color32 Pixel(Color32[] pixels, Sample sample, int x, int y) =>
            pixels[(Height - 1 - Mathf.RoundToInt(sample.screenRect.y) - y) * Width + Mathf.RoundToInt(sample.screenRect.x) + x];

        private static Sample CreateSample(Transform parent, Spec spec, float x, float y, float ratio, bool reference)
        {
            const float width = 320;
            float height = width * spec.canvas.y / spec.canvas.x;
            var root = Rect(parent, spec.name + (reference ? " native reference" : " custom " + ratio), x, y, width, height);
            var sample = new Sample { spec = spec, reference = reference, screenRect = new Rect(x, y, width, height) };
            sample.track = Picture(root, "Track", spec.track, width, height);
            if (reference) sample.fill = Picture(root, "Native full Image", spec.fill, width, height);
            else
            {
                var fill = Rect(root, "Custom ratio fill", 0, 0, width, height).gameObject.AddComponent<PrismV4Gauge>();
                fill.sprite = Sprite(spec.fill); fill.innerBounds = spec.bounds; fill.pointWidth = spec.tip; fill.raycastTarget = false; fill.SetValue(ratio);
                sample.fill = fill;
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(fill.sprite)) as TextureImporter;
                var settings = new TextureImporterSettings();
                if (importer != null) importer.ReadTextureSettings(settings);
                sample.result = new Result {
                    gauge = spec.name, ratio = ratio, originalSpriteSize = fill.sprite.rect.size,
                    actualSpritePadding = DataUtility.GetPadding(fill.sprite), actualOuterUV = DataUtility.GetOuterUV(fill.sprite),
                    importedMeshType = importer != null ? settings.spriteMeshType.ToString() : "unknown"
                };
            }
            sample.frame = Picture(root, "Frame", spec.frame, width, height);
            return sample;
        }

        private static Spec[] Specs() => new[] {
            new Spec { name = "Player HP", canvas = new Vector2(1024, 96), frame = "Player/gauge-frame", track = "Player/gauge-track", fill = "Player/hp-fill", bounds = new Rect(29f / 1024, 18f / 96, 966f / 1024, 60f / 96), tip = 36f / 1024 },
            new Spec { name = "Job resource", canvas = new Vector2(1024, 96), frame = "Player/gauge-frame", track = "Player/gauge-track", fill = "Player/resource-fill", bounds = new Rect(29f / 1024, 18f / 96, 966f / 1024, 60f / 96), tip = 36f / 1024 },
            new Spec { name = "Boss HP", canvas = new Vector2(1280, 96), frame = "Boss/boss-frame", track = "Boss/boss-track", fill = "Boss/boss-fill", bounds = new Rect(27f / 1280, 29f / 96, 1226f / 1280, 38f / 96), tip = 22f / 1280 },
            new Spec { name = "Cast", canvas = new Vector2(512, 48), frame = "Boss/cast-frame", track = "Boss/cast-track", fill = "Boss/cast-fill", bounds = new Rect(15f / 512, 20f / 48, 482f / 512, 8f / 48), tip = 7f / 512 },
            new Spec { name = "Party HP", canvas = new Vector2(320, 40), frame = "Party/bar-frame", track = "Party/bar-track", fill = "Party/bar-fill", bounds = new Rect(14f / 320, 12f / 40, 292f / 320, 16f / 40), tip = 10f / 320 }
        };

        private static GameObject NewObject(string name, Scene scene, params Type[] components)
        {
            var root = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components);
            root.layer = RenderLayer; SceneManager.MoveGameObjectToScene(root, scene); return root;
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var root = NewObject(name, parent.gameObject.scene, typeof(RectTransform)); root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }

        private static Sprite Sprite(string relative)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFolder + relative + ".png");
            if (sprite == null) throw new InvalidOperationException("Missing Prism sprite: " + relative); return sprite;
        }

        private static Image Picture(Transform parent, string name, string relative, float width, float height)
        {
            var image = Rect(parent, name, 0, 0, width, height).gameObject.AddComponent<Image>();
            image.sprite = Sprite(relative); image.type = Image.Type.Simple; image.useSpriteMesh = false;
            image.preserveAspect = false; image.raycastTarget = false; return image;
        }

        private static void Label(Transform parent, string value, float x, float y, float width, float height, int size)
        {
            var text = Rect(parent, value, x, y, width, height).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.text = value; text.fontSize = size;
            text.color = new Color32(237, 228, 245, 255); text.raycastTarget = false; text.alignment = TextAnchor.MiddleLeft;
        }
    }
}
#endif
