using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RaidRogue
{
    [Serializable] public sealed class LocalKeyBinding
    {
        public string action, primary, secondary;
        public LocalKeyBinding Copy() => (LocalKeyBinding)MemberwiseClone();
    }
    [Serializable] public sealed class LocalSettingsData
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public int width = 1280, height = 720, windowMode = 1; // 0 exclusive, 1 borderless, 2 windowed
        public bool vSync = true;
        public int frameLimit = 120;
        public float master = 1, music = .8f, effects = 1, ui = .8f;
        public float rotationSensitivity = .16f, zoom3D = 2, zoom2D = 2;
        public bool invertVertical;
        public bool showAttackRange = true;
        public LocalKeyBinding[] bindings = LocalBindingRules.Defaults();
        public LocalSettingsData Copy()
        {
            var copy = (LocalSettingsData)MemberwiseClone();
            copy.bindings = Array.ConvertAll(bindings ?? Array.Empty<LocalKeyBinding>(), b => b?.Copy()); return copy;
        }
        public void Normalize()
        {
            version = CurrentVersion;
            width = Math.Max(640, Math.Min(16384, width)); height = Math.Max(480, Math.Min(16384, height));
            if (windowMode < 0 || windowMode > 2) windowMode = 1;
            if (Array.IndexOf(new[] { 0, 30, 60, 90, 120, 144, 165, 240, 360 }, frameLimit) < 0) frameLimit = 120;
            master = Clamp(master, 0, 1, 1); music = Clamp(music, 0, 1, .8f);
            effects = Clamp(effects, 0, 1, 1); ui = Clamp(ui, 0, 1, .8f);
            rotationSensitivity = Clamp(rotationSensitivity, .03f, .6f, .16f);
            zoom3D = Clamp(zoom3D, .25f, 5, 2); zoom2D = Clamp(zoom2D, .25f, 5, 2);
            bindings = LocalBindingRules.Repair(bindings);
        }
        private static float Clamp(float v, float min, float max, float fallback) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : Math.Max(min, Math.Min(max, v));
        public bool SameDisplay(LocalSettingsData b) => width == b.width && height == b.height && windowMode == b.windowMode;
    }

    public static class LocalBindingRules
    {
        public sealed class Definition
        {
            public readonly string Id, Name, Key, Alternate;
            public readonly int Context; // Combat and exclusive choice modals may share keys; global windows may not.
            public Definition(string id, string name, string key, string alternate = "", int context = 1)
            { Id = id; Name = name; Key = key; Alternate = alternate; Context = context; }
        }
        public static readonly Definition[] Actions = Build();
        public static readonly string[] Keys = BuildKeys();
        private static Definition[] Build()
        {
            var a = new List<Definition> {
                new Definition("Forward", "앞으로 이동", "w", "upArrow"), new Definition("Back", "뒤로 이동", "s", "downArrow"),
                new Definition("Left", "왼쪽 이동", "a", "leftArrow"), new Definition("Right", "오른쪽 이동", "d", "rightArrow"),
                new Definition("Dash", "대쉬", "space"), new Definition("Potion", "포션", "q") };
            for (int i = 1; i <= 5; i++) a.Add(new Definition("Skill" + i, i == 5 ? "힐러 부활" : "스킬 " + i, "digit" + i, "numpad" + i));
            a.Add(new Definition("ClearTarget", "타겟 해제", "backspace"));
            a.Add(new Definition("CycleEnemy", "적 타겟 순환", "tab")); a.Add(new Definition("TargetOfTarget", "대상의 대상", "t"));
            for (int i = 1; i <= 8; i++) a.Add(new Definition("Party" + i, "파티원 " + i, "f" + i));
            a.Add(new Definition("Roles", "역할 창", "p", "", 3)); a.Add(new Definition("View", "2D / 3D 전환", "v", "", 3));
            a.Add(new Definition("Settings", "설정 창", "f10", "", 3)); a.Add(new Definition("Menu", "게임 메뉴", "f9", "", 3));
            a.Add(new Definition("Results", "전투 기록", "f11", "", 3));
            for (int i = 1; i <= 4; i++) a.Add(new Definition("Choice" + i, "카드 / 직업 선택 " + i, "digit" + i, "numpad" + i, 2));
            return a.ToArray();
        }
        private static string[] BuildKeys()
        {
            var keys = new List<string>(); for (char c = 'a'; c <= 'z'; c++) keys.Add(c.ToString());
            for (int i = 0; i <= 9; i++) { keys.Add("digit" + i); keys.Add("numpad" + i); }
            for (int i = 1; i <= 12; i++) keys.Add("f" + i);
            keys.AddRange(new[] { "space", "tab", "backspace", "upArrow", "downArrow", "leftArrow", "rightArrow", "backquote", "minus", "equals", "leftBracket", "rightBracket", "semicolon", "quote", "backslash", "comma", "period", "slash", "home", "end", "pageUp", "pageDown", "insert", "delete", "numpadPlus", "numpadMinus", "numpadMultiply", "numpadDivide", "numpadPeriod" });
            return keys.ToArray(); // Escape/Enter/modifiers are reserved for recovery/UI and OS/debug chords.
        }
        // Key enum names are stable for persistence; Input System's digit control paths are "1", not "digit1".
        public static string ControlName(string key) => key != null && key.StartsWith("digit", StringComparison.Ordinal) ? key.Substring(5) : key;
        public static string CanonicalKey(string enumName) => Array.Find(Keys, k => string.Equals(k, enumName, StringComparison.OrdinalIgnoreCase));
        public static bool ValidKey(string key) => !string.IsNullOrEmpty(key) && Array.IndexOf(Keys, key) >= 0;
        public static Definition Find(string id) => Array.Find(Actions, d => d.Id == id);
        public static LocalKeyBinding[] Defaults() => Array.ConvertAll(Actions, d => new LocalKeyBinding { action = d.Id, primary = d.Key, secondary = d.Alternate });
        public static string Get(LocalKeyBinding b, int slot) => slot == 0 ? b.primary : b.secondary;
        public static void Set(LocalKeyBinding b, int slot, string key) { if (slot == 0) b.primary = key; else b.secondary = key; }
        public static List<(int row, int slot)> Conflicts(LocalKeyBinding[] all, int row, int slot, string key)
        {
            var result = new List<(int, int)>();
            if (string.IsNullOrEmpty(key)) return result;
            for (int i = 0; i < all.Length; i++)
                if ((Find(all[i].action).Context & Find(all[row].action).Context) != 0)
                    for (int s = 0; s < 2; s++) if ((i != row || s != slot) && Get(all[i], s) == key) result.Add((i, s));
            return result;
        }
        public static bool TryBind(LocalKeyBinding[] all, int row, int slot, string key, bool swap)
        {
            if (row < 0 || row >= all.Length || slot < 0 || slot > 1 || (!ValidKey(key) && !(slot == 1 && key == ""))) return false;
            var collisions = Conflicts(all, row, slot, key); var copy = Array.ConvertAll(all, b => b.Copy());
            string previous = Get(copy[row], slot);
            if (collisions.Count > 0)
            {
                if (!swap || collisions.Count != 1) return false;
                var c = collisions[0]; if (c.slot == 0 && previous == "") return false;
                Set(copy[c.row], c.slot, previous);
            }
            Set(copy[row], slot, key);
            for (int i = 0; i < copy.Length; i++)
                if (!ValidKey(copy[i].primary) || Conflicts(copy, i, 0, copy[i].primary).Count > 0 || Conflicts(copy, i, 1, copy[i].secondary).Count > 0) return false;
            for (int i = 0; i < all.Length; i++) { all[i].primary = copy[i].primary; all[i].secondary = copy[i].secondary; }
            return true;
        }
        public static LocalKeyBinding[] Repair(LocalKeyBinding[] incoming)
        {
            var result = Defaults();
            for (int i = 0; i < result.Length; i++)
            {
                var old = incoming == null ? null : Array.Find(incoming, b => b != null && b.action == result[i].action);
                result[i].primary = old != null && ValidKey(old.primary) ? old.primary : result[i].primary;
                result[i].secondary = old != null ? ValidKey(old.secondary) ? old.secondary : "" : result[i].secondary;
            }
            // Preserve valid custom mappings, repairing only later conflicting/invalid entries.
            for (int i = 0; i < result.Length; i++) for (int slot = 0; slot < 2; slot++)
            {
                var conflicts = Conflicts(result, i, slot, Get(result[i], slot));
                if (!conflicts.Exists(c => c.row < i || c.row == i && c.slot < slot)) continue;
                Set(result[i], slot, "");
                if (slot == 1) continue;
                string preferred = Actions[i].Key;
                if (Conflicts(result, i, slot, preferred).Count == 0) Set(result[i], slot, preferred);
                else foreach (string key in Keys) if (Conflicts(result, i, slot, key).Count == 0) { Set(result[i], slot, key); break; }
            }
            return result;
        }
    }

    public sealed class DisplayConfirmation
    {
        public bool Pending { get; private set; }
        public double Deadline { get; private set; }
        public bool Begin(double realtime) { if (Pending) return false; Pending = true; Deadline = realtime + 15; return true; }
        public bool Keep(double realtime) { if (!Pending || realtime >= Deadline) return false; Pending = false; return true; }
        public bool Revert() { if (!Pending) return false; Pending = false; return true; }
        public bool Expired(double realtime) => Pending && realtime >= Deadline;
    }

    // Same-directory atomic replace. A failed write never deletes the previous committed file.
    public static class LocalSettingsFile
    {
        public static string Read(string path, Func<string, bool> valid, out string notice)
        {
            notice = "";
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (!File.Exists(candidate)) continue;
                    var info = new FileInfo(candidate); if (info.Length > 262144) throw new IOException("설정 파일이 너무 큽니다");
                    string text = File.ReadAllText(candidate, Encoding.UTF8);
                    if (valid(text)) { if (candidate != path) notice = "설정 파일을 백업에서 복구했습니다."; return text; }
                    notice = "설정 파일을 읽을 수 없어 정상 기본값으로 복구했습니다.";
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
                { notice = "설정 읽기 실패 · 기본값을 사용합니다: " + e.Message; }
            }
            return null;
        }
        public static bool Save(string path, string text, Func<string, bool> valid, out string error)
        {
            error = ""; string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                if (!valid(text)) throw new IOException("유효하지 않은 설정 데이터");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                if (File.Exists(path))
                {
                    bool oldValid = new FileInfo(path).Length <= 262144 && valid(File.ReadAllText(path, Encoding.UTF8));
                    File.Replace(temporary, path, oldValid ? path + ".bak" : null);
                }
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            { error = "설정을 저장하지 못했습니다. 이전 저장값을 유지합니다. 저장 공간/폴더 권한을 확인하세요. " + e.Message; return false; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
