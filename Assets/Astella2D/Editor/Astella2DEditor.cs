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
            public float playerHealth, bossHealth, autopilotBossRatio;
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
                var game = ObjectInScene(existing);
                if (Astella2DPrismAuthoring.AssignIfMissing(game) && !EditorSceneManager.SaveScene(existing))
                    throw new IOException("Could not save the 2D Prism HUD binding: " + ScenePath);
                Debug.Log("ASTELLA 2D: existing scene preserved and opened: " + ScenePath);
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var newGame = new GameObject("Astella 2D Game").AddComponent<Astella2DGame>();
            Astella2DPrismAuthoring.AssignIfMissing(newGame);
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
            SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 420f);
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
                    Debug.Log("ASTELLA 2D PLAY CHECKS PASS: movement, dash, kit skills, revive, defeat, restart, telegraph hit, three encounters with blessings, victory, 13 kits and autopilot combat.");
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
                    var hud = RequirePrismHud();
                    Require(hud.party.Length == 4 && hud.slots.Length == 8 && hud.center.ultimateChargeFill.Ratio > 0 &&
                        hud.party.All(row => row.artwork.enabled && row.artwork.sprite != null),
                        "The Prism composition must show all four members and the eight-slot dock with its ultimate gauge.");
                    hud.party[1].button.onClick.Invoke();
                    Record("prism-hud-binding");
                    SavePosition("moveStart", game.PlayerPosition);
                    game.RequestMove(Vector2.right);
                    Phase("move");
                }
                else if (phase == "move" && elapsed > .65f)
                {
                    Require(Vector2.Distance(game.PlayerPosition, LoadPosition("moveStart")) > .35f,
                        "The actual 2D player did not move under its normal movement simulation.");
                    Record("movement");
                    var hud = RequirePrismHud();
                    Require(hud.party[1].selectedOverlay.enabled && !hud.party[0].selectedOverlay.enabled,
                        "The Prism party button did not update the selected row.");
                    Record("prism-party-selection");
                    game.RequestMove(Vector2.zero);
                    game.AimAt(game.BossPosition);
                    SavePosition("dashStart", game.PlayerPosition);
                    SessionState.SetInt(Key + "dashBefore", game.DodgeCount);
                    float healthBeforeDash = game.PlayerHealth;
                    Require(hud.slots[7].button.IsInteractable(), "The authored dash button must be ready.");
                    hud.slots[7].button.onClick.Invoke();
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
                    var hud = RequirePrismHud();
                    Require(hud.slots[7].cooldownShade.enabled && hud.player.jobResourceFill.Ratio < 1,
                        "Dash cooldown must appear in the Prism slot and right gauge.");
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
                    var button = RequirePrismHud().slots[0].button;
                    if (button.IsInteractable()) button.onClick.Invoke();
                    if (game.Attacks > attacksBefore)
                    {
                        Require(game.BossHealth < bossBefore && game.Attacks == attacksBefore + 1 &&
                            game.DamageEvents == damageBefore + 1,
                            "A basic attack must produce an actual boss damage event.");
                        Record("basic-attack-and-boss-damage");
                        SessionState.SetInt(Key + "observedAttacks", game.Attacks);
                        SessionState.SetInt(Key + "observedDamageEvents", game.DamageEvents);
                        Phase("skills");
                    }
                    else Require(elapsed < 5f, "Basic attacks did not damage the boss within five seconds in combat range.");
                }
                else if (phase == "skills")
                {
                    // Character 0 is the tank kit: Q strikes and guards, E guards the party, R spends full charge.
                    float before = game.BossHealth;
                    Require(game.TryUseSkill(1) && game.BossHealth < before && game.CooldownOf(1) > 0 && game.PlayerGuard > 0,
                        "The tank's Q must hit the boss, start its cooldown and guard the player.");
                    Record("kit-primary");
                    Require(game.TryUseSkill(2) && game.CooldownOf(2) > 0, "The tank's E must start its cooldown.");
                    Record("kit-utility");
                    game.DebugChargeUltimate();
                    before = game.BossHealth;
                    Require(game.TryUseSkill(3) && game.BossHealth < before - 1000 && game.Ultimate <= 0,
                        "R must spend the full charge and hit the boss hard.");
                    Require(!game.TryUseSkill(3), "R must not fire again without charge.");
                    Record("kit-ultimate");
                    Phase("skills-hud");
                }
                else if (phase == "skills-hud")
                {
                    if (elapsed < .2f) return;
                    var hud = RequirePrismHud();
                    Require(hud.slots[1].cooldownShade.enabled && hud.slots[4].cooldownShade.enabled,
                        "The Q and E cooldowns must show on the Prism slots.");
                    Record("kit-cooldown-hud");
                    Phase("down");
                }
                else if (phase == "down")
                {
                    if (!game.Downed) game.ApplyPlayerDamage(game.PlayerHealth + 100000f);
                    if (!game.Downed)
                    {
                        Require(!game.Finished && elapsed < 4f, "Lethal damage with revives left must down the player, not end the run.");
                        return;
                    }
                    Require(game.Running && !game.Finished && game.RevivesUsed >= 1 && game.RevivesLeft < 2,
                        "A downed player must spend one shared revive.");
                    Require(!game.TryUseSkill(0), "A downed player cannot act.");
                    Record("downed-with-revive");
                    Phase("revive");
                }
                else if (phase == "revive")
                {
                    if (game.Downed) { Require(elapsed < 5f, "The downed player was not revived."); return; }
                    Require(game.Running && game.PlayerHealth > 0, "Revival must restore a live player in the same encounter.");
                    Record("revived");
                    Phase("death");
                }
                else if (phase == "death")
                {
                    // Spend the remaining revives. Invulnerability after a revive briefly blocks lethal damage.
                    if (!game.Finished && !game.Downed) game.ApplyPlayerDamage(game.PlayerHealth + 100000f);
                    if (!game.Finished)
                    {
                        Require(elapsed < 20f, "Lethal damage never reached the defeat state after revives ran out.");
                        return;
                    }
                    Require(game.Finished && !game.Won && game.PlayerHealth <= 0 && game.RevivesLeft == 0 && game.ResultTitle == "원정 실패",
                        "Lethal damage with no revives left must reach the actual defeat state.");
                    Record("defeat");
                    var wipe = UnityEngine.Object.FindObjectsByType<RaidRogue.RaidUIView>(FindObjectsSortMode.None)
                        .Single(view => view.viewId == "wipe");
                    var returnButton = wipe.actions.Single(action => action.id == "메인 메뉴로").button;
                    Require(returnButton.isActiveAndEnabled, "The authored wipe view must display its menu button.");
                    returnButton.onClick.Invoke();
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
                    var boss = RequirePrismHud().boss;
                    Require(boss.castFill.isActiveAndEnabled && boss.castFill.Ratio > 0 && !string.IsNullOrEmpty(boss.castNameText.text),
                        "The authored Prism cast bar must reflect the first boss warning.");
                    Record("prism-cast-bar");
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
                    Require(RequirePrismHud().player.healthFill.Ratio < 1, "The Prism health bar must show the hit.");
                    Record("prism-health-after-hit");
                    // Ranged archer: the basic attack reaches the boss from the spawn point.
                    game.BeginRun(8);
                    game.SetAutomation(true);
                    Require(game.Running && game.Encounter == 0, "A new run must start at the first encounter.");
                    game.DebugSetBossHealth(1);
                    Phase("win");
                }
                else if (phase == "win")
                {
                    if (!game.Finished)
                    {
                        game.TryUseSkill(0);
                        if (!game.Finished) { Require(elapsed < 5f, "The weakened boss was not defeated."); return; }
                    }
                    int encounter = game.Encounter;
                    Require(game.Won && game.RewardOffered, "Winning encounter " + (encounter + 1) + " must offer blessings.");
                    var reward = UnityEngine.Object.FindObjectsByType<RaidRogue.RaidUIView>(FindObjectsSortMode.None)
                        .Single(view => view.viewId == "skill-reward" && view.isActiveAndEnabled);
                    for (int i = 0; i < 3; i++)
                        Require(!string.IsNullOrEmpty(game.OfferedBlessing(i)) &&
                            reward.actions.Single(action => action.id == "choice." + i).button.IsInteractable(),
                            "Each of the three blessing choices must be offered and clickable.");
                    Record("reward-offer-" + (encounter + 1));
                    if (encounter == 0) { CaptureNow("reward.png"); Phase("reward-shot"); return; }
                    PickBlessing(game);
                }
                else if (phase == "reward-shot")
                {
                    if (!CaptureReady("reward.png")) { Require(elapsed < 15f, "Unity did not write the reward screenshot."); return; }
                    Record("reward-screenshot");
                    PickBlessing(game);
                }
                else if (phase == "final-telegraph")
                {
                    if (game.ActiveTelegraphs == 0) { Require(elapsed < 15f, "The final boss did not cast."); return; }
                    if (elapsed < 1f) return;
                    CaptureNow("encounter3.png");
                    Phase("final-shot");
                }
                else if (phase == "final-shot")
                {
                    if (!CaptureReady("encounter3.png")) { Require(elapsed < 15f, "Unity did not write the encounter 3 screenshot."); return; }
                    Record("encounter-3-screenshot");
                    game.DebugSetBossHealth(1);
                    Phase("final-win");
                }
                else if (phase == "final-win")
                {
                    if (!game.Finished)
                    {
                        if (game.Downed) { Require(elapsed < 10f, "The player stayed down during the final check."); return; }
                        game.DebugSetBossHealth(1);
                        game.TryUseSkill(0);
                        if (!game.Finished) { Require(elapsed < 10f, "The final boss was not defeated."); return; }
                    }
                    Require(game.Won && !game.RewardOffered && game.ResultShown && game.ResultTitle == "원정 완료",
                        "Winning the third encounter must show the expedition-complete result.");
                    Record("final-victory");
                    Phase("final-result");
                }
                else if (phase == "final-result")
                {
                    if (elapsed < .5f) return;
                    CaptureNow("victory.png");
                    Phase("roster");
                }
                else if (phase == "roster")
                {
                    if (!CaptureReady("victory.png")) { Require(elapsed < 15f, "Unity did not write the victory screenshot."); return; }
                    var directions = new[] { Vector2.down, new Vector2(1, -1), Vector2.right, Vector2.one, Vector2.up, new Vector2(-1, 1), Vector2.left, -Vector2.one };
                    for (int i = 0; i < Astella.TwoD.Astella2DRoster.All.Count; i++)
                    {
                        string name = Astella.TwoD.Astella2DRoster.All[i].Name;
                        game.BeginRun(i);
                        game.SetAutomation(true);
                        var kit = game.PlayerKit;
                        Require(game.Running && Mathf.Approximately(game.PlayerMaxHealth, kit.MaxHp) && game.LivingAllies == 3,
                            name + " must start with its kit health and three allies.");
                        foreach (var direction in directions)
                            Require(Astella.TwoD.Astella2DRoster.Frame(i, direction) != null, name + " is missing a facing sprite.");
                        game.DebugTeleport(game.BossPosition + new Vector2(0, -1.6f));
                        for (int slot = 0; slot < 4; slot++)
                        {
                            if (slot == 3) game.DebugChargeUltimate();
                            Require(game.TryUseSkill(slot), name + " could not use " + kit.Skill(slot).Name + " (slot " + slot + ").");
                        }
                        Require(game.BossHealth < game.BossMaxHealth, name + " skills must damage the boss.");
                    }
                    Record("roster-13-kits");
                    game.ReturnToMenu();
                    Phase("menu-shot");
                }
                else if (phase == "menu-shot")
                {
                    if (elapsed < .5f) return;
                    CaptureNow("menu.png");
                    Phase("menu-wait");
                }
                else if (phase == "menu-wait")
                {
                    if (!CaptureReady("menu.png")) { Require(elapsed < 15f, "Unity did not write the menu screenshot."); return; }
                    Record("menu-screenshot");
                    game.BeginRun(6);
                    game.SetAutopilot(true);
                    SessionState.SetInt(Key + "autopilotShot", 0);
                    SessionState.SetFloat(Key + "autopilotFirstWin", -1);
                    Phase("autopilot");
                }
                else if (phase == "autopilot")
                {
                    // The player is driven by the party AI for a stretch of real combat.
                    if (SessionState.GetInt(Key + "autopilotShot", 0) == 0 && elapsed > 20f && game.Running)
                    {
                        CaptureNow("autopilot.png");
                        SessionState.SetInt(Key + "autopilotShot", 1);
                    }
                    if (game.RewardOffered)
                    {
                        if (SessionState.GetFloat(Key + "autopilotFirstWin", -1) < 0) SessionState.SetFloat(Key + "autopilotFirstWin", elapsed);
                        game.ChooseBlessing(0);
                    }
                    if (elapsed < 60f && !game.Finished) return;
                    Debug.Log("ASTELLA 2D AUTOPILOT: encounter " + (game.Encounter + 1) + ", boss " + (game.BossHealth / Mathf.Max(1, game.BossMaxHealth)).ToString("P0") +
                        ", first win at " + SessionState.GetFloat(Key + "autopilotFirstWin", -1).ToString("0.0") + "s, revives used " + game.RevivesUsed +
                        ", dodges " + game.DodgeCount + ", finished " + game.Finished + ", won " + game.Won);
                    Require(game.Attacks > 10 && game.DamageDealt > 1000, "Autopilot combat must keep attacking the boss.");
                    Require(CaptureReady("autopilot.png") || SessionState.GetInt(Key + "autopilotShot", 0) == 0,
                        "Unity did not write the autopilot screenshot.");
                    Record("autopilot-combat");
                    SessionState.SetFloat(Key + "autopilotBoss", game.BossHealth / Mathf.Max(1, game.BossMaxHealth));
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

        // Clicks the authored reward button and verifies the next encounter starts fresh.
        private static void PickBlessing(Astella2DGame game)
        {
            int encounter = game.Encounter;
            var reward = UnityEngine.Object.FindObjectsByType<RaidRogue.RaidUIView>(FindObjectsSortMode.None)
                .Single(view => view.viewId == "skill-reward" && view.isActiveAndEnabled);
            reward.actions.Single(action => action.id == "choice." + (encounter % 3)).button.onClick.Invoke();
            var next = Astella2DEncounters.Get(encounter + 1);
            Require(game.Running && game.Encounter == encounter + 1 && Mathf.Approximately(game.BossMaxHealth, next.BossHp) &&
                Mathf.Approximately(game.BossHealth, game.BossMaxHealth) && game.RevivesLeft >= 2,
                "Choosing a blessing must start the next encounter with a fresh boss and revives.");
            Record("encounter-" + (encounter + 2) + "-started");
            if (game.Encounter < 2) game.DebugSetBossHealth(1);
            Phase(game.Encounter < 2 ? "win" : "final-telegraph");
        }

        private static string EvidencePath(string name) => Path.Combine(SessionState.GetString(Key + "evidence", DefaultEvidence), name);

        private static void CaptureNow(string name)
        {
            string path = EvidencePath(name);
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
        }

        private static bool CaptureReady(string name)
        {
            string path = EvidencePath(name);
            return File.Exists(path) && new FileInfo(path).Length > 1024;
        }

        private static Astella2DPrismView RequirePrismHud()
        {
            var hud = UnityEngine.Object.FindFirstObjectByType<Astella2DPrismView>();
            Require(hud != null, "The authored 2D Prism HUD must be active in combat.");
            return hud;
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
                dodges = game != null ? game.DodgeCount : 0, autopilotBossRatio = SessionState.GetFloat(Key + "autopilotBoss", -1),
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

        private static Astella2DGame ObjectInScene(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Astella2DGame>(true)).Single();

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
