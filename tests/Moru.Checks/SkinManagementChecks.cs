using Moru;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class SkinManagementChecks
{
    internal static void Run(string scratch, Action<string, bool> check)
    {
        string root = Path.Combine(scratch, "manage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string original = Path.Combine(root, "original.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 50, 90, 255 }, 4)));
        using (var output = File.Create(original)) encoder.Save(output);
        byte[] originalBytes = File.ReadAllBytes(original);
        string user = Path.Combine(root, "UserSkins");
        var storage = new UserSkinStore(user);
        var first = storage.Import(original);
        first = storage.SetStateImage(first, PetActivity.Working, original);
        var second = storage.Import(original);
        var bundled = new Skin(SkinCatalog.Fallback);
        Skin[] all = [bundled, first, second];
        var preferences = new Preferences { SkinId = first.Id, SkinNames = new() { [first.Id] = "  우리 우사기  ", ["sprout"] = "초록 친구" }, HiddenSkinIds = ["sprout"] };
        var settings = new SettingsStore(root);
        check("Renamed and hidden pets save successfully", settings.Save(preferences));
        var restored = settings.Load();
        var visible = SkinNamesPolicy.Visible(all, restored);
        check("Custom name survives restart and preserves identity", visible[0].Id == first.Id && visible[0].Name == "우리 우사기");
        check("Renaming reuses original and state image cache", ReferenceEquals(visible[0].Image, first.Image) && ReferenceEquals(visible[0].ImageFor(PetActivity.Working), first.ImageFor(PetActivity.Working)));
        check("Hidden bundled pet does not hide custom pets", visible.Count == 2 && visible.All(s => s.IsCustom));
        check("Restoring bundled pets preserves custom names", SkinNamesPolicy.Visible(all, restored with { HiddenSkinIds = [] }).Count == 3
            && SkinNamesPolicy.Visible(all, restored with { HiddenSkinIds = [] })[0].Name == "초록 친구");
        check("All-hidden damaged preferences retain a usable pet", SkinNamesPolicy.Visible([bundled], restored).Count == 1);
        check("Names reject blank, controls and overlong input", !SkinNamesPolicy.IsValid("   ") && !SkinNamesPolicy.IsValid("a\nb") && !SkinNamesPolicy.IsValid(new string('가', 13)));
        check("Names accept Korean, spaces and twelve characters", SkinNamesPolicy.IsValid("우사기 친구") && SkinNamesPolicy.IsValid(new string('가', 12)));
        var normalized = (new Preferences { SkinNames = new() { ["sprout"] = "\n", ["cat"] = " 소금 " }, HiddenSkinIds = ["sprout", "sprout", ""] }).Normalize();
        check("Malformed name overrides are discarded and hidden IDs deduplicated", normalized.SkinNames.Count == 1 && normalized.SkinNames["cat"] == "소금" && normalized.HiddenSkinIds.SequenceEqual(new[] { "sprout" }));
        string manifest = Path.Combine(user, first.Id + ".json");
        bool rejected = false;
        using (var locked = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { storage.Delete(first); } catch (IOException) { rejected = true; }
        }
        check("Failed deletion preserves manifest and pose files", rejected && File.Exists(manifest) && File.Exists(Path.Combine(user, first.Definition.StateImages!["Working"])));
        storage.Delete(first);
        check("Delete removes selected pet manifest and its owned state copies", !File.Exists(manifest) && !Directory.EnumerateFiles(user, first.Id + "*.png").Any());
        check("Deleting a pet preserves source PNG and other pets", File.ReadAllBytes(original).SequenceEqual(originalBytes) && SkinCatalog.Load(user, false, true).Single().Id == second.Id);
        rejected = false;
        try { storage.Delete(bundled); } catch (InvalidDataException) { rejected = true; }
        check("Bundled files cannot be deleted by the PNG store", rejected);
        rejected = false;
        try { storage.Delete(new Skin(first.Definition with { Id = "../outside" }, first.Image, true)); } catch (InvalidDataException) { rejected = true; }
        check("Deletion rejects path traversal identifiers", rejected);
        var shared = storage.Import(original);
        string sharedManifest = Path.Combine(user, shared.Id + ".json");
        File.WriteAllText(sharedManifest, JsonSerializer.Serialize(shared.Definition with { ImageFile = second.Definition.ImageFile }));
        storage.Delete(second);
        check("Deletion keeps copies referenced by another manifest", File.Exists(Path.Combine(user, second.Definition.ImageFile!)) && SkinCatalog.Load(user, false, true).Count == 1);
        string claudePath = Path.Combine(root, "claude-settings.json");
        string ourExe = @"C:\Apps\Moru\Moru.exe";
        string otherExe = @"C:\Other\Moru.exe";
        File.WriteAllText(claudePath, JsonSerializer.Serialize(new {
            theme = "keep",
            hooks = new { Stop = new object[] { new { hooks = new[] {
                new { type = "command", command = "\"" + ourExe.Replace('\\', '/') + "\" --moru-claude-hook pipe" },
                new { type = "command", command = "\"" + otherExe.Replace('\\', '/') + "\" --moru-claude-hook pipe" },
                new { type = "command", command = "echo keep" }
            } }, new { hooks = Array.Empty<object>() } } },
            statusLine = new { type = "command", command = "\"" + ourExe.Replace('\\', '/') + "\" --moru-claude-status pipe" }
        }));
        ClaudeBridge.Configure(claudePath, ourExe, "", false, onlyThisExecutable: true);
        string cleaned = File.ReadAllText(claudePath);
        check("Uninstall disconnects only its own Claude executable", !cleaned.Contains("C:/Apps/Moru/") && cleaned.Contains("C:/Other/Moru.exe") && cleaned.Contains("echo keep") && cleaned.Contains("theme")
            && JsonDocument.Parse(cleaned).RootElement.GetProperty("hooks").GetProperty("Stop").GetArrayLength() == 2);
        ClaudeBridge.Configure(claudePath, ourExe, "", false, onlyThisExecutable: true);
        check("Uninstall cleanup with no owned hooks leaves configuration unchanged", cleaned == File.ReadAllText(claudePath));
        check("Integration ownership requires exact quoted executable", ClaudeBridge.CommandUsesExecutable("\"" + ourExe + "\" --startup", ourExe)
            && !ClaudeBridge.CommandUsesExecutable("\"" + otherExe + "\" --startup", ourExe)
            && !ClaudeBridge.CommandUsesExecutable("\"" + ourExe + ".other\" --startup", ourExe));
    }
}
