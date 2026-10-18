using System;
using System.Windows;
using System.Windows.Media;

namespace Moru;

// Retained drawing; imported PNGs are decoded once and shared with the skin picker.
public sealed class PetView : FrameworkElement
{
    public static readonly DependencyProperty SkinProperty = DependencyProperty.Register(nameof(Skin),
        typeof(Skin), typeof(PetView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Skin? Skin { get => (Skin?)GetValue(SkinProperty); set => SetValue(SkinProperty, value); }
    private PetActivity activity;
    internal void SetActivity(PetActivity value) { if (activity == value) return; activity = value; InvalidateVisual(); }
    private static readonly Brush Shadow = Frozen("#150E2920");
    private static readonly Brush White = Frozen("#FFFDF8");
    private static readonly Brush Highlight = Frozen("#55FFFFFF");
    private static readonly Geometry SproutBody = Shape("M 31,102 C 24,69 41,43 78,42 C 114,40 135,67 130,100 C 129,120 111,131 81,131 C 52,132 34,121 31,102 Z");
    private static readonly Geometry Leaf = Shape("M 81,43 C 78,23 91,13 109,17 C 109,35 94,42 81,43 Z");
    private static readonly Geometry CatBody = Shape("M 31,98 L 29,34 Q 30,25 38,31 L 59,47 Q 79,40 101,47 L 122,31 Q 131,27 130,37 L 129,99 Q 128,130 80,131 Q 31,131 31,98 Z");
    private static readonly Geometry EarLeft = Shape("M 36,39 L 38,60 L 52,51 Z");
    private static readonly Geometry EarRight = Shape("M 123,39 L 121,60 L 107,51 Z");
    private static readonly Geometry Smile = Shape("M 72,95 Q 80,103 88,95");
    private static readonly Geometry CatSmile = Shape("M 70,95 Q 75,101 80,95 Q 85,101 90,95");

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Skin is not { } skin || ActualWidth <= 0 || ActualHeight <= 0) return;
        if (skin.ImageFor(activity) is { } image)
        {
            double imageScale = Math.Min(ActualWidth / image.PixelWidth, ActualHeight / image.PixelHeight);
            double width = image.PixelWidth * imageScale;
            double height = image.PixelHeight * imageScale;
            dc.DrawImage(image, new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height));
            return;
        }
        double scale = Math.Min(ActualWidth / 160, ActualHeight / 154);
        dc.PushTransform(new TranslateTransform((ActualWidth - 160 * scale) / 2, (ActualHeight - 154 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        var outline = new Pen(skin.Outline, 3) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        outline.Freeze();
        dc.DrawEllipse(Shadow, null, new Point(80, 141), 48, 6);
        dc.DrawEllipse(skin.Body, outline, new Point(58, 131), 14, 7);
        dc.DrawEllipse(skin.Body, outline, new Point(103, 131), 14, 7);
        if (skin.Shape == "robot")
        {
            dc.DrawLine(outline, new Point(80, 30), new Point(80, 44));
            dc.DrawEllipse(skin.Accent, outline, new Point(80, 23), 7, 7);
            dc.DrawRoundedRectangle(skin.Body, outline, new Rect(24, 73, 12, 25), 5, 5);
            dc.DrawRoundedRectangle(skin.Body, outline, new Rect(124, 73, 12, 25), 5, 5);
            dc.DrawRoundedRectangle(skin.Body, outline, new Rect(33, 44, 94, 86), 22, 22);
            dc.DrawRoundedRectangle(White, null, new Rect(44, 62, 72, 43), 13, 13);
            dc.DrawRoundedRectangle(skin.Accent, null, new Rect(69, 114, 23, 5), 2, 2);
        }
        else
        {
            dc.DrawGeometry(skin.Body, outline, skin.Shape == "cat" ? CatBody : SproutBody);
            if (skin.Shape == "cat")
            {
                dc.DrawGeometry(skin.Cheek, null, EarLeft);
                dc.DrawGeometry(skin.Cheek, null, EarRight);
                dc.DrawLine(outline, new Point(33, 91), new Point(22, 88));
                dc.DrawLine(outline, new Point(128, 91), new Point(139, 88));
            }
            else
            {
                dc.DrawGeometry(skin.Accent, outline, Leaf);
                dc.DrawLine(new Pen(skin.Body, 2), new Point(85, 35), new Point(101, 23));
                dc.DrawEllipse(Highlight, null, new Point(49, 67), 7, 10);
            }
        }
        dc.DrawEllipse(skin.Outline, null, new Point(60, 83), 3.5, 5);
        dc.DrawEllipse(skin.Outline, null, new Point(100, 83), 3.5, 5);
        dc.DrawEllipse(skin.Cheek, null, new Point(47, 96), 7, 3.5);
        dc.DrawEllipse(skin.Cheek, null, new Point(113, 96), 7, 3.5);
        dc.DrawGeometry(null, new Pen(skin.Outline, 2.5), skin.Shape == "cat" ? CatSmile : Smile);
        dc.Pop();
        dc.Pop();
    }

    private static Brush Frozen(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    private static Geometry Shape(string data) { var g = Geometry.Parse(data); g.Freeze(); return g; }
}
