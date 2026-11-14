using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RaidRogue;

namespace RaidRogue
{
    public static class CampaignIdentity
    {
        public static bool Valid(string value) => value == "offline-local" || value != null && value.StartsWith("steam-");
    }
}
public static class DevBugReportTests
{
    private static int _checks;
    private static void Check(bool condition, string label) { _checks++; if (!condition) { Console.Error.WriteLine("FAILED CHECK: " + label); throw new Exception(label); } }
    public static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "RaidBugFixture-" + Guid.NewGuid().ToString("N"));
        string profile = Path.Combine(root, "test-profiles", "isolated");
        string saves = Path.Combine(profile, "campaign", "steam-76561198000000001");
        string logs = Path.Combine(profile, "diagnostics");
        string output = Path.Combine(root, "reports");
        try
        {
            Directory.CreateDirectory(saves); Directory.CreateDirectory(logs);
            string current = Path.Combine(logs, "session-current.log"), previous = Path.Combine(logs, "session-previous.log");
            string save = Path.Combine(saves, "checkpoint.json"), backup = save + ".bak";
            string original = "{\"version\":2,\"owner\":\"steam-76561198000000001\",\"name\":\"Real Player\",\"userId\":\"steam-76561198000000001\"}";
            File.WriteAllText(save, original); File.WriteAllText(backup, original);
            File.WriteAllText(current, "at C:\\Users\\someone\\game\\secret.cs; owner steam-76561198000000001; token Bearer abc.def; name@example.com\n");
            File.WriteAllText(previous, "previous exception\nstack line\n");
            string normal = Path.Combine(root, "campaign", "offline-local", "checkpoint.json");
            Directory.CreateDirectory(Path.GetDirectoryName(normal)); File.WriteAllText(normal, "ordinary save must never appear");
            var request = new DevBugReportArchive.Request { profileRoot = profile, owner = "steam-76561198000000001", outputRoot = output,
                sessionId = "fixture", reportId = "0123456789", currentLog = current, previousLog = previous, summary = "current memory, not disk", utc = DateTime.UtcNow };
            var result = DevBugReportArchive.Export(request);
            Check(result.status == "부분 성공", "absent optional save files explicitly partial");
            Check(File.Exists(result.path), "final zip exists");
            Check(!File.Exists(result.path + ".partial"), "no partial after success");
            Check(File.ReadAllText(save) == original && File.ReadAllText(backup) == original, "source files unchanged");
            using (var zip = ZipFile.OpenRead(result.path))
            {
                string all = string.Join("\n", zip.Entries.Select(e => Read(e)));
                Check(zip.GetEntry("save/checkpoint.json") != null && zip.GetEntry("save/checkpoint.json.bak") != null, "save and backup");
                Check(zip.GetEntry("logs/current-session.log") != null && zip.GetEntry("logs/previous-session.log") != null, "current and prior log");
                Check(!all.Contains("Real Player") && !all.Contains("name@example.com") && !all.Contains("76561198000000001") && !all.Contains("C:\\Users\\someone") && !all.Contains("Bearer abc"), "copies redacted");
                Check(!all.Contains("ordinary save must never appear"), "normal save excluded");
                Check(Read(zip.GetEntry("manifest.tsv")).Contains("SHA256"), "hash manifest");
                foreach (string line in Read(zip.GetEntry("manifest.tsv")).Split('\n').Skip(1))
                {
                    string[] cols = line.TrimEnd('\r').Split('\t');
                    if (cols.Length < 5 || cols[1] != "included") continue;
                    byte[] bytes;
                    using (var stream = zip.GetEntry(cols[0]).Open())
                    using (var memory = new MemoryStream()) { stream.CopyTo(memory); bytes = memory.ToArray(); }
                    using (var sha = SHA256.Create())
                        Check(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "") == cols[3], "manifest hash " + cols[0]);
                    Check(bytes.Length.ToString() == cols[2], "manifest size " + cols[0]);
                }
                Check(Read(zip.GetEntry("collection-status.txt")).Contains("partial"), "partial status");
                Check(Read(zip.GetEntry("save/checkpoint.json")).Contains("\"version\":2"), "save structure retained");
            }
            request.currentLog = normal;
            request.reportId = Guid.NewGuid().ToString("N").Substring(0, 10);
            var escaped = DevBugReportArchive.Export(request);
            Check(escaped.missing.Any(x => x.Contains("허용 경로 밖")), "out-of-scope log rejected");
            request.currentLog = current;
            File.WriteAllText(save, "{\"access_token\":\"secret\"}");
            request.reportId = Guid.NewGuid().ToString("N").Substring(0, 10);
            var sensitive = DevBugReportArchive.Export(request);
            Check(sensitive.missing.Any(x => x.Contains("자격 증명")), "unsafe save excluded");
            Check(File.ReadAllText(save).Contains("secret"), "unsafe source preserved");
            File.WriteAllText(current, new string('x', 4 * 1024 * 1024 + 1000) + "\nlatest exception\n");
            request.reportId = Guid.NewGuid().ToString("N").Substring(0, 10);
            var large = DevBugReportArchive.Export(request);
            Check(large.warnings.Any(x => x.Contains("앞부분")), "large log truncation reported");
            using (var zip = ZipFile.OpenRead(large.path))
                Check(Read(zip.GetEntry("logs/current-session.log")).Contains("latest exception"), "recent log tail preserved");
            File.WriteAllText(save, original);
            request.reportId = Guid.NewGuid().ToString("N").Substring(0, 10);
            using (var locked = new FileStream(save, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var lockedResult = DevBugReportArchive.Export(request);
                Check(lockedResult.missing.Any(x => x.StartsWith("save/checkpoint.json (")), "locked save reports partial capture");
                Check(lockedResult.included.Contains("save/checkpoint.json.bak"), "other evidence survives save lock");
            }
            Check(File.ReadAllText(save) == original, "lock test source preserved");
            request.outputRoot = Path.Combine(root, "not-a-directory");
            File.WriteAllText(request.outputRoot, "do not overwrite");
            bool writeRejected = false;
            try { DevBugReportArchive.Export(request); } catch (IOException) { writeRejected = true; }
            Check(writeRejected && File.ReadAllText(request.outputRoot) == "do not overwrite", "write failure preserves existing file");
            bool rejected = false;
            try { request.outputRoot = Path.Combine(profile, "reports"); DevBugReportArchive.Export(request); }
            catch (IOException) { rejected = true; }
            Check(rejected, "output inside profile rejected");
            Console.WriteLine("PASS: " + _checks + " diagnostic ZIP checks, no campaign save writes.");
        }
        finally
        {
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(root);
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("RaidBugFixture-", StringComparison.Ordinal))
                throw new Exception("unsafe fixture cleanup");
            Directory.Delete(full, true);
        }
    }
    private static string Read(ZipArchiveEntry entry)
    { using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) return reader.ReadToEnd(); }
}
