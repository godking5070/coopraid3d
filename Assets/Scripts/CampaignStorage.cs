using System;
using System.IO;
using System.Text;

namespace RaidRogue
{
    public enum CampaignFileState { Missing, Valid, Backup, Corrupt, Incompatible, Unreadable }
    public sealed class CampaignFileRead
    {
        public CampaignFileState state;
        public string text, notice = "";
        public bool Writable => state == CampaignFileState.Missing || state == CampaignFileState.Valid || state == CampaignFileState.Backup;
    }
    // Same durable temp/replace implementation as settings; different account paths, schemas and
    // failure policy. Unsupported versions are never normalized or silently overwritten.
    public static class CampaignStorage
    {
        public const int MaximumBytes = 8 * 1024 * 1024;
        public static string DirectoryFor(string root, string owner)
        {
            if (!CampaignIdentity.Valid(owner)) throw new ArgumentException("Invalid account");
            return Path.Combine(root, "campaign", owner);
        }
        public static CampaignFileRead Read(string path, Func<string, CampaignFileState> inspect)
        {
            var primary = ReadOne(path, inspect);
            if (primary.state == CampaignFileState.Valid || primary.state == CampaignFileState.Incompatible || primary.state == CampaignFileState.Unreadable) return primary;
            var backup = ReadOne(path + ".bak", inspect);
            if (backup.state == CampaignFileState.Valid)
            { backup.state = CampaignFileState.Backup; backup.notice = "최근 정상 백업을 읽었습니다. 손상된 원본을 덮어쓰지 않고 이전 정상 백업을 보존하여 다음 경계에 저장합니다."; return backup; }
            if (primary.state == CampaignFileState.Missing && backup.state != CampaignFileState.Missing) return backup;
            return primary;
        }
        private static CampaignFileRead ReadOne(string path, Func<string, CampaignFileState> inspect)
        {
            try
            {
                if (!File.Exists(path)) return new CampaignFileRead { state = CampaignFileState.Missing };
                if (new FileInfo(path).Length > MaximumBytes) return new CampaignFileRead { state = CampaignFileState.Corrupt, notice = "저장 파일 크기가 제한을 초과했습니다" };
                string text = File.ReadAllText(path, Encoding.UTF8);
                var state = inspect(text);
                return new CampaignFileRead { state = state, text = state == CampaignFileState.Valid ? text : null, notice = state == CampaignFileState.Valid ? "" : state == CampaignFileState.Incompatible ? "호환되지 않는 저장입니다. 파일을 보존합니다." : "손상되거나 잘못된 저장입니다. 정상 저장으로 표시하지 않습니다." };
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            { return new CampaignFileRead { state = CampaignFileState.Unreadable, notice = "저장 읽기 실패: " + e.Message }; }
        }
        public static bool Save(string path, string text, Func<string, CampaignFileState> inspect, out string error)
        {
            var previous = Read(path, inspect);
            if (!previous.Writable) { error = previous.notice + " 해당 저장을 명시적으로 삭제하거나 호환 버전에서 열어주세요."; return false; }
            if (ReadOne(path + ".bak", inspect).state == CampaignFileState.Incompatible)
            { error = "호환되지 않는 백업이 있어 덮어쓰지 않았습니다. 저장 정보 확인 후 해당 저장을 명시적으로 삭제하거나 호환 버전에서 열어주세요."; return false; }
            bool ok = LocalSettingsFile.Save(path, text, s => inspect(s) == CampaignFileState.Valid, out error, MaximumBytes);
            if (!ok) error = "체크포인트/진행도 저장 실패 · 기존 정상 저장 유지. " + error;
            return ok;
        }
        // Called only by the in-game confirmation for one selected account/file scope.
        // Remove backup first so a partial failure cannot resurrect a deleted primary on next boot.
        public static bool Delete(string path, out string error)
        {
            try { if (File.Exists(path + ".bak")) File.Delete(path + ".bak"); if (File.Exists(path)) File.Delete(path); error = ""; return true; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            { error = "삭제하지 못했습니다: " + e.Message; return false; }
        }
    }
}
