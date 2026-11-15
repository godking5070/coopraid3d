#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    public static class RaidHudPreview
    {
        // Batch entry point; renders an unsaved scene so previews never overwrite game content.
        [MenuItem("Tools/RaidRogue/Export UI Previews", false, 30)]
        public static void Export()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            Canvas canvas = GameObject.Find("RaidRogue_HUD_Canvas").GetComponent<Canvas>();
            var theme = canvas.GetComponent<RaidHudTheme>() ?? canvas.gameObject.AddComponent<RaidHudTheme>();
            theme.Apply();
            theme.SendMessage("LateUpdate");
            Directory.CreateDirectory("UI-Preview");
            Camera camera = Camera.main;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            var target = new RenderTexture(1920, 1080, 24);
            camera.targetTexture = target;
            Capture(camera, target, "UI-Preview/01-combat.png");
            var cards = Object.FindFirstObjectByType<RoguelikeCardManager>();
            cards.cardSelectionModalRoot.SetActive(true);
            cards.modalHeaderText.text = "전투의 흔적을 힘으로\n<size=20>이번 원정에 적용할 특성을 선택하세요</size>";
            string[] titles = { "후면의 지배자", "유령 걸음", "신속의 각성" };
            string[] descriptions = { "후면 기습 성공 시 피해량 50% 증가\n\n후면 콤보의 재사용 대기시간 초기화", "이동속도 30% 증가\n\n측면 · 후면 판정 각도 15° 확장", "글로벌 쿨타임 0.4초 감소\n\n더 빠르게 이어지는 공격" };
            for (int i = 0; i < 3; i++)
            {
                cards.cardTitleTexts[i].text = titles[i];
                cards.cardDescTexts[i].text = descriptions[i];
            }
            Capture(camera, target, "UI-Preview/02-augments.png");
            cards.cardSelectionModalRoot.SetActive(false);
            var stage = Object.FindFirstObjectByType<StageManager>();
            stage.victoryModalRoot.SetActive(true);
            stage.victorySummaryText.text = "<size=42><color=#F6C96F>RAID COMPLETE</color></size>\n\n세 번의 전투, 하나의 파티\n\n<size=24>원정 완료  /  03 : 42</size>\n\n후면의 지배자  ×1\n신속의 각성  ×1";
            Capture(camera, target, "UI-Preview/03-victory.png");
            camera.targetTexture = null;
            target.Release();
            File.WriteAllText("UI-Preview/validation.txt", "Unity compilation succeeded.\nCombat, augment, victory canvases rendered at 1920x1080.\nPreview labels are sample data; gameplay uses existing live bindings.\n");
            Debug.Log("RAID_HUD_PREVIEW_OK");
            if (!Application.isBatchMode && !string.IsNullOrEmpty(previousScene))
                EditorSceneManager.OpenScene(previousScene);
        }
        private static void Capture(Camera camera, RenderTexture target, string path)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            RenderTexture.active = previous;
        }
    }
}
#endif
