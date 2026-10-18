using Moru;
using System.IO;
using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    private static int passed;
    [STAThread]
    private static int Main(string[] args)
    {
        string scratch = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/check-data");
        Directory.CreateDirectory(scratch);
        Check("CPU subtracts idle from kernel + user", CpuMath.Usage(new(100, 200, 100), new(200, 500, 200)) == 75);
        Check("CPU all idle", CpuMath.Usage(new(0, 0, 0), new(100, 100, 0)) == 0);
        Check("CPU all busy", CpuMath.Usage(new(0, 0, 0), new(0, 50, 50)) == 100);
        Check("Zero interval is unknown, not 0%", CpuMath.Usage(new(5, 10, 10), new(5, 10, 10)) is null);
        Check("Counter rollback resets baseline", CpuMath.Usage(new(5, 10, 10), new(4, 11, 11)) is null);
        Check("Inconsistent idle counters rejected", CpuMath.Usage(new(0, 0, 0), new(100, 10, 10)) is null);
        Check("Memory handles inconsistent available value", new Metrics(0, 100, 150).UsedMemory == 0);
        Check("Memory unavailable has no division by zero", new Metrics(null, 0, 0).MemoryPercent == 0);

        var store = new SettingsStore(scratch);
        var desired = new Preferences { SkinId = "cat", RefreshSeconds = 5, AlwaysOnTop = false, X = -1200, Y = 400 };
        Check("Settings save succeeds", store.Save(desired));
        Check("Settings round trip including negative monitor coordinates", System.Text.Json.JsonSerializer.Serialize(store.Load()) == System.Text.Json.JsonSerializer.Serialize(desired));
        File.WriteAllText(Path.Combine(scratch, "settings.json"), "{broken");
        Check("Corrupt settings recover to defaults", System.Text.Json.JsonSerializer.Serialize(store.Load()) == System.Text.Json.JsonSerializer.Serialize(new Preferences()));
        File.WriteAllText(Path.Combine(scratch, "settings.json"), "{\"RefreshSeconds\":0,\"X\":999999}");
        Check("Invalid settings normalized", store.Load().RefreshSeconds == 2 && store.Load().X is null);
        string blocked = Path.Combine(scratch, "not-a-directory");
        File.WriteAllText(blocked, "test");
        Check("Save failure is reported without crashing", !new SettingsStore(blocked).Save(desired));

        Check("Valid skin accepted", SkinCatalog.IsValid(SkinCatalog.Fallback));
        Check("Invalid color rejected", !SkinCatalog.IsValid(SkinCatalog.Fallback with { Body = "not-a-color" }));
        Check("Unsupported shape rejected", !SkinCatalog.IsValid(SkinCatalog.Fallback with { Shape = "arbitrary-script" }));
        string skins = Path.Combine(scratch, "skins");
        Directory.CreateDirectory(skins);
        File.WriteAllText(Path.Combine(skins, "bad.json"), "{}");
        Check("Invalid skin pack has built-in fallback", SkinCatalog.Load(skins)[0].Id == "sprout");
        var bundled = SkinCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Skins"));
        Check("Three bundled skins load and use frozen brushes", bundled.Count == 3 && bundled.All(s => s.Body.IsFrozen));

        Check("Empty user skin folder does not add a fallback", SkinCatalog.Load(Path.Combine(scratch, "missing"), false).Count == 0);
        var imageDefinition = SkinCatalog.Fallback with { Shape = "image", ImageFile = "friend.png" };
        Check("Local image skin accepted", SkinCatalog.IsValid(imageDefinition));
        Check("Image path traversal rejected", !SkinCatalog.IsValid(imageDefinition with { ImageFile = "../friend.png" }));
        Check("Remote image URLs rejected", !SkinCatalog.IsValid(imageDefinition with { ImageFile = "https://example.com/friend.png" }));
        Check("Image without filename rejected", !SkinCatalog.IsValid(imageDefinition with { ImageFile = null }));
        string sourcePng = Path.Combine(scratch, "test-friend.png");
        var pixels = new byte[512 * 512 * 4];
        pixels[3] = 255;
        var sourceBitmap = BitmapSource.Create(512, 512, 96, 96, PixelFormats.Bgra32, null, pixels, 512 * 4);
        var pngEncoder = new PngBitmapEncoder();
        pngEncoder.Frames.Add(BitmapFrame.Create(sourceBitmap));
        using (var output = File.Create(sourcePng)) pngEncoder.Save(output);
        byte[] sourceBytes = File.ReadAllBytes(sourcePng);
        string userFolder = Path.Combine(scratch, "user-skins-" + Guid.NewGuid().ToString("N"));
        var imported = new UserSkinStore(userFolder).Import(sourcePng);
        Check("PNG import decodes at most 320px and freezes the image", imported.Image is { PixelWidth: 320, PixelHeight: 320, IsFrozen: true });
        Check("Import leaves original PNG unchanged", File.ReadAllBytes(sourcePng).SequenceEqual(sourceBytes));
        using (var exclusiveSource = File.Open(sourcePng, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check("Source file is released after import", exclusiveSource.CanWrite);
        var restored = SkinCatalog.Load(userFolder, false, true);
        Check("Imported image and identity survive reload", restored.Count == 1 && restored[0].Id == imported.Id && restored[0].Image is not null && restored[0].IsCustom);
        var transparent = new byte[4];
        new FormatConvertedBitmap(restored[0].Image!, PixelFormats.Bgra32, null, 0).CopyPixels(new System.Windows.Int32Rect(100, 100, 1, 1), transparent, 4, 0);
        Check("PNG alpha survives import and reload", transparent[3] == 0);
        byte[] invalid = sourceBytes.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(invalid.AsSpan(16, 4), 50000);
        string oversizedPng = Path.Combine(scratch, "oversized.png");
        File.WriteAllBytes(oversizedPng, invalid);
        Throws<InvalidDataException>("Oversized PNG rejected before decode", () => PngSkinImage.Load(oversizedPng));
        File.WriteAllText(Path.Combine(userFolder, "missing.json"), System.Text.Json.JsonSerializer.Serialize(imageDefinition));
        Check("Missing image is skipped without breaking saved skins", SkinCatalog.Load(userFolder, false).Count == 1);

        var metrics = new SystemMetrics();
        var first = metrics.Read();
        Check("First CPU reading waits for interval", first.CpuPercent is null);
        Check("Native memory reads real physical RAM", first.TotalMemory > 0 && first.AvailableMemory <= first.TotalMemory);
        Thread.Sleep(300);
        var next = metrics.Read();
        Check("Native CPU sample stays in bounds", !metrics.SupportsCpu || next.CpuPercent is >= 0 and <= 100);
        metrics.Reset();
        Check("Reopening starts a new CPU interval", metrics.Read().CpuPercent is null);
        AiChecks.Run(scratch, Check);
        UtilityChecks.Run(scratch, Check);
        StateSkinChecks.Run(scratch, Check);
        SkinManagementChecks.Run(scratch, Check);
        Console.WriteLine($"PASS: {passed} checks");
        return 0;
    }
    private static void Check(string label, bool condition)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        passed++;
        Console.WriteLine("PASS: " + label);
    }
    private static void Throws<T>(string label, Action action) where T : Exception
    {
        try { action(); } catch (T) { Check(label, true); return; }
        Check(label, false);
    }
}
