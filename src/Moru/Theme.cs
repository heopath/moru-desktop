using System.Windows;
using System.Windows.Media;

namespace Moru;

internal static class Theme
{
    internal static void Apply(Application app, Preferences preferences, Skin skin)
    {
        bool dark = preferences.DarkMode;
        Set("Canvas", dark ? "#151B24" : "#F5F7FA");
        Set("Surface", dark ? "#202936" : "#FFFFFF");
        Set("Raised", dark ? "#2A3544" : "#EDF1F5");
        Set("Line", dark ? "#364354" : "#DCE3EA");
        Set("Ink", dark ? "#F3F6FA" : "#202D3B");
        Set("Muted", dark ? "#B4C0D0" : "#556577");
        Set("Danger", dark ? "#FFB6B6" : "#B33042");
        app.Resources["Accent"] = dark ? Brush("#91DCC4") : skin.Accent;
        app.Resources["Soft"] = dark ? Brush("#253E3A") : skin.Soft;
        double scale = preferences.TextSize / 16d;
        app.Resources["BodySize"] = (double)preferences.TextSize;
        app.Resources["SmallSize"] = 14d * scale;
        app.Resources["TitleSize"] = 24d * scale;
        app.Resources["MetricSize"] = 36d * scale;
        app.Resources["ClockSize"] = 32d * scale;
        void Set(string key, string color) => app.Resources[key] = Brush(color);
    }
    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze(); return brush;
    }
}
