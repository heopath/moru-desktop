using System;
using System.IO;
using System.Text.Json;

namespace Moru;

internal sealed class SettingsStore(string directory)
{
    private readonly string path = Path.Combine(directory, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public Preferences Load()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return new();
            return (JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new()).Normalize();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public bool Save(Preferences preferences)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences.Normalize(), Options));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
