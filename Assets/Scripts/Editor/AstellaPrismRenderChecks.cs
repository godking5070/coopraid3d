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
    /// <summary>Explicit GPU/uGUI gauge check in a disposable scene; no main-HUD or asset writes.</summary>
    public static class AstellaPrismRenderChecks
    {
        private const string Folder = "Logs/PrismV4";
        private const string SpriteFolder = "Assets/UI/AstellaPrismV4/Sprites/";
        private const int Width = 1920, Height = 940;
        private const int RenderLayer = 31;
        [Serializable] public sealed class Case
        {
            public string gauge;
            public float ratio;
            public int activeVertices, activeTriangleIndices, frameVertices, trackVertices, textureWidth, textureHeight;
            public int nonBackgroundPixels, differentFromZeroPixels;
            public bool fixedRect, fixedTextureDimensions, fixedTrackAndFrame, zeroHasNoActiveMesh, fullUsesOriginalUv;
            public bool visiblePixelsRendered, nonzeroFillDiffersFromZero;
        }
        [Serializable] public sealed class Report
        {
            public int toolVersion = 3, uiLayer = RenderLayer;
            public string generatedUtc, graphicsDevice, renderPath;
            public string sceneCullingMask;
            public string sceneKind = "Temporary additive EmptyScene / Game camera";
            public Vector3 canvasPosition, canvasScale;
            public Vector2 canvasSize;
            public bool passed, rendered, captured;
            public bool originalActiveSceneRestored, originalSceneDirtyUnchanged;
            public int width = Width, height = Height;
            public int nonBackgroundPixels;
            public Case[] cases;
            public string[] failures, warnings;
            public string boundary = "Actual temporary-scene uGUI/RenderTexture rendering and mesh checks only. No gameplay, input, active HUD, multiplayer or performance verification.";
        }
        private sealed class Sample
        {
            public Case result;
            public PrismV4Gauge fill;
            public Image track, frame;
            public Vector2 size;
            public Rect screenRect;
        }

        [MenuItem("Tools/Astella/Prism V4/Render Fill Validation")]
        public static void RenderFillValidation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Run the gauge render check in Edit Mode after compilation.");
            Directory.CreateDirectory(Folder);
            var report = new Report { generatedUtc = DateTime.UtcNow.ToString("o"), graphicsDevice = SystemInfo.graphicsDeviceType.ToString() };
            var failures = new List<string>();
            var warnings = new List<string>();
            var samples = new List<Sample>();
            Scene scene = default;
            var originalScene = SceneManager.GetActiveScene();
            bool originalDirty = originalScene.IsValid() && originalScene.isDirty;
            RenderTexture target = null;
            Texture2D pixels = null;
            var previousTarget = RenderTexture.active;
            Application.LogCallback onLog = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failures.Add(message);
                else if (type == LogType.Warning) warnings.Add(message);
            };
            Application.logMessageReceived += onLog;
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("A real graphics device is required; do not use -nographics for this check.");
                // Native Canvas geometry is not reliably emitted for preview-scene cameras in URP.
                // Use a disposable normal scene and an isolated layer, with no persistent scene writes.
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                scene.name = "Prism temporary fill validation";
                var cameraObject = NewObject("Prism fill render camera", scene, typeof(Camera));
                var camera = cameraObject.GetComponent<Camera>();
                camera.cameraType = CameraType.Game;
                camera.enabled = false;
                camera.scene = scene;
                // Scene and layer masks are both explicit. Meshes alone do not prove pixels were drawn.
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                report.sceneCullingMask = camera.overrideSceneCullingMask.ToString("x");
                camera.orthographic = true;
                camera.orthographicSize = Height * .5f;
                camera.aspect = (float)Width / Height;
                camera.transform.position = new Vector3(0, 0, -10);
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 30;
                camera.cullingMask = 1 << RenderLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(13, 16, 33, 255);
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.useOcclusionCulling = false;
                var canvasObject = NewObject("Prism fill validation Canvas", scene, typeof(RectTransform), typeof(Canvas));
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                var canvasRect = (RectTransform)canvas.transform;
                canvasRect.position = Vector3.zero;
                canvasRect.rotation = Quaternion.identity;
                canvasRect.localScale = Vector3.one;
                canvasRect.sizeDelta = new Vector2(Width, Height);
                report.canvasPosition = canvasRect.position; report.canvasScale = canvasRect.lossyScale; report.canvasSize = canvasRect.sizeDelta;
                Label(canvas.transform, "Prism V4 / actual uGUI fill rendering", 20, 12, 1200, 42, 27);
                float[] ratios = { 0, .01f, .5f, .6f, 1 };
                for (int column = 0; column < ratios.Length; column++)
                    Label(canvas.transform, (ratios[column] * 100).ToString("0") + "%", 160 + column * 348, 70, 330, 36, 25);
                string[] names = { "Player HP", "Job resource", "Boss HP", "Cast", "Ultimate" };
                for (int row = 0; row < names.Length; row++)
                {
                    Label(canvas.transform, names[row], 12, 134 + row * 144, 142, 48, 21);
                    for (int column = 0; column < ratios.Length; column++)
                        samples.Add(CreateSample(canvas.transform, names[row], row, 160 + column * 348, 128 + row * 144, ratios[column]));
                }
                Label(canvas.transform, "Fixed track + clipped fill + fixed frame. Text is independent of fill geometry. No active HUD or gameplay is instantiated.", 20, 884, 1850, 34, 20);
                Canvas.ForceUpdateCanvases();
                foreach (var sample in samples) sample.fill.Rebuild(CanvasUpdate.PreRender);
                Canvas.ForceUpdateCanvases();
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Prism fill validation", antiAliasing = 1 };
                target.Create();
                camera.targetTexture = target;
                if (GraphicsSettings.currentRenderPipeline != null)
                {
                    var additional = camera.GetUniversalAdditionalCameraData();
                    additional.renderPostProcessing = false;
                    additional.renderShadows = false;
                    additional.volumeLayerMask = 0;
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    report.renderPath = "UniversalRenderPipeline.SingleCameraRequest";
                }
                else { camera.Render(); report.renderPath = "Camera.Render (built-in pipeline)"; }
                foreach (var sample in samples) ValidateSample(sample, failures);
                RenderTexture.active = target;
                pixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                pixels.Apply();
                ValidateRenderedPixels(pixels.GetPixels32(), samples, report, failures);
                File.WriteAllBytes(Folder + "/fill-render-validation.png", pixels.EncodeToPNG());
                report.captured = true;
                report.rendered = report.nonBackgroundPixels >= 500 && samples.TrueForAll(s =>
                    s.result.visiblePixelsRendered && s.result.nonzeroFillDiffersFromZero);
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                Application.logMessageReceived -= onLog;
                RenderTexture.active = previousTarget;
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                report.originalActiveSceneRestored = SceneManager.GetActiveScene() == originalScene;
                report.originalSceneDirtyUnchanged = !originalScene.IsValid() || originalScene.isDirty == originalDirty;
                if (!report.originalActiveSceneRestored || !report.originalSceneDirtyUnchanged)
                    failures.Add("The original scene active/dirty state was not preserved.");
                report.cases = samples.ConvertAll(s => s.result).ToArray();
                report.failures = failures.ToArray(); report.warnings = warnings.ToArray();
                report.passed = report.rendered && failures.Count == 0;
                File.WriteAllText(Folder + "/fill-render-validation.json", JsonUtility.ToJson(report, true) + "\n");
            }
            if (!report.passed) throw new InvalidOperationException("Prism gauge render check failed; see " + Folder + "/fill-render-validation.json");
            Debug.Log("PRISM FILL RENDER PASS: 25 real uGUI samples at 0/1/50/60/100%; report and PNG: " + Folder + "/fill-render-validation.*");
        }

        private static Sample CreateSample(Transform parent, string name, int row, float x, float y, float ratio)
        {
            string track, fill, frame;
            Vector2 size;
            Rect bounds;
            float tip;
            switch (row)
            {
                case 0: case 1:
                    track = "Player/gauge-track"; fill = row == 0 ? "Player/hp-fill" : "Player/resource-fill"; frame = "Player/gauge-frame";
                    size = new Vector2(330, 330f * 96 / 1024); bounds = new Rect(29f / 1024, 18f / 96, 966f / 1024, 60f / 96); tip = 36f / 1024; break;
                case 2:
                    track = "Boss/boss-track"; fill = "Boss/boss-fill"; frame = "Boss/boss-frame";
                    size = new Vector2(330, 330f * 96 / 1280); bounds = new Rect(27f / 1280, 29f / 96, 1226f / 1280, 38f / 96); tip = 22f / 1280; break;
                case 3:
                    track = "Boss/cast-track"; fill = "Boss/cast-fill"; frame = "Boss/cast-frame";
                    size = new Vector2(330, 330f * 48 / 512); bounds = new Rect(15f / 512, 20f / 48, 482f / 512, 8f / 48); tip = 7f / 512; break;
                default:
                    track = "Hub/hub-background"; fill = "Hub/hub-crystal"; frame = "Hub/hub-frame";
                    size = new Vector2(120, 120); bounds = new Rect(0, 0, 1, 1); tip = 0; x += 105; break;
            }
            var sample = new Sample { result = new Case { gauge = name, ratio = ratio }, size = size,
                screenRect = new Rect(x, y, size.x, size.y) };
            var root = Rect(parent, name + " " + ratio, x, y, size.x, size.y);
            sample.track = Picture(root, "Track", track, size);
            sample.fill = Rect(root, "Fill", 0, 0, size.x, size.y).gameObject.AddComponent<PrismV4Gauge>();
            sample.fill.sprite = Sprite(fill); sample.fill.innerBounds = bounds; sample.fill.pointWidth = tip;
            sample.fill.direction = row == 4 ? PrismV4Gauge.Direction.BottomToTop : PrismV4Gauge.Direction.LeftToRight;
            sample.fill.raycastTarget = false; sample.fill.SetValue(ratio);
            sample.frame = Picture(root, "Frame", frame, size);
            sample.result.textureWidth = sample.fill.sprite.texture.width; sample.result.textureHeight = sample.fill.sprite.texture.height;
            return sample;
        }
        private static void ValidateSample(Sample sample, List<string> failures)
        {
            var result = sample.result;
            var mesh = sample.fill.canvasRenderer.GetMesh();
            var trackMesh = sample.track.canvasRenderer.GetMesh(); var frameMesh = sample.frame.canvasRenderer.GetMesh();
            result.activeVertices = mesh != null ? mesh.vertexCount : 0;
            result.activeTriangleIndices = mesh != null ? mesh.triangles.Length : 0;
            result.trackVertices = trackMesh != null ? trackMesh.vertexCount : 0;
            result.frameVertices = frameMesh != null ? frameMesh.vertexCount : 0;
            result.fixedRect = sample.fill.rectTransform.sizeDelta == sample.size && sample.track.rectTransform.sizeDelta == sample.size && sample.frame.rectTransform.sizeDelta == sample.size;
            result.fixedTextureDimensions = sample.fill.sprite.texture.width == result.textureWidth && sample.fill.sprite.texture.height == result.textureHeight;
            result.fixedTrackAndFrame = result.trackVertices == 4 && result.frameVertices == 4;
            result.zeroHasNoActiveMesh = result.ratio != 0 || result.activeVertices == 0;
            result.fullUsesOriginalUv = result.ratio != 1;
            if (result.ratio == 1 && mesh != null)
            {
                var expected = DataUtility.GetOuterUV(sample.fill.sprite);
                var min = new Vector2(float.MaxValue, float.MaxValue); var max = new Vector2(float.MinValue, float.MinValue);
                foreach (var uv in mesh.uv) { min = Vector2.Min(min, uv); max = Vector2.Max(max, uv); }
                result.fullUsesOriginalUv = result.activeVertices == 4 && result.activeTriangleIndices == 6 &&
                    Mathf.Abs(min.x - expected.x) < .00001f && Mathf.Abs(min.y - expected.y) < .00001f &&
                    Mathf.Abs(max.x - expected.z) < .00001f && Mathf.Abs(max.y - expected.w) < .00001f;
            }
            if (!result.fixedRect || !result.fixedTextureDimensions || !result.fixedTrackAndFrame || !result.zeroHasNoActiveMesh || !result.fullUsesOriginalUv)
                failures.Add(result.gauge + " ratio=" + result.ratio + " failed fixed-layer/endpoint mesh checks.");
        }
        private static bool DifferentPixel(Color32 a, Color32 b) =>
            Math.Abs(a.r - b.r) > 4 || Math.Abs(a.g - b.g) > 4 || Math.Abs(a.b - b.b) > 4 || Math.Abs(a.a - b.a) > 4;

        private static int CountDifferentPixels(Color32[] pixels, Color32 background)
        {
            int count = 0;
            foreach (var pixel in pixels) if (DifferentPixel(pixel, background)) count++;
            return count;
        }
        private static void ValidateRenderedPixels(Color32[] pixels, List<Sample> samples, Report report, List<string> failures)
        {
            // (0,0) is intentionally outside all UI. Use the actual clear color after GPU color conversion.
            var background = pixels[0];
            report.nonBackgroundPixels = CountDifferentPixels(pixels, background);
            if (report.nonBackgroundPixels < 500) failures.Add("Rendered image is blank or contains too few visible UI pixels.");
            var zeroSamples = new Dictionary<string, Sample>();
            foreach (var sample in samples)
            {
                var rect = sample.screenRect;
                int left = Math.Max(0, (int)Math.Ceiling(rect.x));
                int top = Math.Max(0, (int)Math.Ceiling(rect.y));
                int width = Math.Min(Width - left, (int)Math.Floor(rect.width));
                int height = Math.Min(Height - top, (int)Math.Floor(rect.height));
                zeroSamples.TryGetValue(sample.result.gauge, out var zero);
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var pixel = pixels[(Height - 1 - top - y) * Width + left + x];
                    if (DifferentPixel(pixel, background)) sample.result.nonBackgroundPixels++;
                    if (zero != null)
                    {
                        int zeroLeft = (int)Math.Ceiling(zero.screenRect.x);
                        int zeroTop = (int)Math.Ceiling(zero.screenRect.y);
                        var zeroPixel = pixels[(Height - 1 - zeroTop - y) * Width + zeroLeft + x];
                        if (DifferentPixel(pixel, zeroPixel)) sample.result.differentFromZeroPixels++;
                    }
                }
                if (sample.result.ratio == 0) zeroSamples[sample.result.gauge] = sample;
                sample.result.visiblePixelsRendered = sample.result.nonBackgroundPixels > Math.Max(8, width * height / 100);
                // A 1% triangular sliver may be smaller than a pixel at this preview scale.
                // Subpixel endpoints are checked by the real mesh; larger fills must also change rendered pixels.
                sample.result.nonzeroFillDiffersFromZero = sample.result.ratio < .5f ||
                    zero != null && sample.result.differentFromZeroPixels > Math.Max(8, width * height / 100);
                if (!sample.result.visiblePixelsRendered)
                    failures.Add(sample.result.gauge + " ratio=" + sample.result.ratio + " has no visible track/frame pixels.");
                if (!sample.result.nonzeroFillDiffersFromZero)
                    failures.Add(sample.result.gauge + " ratio=" + sample.result.ratio + " did not visually change from 0%.");
            }
        }
        private static GameObject NewObject(string name, Scene scene, params Type[] components)
        {
            var root = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components);
            root.layer = RenderLayer; SceneManager.MoveGameObjectToScene(root, scene); return root;
        }
        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var root = NewObject(name, parent.gameObject.scene, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        private static Sprite Sprite(string relative)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFolder + relative + ".png");
            if (sprite == null) throw new InvalidOperationException("Required Prism sprite is missing: " + relative); return sprite;
        }
        private static Image Picture(Transform parent, string name, string relative, Vector2 size)
        {
            var image = Rect(parent, name, 0, 0, size.x, size.y).gameObject.AddComponent<Image>();
            image.sprite = Sprite(relative); image.raycastTarget = false; return image;
        }
        private static void Label(Transform parent, string text, float x, float y, float width, float height, int fontSize)
        {
            var label = Rect(parent, text, x, y, width, height).gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.text = text;
            label.fontSize = fontSize; label.color = new Color32(237, 228, 245, 255); label.raycastTarget = false;
            label.alignment = TextAnchor.MiddleLeft;
        }
    }
}
#endif
