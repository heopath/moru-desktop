using Moru;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class StateSkinChecks
{
    internal static void Run(string scratch, Action<string, bool> check)
    {
        string root = Path.Combine(scratch, "state-skins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "pose.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 20, 70, 200, 255 }, 4)));
        using (var output = File.Create(source)) encoder.Save(output);
        var store = new UserSkinStore(root);
        var skin = store.Import(source);
        check("Old one-image skins fall back in every state", Enum.GetValues<PetActivity>().All(s => ReferenceEquals(skin.ImageFor(s), skin.Image)));
        var working = store.SetStateImage(skin, PetActivity.Working, source);
        check("State pose is cached, frozen and distinct from base", working.ImageFor(PetActivity.Working) is { IsFrozen: true } image && !ReferenceEquals(image, skin.Image) && ReferenceEquals(image, working.ImageFor(PetActivity.Working)));
        check("Other states still use base image", ReferenceEquals(working.ImageFor(PetActivity.Waiting), skin.Image));
        var done = store.SetStateImage(working, PetActivity.Done, source);
        var restored = SkinCatalog.Load(root, false, true).Single();
        check("Multiple state images and identity survive restart", restored.Id == skin.Id && restored.StateImages.Count == 2 && restored.ImageFor(PetActivity.Done) is not null);
        string oldWorking = done.Definition.StateImages!["Working"];
        var replacement = store.SetStateImage(done, PetActivity.Working, source);
        check("Replacing a pose cleans its old copy and preserves other states", !File.Exists(Path.Combine(root, oldWorking)) && replacement.StateImages.ContainsKey("Done"));
        check("State updates never alter original PNG", File.Exists(source) && PngSkinImage.Load(source).PixelWidth == 1);
        string manifest = Path.Combine(root, skin.Id + ".json");
        string saved = File.ReadAllText(manifest);
        int count = Directory.GetFiles(root).Length;
        string bad = Path.Combine(scratch, "bad-state.png");
        File.WriteAllText(bad, "not an image");
        bool rejected = false;
        try { store.SetStateImage(replacement, PetActivity.Done, bad); } catch (InvalidDataException) { rejected = true; }
        check("Failed pose import leaves previous skin and files intact", rejected && saved == File.ReadAllText(manifest) && count == Directory.GetFiles(root).Length);
        var reset = store.SetStateImage(replacement, PetActivity.Working, null);
        check("Reset restores base and preserves the other pose", ReferenceEquals(reset.ImageFor(PetActivity.Working), reset.Image) && reset.StateImages.ContainsKey("Done"));
        File.WriteAllText(Path.Combine(root, reset.Definition.StateImages!["Done"]), "broken");
        var recovered = SkinCatalog.Load(root, false, true).Single();
        check("Corrupt optional pose falls back without discarding the pet", ReferenceEquals(recovered.ImageFor(PetActivity.Done), recovered.Image));
        check("State image path traversal is rejected", !SkinCatalog.IsValid(skin.Definition with { StateImages = new() { ["Working"] = "../secret.png" } }));
        check("Unknown state names are rejected", !SkinCatalog.IsValid(skin.Definition with { StateImages = new() { ["Script"] = "pose.png" } }));
        rejected = false;
        try { store.SetStateImage(new Skin(SkinCatalog.Fallback), PetActivity.Done, source); } catch (InvalidDataException) { rejected = true; }
        check("Bundled skins cannot be overwritten by state editing", rejected);
        var settings = new SettingsStore(Path.Combine(root, "preferences"));
        settings.Save(new Preferences { DesktopShortcutCreated = true });
        check("First-launch shortcut marker survives restart", !new Preferences().DesktopShortcutCreated && settings.Load().DesktopShortcutCreated);
        string spacedExe = Path.Combine(root, "space folder", "Moru.exe");
        check("Startup command quotes spaced paths and opens pet quietly", WindowsIntegration.StartupCommand(spacedExe) == "\"" + spacedExe + "\" --startup");
        rejected = false;
        try { WindowsIntegration.StartupCommand("relative.exe"); } catch (ArgumentException) { rejected = true; }
        check("Relative autostart targets are rejected", rejected);
        rejected = false;
        try { WindowsIntegration.StartupCommand("C:\\" + new string('a', 260) + "\\Moru.exe"); } catch (IOException) { rejected = true; }
        check("Oversized Run commands are rejected before registry writes", rejected);

        // Real shell-link round trip in an isolated folder; no user Desktop or Run key touched.
        Directory.CreateDirectory(Path.GetDirectoryName(spacedExe)!);
        File.WriteAllText(spacedExe, "test fixture, never executed");
        string shortcut = WindowsIntegration.CreateDesktopShortcut(spacedExe, root);
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!)!;
        object link = ((dynamic)shell).CreateShortcut(shortcut);
        try
        {
            check("Desktop shortcut has executable, arguments and working directory", (string)((dynamic)link).TargetPath == spacedExe
                && (string)((dynamic)link).Arguments == "--show-panel"
                && (string)((dynamic)link).WorkingDirectory == Path.GetDirectoryName(spacedExe));
        }
        finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
    }
}
