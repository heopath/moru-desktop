using System;
using System.IO;
using System.Windows;

namespace Moru;

// Opt-in startup/event diagnostics; no polling, no logs in ordinary runs.
internal static class DiagnosticLog
{
    private static string? path;
    internal static void Enable(string directory)
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "diagnostics.log");
        Write("START " + Environment.ProcessId);
    }
    internal static void Write(string message)
    {
        if (path is null) return;
        try { File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    internal static void Window(string stage, Window window)
    {
        if (path is null) return;
        var r = WindowPlacement.Bounds(window);
        Write($"{stage} visible={window.IsVisible} bounds={r.Left},{r.Top},{r.Right},{r.Bottom} logical={window.Left},{window.Top},{window.ActualWidth},{window.ActualHeight}");
    }
}
