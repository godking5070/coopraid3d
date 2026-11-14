using System;
using System.IO;
using System.Linq;
using RaidRogue;

internal static class LocalSettingsTests
{
    private static int _checks;
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
    private static int Row(LocalKeyBinding[] b, string id) => Array.FindIndex(b, v => v.action == id);
    private static string Fingerprint(LocalKeyBinding[] b) => string.Join("|", b.Select(v => v.action + ":" + v.primary + ":" + v.secondary));
    private static void ValidBindings(LocalKeyBinding[] bindings)
    {
        Check(bindings.Length == LocalBindingRules.Actions.Length, "all actions retained");
        for (int i = 0; i < bindings.Length; i++)
        {
            Check(LocalBindingRules.ValidKey(bindings[i].primary), "essential primary retained");
            Check(LocalBindingRules.Conflicts(bindings, i, 0, bindings[i].primary).Count == 0, "primary conflict");
            Check(LocalBindingRules.Conflicts(bindings, i, 1, bindings[i].secondary).Count == 0, "secondary conflict");
        }
    }
    private static void BindingTests()
    {
        var b = LocalBindingRules.Defaults(); ValidBindings(b);
        Check(LocalBindingRules.Conflicts(b, Row(b,"Skill1"), 0, "digit1").Count == 0, "combat and choices share context safely");
        Check(LocalBindingRules.Conflicts(b, Row(b,"Settings"), 0, "digit1").Count == 2, "global key collides with both contexts");
        string before = Fingerprint(b);
        Check(!LocalBindingRules.TryBind(b, Row(b,"Settings"),0,"digit1",true) && before == Fingerprint(b), "ambiguous swap must be atomic");
        Check(!LocalBindingRules.TryBind(b, Row(b,"Dash"),0,"x",false) && before == Fingerprint(b), "conflict needs explicit confirmation");
        Check(LocalBindingRules.TryBind(b, Row(b,"Dash"),0,"x",true), "explicit swap accepted");
        Check(b[Row(b,"Dash")].primary == "x" && b[Row(b,"Potion")].primary == "space", "swap both sides");
        before = Fingerprint(b);
        Check(!LocalBindingRules.TryBind(b, Row(b,"Dash"),1,"space",true) && before == Fingerprint(b), "cannot empty essential primary via secondary swap");
        foreach (string key in new[]{"escape","enter","leftShift","rightCtrl","leftAlt","leftMeta","mouseLeft","",null,"<Keyboard>/w"})
            Check(!LocalBindingRules.TryBind(b,0,0,key,true), "reserve recovery and malformed keys");
        Check(LocalBindingRules.TryBind(b,0,1,"",false), "secondary clear");
        Check(LocalBindingRules.TryBind(b,0,0,"i",false), "free key");
        ValidBindings(b);
        var saved = LocalBindingRules.Repair(b);
        Check(Fingerprint(saved) == Fingerprint(b), "valid persisted custom keys retain exact mapping");
        var random = new Random(46);
        for (int round = 0; round < 180; round++)
        {
            var incoming = LocalBindingRules.Defaults();
            for (int i = 0; i < incoming.Length; i++)
            {
                if (random.Next(5) == 0) { incoming[i] = null; continue; }
                incoming[i].primary = random.Next(5) == 0 ? "escape" : LocalBindingRules.Keys[random.Next(LocalBindingRules.Keys.Length)];
                incoming[i].secondary = random.Next(3) == 0 ? null : incoming[i].primary;
            }
            var fixedKeys = LocalBindingRules.Repair(incoming); ValidBindings(fixedKeys);
            Check(Fingerprint(fixedKeys) == Fingerprint(LocalBindingRules.Repair(fixedKeys)), "repair is idempotent");
            int row = random.Next(fixedKeys.Length); int slot = random.Next(2); string key = LocalBindingRules.Keys[random.Next(LocalBindingRules.Keys.Length)];
            before = Fingerprint(fixedKeys);
            bool success = LocalBindingRules.TryBind(fixedKeys,row,slot,key,true);
            if (!success) Check(before == Fingerprint(fixedKeys), "failed edit cannot partially mutate bindings");
            ValidBindings(fixedKeys);
        }
        ValidBindings(LocalBindingRules.Repair(null));
        ValidBindings(LocalBindingRules.Repair(new[]{new LocalKeyBinding { action="removed-action",primary="f10" }}));
    }
    private static LocalKeyBinding[] OldDefaults()
    {
        var old=LocalBindingRules.Defaults().Where(b=>Array.IndexOf(new[]{"Counter","JobSkill2","Identity","SelectedActive","SelectedOffGcd","Ultimate","Interact"},b.action)<0).ToArray();
        old[Row(old,"Potion")].primary="q";old[Row(old,"Skill4")].primary="digit4";old[Row(old,"Skill4")].secondary="numpad4";old[Row(old,"View")].primary="v";return old;
    }
    private static void MigrationTests()
    {
        var old=OldDefaults();
        Check(!old.Any(b=>b.action=="Interact"),"v1 factory fixture excludes the later Interact action");
        Check(LocalBindingRules.IsVersionOneFactory(old),"v1 factory fixture is recognized before migration");
        var data=new LocalSettingsData{version=1,bindings=old};data.Normalize();
        Check(data.bindings[Row(data.bindings,"Potion")].primary=="x"&&data.bindings[Row(data.bindings,"Skill4")].primary=="q"&&data.bindings[Row(data.bindings,"View")].primary=="f12","factory Q potion/X and V view migration");
        ValidBindings(data.bindings);string first=Fingerprint(data.bindings);data.Normalize();Check(Fingerprint(data.bindings)==first&&data.version==2,"migration is versioned and idempotent");
        var custom=OldDefaults();custom[Row(custom,"Skill1")].primary="x";
        data=new LocalSettingsData{version=1,bindings=custom};data.Normalize();
        Check(data.bindingsNeedReview&&data.bindingNotice.Length>0,"ambiguous v1 settings request conflict review");
        Check(data.bindings[Row(data.bindings,"Skill1")].primary=="x"&&data.bindings[Row(data.bindings,"Potion")].primary=="q","ambiguous/custom mappings not reset");
        foreach(var original in custom) {var retained=data.bindings[Row(data.bindings,original.action)];Check(retained.primary==original.primary&&retained.secondary==original.secondary,"all legacy custom keys preserved");}
        var b=data.bindings;
        Check(LocalBindingRules.TryBind(b,Row(b,"Counter"),0,"h",false),"repair one collision while others pending");
        Check(LocalBindingRules.TryBind(b,Row(b,"Counter"),1,"",false),"repair alternate collision");
        Check(LocalBindingRules.TryBind(b,Row(b,"View"),0,"f12",false),"resolve view/ultimate conflict");
        Check(!LocalBindingRules.TryBind(b,Row(b,"Potion"),0,"x",false),"custom X conflict requires explicit swap");
        Check(LocalBindingRules.TryBind(b,Row(b,"Potion"),0,"x",true),"explicit user swap X accepted");
        Check(b[Row(b,"Potion")].primary=="x"&&b[Row(b,"Skill1")].primary=="q"&&b[Row(b,"Potion")].customized,"provenance after explicit edit");
        data.Normalize();Check(!data.bindingsNeedReview,"all conflicts resolved without full reset");
        first=Fingerprint(data.bindings);data.version=2;data.Normalize();Check(first==Fingerprint(data.bindings),"v2 custom Q/X never migrated again");
        foreach(var id in new[]{"Skill1","Skill2","Skill3","Counter","Skill4","JobSkill2","Dash","Identity","SelectedActive","SelectedOffGcd","Ultimate","Potion"})Check(LocalBindingRules.Find(id)!=null,"all actual slots present");
    }
    private static void DataTests()
    {
        var data = new LocalSettingsData { version = 0, width = -5, height = int.MaxValue, windowMode = 17, frameLimit = 7,
            master = float.NaN, music = -1, effects = float.PositiveInfinity, ui = 42, rotationSensitivity = float.NegativeInfinity, zoom2D = -5, zoom3D = 100, bindings = null };
        data.Normalize();
        Check(data.version == LocalSettingsData.CurrentVersion && data.width == 640 && data.height == 16384, "version/dimensions normalize");
        Check(data.windowMode == 1 && data.frameLimit == 120, "display enums and caps fallback");
        Check(data.master == 1 && data.music == 0 && data.effects == 1 && data.ui == 1, "invalid volume fallback");
        Check(data.rotationSensitivity == .16f && data.zoom2D == .25f && data.zoom3D == 5, "camera bounds");
        ValidBindings(data.bindings);
        var copy = data.Copy(); copy.bindings[0].primary = "i"; copy.master = .2f;
        Check(data.bindings[0].primary == "w" && data.master == 1, "draft never mutates applied/local peers");
        Check(data.SameDisplay(copy), "sound does not need screen confirmation"); copy.width++;
        Check(!data.SameDisplay(copy), "resolution needs confirmation");
        var confirmation = new DisplayConfirmation();
        Check(confirmation.Begin(100) && !confirmation.Begin(101), "one pending change");
        Check(!confirmation.Expired(114.999) && confirmation.Expired(115), "15 real seconds boundary");
        Check(!confirmation.Keep(115) && confirmation.Revert() && !confirmation.Revert(), "timeout and revert once");
        Check(confirmation.Begin(200) && confirmation.Keep(214.999) && !confirmation.Pending, "keep in time");
        Check(!confirmation.Keep(215) && !confirmation.Revert(), "no duplicate commit/revert");
        Check(confirmation.Begin(300) && confirmation.Revert() && !confirmation.Expired(999), "cancel or focus-loss ends timeout");
    }
    private static void StorageTests()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Raid-Settings-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); string path = Path.Combine(folder, "settings.json");
        Func<string,bool> valid = value => value == "good-one" || value == "good-two" || value == "good-three";
        try
        {
            Check(LocalSettingsFile.Read(path, valid, out _) == null, "no file defaults");
            Check(LocalSettingsFile.Save(path,"good-one",valid,out _), "initial save");
            Check(LocalSettingsFile.Read(path,valid,out _) == "good-one", "restart read");
            Check(LocalSettingsFile.Save(path,"good-two",valid,out _), "atomic second save");
            Check(File.ReadAllText(path + ".bak") == "good-one", "backup previous valid value");
            File.WriteAllText(path,"corrupt");
            Check(LocalSettingsFile.Read(path,valid,out string notice) == "good-one" && notice.Length > 0, "corrupt primary restores backup and reports");
            Check(LocalSettingsFile.Save(path,"good-three",valid,out _), "overwrite broken primary");
            Check(File.ReadAllText(path + ".bak") == "good-one", "broken primary cannot poison good backup");
            Check(!LocalSettingsFile.Save(path,"bad",valid,out string error) && error.Length > 0 && File.ReadAllText(path) == "good-three", "invalid save preserves committed file");
            using (var locked = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))
                Check(!LocalSettingsFile.Save(path,"good-two",valid,out error) && error.Length > 0, "locked file yields recoverable error");
            Check(File.ReadAllText(path) == "good-three", "failed replace preserves committed file");
            Check(Directory.GetFiles(folder,"*.tmp-*").Length == 0, "temporary files cleaned after failure");
            File.WriteAllText(path,new string('x',262145)); File.Delete(path+".bak");
            Check(LocalSettingsFile.Read(path,valid,out notice) == null && notice.Length > 0, "oversized file safely defaults");
            File.WriteAllText(path,"bad"); File.WriteAllText(path+".bak","bad");
            Check(LocalSettingsFile.Read(path,valid,out notice) == null && notice.Length > 0, "two corrupt files safely default");
            string notFolder = Path.Combine(folder,"blocked"); File.WriteAllText(notFolder,"occupied");
            Check(!LocalSettingsFile.Save(Path.Combine(notFolder,"config"),"good-one",valid,out error), "unwritable path does not crash");
            Check(LocalSettingsFile.Save(path,"good-one",valid,out _), "save can retry after failure");
        }
        finally { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
    }
    private static void InstalledInputControls(string path)
    {
        string source = File.ReadAllText(path);
        foreach (string key in LocalBindingRules.Keys)
        {
            string name = LocalBindingRules.ControlName(key);
            Check(source.Contains("name = \"" + name + "\""), "installed Input System control exists: " + key);
            Check(LocalBindingRules.CanonicalKey(key.ToUpperInvariant()) == key, "capture canonicalization: " + key);
        }
        for (int i = 0; i <= 9; i++) Check(LocalBindingRules.ControlName("digit" + i) == i.ToString(), "numeric action path");
    }
    public static int Main(string[] args)
    {
        BindingTests(); MigrationTests(); DataTests(); StorageTests();
        if (args.Length > 0) InstalledInputControls(args[0]);
        Console.WriteLine("PASS: " + _checks + " local settings, binding conflicts, display rollback and real file recovery checks (no Unity native/UI execution)."); return 0;
    }
}
