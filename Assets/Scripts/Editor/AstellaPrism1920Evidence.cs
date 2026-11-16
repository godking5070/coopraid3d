#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Astella.PrismV4;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Sprites;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    /// <summary>Explicit end-of-frame evidence only. Does not enter Play, change menus, save scenes or update the HUD.</summary>
    public static class AstellaPrism1920Evidence
    {
        public const string EvidenceFolder = "C:/Astella/prism-v4-layout-1920-20261116";
        private const int TargetWidth = 1920, TargetHeight = 1080;
        private static bool _capturing;
        private static AstellaPrismHUDBridge _captureHost;

        [Serializable] private sealed class Node
        {
            public string key, path, cameraPath, canvasPath, renderMode;
            public bool found, activeSelf, activeInHierarchy, componentEnabled, insideScreen;
            public Rect localRect;
            public Vector2 anchoredPosition, sizeDelta, anchorMin, anchorMax, pivot;
            public Vector3 localScale, lossyScale, worldPosition;
            public Vector3[] worldCorners;
            public Vector2[] screenCorners;
            public Vector4 screenBounds;
            public float canvasScaleFactor;
        }

        [Serializable] private sealed class Gauge
        {
            public string key, path, sourcePath, spritePath;
            public bool active, hasSource, finite;
            public float ratio, previewValue, sourceFillAmount;
            public Rect innerBounds, spriteRect;
            public Vector4 spritePadding, outerUV;
        }

        [Serializable] private sealed class View
        {
            public string key, path, name, value, resource, binding, cooldown;
            public bool active;
        }

        [Serializable] private sealed class Report
        {
            public int version = 1, targetWidth = TargetWidth, targetHeight = TargetHeight;
            public string utc, pngPath, state, mode, battlePhase, scenePath, graphicsDevice, graphicsDeviceName;
            public string presentationRootField, layoutFilePath;
            public int screenWidth, screenHeight, textureWidth, textureHeight, frame, renderedFrame, stage, activeBridgeCount, totalBridgeCount;
            public bool targetMatched, hasSession, menuOpen, ending, applicationFocused, editorPaused, layoutEditing, resultsOpen;
            public float timeScale;
            public Node presentationRoot;
            public Node[] bounds;
            public Gauge[] gauges;
            public View[] views;
            public string[] notes;
            public string boundary = "Actual runtime GameView GPU capture and same-frame UI geometry. No gameplay, menu, Play state or authored asset was changed by this helper.";
        }

        [MenuItem("Tools/Astella/Prism V4/Set Game View 1920 x 1080")]
        public static void SetGameView1920()
        {
            RequireOriginalProject();
            Type type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView", true);
            var method = type.GetMethod("SetCustomResolution", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Vector2), typeof(string) }, null);
            if (method == null) throw new MissingMethodException(type.FullName, "SetCustomResolution(Vector2, string)");
            EditorWindow gameView = EditorWindow.GetWindow(type);
            method.Invoke(gameView, new object[] { new Vector2(TargetWidth, TargetHeight), "Astella 1920x1080" });
            gameView.Show(); gameView.Focus(); gameView.Repaint();
            Debug.Log("PRISM 1920 GAME VIEW: fixed 1920x1080 selected. Play and menu state were not changed.");
        }

        [MenuItem("Tools/Astella/Prism V4/Capture 1920 Runtime Evidence %#F4")]
        public static void CaptureRuntimeEvidence()
        {
            RequireOriginalProject();
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.isCompiling)
                throw new InvalidOperationException("Capture requires unpaused Play Mode after compilation. Select the Game View so its end-of-frame rendering runs.");
            if (_capturing && _captureHost != null && _captureHost.isActiveAndEnabled)
            { Debug.LogWarning("A Prism end-of-frame capture is already pending."); return; }
            _capturing = true;
            try
            {
                var host = Object.FindObjectsByType<AstellaPrismHUDBridge>(FindObjectsSortMode.None)
                    .FirstOrDefault(candidate => candidate.isActiveAndEnabled);
                if (host == null) throw new InvalidOperationException("An active Prism bridge is required to schedule end-of-frame evidence.");
                _captureHost = host;
                host.StartCoroutine(CaptureFrame());
            }
            catch { _capturing = false; throw; }
        }

        [MenuItem("Tools/Astella/Prism V4/Capture 1920 Runtime Evidence %#F4", true)]
        private static bool CanCapture() => EditorApplication.isPlaying && !EditorApplication.isPaused && !EditorApplication.isCompiling &&
            (!_capturing || _captureHost == null || !_captureHost.isActiveAndEnabled);

        private static IEnumerator CaptureFrame()
        {
            // A new frame lets the existing LateUpdate presenters and Canvas finish normally.
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D pixels = null;
            try
            {
                pixels = ScreenCapture.CaptureScreenshotAsTexture();
                if (pixels == null) throw new InvalidOperationException("The Game View screenshot texture was not returned.");
                var session = RaidNetworkSession.Instance;
                string state = session == null ? "no-session" : session.Mode + (session.MenuOpen ? "-menu-open" : "-game-view");
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                Directory.CreateDirectory(EvidenceFolder);
                string stem = Path.Combine(EvidenceFolder, state + "-" + stamp);
                var report = CaptureReport(pixels, stem + ".png", state);
                WriteNew(stem + ".png", pixels.EncodeToPNG());
                WriteNew(stem + ".json", new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(report, true) + "\n"));
                string message = "PRISM RUNTIME EVIDENCE: " + stem + ".{png,json} | actual " + pixels.width + "x" + pixels.height;
                if (report.targetMatched) Debug.Log(message);
                else Debug.LogWarning(message + " | target mismatch; image was NOT resized. Select fixed 1920x1080 and capture again.");
            }
            catch (Exception exception) { Debug.LogError("PRISM RUNTIME EVIDENCE FAILED: " + exception); }
            finally
            {
                if (pixels != null) Object.Destroy(pixels);
                _capturing = false;
                _captureHost = null;
            }
        }

        private static Report CaptureReport(Texture2D pixels, string pngPath, string state)
        {
            var bridges = Object.FindObjectsByType<AstellaPrismHUDBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bridge = bridges.FirstOrDefault(candidate => candidate.isActiveAndEnabled) ?? bridges.FirstOrDefault();
            var session = RaidNetworkSession.Instance;
            var nodes = new List<Node>(); var gauges = new List<Gauge>(); var views = new List<View>(); var notes = new List<string>();
            var report = new Report {
                utc = DateTime.UtcNow.ToString("O"), pngPath = pngPath, state = state,
                mode = session != null ? session.Mode.ToString() : "none", hasSession = session != null,
                menuOpen = session != null && session.MenuOpen, ending = session != null && session.Ending,
                battlePhase = session != null ? session.BattlePhase.ToString() : "none",
                screenWidth = Screen.width, screenHeight = Screen.height, textureWidth = pixels.width, textureHeight = pixels.height,
                targetMatched = Screen.width == TargetWidth && Screen.height == TargetHeight && pixels.width == TargetWidth && pixels.height == TargetHeight,
                frame = Time.frameCount, renderedFrame = Time.renderedFrameCount, timeScale = Time.timeScale,
                stage = StageManager.Instance != null ? StageManager.Instance.currentStage : 0,
                applicationFocused = Application.isFocused, editorPaused = EditorApplication.isPaused,
                layoutEditing = LocalHUDLayoutController.IsEditing, resultsOpen = CombatMetricsUI.ResultsOpen,
                layoutFilePath = LocalHUDLayoutController.Instance != null ? LocalHUDLayoutController.Instance.FilePath : "",
                scenePath = SceneManager.GetActiveScene().path, graphicsDevice = SystemInfo.graphicsDeviceType.ToString(), graphicsDeviceName = SystemInfo.graphicsDeviceName,
                activeBridgeCount = bridges.Count(candidate => candidate.isActiveAndEnabled), totalBridgeCount = bridges.Length
            };
            if (bridge != null)
            {
                var field = bridge.GetType().GetField("presentationRoot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = field != null ? field.GetValue(bridge) : null;
                Component root = value as Component;
                if (root == null && value is GameObject rootObject) root = rootObject.transform;
                report.presentationRootField = field == null ? "field missing" : value == null ? "unassigned" : field.FieldType.FullName;
                report.presentationRoot = DescribeNode("presentationRoot", root);
                AddView("player", bridge.playerView, nodes, gauges, views);
                AddView("center", bridge.centerView, nodes, gauges, views);
                AddView("boss", bridge.bossView, nodes, gauges, views);
                foreach (var slot in bridge.slots ?? Array.Empty<AstellaPrismHUDBridge.SlotMirror>())
                    if (slot != null) AddView("slot-" + slot.sourceIndex, slot.view, nodes, gauges, views);
                foreach (var party in bridge.partyRows ?? Array.Empty<AstellaPrismHUDBridge.PartyMirror>())
                    if (party != null) { AddView("party-" + party.sourceIndex, party.view, nodes, gauges, views); AddGauge("party-" + party.sourceIndex + "/shield", party.shieldFill, nodes, gauges); }
                if (root == null) notes.Add("presentationRoot was not assigned; no inferred root was substituted.");
            }
            else { report.presentationRoot = DescribeNode("presentationRoot", null); notes.Add("No live Prism bridge was found, including inactive objects."); }
            if (report.activeBridgeCount != 1) notes.Add("Expected exactly one active Prism bridge; inspect counts and active bounds.");
            if (!report.targetMatched) notes.Add("Actual Game View dimensions do not match 1920x1080; PNG retains actual captured dimensions.");
            notes.Add("Screen bounds use bottom-left origin; PNG image coordinates use top-left origin.");
            notes.Add("MenuOpen may intentionally hide combat views. Capturing does not change MenuOpen or the session Mode.");
            report.bounds = nodes.ToArray(); report.gauges = gauges.ToArray(); report.views = views.ToArray(); report.notes = notes.ToArray();
            return report;
        }

        private static void AddView(string key, PrismV4Bindings view, List<Node> nodes, List<Gauge> gauges, List<View> views)
        {
            nodes.Add(DescribeNode(key, view));
            if (view == null) return;
            views.Add(new View { key = key, path = PathOf(view.transform), active = view.gameObject.activeInHierarchy,
                name = TextOf(view.nameText), value = TextOf(view.valueText), resource = TextOf(view.resourceText), binding = TextOf(view.keyText), cooldown = TextOf(view.cooldownText) });
            AddGauge(key + "/HP", view.healthFill, nodes, gauges);
            AddGauge(key + "/job-resource", view.jobResourceFill, nodes, gauges);
            AddGauge(key + "/ultimate", view.ultimateChargeFill, nodes, gauges);
            AddGauge(key + "/boss-HP", view.bossFill, nodes, gauges);
            AddGauge(key + "/cast", view.castFill, nodes, gauges);
            if (view.button != null) nodes.Add(DescribeNode(key + "/button", view.button));
            if (view.artwork != null) nodes.Add(DescribeNode(key + "/artwork", view.artwork));
            if (view.cooldownShade != null) nodes.Add(DescribeNode(key + "/cooldown", view.cooldownShade));
        }

        private static void AddGauge(string key, PrismV4Gauge gauge, List<Node> nodes, List<Gauge> gauges)
        {
            if (gauge == null) return;
            nodes.Add(DescribeNode(key, gauge));
            Sprite sprite = gauge.overrideSprite != null ? gauge.overrideSprite : gauge.sprite;
            gauges.Add(new Gauge { key = key, path = PathOf(gauge.transform), active = gauge.isActiveAndEnabled,
                ratio = gauge.Ratio, previewValue = gauge.previewValue, finite = Finite(gauge.Ratio) && Finite(gauge.previewValue),
                hasSource = gauge.sourceFill != null, sourcePath = gauge.sourceFill != null ? PathOf(gauge.sourceFill.transform) : "",
                sourceFillAmount = gauge.sourceFill != null ? gauge.sourceFill.fillAmount : 0, innerBounds = gauge.innerBounds,
                spritePath = sprite != null ? AssetDatabase.GetAssetPath(sprite) : "", spriteRect = sprite != null ? sprite.rect : default,
                spritePadding = sprite != null ? DataUtility.GetPadding(sprite) : Vector4.zero,
                outerUV = sprite != null ? DataUtility.GetOuterUV(sprite) : Vector4.zero });
        }

        private static Node DescribeNode(string key, Component component)
        {
            var node = new Node { key = key, found = component != null };
            if (component == null) return node;
            Transform transform = component.transform;
            node.path = PathOf(transform); node.activeSelf = transform.gameObject.activeSelf; node.activeInHierarchy = transform.gameObject.activeInHierarchy;
            node.componentEnabled = component is Behaviour behaviour ? behaviour.enabled : true;
            node.localScale = transform.localScale; node.lossyScale = transform.lossyScale; node.worldPosition = transform.position;
            var rect = transform as RectTransform;
            if (rect == null) return node;
            node.localRect = rect.rect; node.anchoredPosition = rect.anchoredPosition; node.sizeDelta = rect.sizeDelta;
            node.anchorMin = rect.anchorMin; node.anchorMax = rect.anchorMax; node.pivot = rect.pivot;
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            if (canvas != null) canvas = canvas.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            node.canvasPath = canvas != null ? PathOf(canvas.transform) : ""; node.renderMode = canvas != null ? canvas.renderMode.ToString() : "none";
            node.canvasScaleFactor = canvas != null ? canvas.scaleFactor : 0; node.cameraPath = camera != null ? PathOf(camera.transform) : "";
            node.worldCorners = new Vector3[4]; rect.GetWorldCorners(node.worldCorners);
            node.screenCorners = node.worldCorners.Select(point => RectTransformUtility.WorldToScreenPoint(camera, point)).ToArray();
            float left = node.screenCorners.Min(point => point.x), right = node.screenCorners.Max(point => point.x);
            float bottom = node.screenCorners.Min(point => point.y), top = node.screenCorners.Max(point => point.y);
            node.screenBounds = new Vector4(left, bottom, right, top);
            node.insideScreen = left >= -1 && bottom >= -1 && right <= Screen.width + 1 && top <= Screen.height + 1;
            return node;
        }

        private static string PathOf(Transform transform)
        {
            if (transform == null) return "";
            var names = new List<string>();
            for (Transform current = transform; current != null; current = current.parent) names.Add(current.name);
            names.Reverse(); return string.Join("/", names);
        }

        private static string TextOf(Text text) => text != null ? text.text ?? "" : "";
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void WriteNew(string path, byte[] bytes)
        { using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) file.Write(bytes, 0, bytes.Length); }

        private static void RequireOriginalProject()
        {
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Run this evidence helper only in the original C:/Astella/Astella project.");
        }
    }
}
#endif
