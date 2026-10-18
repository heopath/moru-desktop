using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Moru;

internal static class WindowsIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "Moru";

    // Registry is the source of truth; loading preferences never enables startup.
    internal static bool StartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(EntryName) is string command && !string.IsNullOrWhiteSpace(command);
    }

    internal static string StartupCommand(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.IndexOfAny(['"', '\r', '\n']) >= 0)
            throw new ArgumentException("실행 파일 경로를 확인해 주세요.");
        string command = "\"" + executable + "\" --startup";
        if (command.Length > 260) throw new IOException("모루 폴더를 더 짧은 경로로 옮긴 뒤 다시 켜 주세요.");
        return command;
    }

    internal static void SetStartup(bool enabled, string executable)
    {
        if (enabled)
        {
            string command = StartupCommand(executable);
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(EntryName, command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(EntryName, throwOnMissingValue: false);
        }
    }

    internal static string CreateDesktopShortcut(string executable, string desktop)
    {
        if (!File.Exists(executable) || string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
            throw new IOException("실행 파일 또는 바탕화면 폴더를 찾지 못했어요.");
        string path = Path.Combine(desktop, "모루 실행.lnk");
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!);
            shortcut = ((dynamic)shell!).CreateShortcut(path);
            dynamic link = shortcut;
            if (File.Exists(path) && !string.Equals(Path.GetFileName((string)link.TargetPath), "Moru.exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("같은 이름의 다른 바로가기가 있어요. 이름을 바꾼 뒤 다시 만들어 주세요.");
            link.TargetPath = executable;
            link.Arguments = "--show-panel";
            link.WorkingDirectory = Path.GetDirectoryName(executable)!;
            link.IconLocation = executable + ",0";
            link.Description = "모루 · 바탕화면 친구, PC 상태와 알람";
            link.WindowStyle = 1;
            link.Save();
            return path;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}
