using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RaidRogue
{
    [DefaultExecutionOrder(-1500)]
    public sealed class LocalSettingsRuntime : MonoBehaviour
    {
        public static LocalSettingsRuntime Instance { get; private set; }
        public LocalSettingsData Applied { get; private set; }
        public string Notice { get; private set; } = "";
        public string FilePath => Path.Combine(Application.persistentDataPath, "local-settings.json");
        public readonly DisplayConfirmation Confirmation = new DisplayConfirmation();
        public LocalSettingsData Previewed { get; private set; }
        private LocalSettingsData _candidate, _beforeDisplay;
        public LocalSettingsData LastRevertedDisplay { get; private set; }
        private float _nextAudioScan;
        public bool CanChangeDisplay => !Application.isEditor && (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.OSXPlayer);
        public bool CanExclusive => Application.platform == RuntimePlatform.WindowsPlayer;
        public bool CanVSync => Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.IPhonePlayer;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() { if (Instance == null) new GameObject("LocalSettings").AddComponent<LocalSettingsRuntime>(); }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; DontDestroyOnLoad(gameObject);
            string json = LocalSettingsFile.Read(FilePath, ValidJson, out string notice); Notice = notice;
            Applied = Decode(json);
            if (Applied.bindingNotice.Length > 0) Notice = (Notice + " " + Applied.bindingNotice).Trim();
            if (json != null)
            {
                var old = new LocalSettingsData { version = 0 }; JsonUtility.FromJsonOverwrite(json, old);
                if (old.version < LocalSettingsData.CurrentVersion && !LocalSettingsFile.Save(FilePath, JsonUtility.ToJson(Applied, true), ValidJson, out string migrationError)) Notice += " " + migrationError;
            }
            if (json == null && PlayerPrefs.HasKey("RaidRogue.Camera.v1.local-player"))
            {
                try
                {
                    var old = JsonUtility.FromJson<LocalCameraPreferences>(PlayerPrefs.GetString("RaidRogue.Camera.v1.local-player"));
                    if (old != null) { Applied.rotationSensitivity = old.rotationSensitivity; Applied.invertVertical = old.invertVertical; Applied.zoom3D = old.zoomSensitivity3D; Applied.zoom2D = old.zoomSensitivity2D; }
                }
                catch (ArgumentException) { }
            }
            Applied.Normalize(); ResolveDisplay(Applied); ApplyDisplay(Applied); Preview(Applied); LocalInputBindings.Configure(Applied.bindings);
        }
        private LocalSettingsData Decode(string text)
        {
            var data = Defaults();
            if (text != null) { data.version = 0; JsonUtility.FromJsonOverwrite(text, data); }
            data.Normalize(); return data;
        }
        private static bool ValidJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{") || !json.TrimEnd().EndsWith("}")) return false;
            try { var data = new LocalSettingsData { version = 0 }; JsonUtility.FromJsonOverwrite(json, data); return data.version >= 0 && data.version <= LocalSettingsData.CurrentVersion; }
            catch (ArgumentException) { return false; }
        }
        public LocalSettingsData Defaults()
        {
            var data = new LocalSettingsData { width = Math.Max(640, Screen.currentResolution.width), height = Math.Max(480, Screen.currentResolution.height) };
            if (Application.isEditor) { data.width = Math.Max(640, Screen.width); data.height = Math.Max(480, Screen.height); data.windowMode = 2; }
            return data;
        }
        public List<Vector2Int> Resolutions()
        {
            var result = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            { var value = new Vector2Int(r.width, r.height); if (r.width >= 640 && r.height >= 480 && !result.Contains(value)) result.Add(value); }
            var current = new Vector2Int(Math.Max(640, Screen.width), Math.Max(480, Screen.height));
            if (!result.Contains(current)) result.Add(current);
            result.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x)); return result;
        }
        public void ResolveDisplay(LocalSettingsData data)
        {
            if (data.windowMode == 0 && !CanExclusive) data.windowMode = 1;
            var sizes = Resolutions();
            if (!sizes.Contains(new Vector2Int(data.width, data.height)))
            {
                var best = sizes[0]; long score = long.MaxValue;
                foreach (var r in sizes) { long d = Math.Abs((long)r.x - data.width) + Math.Abs((long)r.y - data.height); if (d < score) { score = d; best = r; } }
                data.width = best.x; data.height = best.y;
            }
            if (!CanVSync) data.vSync = false;
        }
        private void ApplyDisplay(LocalSettingsData data)
        {
            QualitySettings.vSyncCount = CanVSync && data.vSync ? 1 : 0;
            Application.targetFrameRate = data.frameLimit == 0 ? -1 : data.frameLimit;
            if (CanChangeDisplay) Screen.SetResolution(data.width, data.height, data.windowMode == 0 ? FullScreenMode.ExclusiveFullScreen : data.windowMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }
        public void Preview(LocalSettingsData data)
        {
            Previewed = data.Copy(); Previewed.Normalize(); AudioListener.volume = Previewed.master;
            QuarterViewCamera.LocalCamera?.ApplyLocalSettings(Previewed);
        }
        public void CaptureCurrentDisplay(LocalSettingsData data)
        {
            if (!CanChangeDisplay) return;
            data.width = Screen.width; data.height = Screen.height;
            data.windowMode = Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? 0 : Screen.fullScreenMode == FullScreenMode.Windowed ? 2 : 1;
        }
        public bool Apply(LocalSettingsData draft)
        {
            if (Confirmation.Pending) return false;
            if (LocalBindingRules.HasConflicts(draft.bindings)) { Notice = "조작 탭에서 충돌한 키를 교체하거나 다시 지정하세요. 기존 키 전체를 초기화하지 않습니다."; return false; }
            draft.bindingsNeedReview = false; draft.bindingNotice = "";
            _candidate = draft.Copy(); _candidate.Normalize(); ResolveDisplay(_candidate);
            var actual = Applied.Copy(); CaptureCurrentDisplay(actual);
            if (CanChangeDisplay && !_candidate.SameDisplay(actual))
            {
                _beforeDisplay = actual;
                Confirmation.Begin(Time.realtimeSinceStartupAsDouble); ApplyDisplay(_candidate);
                Notice = "화면 설정을 미리 적용했습니다. 15초 안에 유지하세요."; return false;
            }
            return Commit();
        }
        public bool KeepDisplay()
        {
            if (!Confirmation.Keep(Time.realtimeSinceStartupAsDouble)) { RevertDisplay(); return false; }
            return Commit();
        }
        private bool Commit()
        {
            if (!LocalSettingsFile.Save(FilePath, JsonUtility.ToJson(_candidate, true), ValidJson, out string error))
            { Notice = error; if (_beforeDisplay != null) { LastRevertedDisplay = _beforeDisplay.Copy(); ApplyDisplay(_beforeDisplay); } Preview(Applied); _candidate = _beforeDisplay = null; return false; }
            Applied = _candidate.Copy(); _candidate = _beforeDisplay = null;
            ApplyDisplay(Applied); Preview(Applied); LocalInputBindings.Configure(Applied.bindings);
            Notice = "설정을 적용하고 저장했습니다."; return true;
        }
        public void RevertDisplay()
        {
            if (!Confirmation.Revert()) return;
            if (_beforeDisplay != null) { LastRevertedDisplay = _beforeDisplay.Copy(); ApplyDisplay(_beforeDisplay); }
            _beforeDisplay = _candidate = null; Notice = "이전 화면 설정으로 되돌렸습니다.";
        }
        public void CancelPreview() { RevertDisplay(); Preview(Applied); LocalInputBindings.QuarantineHeld(); }
        private void Update()
        {
            LocalInputBindings.Tick();
            // Gameplay shortcuts must not also submit the last clicked HUD button.
            if (!LocalGameplayInput.Blocked && UnityEngine.EventSystems.EventSystem.current != null && !LocalGameplayInput.IsTyping)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            if (Confirmation.Expired(Time.realtimeSinceStartupAsDouble)) RevertDisplay();
            if (Time.unscaledTime < _nextAudioScan) return;
            _nextAudioScan = Time.unscaledTime + 1;
            foreach (var source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                if (source.GetComponent<LocalAudioChannel>() == null) source.gameObject.AddComponent<LocalAudioChannel>();
        }
        private void OnApplicationFocus(bool focused) { LocalInputBindings.QuarantineHeld(); if (!focused) RevertDisplay(); }
        private void OnApplicationQuit() { RevertDisplay(); }
        private void OnDestroy() { if (Instance == this) { RevertDisplay(); Instance = null; } }
    }
}
