using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Moru;

public partial class AiWindow : Window
{
    private readonly App host;
    internal AiWindow(App app)
    {
        host = app; InitializeComponent();
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 24);
        Height = Math.Min(740, MaxHeight);
        Closing += (_, e) => { if (!host.Exiting) { e.Cancel = true; Hide(); host.ShowPanel(); } };
        Refresh();
    }
    internal void Refresh()
    {
        CodexConnection.Text = host.Preferences.CodexEnabled ? "연결 켜짐" : "연결 꺼짐";
        ClaudeConnection.Text = host.Preferences.ClaudeEnabled ? "연결 켜짐" : "연결 꺼짐";
        CodexState.Text = host.Preferences.CodexEnabled ? host.Ai.Activity.Summary("Codex") : "연결을 기다려요";
        ClaudeState.Text = host.Preferences.ClaudeEnabled ? host.Ai.Activity.Summary("Claude Code") : "연결을 기다려요";
        CodexUsage.Text = host.Preferences.CodexEnabled ? host.Ai.Activity.Usage("Codex") : "설정에서 Codex를 켜 주세요.";
        ClaudeUsage.Text = host.Preferences.ClaudeEnabled ? host.Ai.Activity.Usage("Claude Code") : "설정에서 Claude Code를 켜 주세요.";
        var tasks = host.Ai.Activity.Sessions.Take(6).Select(s => new AiTaskRow(s)).ToArray();
        // Keep keyboard focus on a card when a timer refresh has no changed sessions.
        if (RecentTasks.ItemsSource is not AiTaskRow[] previous || !previous.SequenceEqual(tasks))
            RecentTasks.ItemsSource = tasks;
        EmptyTasks.Visibility = tasks.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnOpenTask(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AiTaskRow task } || !task.CanOpen) return;
        TaskOpenNotice.Text = CodexThreadLink.Open(task.Session)
            ? "Codex에 채팅 열기를 요청했어요."
            : "채팅을 열지 못했어요. Codex 데스크톱 앱이 설치되어 있는지 확인해 주세요.";
        TaskOpenNotice.Visibility = Visibility.Visible;
    }
    private void OnSettings(object sender, RoutedEventArgs e) => host.ShowSettings(2);
    private void OnHide(object sender, RoutedEventArgs e) { Hide(); host.ShowPanel(); }
    private void OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Hide(); host.ShowPanel(); } }
}
