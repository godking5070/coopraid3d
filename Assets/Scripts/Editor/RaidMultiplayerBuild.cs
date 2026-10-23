using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidRogue.EditorTools
{
    public static class RaidMultiplayerBuild
    {
        [MenuItem("Raid Rogue/Build LAN Test (Windows)")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("Play를 종료한 뒤 빌드하세요."); return; }
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path) || scene.isDirty)
            { Debug.LogError("원본 프로젝트의 테스트 씬을 저장한 뒤 다시 빌드하세요."); return; }
            string folder = EditorUtility.SaveFolderPanel("LAN 테스트 빌드 폴더", "", "RaidLAN");
            if (string.IsNullOrEmpty(folder)) return;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scene.path },
                locationPathName = System.IO.Path.Combine(folder, "RaidLAN.exe"), target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development });
            if (report.summary.result == BuildResult.Succeeded) Debug.Log("LAN 빌드 완료. 같은 실행 파일을 여러 번 실행해 호스트 1명과 클라이언트를 테스트하세요: " + folder);
            else Debug.LogError("LAN 테스트 빌드 실패: " + report.summary.result);
        }
    }
}
