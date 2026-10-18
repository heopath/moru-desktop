using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Moru;

internal sealed class UserSkinStore(string directory)
{
    internal void Delete(Skin skin)
    {
        if (!skin.IsCustom || !System.Text.RegularExpressions.Regex.IsMatch(skin.Id, "^user-[a-f0-9]{32}$"))
            throw new InvalidDataException("내가 추가한 PNG 친구만 삭제할 수 있어요.");
        string manifest = Path.Combine(directory, skin.Id + ".json");
        // Only app-owned copies are candidates. Never follow ImageFile paths supplied in a manifest.
        var owned = Directory.EnumerateFiles(directory, skin.Id + "*.png").Where(path =>
            System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path),
                "^" + skin.Id + "(?:-(?:Idle|Working|Done|Waiting|Error)-[a-f0-9]{32})?\\.png$")).ToArray();
        var referenced = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool canClean = true;
        foreach (string other in Directory.EnumerateFiles(directory, "*.json").Where(p => p != manifest))
        {
            try
            {
                if (new FileInfo(other).Length > 16384) { canClean = false; break; }
                var definition = JsonSerializer.Deserialize<SkinDefinition>(File.ReadAllText(other),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (definition?.ImageFile is { } file) referenced.Add(file);
                if (definition?.StateImages is { } states)
                    foreach (var fileName in states.Values) referenced.Add(fileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { canClean = false; break; }
        }
        File.Delete(manifest); // Commit removal before best-effort cleanup. Failure leaves the pet intact.
        if (canClean)
            foreach (string path in owned.Where(p => !referenced.Contains(Path.GetFileName(p)))) TryDelete(path);
    }

    internal Skin SetStateImage(Skin skin, PetActivity state, string? source)
    {
        if (!skin.IsCustom || !System.Text.RegularExpressions.Regex.IsMatch(skin.Id, "^user-[a-f0-9]{32}$")
            || !SkinCatalog.IsStateName(state.ToString()))
            throw new InvalidDataException("내가 추가한 PNG 친구의 상태를 선택해 주세요.");
        string manifest = Path.Combine(directory, skin.Id + ".json");
        if (!File.Exists(manifest)) throw new IOException("스킨 정보를 찾지 못했어요.");
        var files = skin.Definition.StateImages is { } previous ? new Dictionary<string, string>(previous) : new();
        var images = new Dictionary<string, BitmapSource>(skin.StateImages);
        string key = state.ToString();
        files.TryGetValue(key, out string? oldFile);
        string? newFile = null;
        string temporary = manifest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (source is null) { files.Remove(key); images.Remove(key); }
            else
            {
                var image = PngSkinImage.Load(source);
                newFile = skin.Id + "-" + key + "-" + Guid.NewGuid().ToString("N") + ".png";
                using (var output = new FileStream(Path.Combine(directory, newFile), FileMode.CreateNew, FileAccess.Write))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    encoder.Save(output);
                }
                files[key] = newFile;
                images[key] = image;
            }
            var definition = skin.Definition with { StateImages = files.Count > 0 ? files : null };
            File.WriteAllText(temporary, JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, manifest, overwrite: true);
            // Never delete base images, user originals, or files referenced by another state.
            if (oldFile is not null && oldFile.StartsWith(skin.Id + "-" + key + "-", StringComparison.Ordinal)
                && Path.GetFileName(oldFile) == oldFile && !files.ContainsValue(oldFile))
                TryDelete(Path.Combine(directory, oldFile));
            return new Skin(definition, skin.Image, isCustom: true, images);
        }
        catch
        {
            TryDelete(temporary);
            if (newFile is not null) TryDelete(Path.Combine(directory, newFile));
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    internal Skin Import(string source)
    {
        if (!string.Equals(Path.GetExtension(source), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PNG 이미지를 선택해 주세요.");
        var image = PngSkinImage.Load(source);
        string id = "user-" + Guid.NewGuid().ToString("N");
        string name = Path.GetFileNameWithoutExtension(source).Trim();
        if (name.Length > 12) name = name[..12];
        if (string.IsNullOrWhiteSpace(name)) name = "나의 친구";
        var definition = SkinCatalog.Fallback with
        {
            Id = id, Name = name, Description = "내가 추가한 PNG 친구", Shape = "image", ImageFile = id + ".png"
        };
        Directory.CreateDirectory(directory);
        string imagePath = Path.Combine(directory, definition.ImageFile);
        string jsonPath = Path.Combine(directory, id + ".json");
        try
        {
            // Store a small, normalized copy; the source image stays untouched.
            using (var output = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(output);
            }
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Only files created by this import have these randomly generated names.
            try { File.Delete(imagePath); File.Delete(jsonPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        return new Skin(definition, image, isCustom: true);
    }
}
