using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Globalization;
using System.Linq;

namespace Moru;

public partial class PetWindow : Window
{
    private readonly App host;
    private Point? pointerDown;
    private bool dragged;
    private readonly PetAnimator animator;
    private PetActivity activity;
    private readonly SystemMetrics hoverMetrics = new();
    private readonly DispatcherTimer hoverTimer;
    private readonly DispatcherTimer posePreviewTimer;
    private bool paused;
    private long nextHoverSample;

    internal PetWindow(App app)
    {
        host = app;
        InitializeComponent();
        hoverTimer = new(DispatcherPriority.Background, Dispatcher);
        hoverTimer.Tick += (_, _) => RefreshHover();
        posePreviewTimer = new(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(5) };
        posePreviewTimer.Tick += (_, _) => { posePreviewTimer.Stop(); Character.SetActivity(activity); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseHover(); };
        ApplyPreferences();
        animator = new PetAnimator(Character);
        animator.SetEnabled(host.Preferences.AnimationEnabled);
        Closed += (_, _) => { animator.Dispose(); hoverTimer.Stop(); posePreviewTimer.Stop(); };
        var menu = new ContextMenu();
        AddMenu(menu, "대시보드 열기", () => host.ShowPanel());
        AddMenu(menu, "설정", () => host.ShowSettings());
        AddMenu(menu, "AI 작업", () => host.ShowAi());
        AddMenu(menu, "캐릭터 숨기기", () => host.TogglePet());
        AddMenu(menu, "위치 초기화", () => host.ResetPosition());
        menu.Items.Add(new Separator());
        AddMenu(menu, "모루 종료", () => host.ExitApp());
        PetButton.ContextMenu = menu;
        SourceInitialized += (_, _) => WindowPlacement.Restore(this, host.Preferences.X, host.Preferences.Y);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => WindowPlacement.Clamp(this));
        Closing += (_, args) =>
        {
            if (host.Exiting) return;
            args.Cancel = true;
            Dispatcher.BeginInvoke(host.ExitApp);
        };
    }

    internal void ApplySkin(Skin skin)
    {
        Character.Skin = skin;
        UpdateLabel();
    }
    internal void SetActivity(PetActivity value)
    {
        activity = value; UpdateLabel(); animator.SetState(value);
        if (!posePreviewTimer.IsEnabled) Character.SetActivity(value);
    }
    private void UpdateLabel() => NameLabel.Text = activity == PetActivity.Idle ? host.CurrentSkin.Name : AiActivity.Label(activity);
    internal void SetAnimation(bool enabled) => animator.SetEnabled(enabled);
    internal void SetPaused(bool value) { paused = value; if (value) CloseHover(); animator.SetPaused(value); }
    internal void ApplyPreferences()
    {
        ToolTipService.SetIsEnabled(PetButton, host.Preferences.HoverEnabled);
        UpdateHoverInterval();
        nextHoverSample = 0;
        if (!host.Preferences.HoverEnabled) CloseHover();
    }
    internal void CloseHover() { HoverTip.IsOpen = false; hoverTimer.Stop(); hoverMetrics.Reset(); }
    private void OnHoverOpened(object sender, RoutedEventArgs e)
    {
        if (paused || !host.Preferences.HoverEnabled || !IsVisible) { CloseHover(); return; }
        hoverMetrics.Reset(); nextHoverSample = 0; UpdateHoverInterval(); RefreshHover(); hoverTimer.Start();
    }
    private void OnHoverClosed(object sender, RoutedEventArgs e) { hoverTimer.Stop(); hoverMetrics.Reset(); }
    private void UpdateHoverInterval() => hoverTimer.Interval = TimeSpan.FromSeconds(host.Preferences.Reminders.Count > 0 ? 1 : host.Preferences.RefreshSeconds);
    internal void RefreshReminderHover()
    {
        UpdateHoverInterval();
        if (HoverTip.IsOpen) RefreshHover();
    }
    private void RefreshHover()
    {
        if (!HoverTip.IsOpen || paused) { hoverTimer.Stop(); return; }
        // The countdown ticks once a second only while visible. Keep metric sampling at the user's interval.
        if (Environment.TickCount64 >= nextHoverSample)
        {
            var sample = hoverMetrics.Read();
            string cpu = sample.CpuPercent is double value ? $"{value:0}%" : "측정 중";
            string ram = sample.TotalMemory > 0 ? $"{sample.MemoryPercent:0}%" : "—";
            HoverMetrics.Text = $"CPU {cpu}  ·  메모리 {ram}";
            nextHoverSample = Environment.TickCount64 + host.Preferences.RefreshSeconds * 1000L;
        }
        HoverTime.Text = DateTime.Now.ToString("HH:mm");
        HoverDate.Text = DateTime.Now.ToString("M월 d일 dddd", CultureInfo.GetCultureInfo("ko-KR"));
        var next = host.Preferences.Reminders.MinBy(r => r.DueAt);
        HoverAlarm.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
        if (next is not null)
        {
            HoverRemaining.Text = "다음 알람 · " + ReminderRules.Remaining(next, DateTimeOffset.Now);
            HoverAlarmDetail.Text = $"{next.Title} · {next.RepeatLabel}" + (host.Preferences.Reminders.Count > 1 ? $" · 외 {host.Preferences.Reminders.Count - 1}개" : "");
        }
        HoverAi.Text = host.Preferences.CodexEnabled || host.Preferences.ClaudeEnabled ? "AI · " + AiActivity.Label(host.Ai.Activity.State) : "AI 연결 꺼짐";
    }
    internal void PreviewAnimation(PetActivity value)
    {
        posePreviewTimer.Stop();
        Character.SetActivity(value);
        animator.Preview(value);
        posePreviewTimer.Start();
    }

    private static void AddMenu(ContextMenu menu, string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void OnPetClick(object sender, RoutedEventArgs e)
    {
        CloseHover();
        pointerDown = null;
        if (!dragged) { PreviewAnimation(PetActivity.Done); host.TogglePanel(); }
        dragged = false;
    }
    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        pointerDown = e.GetPosition(this);
        dragged = false;
    }
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (pointerDown is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        pointerDown = null;
        dragged = true;
        CloseHover();
        Mouse.Capture(null);
        host.HidePanel();
        animator.SetPaused(true);
        try { DragMove(); } catch (InvalidOperationException) { }
        animator.SetPaused(false);
        WindowPlacement.Clamp(this);
        host.SavePosition();
        e.Handled = true;
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) host.HidePanel();
    }
}
