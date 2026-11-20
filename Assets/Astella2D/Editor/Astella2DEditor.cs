#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Astella.TwoD.Editor
{
    /// <summary>Explicit authoring and checks for the isolated 2D game. Never rewrites the 3D scene.</summary>
    public static class Astella2DEditor
    {
        public const string ScenePath = "Assets/Astella2D/Scenes/Astella2D.unity";
        private const string AssetRoot = "Assets/Astella2D";
        private const string FontRoot = AssetRoot + "/Resources/Astella2D/Fonts";
        private const string Key = "Astella.TwoD.Checks.";
        private const string DefaultEvidence = "C:/Astella/2d-evidence-20261120";

        [Serializable]
        private sealed class CheckReport
        {
            public bool success;
            public string utc, project, scene, screenshot, error, graphics;
            public string[] checks;
            public int screenWidth, screenHeight, frame, attacks, damageEvents, dodges;
            public int observedAttacks, observedDamageEvents, observedDodges;
            public float playerHealth, bossHealth;
            public bool actualPlayMode, actualScreenshot;
        }

        [MenuItem("Astella/2D/Create Scene (Preserve Existing)")]
        public static void BuildScene()
        {
            RequireOriginalProject();
            RequireStoppedAndCleanScenes();
            EnsureFolder(AssetRoot + "/Scenes");
            ConfigureTextures();
            CopyFont("MaruBuri-SemiBold.ttf");
            CopyFont("Pretendard-Medium.ttf");

            if (File.Exists(ScenePath))
            {
                var existing = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                ValidateScene(existing);
                Debug.Log("ASTELLA 2D: existing scene preserved and opened: " + ScenePath);
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Astella 2D Game").AddComponent<Astella2DGame>();
            ValidateScene(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Could not save the new 2D scene: " + ScenePath);
            Debug.Log("ASTELLA 2D: created a separate scene; 3D scene and build settings preserved: " + ScenePath);
        }

        [MenuItem("Astella/2D/Open Scene")]
        public static void OpenScene()
        {
            RequireOriginalProject();
            RequireStoppedAndCleanScenes();
            if (!File.Exists(ScenePath)) { BuildScene(); return; }
            ValidateScene(EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single));
        }

        [MenuItem("Astella/2D/Play Game")]
        public static void Play2D()
        {
            BuildScene();
            PrepareGameView();
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Astella/2D/Build Windows Player")]
        public static void BuildWindows()
        {
            RequireOriginalProject();
            RequireStoppedAndCleanScenes();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new BuildFailedException("Install Windows Build Support for this Unity editor.");
            BuildScene();
            string output = Path.Combine(ProjectRoot(), "Builds", "Astella2DWindows");
            string executable = Path.Combine(output, "Astella2D.exe");
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("Astella2D"))
            {
                using (process)
                {
                    if (!process.HasExited && string.Equals(process.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase))
                        throw new BuildFailedException("Close the running 2D player before replacing its build.");
                }
            }
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = executable,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Astella 2D build failed: " + report.summary.result);
            File.WriteAllText(Path.Combine(output, "BUILD_INFO.txt"),
                "Astella 2D\nScene: " + ScenePath + "\nUTC: " + DateTime.UtcNow.ToString("O") +
                "\nUnity: " + Application.unityVersion + "\n", new System.Text.UTF8Encoding(false));
            Debug.Log("ASTELLA 2D BUILD PASS: " + executable);
        }

        /// <summary>Run with -batchmode, graphics enabled, and no -quit; the checks exit Unity explicitly.</summary>
        public static void RunChecks()
        {
            RequireOriginalProject();
            if (!Application.isBatchMode) throw new InvalidOperationException("RunChecks is an explicit batch-only entry point.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("2D Play rendering checks require graphics; omit -nographics.");
            BuildScene();
            string evidence = EvidenceDirectory();
            Directory.CreateDirectory(evidence);
            SessionState.SetString(Key + "evidence", evidence);
            SessionState.SetString(Key + "checks", "");
            SessionState.SetString(Key + "phase", "enter");
            SessionState.SetInt(Key + "errors", 0);
            SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 180f);
            PrepareGameView();
            HookChecks();
            EditorApplication.isPaused = false;
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void HookChecks()
        {
            if (!Application.isBatchMode || SessionState.GetString(Key + "phase", "").Length == 0) return;
            EditorApplication.update -= TickChecks;
            EditorApplication.update += TickChecks;
            Application.logMessageReceived -= ObserveLog;
            Application.logMessageReceived += ObserveLog;
        }

        private static void ObserveLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                SessionState.SetInt(Key + "errors", SessionState.GetInt(Key + "errors", 0) + 1);
        }

        private static void TickChecks()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0))
                    throw new TimeoutException("2D Play checks timed out in phase " + SessionState.GetString(Key + "phase", ""));
                if (SessionState.GetInt(Key + "errors", 0) != 0)
                    throw new InvalidOperationException("Unity logged an error during the 2D Play checks; inspect the batch log.");
                string phase = SessionState.GetString(Key + "phase", "");
                if (phase == "exit")
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
                    StopChecks();
                    Debug.Log("ASTELLA 2D PLAY CHECKS PASS: movement, dash, basic attack, defeat, restart, telegraph screenshot and hit.");
                    EditorApplication.Exit(0);
                    return;
                }
                if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.isCompiling) return;
                var game = Astella2DGame.Instance;
                if (game == null) return;
                float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "phaseAt", 0);
                if (phase == "enter")
                {
                    Require(!game.Running && !game.Finished, "Initial screen is the 2D menu.");
                    Record("initial-menu");
                    game.BeginRun(0);
                    game.SetAutomation(true);
                    Require(game.Running && !game.Finished && game.PlayerHealth > 0 && game.BossHealth > 0,
                        "Starting an expedition must create a live player and boss.");
                    Record("begin-run");
                    SavePosition("moveStart", game.PlayerPosition);
                    game.RequestMove(Vector2.right);
                    Phase("move");
                }
                else if (phase == "move" && elapsed > .65f)
                {
                    Require(Vector2.Distance(game.PlayerPosition, LoadPosition("moveStart")) > .35f,
                        "The actual 2D player did not move under its normal movement simulation.");
                    Record("movement");
                    game.RequestMove(Vector2.zero);
                    game.AimAt(game.BossPosition);
                    SavePosition("dashStart", game.PlayerPosition);
                    SessionState.SetInt(Key + "dashBefore", game.DodgeCount);
                    float healthBeforeDash = game.PlayerHealth;
                    Require(game.TryUseSkill(4), "A ready dash must be accepted.");
                    game.ApplyPlayerDamage(1f);
                    Require(Mathf.Approximately(game.PlayerHealth, healthBeforeDash) &&
                        game.DodgeCount > SessionState.GetInt(Key + "dashBefore", 0),
                        "Damage during the dash invulnerability window must be blocked and recorded as an actual dodge.");
                    Record("dash-invulnerability");
                    Phase("dash");
                }
                else if (phase == "dash" && elapsed > .45f)
                {
                    Require(game.DodgeCount > SessionState.GetInt(Key + "dashBefore", 0) &&
                        Vector2.Distance(game.PlayerPosition, LoadPosition("dashStart")) > .2f,
                        "The dash did not advance actual position after its invulnerability dodge was observed.");
                    Record("dash");
                    SessionState.SetInt(Key + "observedDodges", game.DodgeCount);
                    Phase("approach");
                }
                else if (phase == "approach")
                {
                    Vector2 delta = game.BossPosition - game.PlayerPosition;
                    if (delta.magnitude > Mathf.Max(.5f, game.CombatRange * .72f)) game.RequestMove(delta.normalized);
                    else
                    {
                        game.RequestMove(Vector2.zero);
                        game.AimAt(game.BossPosition);
                        Phase("attack");
                    }
                }
                else if (phase == "attack")
                {
                    game.AimAt(game.BossPosition);
                    float bossBefore = game.BossHealth;
                    int attacksBefore = game.Attacks;
                    int damageBefore = game.DamageEvents;
                    if (game.TryUseSkill(0))
                    {
                        Require(game.BossHealth < bossBefore && game.Attacks == attacksBefore + 1 &&
                            game.DamageEvents == damageBefore + 1,
                            "A basic attack must produce an actual boss damage event.");
                        Record("basic-attack-and-boss-damage");
                        SessionState.SetInt(Key + "observedAttacks", game.Attacks);
                        SessionState.SetInt(Key + "observedDamageEvents", game.DamageEvents);
                        Phase("death");
                    }
                    else Require(elapsed < 5f, "Basic attacks did not damage the boss within five seconds in combat range.");
                }
                else if (phase == "death")
                {
                    // A recent hit can temporarily block this damage. Wait for that window to end.
                    if (!game.Finished) game.ApplyPlayerDamage(game.PlayerHealth + 10000f);
                    if (!game.Finished)
                    {
                        Require(elapsed < 4f, "Lethal damage never reached the defeat state.");
                        return;
                    }
                    Require(game.Finished && !game.Won && game.PlayerHealth <= 0, "Lethal damage must reach the actual defeat state.");
                    Record("defeat");
                    game.ReturnToMenu();
                    Require(!game.Running && !game.Finished, "Returning from defeat must restore the menu state.");
                    Record("return-to-menu");
                    game.BeginRun(0);
                    game.SetAutomation(true);
                    game.RequestMove(Vector2.zero);
                    Require(game.Running && !game.Finished && game.PlayerHealth > 0 && game.BossHealth > 0,
                        "Restart must restore a live expedition.");
                    Record("restart");
                    Phase("restarted");
                }
                else if (phase == "restarted")
                {
                    Require(game.Running && !game.Finished && game.PlayerHealth > 0, "Restarted expedition must remain alive before capture.");
                    if (game.ActiveTelegraphs == 0)
                    {
                        Require(elapsed < 15f, "The restarted boss did not start a telegraph.");
                        return;
                    }
                    Record("boss-telegraph");
                    Require(Screen.width >= 320 && Screen.height >= 180, "The Game View must have a rendered size.");
                    string path = Path.Combine(SessionState.GetString(Key + "evidence", DefaultEvidence), "unity-play.png");
                    SessionState.SetFloat(Key + "healthBeforeTelegraphHit", game.PlayerHealth);
                    SessionState.SetString(Key + "captureStarted", DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    ScreenCapture.CaptureScreenshot(path);
                    Phase("screenshot");
                }
                else if (phase == "screenshot")
                {
                    string path = Path.Combine(SessionState.GetString(Key + "evidence", DefaultEvidence), "unity-play.png");
                    long started = long.Parse(SessionState.GetString(Key + "captureStarted", "0"), System.Globalization.CultureInfo.InvariantCulture);
                    if (File.Exists(path) && new FileInfo(path).Length > 1024 && File.GetLastWriteTimeUtc(path).Ticks >= started)
                    {
                        Record("rendered-screenshot");
                        Phase("telegraph-hit");
                    }
                    else Require(elapsed < 15f, "Unity did not write a new rendered screenshot. Check that graphics and the Game View are available.");
                }
                else if (phase == "telegraph-hit")
                {
                    // With no movement after restart, the first targeted circle should hit the player.
                    if (game.PlayerHealth >= SessionState.GetFloat(Key + "healthBeforeTelegraphHit", 0))
                    {
                        Require(elapsed < 8f, "The first targeted telegraph did not damage the stationary player.");
                        return;
                    }
                    Record("boss-telegraph-hit");
                    string path = Path.Combine(SessionState.GetString(Key + "evidence", DefaultEvidence), "unity-play.png");
                    WriteReport(true, "", game, path);
                    Phase("exit");
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception error)
            {
                try { WriteReport(false, error.ToString(), Astella2DGame.Instance, ""); }
                catch (Exception reportError) { Debug.LogError("Could not save 2D check report: " + reportError.Message); }
                StopChecks();
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static void WriteReport(bool success, string error, Astella2DGame game, string screenshot)
        {
            string evidence = SessionState.GetString(Key + "evidence", DefaultEvidence);
            Directory.CreateDirectory(evidence);
            var report = new CheckReport {
                success = success, utc = DateTime.UtcNow.ToString("O"), project = ProjectRoot(),
                scene = SceneManager.GetActiveScene().path, screenshot = screenshot, error = error,
                checks = SessionState.GetString(Key + "checks", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries),
                graphics = SystemInfo.graphicsDeviceType + ": " + SystemInfo.graphicsDeviceName,
                screenWidth = Screen.width, screenHeight = Screen.height, frame = Time.frameCount,
                playerHealth = game != null ? game.PlayerHealth : 0, bossHealth = game != null ? game.BossHealth : 0,
                attacks = game != null ? game.Attacks : 0, damageEvents = game != null ? game.DamageEvents : 0,
                dodges = game != null ? game.DodgeCount : 0,
                observedAttacks = SessionState.GetInt(Key + "observedAttacks", 0),
                observedDamageEvents = SessionState.GetInt(Key + "observedDamageEvents", 0),
                observedDodges = SessionState.GetInt(Key + "observedDodges", 0),
                actualPlayMode = EditorApplication.isPlaying, actualScreenshot = !string.IsNullOrEmpty(screenshot) && File.Exists(screenshot)
            };
            File.WriteAllText(Path.Combine(evidence, "unity-play-report.json"), JsonUtility.ToJson(report, true) + "\n", new System.Text.UTF8Encoding(false));
        }

        private static void ConfigureTextures()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { AssetRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                bool changed = importer.textureType != TextureImporterType.Default || importer.mipmapEnabled ||
                    importer.filterMode != FilterMode.Point || importer.textureCompression != TextureImporterCompression.Uncompressed ||
                    !importer.alphaIsTransparency || importer.maxTextureSize != 2048 || !importer.isReadable ||
                    importer.npotScale != TextureImporterNPOTScale.None || importer.wrapMode != TextureWrapMode.Clamp;
                if (!changed) continue;
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = 2048;
                importer.isReadable = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        private static void CopyFont(string fileName)
        {
            EnsureFolder(FontRoot);
            string destination = FontRoot + "/" + fileName;
            if (File.Exists(destination)) return;
            string source = "Assets/UI/AstellaPrismV4/Typography/Fonts/" + fileName;
            if (!File.Exists(source)) throw new FileNotFoundException("The existing Astella font is missing.", source);
            if (!AssetDatabase.CopyAsset(source, destination)) throw new IOException("Could not create the 2D font copy: " + destination);
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ValidateScene(Scene scene)
        {
            int roots = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Astella2DGame>(true).Length);
            if (roots != 1) throw new InvalidOperationException("The 2D scene must have exactly one Astella2DGame; existing authored content was preserved.");
        }

        private static void PrepareGameView()
        {
            Type type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView", true);
            var method = type.GetMethod("SetCustomResolution", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Vector2), typeof(string) }, null);
            if (method == null) throw new MissingMethodException(type.FullName, "SetCustomResolution(Vector2, string)");
            EditorWindow view = EditorWindow.GetWindow(type);
            method.Invoke(view, new object[] { new Vector2(1920, 1080), "Astella 2D 1920x1080" });
            view.Show(); view.Focus(); view.Repaint();
        }

        private static void RequireStoppedAndCleanScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play, finish compilation and close Prefab Mode before authoring the 2D scene.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("An open scene has unsaved user changes. Preserve those changes before opening the separate 2D scene.");
        }

        private static void RequireOriginalProject()
        {
            if (File.Exists(Path.Combine(ProjectRoot(), ".git")))
                throw new InvalidOperationException("Run Unity only in the original project, never in a Codex worktree.");
        }

        private static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;

        private static string EvidenceDirectory()
        {
            const string prefix = "--astella2d-evidence=";
            string argument = Environment.GetCommandLineArgs().FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
            return Path.GetFullPath(argument == null ? DefaultEvidence : argument.Substring(prefix.Length));
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, parts[i])))
                    throw new IOException("Could not create asset folder: " + next);
                current = next;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Record(string check)
        {
            SessionState.SetString(Key + "checks", SessionState.GetString(Key + "checks", "") + check + "\n");
            Debug.Log("ASTELLA 2D CHECK: " + check);
        }

        private static void Phase(string phase)
        {
            SessionState.SetString(Key + "phase", phase);
            SessionState.SetFloat(Key + "phaseAt", (float)EditorApplication.timeSinceStartup);
        }

        private static void SavePosition(string name, Vector2 value)
        {
            SessionState.SetFloat(Key + name + "X", value.x);
            SessionState.SetFloat(Key + name + "Y", value.y);
        }

        private static Vector2 LoadPosition(string name) => new Vector2(
            SessionState.GetFloat(Key + name + "X", 0), SessionState.GetFloat(Key + name + "Y", 0));

        private static void StopChecks()
        {
            SessionState.SetString(Key + "phase", "");
            EditorApplication.update -= TickChecks;
            Application.logMessageReceived -= ObserveLog;
        }
    }
}
#endif
