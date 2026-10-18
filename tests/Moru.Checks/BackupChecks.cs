using Moru;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class BackupChecks
{
    internal static void Run(string scratch, Action<string, bool> check)
    {
        string root = Path.Combine(scratch, "backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string Png(string name, byte value)
        {
            string file = Path.Combine(root, name + ".png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { value, 50, 90, 130 }, 4)));
            using var output = File.Create(file); encoder.Save(output); return file;
        }
        string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        Dictionary<string, string> Snapshot(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(folder, p), Hash);
        bool Same(Dictionary<string, string> first, Dictionary<string, string> second) => first.Count == second.Count && first.All(p => second.GetValueOrDefault(p.Key) == p.Value);
        string original = Png("원본", 12);
        string originalHash = Hash(original);
        Skin[] bundled = [new(SkinCatalog.Fallback), new(SkinCatalog.Fallback with { Id = "cat", Shape = "cat", Name = "소금" }),
            new(SkinCatalog.Fallback with { Id = "robot", Shape = "robot", Name = "루미" })];
        string source = Path.Combine(root, "source");
        string sourceUser = Path.Combine(source, "UserSkins");
        var sourceStore = new UserSkinStore(sourceUser);
        var first = sourceStore.Import(original);
        var states = new[] { PetActivity.Idle, PetActivity.Working, PetActivity.Done, PetActivity.Waiting, PetActivity.Error };
        foreach (var state in states) first = sourceStore.SetStateImage(first, state, Png(state.ToString(), (byte)(30 + (int)state)));
        var second = sourceStore.Import(Png("둘째", 29));
        var sourcePreferences = new Preferences { SkinId = first.Id, SkinNames = new() { [first.Id] = "우리 우사기", ["sprout"] = "초록 친구" },
            CodexEnabled = true, DarkMode = true, X = 123, RepeatTimer = true };
        new SettingsStore(source).Save(sourcePreferences);
        string backupFile = Path.Combine(root, "친구들.moru-backup");
        var sourceBefore = Snapshot(source);
        CharacterBackup.Export(backupFile, sourceUser, bundled.Concat([first, second]).ToArray(), sourcePreferences);
        check("Backup leaves original images and current data unchanged", Hash(original) == originalHash && Same(sourceBefore, Snapshot(source)));
        bool selfOverwriteRejected = false;
        try { CharacterBackup.Export(Path.Combine(sourceUser, first.Id + ".json"), sourceUser, bundled.Append(first).ToArray(), sourcePreferences); }
        catch (InvalidDataException) { selfOverwriteRejected = true; }
        check("Backup cannot overwrite its own character storage", selfOverwriteRejected && Same(sourceBefore, Snapshot(source)));
        using (var zip = ZipFile.OpenRead(backupFile))
        {
            check("Backup contains only character manifest and seven PNGs", zip.Entries.Count == 8 && zip.Entries.All(e => e.FullName == "backup.json" || e.FullName.EndsWith(".png")));
            using var reader = new StreamReader(zip.GetEntry("backup.json")!.Open());
            string json = reader.ReadToEnd();
            check("Backup omits AI, alarm, startup, position and personal paths", !json.Contains("CodexEnabled") && !json.Contains("Reminders") && !json.Contains(root) && !json.Contains("DesktopShortcutCreated"));
        }
        using var backup = CharacterBackup.Read(backupFile);
        check("Backup verifies all five poses and renamed Unicode character", backup.Skins.Count == 2 && backup.ImageCount == 7 && backup.Skins[0].Name == "우리 우사기" && backup.Skins[0].StateImages.Count == 5);
        string target = Path.Combine(root, "target");
        string targetUser = Path.Combine(target, "UserSkins");
        var unrelated = new UserSkinStore(targetUser).Import(Png("기존 친구", 99));
        var existingHash = Hash(Path.Combine(targetUser, unrelated.Definition.ImageFile!));
        var prefs = new Preferences { SkinId = unrelated.Id, TextSize = 20, HoverEnabled = false, ClaudeEnabled = true, X = -35 };
        var settings = new SettingsStore(target); settings.Save(prefs);
        var result = backup.Restore(targetUser, bundled.Append(unrelated).ToArray(), prefs, settings.Save);
        var reloaded = SkinCatalog.Load(targetUser, false, true);
        var restoredPrefs = settings.Load();
        var restored = reloaded.Single(s => s.Id == first.Id);
        check("Restore merges all custom characters without removing existing pet", result.Added == 2 && result.Existing == 0 && reloaded.Count == 3 && Hash(Path.Combine(targetUser, unrelated.Definition.ImageFile!)) == existingHash);
        check("Restored identity, selection and custom/bundled names survive reload", restoredPrefs.SkinId == first.Id && restored.Name == "우리 우사기" && restoredPrefs.SkinNames["sprout"] == "초록 친구");
        check("Restore leaves unrelated preferences unchanged", restoredPrefs.TextSize == 20 && !restoredPrefs.HoverEnabled && restoredPrefs.ClaudeEnabled && restoredPrefs.X == -35 && !restoredPrefs.CodexEnabled);
        check("Restored base PNG is byte-identical", Hash(Path.Combine(targetUser, restored.Definition.ImageFile!)) == Hash(Path.Combine(sourceUser, first.Definition.ImageFile!)));
        check("All five state PNGs survive restore byte-for-byte", states.All(s => Hash(Path.Combine(targetUser, restored.Definition.StateImages![s.ToString()])) == Hash(Path.Combine(sourceUser, first.Definition.StateImages![s.ToString()]))));
        var beforeRepeat = Snapshot(target);
        var repeat = backup.Restore(targetUser, bundled.Concat(reloaded).ToArray(), restoredPrefs, settings.Save);
        check("Repeated restore does not create duplicates or change files", repeat.Added == 0 && repeat.Existing == 2 && Same(beforeRepeat, Snapshot(target)));

        var changedPrefs = restoredPrefs with { SkinNames = new(restoredPrefs.SkinNames) { [first.Id] = "지금 친구" } };
        settings.Save(changedPrefs);
        var beforeConflict = Snapshot(targetUser);
        var conflict = backup.Restore(targetUser, bundled.Concat(reloaded).ToArray(), changedPrefs, settings.Save);
        string copyId = conflict.Preferences.SkinId;
        check("Changed name keeps current character and restores separate named copy", conflict.Added == 1 && copyId != first.Id
            && conflict.Preferences.SkinNames[first.Id] == "지금 친구" && conflict.All.Single(s => s.Id == copyId).Name == "우리 우사기"
            && beforeConflict.All(p => Snapshot(targetUser).GetValueOrDefault(p.Key) == p.Value));
        var conflictRepeat = backup.Restore(targetUser, conflict.All, conflict.Preferences, settings.Save);
        check("Re-restoring a conflicting backup reuses restored copy", conflictRepeat.Added == 0 && conflictRepeat.Preferences.SkinId == copyId);

        string rollback = Path.Combine(root, "rollback");
        string rollbackUser = Path.Combine(rollback, "UserSkins");
        var keep = new UserSkinStore(rollbackUser).Import(Png("남길 친구", 80));
        var rollbackSettings = new SettingsStore(rollback); rollbackSettings.Save(prefs);
        var beforeRollback = Snapshot(rollback);
        bool failed = false;
        using (var locked = new FileStream(Path.Combine(rollback, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            try { backup.Restore(rollbackUser, bundled.Append(keep).ToArray(), prefs, rollbackSettings.Save); }
            catch (IOException) { failed = true; }
        // SettingsStore may leave its own temporary JSON; only user settings and skin files are durable data.
        string settingsTemporary = Path.Combine(rollback, "settings.json.tmp");
        if (File.Exists(settingsTemporary)) File.Delete(settingsTemporary);
        check("Settings write failure rolls back added PNGs/manifests and preserves originals", failed && Same(beforeRollback, Snapshot(rollback)));

        string fullUser = Path.Combine(root, "full", "UserSkins");
        var full = bundled.Concat(Enumerable.Range(0, 9).Select(i => new UserSkinStore(fullUser).Import(Png("가득" + i, (byte)(100 + i))))).ToArray();
        var fullBefore = Snapshot(fullUser); failed = false;
        try { backup.Restore(fullUser, full, new(), _ => true); } catch (InvalidDataException) { failed = true; }
        check("Capacity failure makes no partial imports", failed && Same(fullBefore, Snapshot(fullUser)));

        void Reject(string label, Action<ZipArchive> mutate)
        {
            var destinationBefore = Snapshot(target);
            string bad = Path.Combine(root, Guid.NewGuid().ToString("N") + ".moru-backup");
            File.Copy(backupFile, bad);
            using (var zip = ZipFile.Open(bad, ZipArchiveMode.Update)) mutate(zip);
            bool rejected = false;
            try { using var parsed = CharacterBackup.Read(bad); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or FormatException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) { rejected = true; }
            check(label, rejected && Same(destinationBefore, Snapshot(target)));
        }
        void EditManifest(ZipArchive zip, Action<JsonObject> edit)
        {
            JsonObject json;
            var old = zip.GetEntry("backup.json")!;
            using (var reader = new StreamReader(old.Open())) json = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
            edit(json); old.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("backup.json").Open()); writer.Write(json.ToJsonString());
        }
        Reject("Path traversal is rejected before extraction", zip => { using var writer = new StreamWriter(zip.CreateEntry("../outside.txt").Open()); writer.Write("escape"); });
        check("Archive never wrote outside staging", !File.Exists(Path.Combine(root, "outside.txt")));
        Reject("Case-insensitive duplicate archive entries are rejected", zip => zip.CreateEntry("skin-0.png"));
        Reject("Missing state image rejects entire backup", zip => zip.GetEntry("skin-0-Working.png")!.Delete());
        Reject("Unknown backup version is rejected", zip => EditManifest(zip, json => json["Version"] = 999));
        Reject("Null character records are rejected", zip => EditManifest(zip, json => json["Skins"] = new JsonArray((JsonNode?)null)));
        Reject("Unknown selected character is rejected", zip => EditManifest(zip, json => json["SelectedSkinId"] = "missing"));
        Reject("Changed image hash rejects entire backup", zip => EditManifest(zip, json => json["ImageHashes"]!["skin-0.png"] = new string('0', 64)));
        Reject("Oversized expanded image is rejected before decode", zip =>
        {
            zip.GetEntry("skin-0.png")!.Delete();
            using var writer = zip.CreateEntry("skin-0.png").Open(); writer.Write(new byte[PngSkinImage.MaxFileBytes + 1]);
        });
        Reject("Malformed PNG is rejected even with a matching checksum", zip =>
        {
            byte[] invalid = new byte[80];
            zip.GetEntry("skin-0.png")!.Delete();
            using (var writer = zip.CreateEntry("skin-0.png").Open()) writer.Write(invalid);
            EditManifest(zip, json => json["ImageHashes"]!["skin-0.png"] = Convert.ToHexString(SHA256.HashData(invalid)));
        });
        string goodHash = Hash(backupFile);
        string missingPose = Path.Combine(sourceUser, first.Definition.StateImages!["Working"]);
        string savedPose = missingPose + ".saved";
        File.Move(missingPose, savedPose); failed = false;
        try { CharacterBackup.Export(backupFile, sourceUser, bundled.Concat([first, second]).ToArray(), sourcePreferences); }
        catch (IOException) { failed = true; }
        finally { File.Move(savedPose, missingPose); }
        check("Failed backup never replaces an existing good archive", failed && Hash(backupFile) == goodHash);
        string emptyBackup = Path.Combine(root, "기본친구.moru-backup");
        CharacterBackup.Export(emptyBackup, sourceUser, bundled, new Preferences { SkinNames = new() { ["sprout"] = "새 이름" } });
        using var builtInBackup = CharacterBackup.Read(emptyBackup);
        var builtInResult = builtInBackup.Restore(Path.Combine(root, "builtInOnly"), bundled, new(), _ => true);
        check("Built-in names can be backed up without any custom characters", builtInResult.Added == 0 && builtInResult.Preferences.SkinNames["sprout"] == "새 이름");
    }
}
