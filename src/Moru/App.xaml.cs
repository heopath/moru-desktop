using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Moru;

public partial class App : Application
{
    private Mutex? instance;
    private bool ownsMutex;
    private SettingsStore store = null!;
    private UserSkinStore userSkins = null!;
    private PetWindow pet = null!;
    private StatusWindow? panel;
    private AiWindow? aiPanel;
    private SettingsWindow? settingsPanel;
    private AlarmWindow? alarmPanel;
    private ReminderService? reminders;
    private readonly AlarmTone alarmTone = new();
    private Forms.NotifyIcon? tray;
    private Icon? trayIcon;
    private Forms.ToolStripMenuItem? visibilityItem;
    private bool sessionLocked;
    private bool suspended;
    internal Preferences Preferences { get; private set; } = new();
    internal IReadOnlyList<Skin> Skins { get; private set; } = Array.Empty<Skin>();
    internal Skin CurrentSkin { get; private set; } = null!;
    internal bool Exiting { get; private set; }
    internal AiMonitor Ai { get; private set; } = null!;
    internal string ClaudeNotice { get; private set; } = "Claude Code의 새 세션부터 상태를 받아요.";
    internal bool IntegrationEnabled { get; private set; }
    private static string Executable => Path.Combine(AppContext.BaseDirectory, "Moru.exe");
    internal bool StartupEnabled => IntegrationEnabled && WindowsIntegration.StartupEnabled();
    internal void ChangeStartup(bool enabled)
    {
        if (!IntegrationEnabled) throw new InvalidOperationException("테스트 프로필에서는 자동 실행을 바꾸지 않아요.");
        WindowsIntegration.SetStartup(enabled, Executable);
    }
    internal string CreateDesktopShortcut()
    {
        if (!IntegrationEnabled) throw new InvalidOperationException("테스트 프로필에서는 바탕화면을 바꾸지 않아요.");
        string path = WindowsIntegration.CreateDesktopShortcut(Executable, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Preferences = Preferences with { DesktopShortcutCreated = true };
        Save();
        return path;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Moru");
        int dataIndex = Array.IndexOf(e.Args, "--data-dir");
        IntegrationEnabled = dataIndex < 0;
        if (dataIndex >= 0 && dataIndex + 1 < e.Args.Length) directory = Path.GetFullPath(e.Args[dataIndex + 1]);
        if (e.Args.Contains("--diagnostics")) DiagnosticLog.Enable(directory);
        DispatcherUnhandledException += (_, args) => DiagnosticLog.Write("ERROR " + args.Exception);
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())))[..16];
        instance = new Mutex(true, @"Local\Moru.DesktopCompanion." + suffix, out ownsMutex);
        if (!ownsMutex)
        {
            if (!e.Args.Contains("--startup")) MessageBox.Show("모루가 이미 실행 중이에요. 작업 표시줄의 트레이 아이콘에서 열어 주세요.", "모루", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        store = new SettingsStore(directory);
        string userSkinDirectory = Path.Combine(directory, "UserSkins");
        userSkins = new UserSkinStore(userSkinDirectory);
        DiagnosticLog.Write("Load preferences");
        Preferences = store.Load();
        if (IntegrationEnabled && !Preferences.DesktopShortcutCreated)
        {
            try { CreateDesktopShortcut(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
                or COMException or System.Security.SecurityException or NotSupportedException)
            { DiagnosticLog.Write("Desktop shortcut unavailable: " + ex.GetType().Name); }
        }
        Ai = new AiMonitor(Dispatcher, suffix);
        Skins = SkinCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Skins"))
            .Concat(SkinCatalog.Load(userSkinDirectory, includeFallback: false, isCustom: true))
            .GroupBy(s => s.Id).Select(group => group.First()).Take(12).ToArray();
        CurrentSkin = Skins.FirstOrDefault(s => s.Id == Preferences.SkinId) ?? Skins[0];
        Theme.Apply(this, Preferences, CurrentSkin);
        DiagnosticLog.Write("Create pet window");
        pet = new PetWindow(this) { Topmost = Preferences.AlwaysOnTop, ShowInTaskbar = e.Args.Contains("--inspect-pet") };
        MainWindow = pet;
        pet.ApplySkin(CurrentSkin);
        DiagnosticLog.Write("Create tray");
        CreateTray();
        var icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(trayIcon!.Handle,
            Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        icon.Freeze();
        pet.Icon = icon;
        DiagnosticLog.Write("Register system events");
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.PowerModeChanged += OnPowerChanged;
        SystemEvents.SessionSwitch += OnSessionChanged;
        pet.ContentRendered += (_, _) => DiagnosticLog.Window("Pet rendered", pet);
        DiagnosticLog.Write("Show pet");
        pet.Show();
        Ai.Changed += () => { pet.SetActivity(Ai.Activity.State); panel?.RefreshAi(); aiPanel?.Refresh(); settingsPanel?.RefreshConnections(); };
        Ai.TaskCompleted += () => pet.PreviewAnimation(PetActivity.Done);
        Ai.SetCodex(Preferences.CodexEnabled);
        Ai.SetClaude(Preferences.ClaudeEnabled);
        reminders = new ReminderService(Dispatcher, () => Preferences.Reminders, DeliverReminders);
        reminders.Schedule();
        if (e.Args.Contains("--enable-codex")) ChangeCodex(true);
        if (e.Args.Contains("--enable-claude")) ChangeClaude(true);
        if (e.Args.Contains("--show-ai")) Dispatcher.BeginInvoke(ShowAi);
        if (e.Args.Contains("--show-settings")) Dispatcher.BeginInvoke(() => ShowSettings());
        if (e.Args.Contains("--show-alarms")) Dispatcher.BeginInvoke(() => ShowSettings(3));
        DiagnosticLog.Window("Pet shown", pet);
        string? importPath = Option(e.Args, "--import-png");
        string? previewPath = Option(e.Args, "--export-preview");
        string? motionPath = Option(e.Args, "--export-motion");
        string? aiPreviewPath = Option(e.Args, "--export-ai-preview");
        string? alarmsPreviewPath = Option(e.Args, "--export-alarms-preview");
        if (importPath is not null)
        {
            try { ImportSkin(Path.GetFullPath(importPath)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException or FormatException or System.Runtime.InteropServices.COMException)
            {
                MessageBox.Show("PNG 친구를 추가하지 못했어요. 파일 형식·크기와 저장 폴더를 확인해 주세요.", "모루");
            }
        }
        string? stateFolder = Option(e.Args, "--import-states");
        if (stateFolder is not null && CurrentSkin.IsCustom)
        {
            try
            {
                foreach (var state in new[] { PetActivity.Idle, PetActivity.Working, PetActivity.Done, PetActivity.Waiting, PetActivity.Error })
                {
                    string path = Path.Combine(Path.GetFullPath(stateFolder), state.ToString().ToLowerInvariant() + ".png");
                    if (File.Exists(path)) SetStateImage(state, path);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException or FormatException or COMException)
            { MessageBox.Show("상태 이미지를 추가하지 못했어요. 설정에서 다시 선택해 주세요.", "모루"); }
        }
        if (motionPath is not null) Dispatcher.BeginInvoke(() => MotionExporter.Save(motionPath, pet,
            () => { if (e.Args.Contains("--exit-after-export")) ExitApp(); }));
        if (aiPreviewPath is not null) Dispatcher.BeginInvoke(() =>
        {
            ShowAi();
            var capture = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            capture.Tick += (_, _) =>
            {
                capture.Stop();
                try { PreviewExporter.Save(aiPreviewPath, aiPanel!, pet, CurrentSkin, true); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                { MessageBox.Show("AI 소개 화면을 저장하지 못했어요.", "모루"); }
                if (e.Args.Contains("--exit-after-export")) ExitApp();
            };
            capture.Start();
        });
        if (alarmsPreviewPath is not null) Dispatcher.BeginInvoke(() =>
        {
            ShowSettings(3);
            var capture = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            capture.Tick += (_, _) =>
            {
                capture.Stop();
                try { PreviewExporter.Save(alarmsPreviewPath, settingsPanel!, pet, CurrentSkin, description: "실제 앱 화면 · 분 단위 타이머와 알람 설정"); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                { MessageBox.Show("알람 소개 화면을 저장하지 못했어요.", "모루"); }
                if (e.Args.Contains("--exit-after-export")) ExitApp();
            };
            capture.Start();
        });
        if (e.Args.Contains("--show-panel") || previewPath is not null) Dispatcher.BeginInvoke(() =>
        {
            ShowPanel();
            if (previewPath is null) return;
            // One-shot export after the first CPU interval; no extra timer in ordinary runs.
            var exportTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            exportTimer.Tick += (_, _) =>
            {
                exportTimer.Stop();
                try { ExportPreview(Path.GetFullPath(previewPath)); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
                    or NotSupportedException or System.Runtime.InteropServices.COMException)
                {
                    MessageBox.Show("소개 화면을 저장하지 못했어요. 저장 경로를 확인해 주세요.", "모루");
                }
                if (e.Args.Contains("--exit-after-export")) ExitApp();
            };
            exportTimer.Start();
        });
    }

    private static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    internal void ShowPanel()
    {
        DiagnosticLog.Write("Show panel requested");
        if (!pet.IsVisible) TogglePet();
        aiPanel?.Hide(); settingsPanel?.Hide(); pet.CloseHover();
        panel ??= new StatusWindow(this) { Owner = pet, Icon = pet.Icon, Topmost = Preferences.AlwaysOnTop };
        panel.Show();
        WindowPlacement.Beside(panel, pet);
        panel.SetPaused(sessionLocked || suspended);
        panel.Activate();
        DiagnosticLog.Window("Panel shown", panel);
    }
    internal void HidePanel() => panel?.Hide();
    internal void ShowAi()
    {
        HidePanel(); settingsPanel?.Hide(); pet.CloseHover();
        aiPanel ??= new AiWindow(this) { Owner = pet, Icon = pet.Icon, Topmost = Preferences.AlwaysOnTop };
        aiPanel.Show(); WindowPlacement.Beside(aiPanel, pet); aiPanel.Refresh(); aiPanel.Activate();
    }
    internal void ShowSettings(int tab = 0)
    {
        HidePanel(); aiPanel?.Hide(); pet.CloseHover();
        settingsPanel ??= new SettingsWindow(this) { Owner = pet, Icon = pet.Icon, Topmost = Preferences.AlwaysOnTop };
        settingsPanel.SelectTab(tab); settingsPanel.Show(); WindowPlacement.Beside(settingsPanel, pet); settingsPanel.Activate();
    }
    internal void PositionPanel() { if (panel is not null) WindowPlacement.Beside(panel, pet); }
    internal void TogglePanel() { if (panel?.IsVisible == true) HidePanel(); else ShowPanel(); }
    internal void TogglePet()
    {
        if (pet.IsVisible) { HidePanel(); aiPanel?.Hide(); settingsPanel?.Hide(); pet.CloseHover(); pet.Hide(); }
        else { pet.Show(); WindowPlacement.Clamp(pet); }
        if (visibilityItem is not null) visibilityItem.Text = pet.IsVisible ? "캐릭터 숨기기" : "캐릭터 보이기";
    }
    internal void ResetPosition()
    {
        if (!pet.IsVisible) TogglePet();
        HidePanel();
        WindowPlacement.Restore(pet, null, null);
        SavePosition();
    }
    internal void SavePosition()
    {
        var r = WindowPlacement.Bounds(pet);
        Preferences = Preferences with { X = r.Left, Y = r.Top };
        Save();
    }
    internal void ChangeSkin(Skin skin)
    {
        CurrentSkin = skin;
        Preferences = Preferences with { SkinId = skin.Id };
        Theme.Apply(this, Preferences, skin);
        pet.ApplySkin(skin);
        DiagnosticLog.Write("Skin selected " + skin.Id);
        Save();
    }
    internal void ImportSkin(string path)
    {
        if (Skins.Count >= 12) throw new InvalidDataException("친구는 기본 스킨을 포함해 최대 12명까지 추가할 수 있어요.");
        var skin = userSkins.Import(path);
        Skins = Skins.Append(skin).ToArray();
        ChangeSkin(skin);
        settingsPanel?.ReloadSkins();
    }
    internal void SetStateImage(PetActivity state, string? source)
    {
        var updated = userSkins.SetStateImage(CurrentSkin, state, source);
        Skins = Skins.Select(s => s.Id == updated.Id ? updated : s).ToArray();
        ChangeSkin(updated);
        settingsPanel?.ReloadSkins();
    }
    internal void ExportPreview(string path)
    {
        if (panel is null || !panel.IsVisible) return;
        PreviewExporter.Save(path, panel, pet, CurrentSkin);
    }
    internal void ChangeTopmost(bool topmost)
    {
        Preferences = Preferences with { AlwaysOnTop = topmost };
        pet.Topmost = topmost;
        if (panel is not null) panel.Topmost = topmost;
        if (aiPanel is not null) aiPanel.Topmost = topmost;
        if (settingsPanel is not null) settingsPanel.Topmost = topmost;
        Save();
    }
    internal void ChangeInterval(int seconds) { Preferences = Preferences with { RefreshSeconds = seconds }; panel?.ApplyPreferences(); pet.ApplyPreferences(); Save(); }
    internal void ChangeAppearance(bool dark, int textSize)
    {
        Preferences = (Preferences with { DarkMode = dark, TextSize = textSize }).Normalize();
        Theme.Apply(this, Preferences, CurrentSkin); panel?.ApplyPreferences(); Save();
    }
    internal void ChangeHover(bool enabled) { Preferences = Preferences with { HoverEnabled = enabled }; pet.ApplyPreferences(); Save(); }
    internal void ChangeAlarmSound(bool enabled) { Preferences = Preferences with { AlarmSound = enabled }; if (!enabled) StopAlarmSound(); Save(); }
    internal void ChangeRepeatTimer(bool enabled) { Preferences = Preferences with { RepeatTimer = enabled }; Save(); }
    internal bool PlayAlarmSound() => alarmTone.Play();
    internal void StopAlarmSound() => alarmTone.Stop();
    internal bool AddReminder(Reminder reminder)
    {
        if (Preferences.Reminders.Count >= 8) return false;
        Preferences = (Preferences with { Reminders = Preferences.Reminders.Append(reminder).ToArray() }).Normalize();
        RefreshReminders(); return true;
    }
    internal void RemoveReminder(Guid id)
    {
        Preferences = Preferences with { Reminders = Preferences.Reminders.Where(r => r.Id != id).ToArray() };
        alarmPanel?.Remove(id);
        RefreshReminders();
    }
    private void RefreshReminders()
    {
        Save(); reminders?.Schedule(); settingsPanel?.RefreshAlarms(); panel?.RefreshClock(); pet.RefreshReminderHover();
    }
    internal bool SnoozeReminders(IEnumerable<Reminder> displayed)
    {
        if (!ReminderRules.TrySnooze(Preferences.Reminders, displayed, DateTimeOffset.Now, out var updated)) return false;
        Preferences = Preferences with { Reminders = updated };
        RefreshReminders(); return true;
    }
    private void DeliverReminders(Reminder[] due, DateTimeOffset now)
    {
        // Use the same cutoff as the delivered set, even if the wall clock changes during this callback.
        Preferences = Preferences with { Reminders = ReminderRules.AfterDelivery(Preferences.Reminders, now) };
        RefreshReminders();
        alarmPanel ??= new AlarmWindow(this) { Owner = pet, Icon = pet.Icon };
        alarmPanel.Deliver(due); WindowPlacement.Beside(alarmPanel, pet);
        if (Preferences.AlarmSound) PlayAlarmSound();
    }
    internal void ChangeAnimation(bool enabled)
    {
        Preferences = Preferences with { AnimationEnabled = enabled }; pet.SetAnimation(enabled); Save();
    }
    internal void PreviewAnimation(PetActivity value) => pet.PreviewAnimation(value);
    internal void ChangeCodex(bool enabled)
    {
        Preferences = Preferences with { CodexEnabled = enabled }; Ai.SetCodex(enabled); panel?.RefreshAi(); Save();
    }
    internal bool ChangeClaude(bool enabled)
    {
        try
        {
            string folder = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            ClaudeNotice = ClaudeBridge.Configure(Path.Combine(folder, "settings.json"), Environment.ProcessPath!, Ai.PipeName, enabled);
            Preferences = Preferences with { ClaudeEnabled = enabled }; Ai.SetClaude(enabled); Save(); panel?.RefreshAi();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            ClaudeNotice = ex is NotSupportedException ? ex.Message : "연결 설정을 저장하지 못했어요. Claude 설정 파일 형식과 권한을 확인해 주세요.";
            panel?.RefreshAi(); aiPanel?.Refresh(); return false;
        }
    }
    private void Save()
    {
        bool saved = store.Save(Preferences);
        panel?.ShowSaveResult(saved);
        settingsPanel?.ShowSaveResult(saved);
    }

    private void CreateTray()
    {
        trayIcon = MakeIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("대시보드 열기", null, (_, _) => Dispatcher.Invoke(ShowPanel));
        menu.Items.Add("설정", null, (_, _) => Dispatcher.Invoke(() => ShowSettings()));
        menu.Items.Add("AI 작업", null, (_, _) => Dispatcher.Invoke(ShowAi));
        visibilityItem = new Forms.ToolStripMenuItem("캐릭터 숨기기", null, (_, _) => Dispatcher.Invoke(TogglePet));
        menu.Items.Add(visibilityItem);
        menu.Items.Add("위치 초기화", null, (_, _) => Dispatcher.Invoke(ResetPosition));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("모루 종료", null, (_, _) => Dispatcher.Invoke(ExitApp));
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "모루 · 내 PC 곁의 작은 친구", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ShowPanel); };
    }

    private static Icon MakeIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var body = new SolidBrush(Color.FromArgb(190, 231, 211));
        using var ink = new SolidBrush(Color.FromArgb(36, 75, 64));
        using var pen = new Pen(ink, 1.5f);
        graphics.FillEllipse(body, 3, 7, 26, 23);
        graphics.DrawEllipse(pen, 3, 7, 26, 23);
        graphics.FillEllipse(ink, 17, 1, 10, 7);
        graphics.FillEllipse(ink, 10, 16, 3, 4);
        graphics.FillEllipse(ink, 20, 16, 3, 4);
        graphics.DrawArc(pen, 14, 20, 6, 4, 0, 180);
        IntPtr handle = bitmap.GetHicon();
        using var temporary = Icon.FromHandle(handle);
        var result = (Icon)temporary.Clone();
        DestroyIcon(handle);
        return result;
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        HidePanel();
        if (pet.IsVisible) WindowPlacement.Clamp(pet);
        if (aiPanel?.IsVisible == true) WindowPlacement.Clamp(aiPanel);
        if (settingsPanel?.IsVisible == true) WindowPlacement.Clamp(settingsPanel);
        if (alarmPanel?.IsVisible == true) WindowPlacement.Clamp(alarmPanel);
    });
    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        suspended = e.Mode == PowerModes.Suspend || (suspended && e.Mode != PowerModes.Resume);
        panel?.SetPaused(sessionLocked || suspended);
        pet.SetPaused(sessionLocked || suspended);
        if (sessionLocked || suspended) reminders?.Pause(); else reminders?.Check();
    });
    private void OnSessionChanged(object sender, SessionSwitchEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (e.Reason == SessionSwitchReason.SessionLock) sessionLocked = true;
        if (e.Reason == SessionSwitchReason.SessionUnlock) sessionLocked = false;
        panel?.SetPaused(sessionLocked || suspended);
        pet.SetPaused(sessionLocked || suspended);
        if (sessionLocked || suspended) reminders?.Pause(); else reminders?.Check();
    });
    internal void ExitApp()
    {
        if (Exiting) return;
        Exiting = true;
        SavePosition();
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        Exiting = true;
        Ai?.Dispose();
        reminders?.Dispose();
        alarmTone.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        SystemEvents.SessionSwitch -= OnSessionChanged;
        if (tray is not null)
        {
            tray.Visible = false;
            tray.ContextMenuStrip?.Dispose();
            tray.Dispose();
        }
        trayIcon?.Dispose();
        if (ownsMutex) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
