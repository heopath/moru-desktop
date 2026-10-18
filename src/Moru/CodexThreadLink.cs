using System;
using System.ComponentModel;
using System.Diagnostics;

namespace Moru;

internal static class CodexThreadLink
{
    // Local Codex Desktop deep link. Never launch a URL or command supplied by a log.
    internal static string? For(AiSession session) => session.Provider == "Codex" && session.Id.Length == 36
        && Guid.TryParseExact(session.Id, "D", out var id) && id != Guid.Empty
        ? $"codex://threads/{id:D}" : null;

    internal static bool Open(AiSession session)
    {
        string? link = For(session);
        if (link is null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or System.IO.IOException)
        { return false; }
    }
}

internal sealed record AiTaskRow(AiSession Session)
{
    public string Title => $"{Session.Provider} · {(Session.Child ? "보조" : "메인")} · {AiActivity.Label(Session.State)}";
    public string Detail => $"#{(Session.Id.Length > 6 ? Session.Id[^6..] : Session.Id)} · {Session.At.ToLocalTime():HH:mm}";
    public bool CanOpen => CodexThreadLink.For(Session) is not null;
    public string OpenHint => CanOpen ? "Codex에서 열기 ↗" : Session.Provider == "Codex" ? "채팅 링크 정보 없음" : "Claude Code 상태";
    public string AccessibleName => $"{Title}, {Detail}, {OpenHint}";
}
