using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Moru;

internal static class WindowPlacement
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Rect rectangle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    public static Rect Bounds(Window window)
    {
        GetWindowRect(new WindowInteropHelper(window).Handle, out var rect);
        return rect;
    }

    private static Rect WorkArea(Rect rectangle)
    {
        var monitor = MonitorFromRect(ref rectangle, 2);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(monitor, ref info)) return info.Work;
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        return new Rect { Left = screen.Left, Top = screen.Top, Right = screen.Right, Bottom = screen.Bottom };
    }

    public static void Restore(Window window, int? x, int? y)
    {
        var size = Bounds(window);
        var desired = new Rect { Left = x ?? 0, Top = y ?? 0, Right = (x ?? 0) + 1, Bottom = (y ?? 0) + 1 };
        var area = WorkArea(desired);
        Move(window, x ?? area.Right - (size.Right - size.Left) - 28,
            y ?? area.Bottom - (size.Bottom - size.Top) - 24);
        Clamp(window);
    }

    public static void Beside(Window panel, Window pet)
    {
        var petRect = Bounds(pet);
        var panelRect = Bounds(panel);
        var area = WorkArea(petRect);
        int width = panelRect.Right - panelRect.Left;
        int height = panelRect.Bottom - panelRect.Top;
        int left = petRect.Left - width - 12;
        if (left < area.Left) left = petRect.Right + 12;
        Move(panel, left, petRect.Bottom - height);
        Clamp(panel);
    }

    public static void Clamp(Window window)
    {
        var r = Bounds(window);
        var work = WorkArea(r);
        int x = Math.Clamp(r.Left, work.Left, Math.Max(work.Left, work.Right - (r.Right - r.Left)));
        int y = Math.Clamp(r.Top, work.Top, Math.Max(work.Top, work.Bottom - (r.Bottom - r.Top)));
        if (x != r.Left || y != r.Top) Move(window, x, y);
    }

    private static void Move(Window window, int x, int y) =>
        SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
}
