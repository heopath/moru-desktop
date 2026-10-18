using System;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Moru;

internal sealed class UserSkinStore(string directory)
{
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
