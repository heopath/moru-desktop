using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Moru;

internal static class PreviewExporter
{
    // Render only our WPF visuals. The user's desktop and other apps never enter this image.
    internal static void Save(string path, Window panel, Window pet, Skin skin)
    {
        panel.UpdateLayout();
        pet.UpdateLayout();
        var panelImage = Capture(panel);
        var petImage = Capture(pet);
        double height = Math.Max(820, panel.ActualHeight + 130);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(239, 245, 240)), null, new Rect(0, 0, 820, height));
            DrawText(dc, "MORU", 32, 26, 25, skin.Accent);
            DrawText(dc, "내 PC 곁의 작은 친구", 139, 32, 17, skin.Outline);
            dc.DrawImage(panelImage, new Rect(32, 88, panel.ActualWidth, panel.ActualHeight));
            dc.DrawImage(petImage, new Rect(490, 236, pet.ActualWidth * 1.45, pet.ActualHeight * 1.45));
            DrawText(dc, skin.Name + " 스킨 적용", 468, 498, 20, skin.Outline);
            DrawText(dc, "캐릭터 클릭 → CPU·메모리 확인", 446, 542, 15, skin.Outline);
            DrawText(dc, "PNG 한 장으로 나만의 친구 추가", 446, 571, 15, skin.Outline);
            DrawText(dc, "Windows · C# / WPF · v0.2.0", 446, 620, 13, skin.Accent);
            DrawText(dc, "실행 중인 앱 화면 · 수치는 PC 전체 사용량", 32, height - 30, 11, skin.Outline);
        }
        var bitmap = new RenderTargetBitmap(1230, (int)Math.Ceiling(height * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static BitmapSource Capture(Window window)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 1.5),
            (int)Math.Ceiling(window.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(window);
        bitmap.Freeze();
        return bitmap;
    }
    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush) =>
        dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
            new Typeface("Malgun Gothic"), size, brush, 1.5), new Point(x, y));
}
