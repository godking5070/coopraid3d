#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RaidRogue
{
    // File-only half of the developer exporter. It never invokes campaign save/repair code.
    public static class DevBugReportArchive
    {
        public sealed class Result { public string path, status; public string[] included, missing, warnings; }
        public sealed class Request
        {
            public string profileRoot, owner, outputRoot, sessionId, reportId, summary;
            public string currentLog, previousLog;
            public DateTime utc;
        }
        private const int MaxSave = 8 * 1024 * 1024;
        private const int MaxLog = 4 * 1024 * 1024;
        private static readonly string[] SaveNames = { "checkpoint.json", "checkpoint.json.bak", "settlement.json", "settlement.json.bak", "progress.json", "progress.json.bak" };
        private static readonly Regex Forbidden = new Regex("\"(?:access[_-]?token|refresh[_-]?token|password|secret|authorization|auth[_-]?key|private[_-]?key)\"\\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Identity = new Regex("steam-[0-9]{1,20}", RegexOptions.CultureInvariant);
        private static readonly Regex Email = new Regex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex WindowsPath = new Regex(@"[A-Za-z]:\\(?:[^\\\s""']+\\)*[^\\\s""']+", RegexOptions.CultureInvariant);
        private static readonly Regex UserField = new Regex("(\"(?:name|displayName|playerName)\"\\s*:\\s*\")(.*?)(\")", RegexOptions.CultureInvariant);
        private static readonly Regex Address = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.CultureInvariant);
        private static readonly Regex LongId = new Regex(@"(?<!steam-)\b\d{16,20}\b", RegexOptions.CultureInvariant);
        private static readonly Regex Bearer = new Regex(@"(?i)\bBearer\s+[A-Za-z0-9._~+/-]+", RegexOptions.CultureInvariant);
        private static readonly Regex CredentialValue = new Regex("(\"(?:access[_-]?token|refresh[_-]?token|password|secret|authorization|auth[_-]?key|private[_-]?key)\"\\s*:\\s*\")[^\"]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static Result Export(Request request)
        {
            if (request == null || string.IsNullOrEmpty(request.profileRoot) || string.IsNullOrEmpty(request.outputRoot) ||
                string.IsNullOrEmpty(request.sessionId) || !Regex.IsMatch(request.reportId ?? "", "^[a-f0-9]{10}$") ||
                !CampaignIdentity.Valid(request.owner)) throw new ArgumentException("Invalid test report scope");
            string profile = Path.GetFullPath(request.profileRoot);
            string output = Path.GetFullPath(request.outputRoot);
            if (Path.GetFileName(Path.GetDirectoryName(profile)) != "test-profiles" ||
                !Regex.IsMatch(Path.GetFileName(profile), @"^[A-Za-z0-9_-]{1,40}$"))
                throw new IOException("Only an explicit isolated test profile can be exported");
            if (ContainsReparse(profile) || ContainsReparse(output)) throw new IOException("Link/reparse point in report scope");
            if (Inside(output, profile) || Inside(profile, output)) throw new IOException("Report output must be separate from test saves");
            string saveDir = Path.GetFullPath(Path.Combine(profile, "campaign", request.owner));
            if (!Inside(saveDir, profile) || ContainsReparse(saveDir)) throw new IOException("Invalid save directory");
            Directory.CreateDirectory(output);
            string file = "BugReport_" + request.utc.ToUniversalTime().ToString("yyyyMMdd_HHmmss") + "_" + request.reportId + ".zip";
            string final = Path.Combine(output, file), partial = final + ".partial";
            var included = new List<string>(); var missing = new List<string>(); var warnings = new List<string>();
            var manifest = new StringBuilder("entry\tstatus\tbytes\tSHA256\tnote\n");
            var redactor = new Redactor();
            try
            {
                using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, false))
                {
                    Add(zip, "report.txt", Encoding.UTF8.GetBytes(redactor.Clean(request.summary ?? "확인 불가")), included, manifest, "current memory; never a checkpoint");
                    foreach (string name in SaveNames)
                        Collect(zip, Path.Combine(saveDir, name), saveDir, "save/" + name, MaxSave, false, redactor, included, missing, warnings, manifest);
                    // Incomplete atomic writes may explain a failed boundary. Enumerate only known basenames.
                    string[] incomplete = Array.Empty<string>();
                    try { if (Directory.Exists(saveDir)) incomplete = Directory.GetFiles(saveDir, "*.tmp-*", SearchOption.TopDirectoryOnly); }
                    catch (IOException) { warnings.Add("미완성 저장 파일 목록을 읽지 못했습니다"); }
                    catch (UnauthorizedAccessException) { warnings.Add("미완성 저장 파일 목록 권한이 없습니다"); }
                    foreach (string path in incomplete.OrderBy(x => x).Take(18))
                        {
                            string name = Path.GetFileName(path);
                            if (!SaveNames.Where(x => x.EndsWith(".json", StringComparison.Ordinal)).Any(x => name.StartsWith(x + ".tmp-", StringComparison.Ordinal)) ||
                                name.Length > 100 || !name.Substring(name.LastIndexOf(".tmp-", StringComparison.Ordinal) + 5).All(Uri.IsHexDigit)) continue;
                            Collect(zip, path, saveDir, "save/incomplete/" + name, MaxSave, false, redactor, included, missing, warnings, manifest);
                        }
                    string logDir = Path.Combine(profile, "diagnostics");
                    Collect(zip, request.currentLog, logDir, "logs/current-session.log", MaxLog, true, redactor, included, missing, warnings, manifest);
                    Collect(zip, request.previousLog, logDir, "logs/previous-session.log", MaxLog, true, redactor, included, missing, warnings, manifest);
                    Add(zip, "manifest.tsv", Encoding.UTF8.GetBytes(manifest.ToString()), included, null, "manifest");
                    Add(zip, "collection-status.txt", Encoding.UTF8.GetBytes("Status: " + (missing.Count == 0 && warnings.Count == 0 ? "complete" : "partial") + "\nMissing: " + string.Join("; ", missing) + "\nWarnings: " + string.Join("; ", warnings)), included, null, "status");
                }
                File.Move(partial, final);
                return new Result { path = final, status = missing.Count == 0 && warnings.Count == 0 ? "완료" : "부분 성공", included = included.ToArray(), missing = missing.ToArray(), warnings = warnings.ToArray() };
            }
            catch { try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { } throw; }
        }
        private static void Collect(ZipArchive zip, string source, string allowedDirectory, string entry, int limit, bool tail, Redactor redactor,
            List<string> included, List<string> missing, List<string> warnings, StringBuilder manifest)
        {
            if (string.IsNullOrEmpty(source)) { Missing(entry, "경로 확인 불가", missing, manifest); return; }
            try
            {
                string full = Path.GetFullPath(source);
                if (!Inside(full, Path.GetFullPath(allowedDirectory))) { Missing(entry, "허용 경로 밖", missing, manifest); return; }
                if (tail && !Regex.IsMatch(Path.GetFileName(full), @"^session-[A-Za-z0-9_-]{1,40}\.log$"))
                { Missing(entry, "테스트 실행 로그 이름이 아님", missing, manifest); return; }
                if (ContainsReparse(full)) { Missing(entry, "링크/재분석 지점 차단", missing, manifest); return; }
                if (!File.Exists(full)) { Missing(entry, "파일 없음", missing, manifest); return; }
                byte[] raw = null; long omitted = 0;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try { raw = ReadBounded(full, limit, tail, out omitted); break; }
                    catch (IOException) when (attempt < 2) { System.Threading.Thread.Sleep(100); }
                }
                string value = new UTF8Encoding(false, !tail).GetString(raw);
                if (!tail && Forbidden.IsMatch(value)) { Missing(entry, "자격 증명 의심 필드 포함; 원본 제외", missing, manifest); return; }
                string cleaned = redactor.Clean(value);
                byte[] bytes = Encoding.UTF8.GetBytes((tail && omitted > 0 ? "[앞부분 " + omitted + " bytes 생략; 최근 로그 우선]\n" : "") + cleaned);
                Add(zip, entry, bytes, included, manifest, omitted > 0 ? "truncated first " + omitted + " bytes" : "redacted copy");
                if (omitted > 0) warnings.Add(entry + ": 앞부분 " + omitted + " bytes 생략");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is DecoderFallbackException || e is System.Security.SecurityException || e is ArgumentException)
            { Missing(entry, "안전한 읽기 실패: " + e.GetType().Name, missing, manifest); }
        }
        private static byte[] ReadBounded(string path, int limit, bool tail, out long omitted)
        {
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long length = f.Length;
                if (!tail && length > limit) throw new IOException("Save exceeds report limit");
                omitted = tail ? Math.Max(0, length - limit) : 0;
                f.Position = omitted;
                int count = checked((int)(length - omitted)); byte[] data = new byte[count]; int read = 0;
                while (read < count) { int n = f.Read(data, read, count - read); if (n == 0) throw new IOException("File changed during capture"); read += n; }
                if (f.Length != length) throw new IOException("File changed during capture");
                if (omitted > 0)
                {
                    int firstNewline = Array.IndexOf(data, (byte)'\n');
                    if (firstNewline >= 0) { omitted += firstNewline + 1; data = data.Skip(firstNewline + 1).ToArray(); }
                }
                return data;
            }
        }
        private static void Add(ZipArchive zip, string name, byte[] data, List<string> included, StringBuilder manifest, string note)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var e = entry.Open()) e.Write(data, 0, data.Length);
            included.Add(name);
            if (manifest != null) using (var sha = SHA256.Create())
                manifest.Append(name).Append("\tincluded\t").Append(data.Length).Append("\t").Append(BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "")).Append("\t").Append(note).Append('\n');
        }
        private static void Missing(string name, string why, List<string> missing, StringBuilder manifest)
        { missing.Add(name + " (" + why + ")"); manifest.Append(name).Append("\tmissing\t0\t-\t").Append(why).Append('\n'); }
        private static bool Inside(string child, string parent) => child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        private static bool ContainsReparse(string path)
        {
            string at = Path.GetPathRoot(path);
            foreach (string part in path.Substring(at.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                at = Path.Combine(at, part);
                if ((File.Exists(at) || Directory.Exists(at)) && (File.GetAttributes(at) & FileAttributes.ReparsePoint) != 0) return true;
            }
            return false;
        }
        private sealed class Redactor
        {
            private readonly Dictionary<string, string> _ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private string Alias(string value, string kind)
            {
                string key = kind == "steam" && value.StartsWith("steam-", StringComparison.OrdinalIgnoreCase) ? value.Substring(6) : value;
                if (!_ids.TryGetValue(key, out string alias))
                    _ids[key] = alias = kind == "steam" ? "steam-" + (76561190000000000UL + (ulong)_ids.Count + 1UL) : kind + "_" + (_ids.Count + 1);
                return alias;
            }
            public string Clean(string value)
            {
                value = Identity.Replace(value, m => Alias(m.Value, "steam"));
                value = LongId.Replace(value, m => Alias(m.Value, "steam").Substring(6));
                value = Email.Replace(value, m => Alias(m.Value, "email"));
                value = WindowsPath.Replace(value, m => Alias(m.Value, "path"));
                value = Address.Replace(value, m => Alias(m.Value, "ip"));
                value = Bearer.Replace(value, "Bearer [REDACTED]");
                value = CredentialValue.Replace(value, m => m.Groups[1].Value + "[REDACTED]");
                value = UserField.Replace(value, m => m.Groups[1].Value + Alias(m.Groups[2].Value, "player") + m.Groups[3].Value);
                return value;
            }
        }
    }
}
#endif
