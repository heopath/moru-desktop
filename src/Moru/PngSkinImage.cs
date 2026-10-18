using System;
using System.Buffers.Binary;
using System.IO;
using System.Windows.Media.Imaging;

namespace Moru;

internal static class PngSkinImage
{
    internal const long MaxFileBytes = 8 * 1024 * 1024;
    internal const int MaxInputSide = 2048;
    internal const int DecodeSide = 320;

    // Inspect the bounded file and dimensions before invoking the WPF decoder.
    internal static BitmapSource Load(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is < 33 or > MaxFileBytes)
            throw new InvalidDataException("PNG 파일은 8 MiB 이하여야 해요.");
        Span<byte> header = stackalloc byte[24];
        input.ReadExactly(header);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!header[..8].SequenceEqual(signature) || BinaryPrimitives.ReadUInt32BigEndian(header[8..12]) != 13
            || !header[12..16].SequenceEqual("IHDR"u8))
            throw new InvalidDataException("올바른 PNG 이미지를 선택해 주세요.");
        uint width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        if (width is < 1 or > MaxInputSide || height is < 1 or > MaxInputSide)
            throw new InvalidDataException("이미지의 가로·세로는 각각 2048px 이하여야 해요.");
        input.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = input;
        if (Math.Max(width, height) > DecodeSide)
        {
            if (width >= height) bitmap.DecodePixelWidth = DecodeSide;
            else bitmap.DecodePixelHeight = DecodeSide;
        }
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
