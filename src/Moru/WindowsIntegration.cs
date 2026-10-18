using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace Moru;

internal static class WindowsIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "Moru";
    internal static int InstallIntegrations(bool uninstall)
    {
        string executable = Path.Combine(AppContext.BaseDirectory, "Moru.exe");
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            string? command = key?.GetValue(EntryName) as string;
            if (uninstall)
            {
                if (command is not null && ClaudeBridge.CommandUsesExecutable(command, executable)) key!.DeleteValue(EntryName, false);
                string claude = Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), "settings.json");
                ClaudeBridge.Configure(claude, executable, "", enable: false, onlyThisExecutable: true);
            }
            else
            {
                // Preserve an existing user opt-in while moving from portable to installed.
                // A fresh installation never adds an autostart entry.
                if (command is not null && command.StartsWith("\"", StringComparison.Ordinal)
                    && command.EndsWith("Moru.exe\" --startup", StringComparison.OrdinalIgnoreCase))
                    SetStartup(true, executable);
                CreateDesktopShortcut(executable, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                // Installation must never rewrite preferences or user skins. In particular,
                // a transient read failure must not replace saved settings with defaults.
                // The normal first launch records its own shortcut marker.
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or COMException or System.Security.SecurityException or NotSupportedException or System.Text.Json.JsonException)
        { return 1; }
    }

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
        object linkObject = new ShellLink();
        try
        {
            var link = (IShellLinkW)linkObject;
            if (File.Exists(path) && !string.Equals(Path.GetFileName(ReadShortcut(path).Target), "Moru.exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("같은 이름의 다른 바로가기가 있어요. 이름을 바꾼 뒤 다시 만들어 주세요.");
            link.SetPath(executable);
            link.SetArguments("--show-panel");
            link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
            link.SetIconLocation(executable, 0);
            link.SetDescription("모루 · 바탕화면 친구, PC 상태와 알람");
            link.SetShowCmd(1);
            ((IPersistFile)linkObject).Save(path, true);
            return path;
        }
        finally { Marshal.FinalReleaseComObject(linkObject); }
    }

    internal static (string Target, string Arguments, string WorkingDirectory) ReadShortcut(string path)
    {
        object linkObject = new ShellLink();
        try
        {
            ((IPersistFile)linkObject).Load(path, 0);
            var link = (IShellLinkW)linkObject;
            var target = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            var directory = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4);
            link.GetArguments(arguments, arguments.Capacity);
            link.GetWorkingDirectory(directory, directory.Capacity);
            return (target.ToString(), arguments.ToString(), directory.ToString());
        }
        finally { Marshal.FinalReleaseComObject(linkObject); }
    }

    // Unicode shell links avoid WScript.Shell's locale-dependent path conversion.
    // https://learn.microsoft.com/windows/win32/shell/links
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
