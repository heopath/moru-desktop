using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Moru;

public partial class AiWindow : Window
{
    private readonly App host;
    private bool ready;
    internal AiWindow(App app)
    {
        host = app; InitializeComponent();
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 32);
        CodexToggle.IsChecked = app.Preferences.CodexEnabled;
        ClaudeToggle.IsChecked = app.Preferences.ClaudeEnabled;
        ready = true; Refresh();
        Closing += (_, e) => { if (!host.Exiting) { e.Cancel = true; Hide(); } };
    }
    internal void Refresh()
    {
        CodexConnection.Text = host.Ai.CodexStatus;
        ClaudeConnection.Text = host.Preferences.ClaudeEnabled ? host.ClaudeNotice : "연결 꺼짐 · 설정을 백업한 뒤 훅과 상태줄을 추가해요";
        CodexState.Text = host.Ai.Activity.Summary("Codex");
        ClaudeState.Text = host.Ai.Activity.Summary("Claude Code");
        CodexUsage.Text = host.Ai.Activity.Usage("Codex");
        ClaudeUsage.Text = host.Ai.Activity.Usage("Claude Code");
        RecentTasks.Text = string.Join("\n", host.Ai.Activity.Sessions.Take(6).Select(s =>
            $"{s.Provider} · {(s.Child ? "보조" : "메인")} #{ShortId(s.Id)} · {AiActivity.Label(s.State)} · {s.At.ToLocalTime():HH:mm}"));
        if (RecentTasks.Text.Length == 0) RecentTasks.Text = "연결한 도구에서 작업을 시작하면 여기에 나타나요.";
    }
    private static string ShortId(string id) => id.Length > 6 ? id[^6..] : id;
    private void OnCodex(object sender, RoutedEventArgs e) { if (ready) host.ChangeCodex(CodexToggle.IsChecked == true); }
    private void OnClaude(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        bool enabled = ClaudeToggle.IsChecked == true;
        if (!host.ChangeClaude(enabled))
        {
            ready = false; ClaudeToggle.IsChecked = host.Preferences.ClaudeEnabled; ready = true;
            MessageBox.Show(this, host.ClaudeNotice, "Claude Code 연결");
        }
    }
    private void OnPreview(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && Enum.TryParse<PetActivity>(name, out var state)) host.PreviewAnimation(state);
    }
}
