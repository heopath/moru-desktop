using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Moru;

public partial class StatusWindow : Window
{
    private readonly App host;
    private readonly SystemMetrics metrics = new();
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer volumeTimer;
    private bool ready, paused, updatingAudio, muted;
    private string audioTarget = "master";
    private string? pendingTarget;
    private double pendingVolume;
    private DateTimeOffset lastAudioRefresh;

    internal StatusWindow(App app)
    {
        host = app; InitializeComponent();
        timer = new(DispatcherPriority.Background, Dispatcher);
        timer.Tick += (_, _) => RefreshMetrics();
        volumeTimer = new(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
        volumeTimer.Tick += (_, _) => FlushVolume();
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 24);
        Height = Math.Min(MaxHeight, 740);
        ready = true;
        IsVisibleChanged += (_, _) => UpdateSampling();
        StateChanged += (_, _) => UpdateSampling();
        Closing += OnClosing;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => WindowPlacement.Clamp(this));
        RefreshAi(); RefreshClock();
    }
    internal void SetPaused(bool value) { paused = value; UpdateSampling(); }
    internal void ApplyPreferences() { UpdateSampling(); UpdateLayout(); WindowPlacement.Clamp(this); }
    private void UpdateSampling()
    {
        timer.Stop(); metrics.Reset();
        if (!IsVisible || WindowState == WindowState.Minimized || paused)
        {
            if (!paused) FlushVolume();
            volumeTimer.Stop(); pendingTarget = null; return;
        }
        timer.Interval = TimeSpan.FromSeconds(host.Preferences.RefreshSeconds);
        lastAudioRefresh = default;
        RefreshMetrics(); timer.Start();
    }
    private void RefreshMetrics()
    {
        var sample = metrics.Read();
        CpuValue.Text = sample.CpuPercent is double cpu ? $"{cpu:0}%" : "—";
        CpuBar.Value = sample.CpuPercent ?? 0;
        CpuDetail.Text = sample.CpuPercent is double value ? value >= 85 ? "바쁘게 일해요" : value >= 50 ? "일하는 중" : "여유로워요" : metrics.SupportsCpu ? "측정 준비 중" : "CPU 그룹 미지원";
        MemoryValue.Text = sample.TotalMemory == 0 ? "—" : $"{sample.MemoryPercent:0}%";
        MemoryBar.Value = sample.MemoryPercent;
        MemoryDetail.Text = sample.TotalMemory == 0 ? "정보 없음" : $"{sample.UsedMemory / 1073741824d:0.0} / {sample.TotalMemory / 1073741824d:0.0} GB";
        FooterStatus.Text = $"PC 전체 사용량 · {host.Preferences.RefreshSeconds}초 간격";
        RefreshClock();
        if (!AudioPicker.IsDropDownOpen && !VolumeSlider.IsMouseCaptureWithin && pendingTarget is null && DateTimeOffset.Now - lastAudioRefresh >= TimeSpan.FromSeconds(4)) RefreshAudio();
    }
    internal void RefreshClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("M월 d일 dddd", CultureInfo.GetCultureInfo("ko-KR"));
        var next = host.Preferences.Reminders.OrderBy(r => r.DueAt).FirstOrDefault();
        AlarmButton.Content = next is null ? "알람 추가" : $"알람 {next.DueAt.ToLocalTime():HH:mm}";
        AlarmButton.ToolTip = next is null ? "알람 설정" : $"{next.Title} · {next.When}";
    }
    internal void RefreshAi()
    {
        bool connected = host.Preferences.CodexEnabled || host.Preferences.ClaudeEnabled;
        int main = host.Ai.Activity.Sessions.Count(s => !s.Child && s.State is PetActivity.Working or PetActivity.Waiting);
        int child = host.Ai.Activity.Sessions.Count(s => s.Child && s.State is PetActivity.Working or PetActivity.Waiting);
        AiSummary.Text = connected ? $"{AiActivity.Label(host.Ai.Activity.State)} · 메인 {main} / 보조 {child}" : "설정에서 AI 도구를 연결해요";
    }
    private void RefreshAudio()
    {
        var audio = AudioControl.Read(audioTarget);
        updatingAudio = true;
        if (AudioPicker.ItemsSource is not System.Collections.Generic.IReadOnlyList<AudioTarget> old || !old.SequenceEqual(audio.Targets))
            AudioPicker.ItemsSource = audio.Targets;
        AudioPicker.SelectedValue = audioTarget;
        VolumeSlider.IsEnabled = MuteButton.IsEnabled = audio.Volume.HasValue;
        VolumeSlider.Value = audio.Volume ?? 0;
        muted = audio.Muted;
        VolumeLabel.Text = audio.Volume is double volume ? muted ? "음소거" : $"{volume:0}%" : "—";
        MuteButton.Content = muted ? "\uE74F" : "\uE767";
        MuteButton.ToolTip = muted ? "음소거 해제" : "음소거";
        AudioHint.Text = audio.Error ?? "기본 출력 장치 · 재생 중인 앱";
        updatingAudio = false;
        lastAudioRefresh = DateTimeOffset.Now;
    }
    private void OnAudioTarget(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updatingAudio || AudioPicker.SelectedValue is not string id) return;
        FlushVolume(); audioTarget = id; RefreshAudio();
    }
    private void OnVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready || updatingAudio || !VolumeSlider.IsEnabled) return;
        pendingTarget = audioTarget; pendingVolume = e.NewValue;
        VolumeLabel.Text = muted ? $"음소거 · {e.NewValue:0}%" : $"{e.NewValue:0}%";
        volumeTimer.Stop(); volumeTimer.Start();
    }
    private void FlushVolume()
    {
        volumeTimer.Stop();
        if (pendingTarget is not { } target) return;
        pendingTarget = null;
        if (!AudioControl.SetVolume(target, pendingVolume)) AudioHint.Text = "볼륨을 바꾸지 못했어요. 출력 장치를 확인해 주세요.";
    }
    private void OnMute(object sender, RoutedEventArgs e) { FlushVolume(); AudioControl.SetMute(audioTarget, !muted); RefreshAudio(); }
    private void OnHide(object sender, RoutedEventArgs e) => Hide();
    private void OnSettings(object sender, RoutedEventArgs e) => host.ShowSettings();
    private void OnAlarms(object sender, RoutedEventArgs e) => host.ShowSettings(3);
    private void OnAi(object sender, RoutedEventArgs e) => host.ShowAi();
    internal void ShowSaveResult(bool saved) { if (!saved) FooterStatus.Text = "설정을 저장하지 못했어요."; }
    private void OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Hide(); }
    private void OnClosing(object? sender, CancelEventArgs e) { timer.Stop(); volumeTimer.Stop(); if (!host.Exiting) { e.Cancel = true; Hide(); } }
}
