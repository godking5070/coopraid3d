#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace RaidRogue
{
    // This entire command is absent from non-development player builds.
    [DefaultExecutionOrder(-1000)]
    public sealed class DevBugReportUI : MonoBehaviour
    {
        private static DevBugReportUI _instance;
        private static readonly List<string> Commands = new List<string>();
        private static readonly object LogLock = new object();
        private static StreamWriter _log;
        private static string _profileRoot, _currentLog, _previousLog, _sessionId;
        private static int _logPart;
        private string _check = "", _action = "", _expected = "", _actual = "", _result = "";
        private string _folder;
        private bool _open, _busy;
        private Vector2 _scroll;
        private DevBugReportArchive.Result _last;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            // A missing/invalid explicit profile never falls back to the normal player's data.
            try
            {
                string persistent = Application.persistentDataPath;
                string root = CampaignStorage.RootFor(persistent, Environment.GetCommandLineArgs(), Debug.isDebugBuild);
                if (root == persistent || !Debug.isDebugBuild) return;
                _profileRoot = root;
                _sessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
                string logs = Path.Combine(root, "diagnostics");
                Directory.CreateDirectory(logs);
                _previousLog = Directory.GetFiles(logs, "session-*.log", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                OpenSessionLog(logs);
                Application.logMessageReceivedThreaded += OnLog;
                var go = new GameObject("Development Bug Report");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<DevBugReportUI>();
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("테스트 프로필 오류 보고 초기화 실패: " + e.GetType().Name); }
        }
        private static void OnLog(string condition, string trace, LogType type)
        {
            lock (LogLock)
            {
                try
                {
                    if (_log == null) return;
                    if (_log.BaseStream.Length >= 8 * 1024 * 1024)
                    {
                        _log.Dispose(); _log = null;
                        _logPart++;
                        OpenSessionLog(Path.Combine(_profileRoot, "diagnostics"));
                    }
                    _log.WriteLine(DateTime.UtcNow.ToString("O") + " [" + type + "] " + condition);
                    if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                        _log.WriteLine(trace);
                }
                catch (IOException) { } catch (ObjectDisposedException) { }
            }
        }
        private static void OpenSessionLog(string directory)
        {
            _currentLog = Path.Combine(directory, "session-" + _sessionId + (_logPart == 0 ? "" : "-" + _logPart) + ".log");
            _log = new StreamWriter(new FileStream(_currentLog, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
            _log.WriteLine(DateTime.UtcNow.ToString("O") + " [Development test log] session=" + _sessionId + " part=" + _logPart);
        }
        public static void NoteCommand(string name)
        {
            if (_instance == null) return;
            string line = DateTime.UtcNow.ToString("O") + " " + name;
            Commands.Add(line);
            if (Commands.Count > 20) Commands.RemoveAt(0);
            UnityEngine.Debug.Log("[Dev command] " + name);
        }
        public static void Open()
        {
            if (_instance == null) { UnityEngine.Debug.LogWarning("오류 보고 묶음은 개발 빌드의 --raid-test-profile=<이름> 실행에서만 사용할 수 있습니다."); return; }
            _instance.OpenPanel();
        }
        private void OpenPanel()
        {
            _open = true;
            QuarterViewCamera.LocalCamera?.ReleaseOrbit();
            LocalInputBindings.QuarantineHeld();
            LocalGameplayInput.SuppressThisFrame();
            LocalGameplayInput.SuppressPointerThisFrame();
        }
        private void Update()
        {
            if (!_open) return;
            LocalGameplayInput.SuppressThisFrame();
            LocalGameplayInput.SuppressPointerThisFrame();
        }
        private void OnGUI()
        {
            if (_profileRoot == null) return;
            if (!_open)
            {
                var button = new Rect(12, 12, 178, 32);
                if (button.Contains(Event.current.mousePosition)) LocalGameplayInput.SuppressPointerThisFrame();
                if (GUI.Button(button, "개발 테스트 · 오류 보고")) OpenPanel();
                return;
            }
            GUILayout.BeginArea(new Rect(12, 50, Mathf.Min(620, Screen.width - 24), Mathf.Min(620, Screen.height - 62)), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("오류 보고 묶음 내보내기 · 테스트 프로필 전용");
            GUILayout.Label("오류 직후 추가 조작을 멈추고 수집하세요. 진행 저장은 호출하지 않습니다.");
            GUILayout.Label("체크리스트 번호 (선택)"); _check = GUILayout.TextField(_check, 100);
            GUILayout.Label("직전에 한 행동 (선택)"); _action = GUILayout.TextField(_action, 500);
            GUILayout.Label("기대한 결과 (선택)"); _expected = GUILayout.TextField(_expected, 500);
            GUILayout.Label("실제 발생한 결과 (선택)"); _actual = GUILayout.TextField(_actual, 500);
            GUI.enabled = !_busy;
            if (GUILayout.Button("오류 보고 묶음 내보내기", GUILayout.Height(36))) BeginExport();
            GUI.enabled = true;
            if (GUILayout.Button("닫기")) { _open = false; LocalInputBindings.QuarantineHeld(); LocalGameplayInput.SuppressPointerThisFrame(); }
            if (_busy) GUILayout.Label("수집 중…");
            if (!string.IsNullOrEmpty(_result)) GUILayout.Label(_result);
            if (_last != null)
            {
                GUILayout.Label("ZIP: " + _last.path);
                GUILayout.Label("포함: " + string.Join(", ", _last.included));
                GUILayout.Label("누락: " + string.Join("; ", _last.missing));
                GUILayout.Label("경고: " + string.Join("; ", _last.warnings));
                if (GUILayout.Button("파일 위치 열기")) OpenFolder();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        private async void BeginExport()
        {
            if (_busy || _profileRoot == null) return;
            _busy = true; _last = null; _result = "수집 중…";
            string reportId = Guid.NewGuid().ToString("N").Substring(0, 10);
            // Capture Unity state on the main thread, then only immutable paths/strings cross to the worker.
            var request = new DevBugReportArchive.Request {
                profileRoot = _profileRoot, owner = CampaignRuntime.CurrentOwner,
                outputRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RaidRogue", "BugReports"),
                sessionId = _sessionId, reportId = reportId, currentLog = _currentLog, previousLog = _previousLog,
                utc = DateTime.UtcNow, summary = BuildSummary(reportId)
            };
            _folder = request.outputRoot;
            lock (LogLock) { try { _log?.Flush(); } catch (IOException) { } }
            try
            {
                _last = await Task.Run(() => DevBugReportArchive.Export(request));
                _result = _last.status + " · " + Path.GetFileName(_last.path);
            }
            catch (Exception e) { _result = "ZIP 생성 실패 · " + e.GetType().Name + ": " + e.Message; }
            finally { _busy = false; }
        }
        private string BuildSummary(string reportId)
        {
            var b = new StringBuilder();
            var stage = StageManager.Instance;
            var network = RaidNetworkSession.Instance;
            var campaign = CampaignRuntime.Instance;
            b.AppendLine("Report ID: " + reportId);
            b.AppendLine("Created: " + DateTime.Now.ToString("O") + " (" + TimeZoneInfo.Local.Id + ")");
            b.AppendLine("Build: " + Application.version + " / Unity " + Application.unityVersion + " / " + Application.platform);
            try
            {
                string info = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "BUILD_INFO.txt");
                if (File.Exists(info) && new FileInfo(info).Length < 4096)
                    b.AppendLine("Build metadata: " + File.ReadAllText(info).Replace("\r", "").Replace("\n", " | "));
            }
            catch (IOException) { b.AppendLine("Build metadata: 확인 불가"); }
            catch (UnauthorizedAccessException) { b.AppendLine("Build metadata: 확인 불가"); }
            b.AppendLine("Source revision: 확인 불가 (BUILD_INFO.txt에 commit 정보 없음)");
            b.AppendLine("Content: " + CampaignCatalog.ContentRevision);
            b.AppendLine("Save schema: " + CampaignCatalog.SaveVersion);
            b.AppendLine("Test session: " + _sessionId + " / profile name omitted");
            b.AppendLine("Current memory (not disk checkpoint):");
            b.AppendLine("  Stage/phase: " + (stage == null ? "확인 불가" : stage.ChapterId + " Stage " + stage.currentStage + "/" + stage.maxStage + " " + (network != null && RaidNetworkSession.Multiplayer ? network.BattlePhase.ToString() : stage.Progress.Phase.ToString())));
            b.AppendLine("  Run/attempt: " + (stage?.RunId ?? "확인 불가") + " / " + (stage?.AttemptId ?? "확인 불가"));
            b.AppendLine("  Shared Iris remaining: " + (stage == null ? "확인 불가" : stage.IrisCoins + " / 4"));
            b.AppendLine("  Session: " + (network == null ? "확인 불가" : network.Mode + " " + network.SessionTag));
            if (stage != null)
                foreach (var member in PartyLife.OrderedMembers())
                {
                    var actor = member.GetComponent<CharacterActor>();
                    var pc = member.GetComponent<PlayerController>();
                    var bot = member.GetComponent<PartyBotAI>();
                    var snap = actor?.Snapshot;
                    var slot = campaign?.ReportSlot(member);
                    var cards = pc?.cardManager?.CaptureCampaignCards() ?? slot?.cards;
                    b.AppendLine("  Party: " + (bot != null ? "AI" : "human") + " role=" + (pc != null ? pc.currentRole.ToString() : bot != null ? bot.role.ToString() : "unknown") +
                        " alive=" + (member.GetComponent<PartyLife>()?.IsDead == true ? "no" : "yes") +
                        " HP=" + (pc != null ? pc.currentHp.ToString("0") : bot != null ? bot.currentHp.ToString("0") : "?") +
                        " job=" + (snap?.jobId ?? "?") + " route=" + (snap?.routeId ?? "?") +
                        " R/F/V=" + (snap == null ? "?" : string.Join(",", snap.selections.Where(s => s.slot == CharacterSlot.Active || s.slot == CharacterSlot.OffGcd2 || s.slot == CharacterSlot.Ultimate).Select(s => s.skillId))) +
                        " cards=" + (cards == null ? "확인 불가" : string.Join(",", cards.Select(c => c.id + "x" + c.stacks))));
                }
            b.AppendLine("  Next departure: " + (campaign?.Settlement != null ? "정산 대기 Stage " + campaign.Settlement.stage : campaign?.Saved == null ? "확인 불가" : campaign.Saved.nextStage.ToString()));
            b.AppendLine("  Last save: " + (campaign == null ? "확인 불가" : campaign.HasSaveFailure ? "실패/재시도 대기 " + campaign.SaveFailureDetail : campaign.LastSaveLabel));
            b.AppendLine("Disk checkpoint: ZIP save/ 파일과 manifest.tsv 참조. 메모리 상태와 별개이며 수집 중 파일 없음/읽기 실패 가능.");
            b.AppendLine("Previous run memory: 확인 불가 (재실행 후에는 직전 로그와 테스트 저장만 수집)");
            b.AppendLine("Recent developer commands: " + (Commands.Count == 0 ? "기록 없음 또는 기록 시작 전" : string.Join("; ", Commands)));
            b.AppendLine("Checklist: " + _check); b.AppendLine("Last action: " + _action);
            b.AppendLine("Expected: " + _expected); b.AppendLine("Actual: " + _actual);
            b.AppendLine("Excluded: ordinary saves, settings, credentials, cloud, screenshots, Assets, Library");
            return b.ToString();
        }
        private void OpenFolder()
        {
            if (_last == null || !File.Exists(_last.path)) return;
            try { Process.Start(new ProcessStartInfo { FileName = _folder, UseShellExecute = true }); }
            catch (Exception e) { _result = "폴더 열기 실패: " + e.GetType().Name; }
        }
        private void OnDestroy()
        {
            if (_instance != this) return;
            Application.logMessageReceivedThreaded -= OnLog;
            lock (LogLock) { _log?.Dispose(); _log = null; }
            _instance = null;
        }
    }
}
#endif
