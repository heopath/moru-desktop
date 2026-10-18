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
    internal static void Save(string path, Window panel, Window pet, Skin skin, bool ai = false)
    {
        panel.UpdateLayout();
        pet.UpdateLayout();
        var panelImage = Capture(panel);
        var petImage = Capture(pet);
        double width = panel.ActualWidth + 440;
        double side = panel.ActualWidth + 65;
        var ink = (Brush)Application.Current.Resources["Ink"];
        var accent = (Brush)Application.Current.Resources["Accent"];
        double height = Math.Max(820, panel.ActualHeight + 130);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((Brush)Application.Current.Resources["Canvas"], null, new Rect(0, 0, width, height));
            DrawText(dc, "MORU", 32, 26, 25, accent);
            DrawText(dc, "내 PC 곁의 작은 친구", 139, 32, 17, ink);
            dc.DrawImage(panelImage, new Rect(32, 88, panel.ActualWidth, panel.ActualHeight));
            dc.DrawImage(petImage, new Rect(side + 24, 190, pet.ActualWidth * 1.45, pet.ActualHeight * 1.45));
            DrawText(dc, skin.Name + " 스킨 적용", side + 10, 514, 20, ink);
            DrawText(dc, "큰 글자 · 다크 모드 · 호버 요약", side, 562, 15, ink);
            DrawText(dc, "PC 상태 · AI 작업 · 알람 · 볼륨", side, 595, 15, ink);
            DrawText(dc, "Windows · C# / WPF · v0.4.0", side, 640, 13, accent);
            DrawText(dc, ai ? "실제 AI 작업 화면 · 데이터가 없으면 미수신으로 표시" : "실제 앱 화면 · 수치는 PC 전체 사용량", 32, height - 30, 11, ink);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * 1.5), (int)Math.Ceiling(height * 1.5), 144, 144, PixelFormats.Pbgra32);
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
