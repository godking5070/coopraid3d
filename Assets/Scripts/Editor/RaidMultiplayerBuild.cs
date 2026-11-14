using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using System.IO;
using System.Linq;

namespace RaidRogue.EditorTools
{
    public static class RaidMultiplayerBuild
    {
        public const string MainScene = "Assets/Scenes/SampleScene.unity";
        [MenuItem("Astella/Build Part 1 Playable (Windows)")]
        public static void BuildPartOne()
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            if (File.Exists(Path.Combine(project, ".git")))
                throw new BuildFailedException("worktree에서는 Unity 실행/빌드를 금지합니다. 원본 프로젝트를 사용하세요.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Play 종료 후 빌드하세요.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty || !Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    throw new BuildFailedException("열린 씬의 사용자 변경을 먼저 저장하세요. 빌드 도구는 이를 덮어쓰지 않습니다.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new BuildFailedException("Windows Build Support 모듈이 필요합니다.");
            string output = Path.Combine(project, "Builds", "AstellaWindows");
            var settings = RaidSteamSettings.Load();
            if (settings.appId == 0 && File.Exists(Path.Combine(output, "steam_appid.txt")))
                throw new BuildFailedException("출력 폴더의 이전 steam_appid.txt를 확인하세요. ID 미설정 빌드에 이전 ID를 사용하지 않습니다.");
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                // Editor play and every game build use the same authored scene.
                // Missing or invalid content must never trigger scene regeneration.
                if (!File.Exists(Path.Combine(project, MainScene)))
                    throw new BuildFailedException("메인 씬이 없습니다: " + MainScene + ". 백업에서 복구하세요.");
                EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
                ValidatePartOneScene(SceneManager.GetActiveScene());
                Directory.CreateDirectory(output);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { MainScene }, locationPathName = Path.Combine(output, "Astella.exe"),
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("1부 빌드 실패: " + report.summary.result);
                if (settings.appId != 0) File.WriteAllText(Path.Combine(output, "steam_appid.txt"), settings.appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                File.WriteAllText(Path.Combine(output, "BUILD_INFO.txt"),
                    "Astella / Part 1 / 4 players / normal and hard / stages 1-6\n" +
                    "Encounters: Guardian, Flora, Campanella, Marionette, Justitia, Janus.\n" +
                    "Unity: " + Application.unityVersion + "\nProtocol: " + RaidWire.Version + "\nContent: " + CampaignCatalog.ContentRevision +
                    "\nScene: " + MainScene + "\nBuilt UTC: " + System.DateTime.UtcNow.ToString("O") + "\nSteam configured: " + (settings.appId != 0) +
                    "\nThis build result does not certify gameplay or Steam connectivity.\n");
                Debug.Log("1부 Windows 테스트 빌드 완료: " + output);
            }
            finally { if (!Application.isBatchMode) EditorSceneManager.RestoreSceneManagerSetup(previous); }
        }

        public static void ValidatePartOneScene(Scene scene)
        {
            var stages = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<StageManager>(true)).ToArray();
            var players = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerController>(true)).ToArray();
            var bosses = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossController>(true)).ToArray();
            var bots = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PartyBotAI>(true)).ToArray();
            var cameras = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<QuarterViewCamera>(true)).ToArray();
            if (stages.Length != 1 || players.Length != 1 || bosses.Length != 1 || bots.Length != 3 || cameras.Length != 1)
                throw new BuildFailedException("진입 씬은 StageManager/플레이어/보스/카메라 각 1개와 AI 3개가 필요합니다.");
            var stage = stages[0];
            if (stage.player != players[0] || stage.boss != bosses[0] || stage.cardManager == null ||
                stage.cardManager.cardSelectionModalRoot == null || stage.victoryModalRoot == null || stage.restartButton == null ||
                stage.stageHeaderText == null || bosses[0].bossHpFillImage == null || cameras[0].target != players[0].transform)
                throw new BuildFailedException("자동 생성 씬의 진행·보상·결과·카메라 참조가 누락되었습니다.");
            if (CampaignCatalog.StageCount(CampaignCatalog.PartOne) != 6 || CampaignCatalog.PartySize(CampaignCatalog.PartOne) != 4 || CampaignCatalog.Available(CampaignCatalog.PartTwo))
                throw new BuildFailedException("1부 4인/6스테이지 전용 설정이 아닙니다.");
        }
        [MenuItem("Astella/Build LAN Test (Windows)")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("Play를 종료한 뒤 빌드하세요."); return; }
            var scene = SceneManager.GetActiveScene();
            if (scene.path != MainScene || scene.isDirty)
            { Debug.LogError("메인 씬 " + MainScene + "을 열고 저장한 뒤 빌드하세요."); return; }
            ValidatePartOneScene(scene);
            string folder = EditorUtility.SaveFolderPanel("LAN 테스트 빌드 폴더", "", "AstellaLAN");
            if (string.IsNullOrEmpty(folder)) return;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { MainScene },
                locationPathName = System.IO.Path.Combine(folder, "Astella.exe"), target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development });
            if (report.summary.result == BuildResult.Succeeded) Debug.Log("LAN 빌드 완료. 같은 실행 파일을 여러 번 실행해 호스트 1명과 클라이언트를 테스트하세요: " + folder);
            else Debug.LogError("LAN 테스트 빌드 실패: " + report.summary.result);
        }
    }
}
