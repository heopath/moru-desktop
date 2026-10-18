using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Moru;

public sealed record SkinDefinition(string Id, string Name, string Description, string Shape,
    string Body, string Outline, string Accent, string Soft, string Cheek, string? ImageFile = null)
{
    public Dictionary<string, string>? StateImages { get; init; }
}

public sealed class Skin(SkinDefinition definition, BitmapSource? image = null, bool isCustom = false,
    IReadOnlyDictionary<string, BitmapSource>? stateImages = null)
{
    public SkinDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string Shape => Definition.Shape;
    public BitmapSource? Image { get; } = image;
    public bool IsCustom { get; } = isCustom;
    internal IReadOnlyDictionary<string, BitmapSource> StateImages { get; } = stateImages ?? new Dictionary<string, BitmapSource>();
    internal BitmapSource? ImageFor(PetActivity state) => StateImages.TryGetValue(state.ToString(), out var value) ? value : Image;
    public SolidColorBrush Body { get; } = Brush(definition.Body);
    public SolidColorBrush Outline { get; } = Brush(definition.Outline);
    public SolidColorBrush Accent { get; } = Brush(definition.Accent);
    public SolidColorBrush Soft { get; } = Brush(definition.Soft);
    public SolidColorBrush Cheek { get; } = Brush(definition.Cheek);

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}

internal static class SkinCatalog
{
    public static readonly SkinDefinition Fallback = new("sprout", "모루", "새싹 친구", "sprout",
        "#BEE7D3", "#244B40", "#327B62", "#EDF6F1", "#EEB8A5");
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    public static bool IsValid(SkinDefinition? skin) => skin is not null
        && !string.IsNullOrWhiteSpace(skin.Id) && skin.Id.Length <= 40
        && !string.IsNullOrWhiteSpace(skin.Name) && skin.Name.Length <= 12
        && !string.IsNullOrWhiteSpace(skin.Description) && skin.Description.Length <= 24
        && (skin.Shape is "sprout" or "cat" or "robot" && skin.ImageFile is null
            || skin.Shape == "image" && IsLocalPngName(skin.ImageFile))
        && (skin.StateImages is null || skin.Shape == "image" && skin.StateImages.Count <= 5
            && skin.StateImages.All(pair => IsStateName(pair.Key) && IsLocalPngName(pair.Value)))
        && new[] { skin.Body, skin.Outline, skin.Accent, skin.Soft, skin.Cheek }
            .All(c => c is not null && Regex.IsMatch(c, "^#[0-9a-fA-F]{6}$"));

    internal static bool IsStateName(string name) => name is "Idle" or "Working" or "Done" or "Waiting" or "Error";

    private static bool IsLocalPngName(string? name) => name is not null
        && Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9._-]{0,79}\\.png$", RegexOptions.IgnoreCase)
        && !name.Contains("..");

    public static IReadOnlyList<Skin> Load(string directory, bool includeFallback = true, bool isCustom = false)
    {
        var skins = new List<Skin>();
        try
        {
            if (Directory.Exists(directory))
                foreach (string path in Directory.EnumerateFiles(directory, "*.json").OrderBy(p => p).Take(12))
                {
                    try
                    {
                        if (new FileInfo(path).Length > 16384) continue;
                        var definition = JsonSerializer.Deserialize<SkinDefinition>(File.ReadAllText(path), Options);
                        if (IsValid(definition) && skins.All(s => s.Id != definition!.Id))
                        {
                            BitmapSource? image = definition!.Shape == "image"
                                ? PngSkinImage.Load(Path.Combine(directory, definition.ImageFile!)) : null;
                            var states = new Dictionary<string, BitmapSource>();
                            if (definition.StateImages is not null)
                                foreach (var pair in definition.StateImages)
                                    try { states[pair.Key] = PngSkinImage.Load(Path.Combine(directory, pair.Value)); }
                                    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
                                        or NotSupportedException or FormatException or System.Runtime.InteropServices.COMException) { }
                            skins.Add(new Skin(definition, image, isCustom, states));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException
                        or ArgumentException or NotSupportedException or FormatException or System.Runtime.InteropServices.COMException) { }
                }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        if (includeFallback && skins.Count == 0) skins.Add(new Skin(Fallback));
        return skins;
    }
}
