using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidRogue.EditorTools
{
    public sealed class RaidSteamSetup : EditorWindow
    {
        private const string SettingsPath = "Assets/Resources/RaidSteamSettings.json";
        private string _appId, _build;
        [MenuItem("Astella/Steam/Configuration")]
        private static void Open() => GetWindow<RaidSteamSetup>("Steam Configuration");
        private void OnEnable()
        {
            var settings = File.Exists(SettingsPath) ? JsonUtility.FromJson<RaidSteamSettings>(File.ReadAllText(SettingsPath)) : new RaidSteamSettings();
            _appId = settings.appId.ToString(CultureInfo.InvariantCulture); _build = settings.build;
        }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("원본 Unity 프로젝트에서 설정하세요. App ID 0은 Steam을 비활성화합니다. 실제 App ID는 Steamworks 앱에서 확인하고 두 테스트 계정에 실행 권한을 부여하세요. 임의의 개발용 ID를 제품 설정으로 저장하지 마세요.", MessageType.Info);
            _appId = EditorGUILayout.TextField("Product App ID (0 = unset)", _appId);
            _build = EditorGUILayout.TextField("Compatible build", _build);
            if (GUILayout.Button("프로젝트 설정 저장"))
            {
                if (!uint.TryParse(_appId, out uint id) || id == 480 || string.IsNullOrWhiteSpace(_build) || _build.Length > 40)
                { EditorUtility.DisplayDialog("Steam 설정", "실제 App ID 또는 0, 1~40자 빌드 식별자를 입력하세요. 샘플 ID 480은 개발 환경 변수로만 지정할 수 있습니다.", "확인"); return; }
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(new RaidSteamSettings { appId = id, build = _build.Trim() }, true) + "\n"); AssetDatabase.Refresh();
            }
            EditorGUILayout.HelpBox("에디터/Development 빌드만 RAID_STEAM_APP_ID 환경 변수로 개발용 ID를 지정할 수 있습니다. 환경 변수를 바꿨다면 Unity/게임을 다시 실행하세요. SDK가 생성한 steam_appid.txt의 샘플 값은 제품 설정으로 사용하지 않습니다.", MessageType.None);
            if (GUILayout.Button("설정한 ID로 에디터용 steam_appid.txt 작성"))
            {
                uint id = RaidSteamSettings.Load().appId;
                if (id == 0) { EditorUtility.DisplayDialog("Steam 설정 필요", "먼저 실제 App ID를 저장하거나 개발용 환경 변수를 명시적으로 지정하세요.", "확인"); return; }
                File.WriteAllText("steam_appid.txt", id.ToString(CultureInfo.InvariantCulture));
                EditorUtility.DisplayDialog("작성 완료", "Steam에 로그인한 뒤 Play 하세요. 오버레이 및 실행 전 초대는 테스트 빌드로 확인하세요. steam_appid.txt는 배포용 Steam 빌드에 포함하지 않습니다.", "확인");
            }
        }
        [MenuItem("Astella/Steam/Build Steam Test (Windows)")]
        public static void BuildTest()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("Play를 종료한 뒤 빌드하세요."); return; }
            var settings = RaidSteamSettings.Load();
            if (settings.appId == 0) { Open(); Debug.LogError("Steam App ID를 먼저 설정하세요."); return; }
            var scene = SceneManager.GetActiveScene();
            if (scene.path != RaidMultiplayerBuild.MainScene || scene.isDirty)
            { Debug.LogError("메인 씬 " + RaidMultiplayerBuild.MainScene + "을 열고 저장하세요."); return; }
            RaidMultiplayerBuild.ValidatePartOneScene(scene);
            string folder = EditorUtility.SaveFolderPanel("Steam 테스트 빌드 폴더", "", "AstellaSteam");
            if (string.IsNullOrEmpty(folder)) return;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { RaidMultiplayerBuild.MainScene },
                locationPathName = Path.Combine(folder, "Astella.exe"), target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) { Debug.LogError("Steam 테스트 빌드 실패: " + report.summary.result); return; }
            File.WriteAllText(Path.Combine(folder, "steam_appid.txt"), settings.appId.ToString(CultureInfo.InvariantCulture));
            Debug.Log("Steam 테스트 빌드 완료: " + folder + "\n두 PC의 서로 다른 Steam 계정에서 같은 빌드를 실행하세요. 개발 환경 변수 사용 시 두 PC에도 같은 RAID_STEAM_APP_ID를 명시적으로 설정하세요. 게임이 꺼진 상태의 초대는 실제 App ID의 Steam 배포/로컬 설치 실행 항목이 이 빌드를 가리켜야 합니다.");
        }
    }
    public sealed class RaidSteamBuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            var asset = Resources.Load<TextAsset>("RaidSteamSettings");
            var settings = asset != null ? JsonUtility.FromJson<RaidSteamSettings>(asset.text) : new RaidSteamSettings();
            if (settings.appId == 480) throw new BuildFailedException("샘플 Steam App ID 480을 제품 설정에 저장하지 마세요. 개발 환경 변수로 분리하세요.");
            if (string.IsNullOrWhiteSpace(settings.build) || settings.build.Length > 40) throw new BuildFailedException("Steam 호환 빌드 식별자는 1~40자로 설정하세요.");
            if ((report.summary.options & BuildOptions.Development) == 0 && File.Exists(Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "steam_appid.txt")))
                throw new BuildFailedException("배포 출력 폴더에 개발용 steam_appid.txt가 남아 있습니다. 새 출력 폴더를 사용하세요.");
        }
    }
}
