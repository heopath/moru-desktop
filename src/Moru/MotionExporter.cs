using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Moru;

// Opt-in capture of the app's own WPF visual; no desktop/other windows are captured.
internal static class MotionExporter
{
    public static void Save(string path, PetWindow pet, Action complete)
    {
        int frame = 0;
        var encoder = new GifBitmapEncoder();
        var timer = new DispatcherTimer(DispatcherPriority.Background, pet.Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            if (frame % 22 == 0) pet.PreviewAnimation((frame / 22) switch { 0 => PetActivity.Idle, 1 => PetActivity.Working, _ => PetActivity.Done });
            int width = 320, height = 370;
            var petBitmap = new RenderTargetBitmap((int)pet.ActualWidth * 2, (int)pet.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32);
            petBitmap.Render(pet);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, width, height));
                dc.DrawImage(petBitmap, new Rect(0, 0, 320, 356));
                string label = frame < 22 ? "대기 동작" : frame < 44 ? "작업 중 동작" : "완료 동작";
                dc.DrawText(new FormattedText("모루 · " + label + " 미리보기", CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
                    new Typeface("Malgun Gothic"), 12, Brushes.DarkSlateGray, 1), new Point(48, 352));
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", (ushort)10);
            metadata.SetQuery("/grctlext/Disposal", (byte)2);
            encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
            if (++frame < 66) return;
            timer.Stop();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using var output = File.Create(path); encoder.Save(output);
            }
            finally { complete(); }
        };
        timer.Start();
    }
}
