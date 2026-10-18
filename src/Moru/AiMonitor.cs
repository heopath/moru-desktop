using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;

namespace Moru;

internal sealed class AiMonitor : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer expiry;
    private CodexObserver? codex;
    private CancellationTokenSource? claude;
    private bool disposed;
    internal AiActivity Activity { get; } = new();
    internal event Action? Changed;
    internal event Action? TaskCompleted;
    internal string CodexStatus { get; private set; } = "연결 꺼짐";
    internal string PipeName { get; }
    public AiMonitor(Dispatcher dispatcher, string suffix)
    {
        this.dispatcher = dispatcher;
        PipeName = "Moru.Ai." + suffix;
        expiry = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(15) };
        expiry.Tick += (_, _) => { Activity.Expire(DateTimeOffset.UtcNow); codex?.Recover(); Changed?.Invoke(); };
    }
    public void SetCodex(bool enabled)
    {
        codex?.Dispose(); codex = null;
        Activity.Clear("Codex");
        string root = Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "sessions");
        CodexStatus = !enabled ? "연결 꺼짐" : Directory.Exists(root) ? "로컬 기록 연결 · 실험적" : "Codex 기록 폴더가 아직 없어요";
        if (enabled && Directory.Exists(root)) codex = new(root, Receive);
        UpdateTimer(); Changed?.Invoke();
    }
    public void SetClaude(bool enabled)
    {
        claude?.Cancel(); claude?.Dispose(); claude = null;
        Activity.Clear("Claude Code");
        if (enabled)
        {
            claude = new CancellationTokenSource();
            _ = ClaudeBridge.Listen(PipeName, e => Receive(new[] { e }), claude.Token);
        }
        UpdateTimer(); Changed?.Invoke();
    }
    private void UpdateTimer() { if (codex is null && claude is null) expiry.Stop(); else expiry.Start(); }
    private void Receive(IReadOnlyList<AiEvent> events)
    {
        if (disposed || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(() =>
        {
            if (disposed) return;
            bool completed = false;
            foreach (var e in events)
                if ((e.Provider == "Codex" && codex is not null) || (e.Provider == "Claude Code" && claude is not null))
                {
                    var old = Activity.Sessions.FirstOrDefault(s => s.Provider == e.Provider && s.Id == e.Session);
                    if (e.Kind == "done" && DateTimeOffset.UtcNow - e.At < TimeSpan.FromSeconds(5) && (old is null || e.At > old.At)) completed = true;
                    Activity.Apply(e);
                }
            Activity.Expire(DateTimeOffset.UtcNow);
            Changed?.Invoke();
            if (completed) TaskCompleted?.Invoke();
        });
    }
    public void Dispose()
    {
        disposed = true; expiry.Stop(); codex?.Dispose(); claude?.Cancel(); claude?.Dispose();
    }
}
