using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Moru;

internal enum PetActivity { Idle, Working, Waiting, Done, Error, Unknown }
internal sealed record UsageWindow(double Used, int Minutes, long? ResetsAt);
internal sealed record AiEvent(string Provider, string Session, string Kind, DateTimeOffset At,
    bool? Child = null, long? Tokens = null, double? Context = null,
    UsageWindow? Primary = null, UsageWindow? Secondary = null, string? Parent = null);
internal sealed record AiSession(string Provider, string Id, PetActivity State, DateTimeOffset At,
    bool Child = false, long? Tokens = null, double? Context = null, string? Parent = null, DateTimeOffset ActivityAt = default);

// Only metadata is retained: no prompts, answers, tool arguments, credentials or transcript paths.
internal sealed class AiActivity
{
    private readonly Dictionary<string, AiSession> sessions = new();
    private readonly Dictionary<string, (UsageWindow? Primary, UsageWindow? Secondary, DateTimeOffset At)> limits = new();
    public IEnumerable<AiSession> Sessions => sessions.Values.OrderByDescending(s => s.At);
    public void Clear(string provider)
    {
        foreach (var key in sessions.Where(p => p.Value.Provider == provider).Select(p => p.Key).ToArray()) sessions.Remove(key);
        limits.Remove(provider);
    }
    public void Apply(AiEvent e)
    {
        if (e.Provider is not ("Codex" or "Claude Code") || string.IsNullOrWhiteSpace(e.Session) || e.Session.Length > 160) return;
        string key = e.Provider + ":" + e.Session;
        sessions.TryGetValue(key, out var old);
        if (old is not null && e.At < old.At) return;
        var state = e.Kind switch
        {
            "working" => PetActivity.Working, "waiting" => PetActivity.Waiting,
            "done" => PetActivity.Done, "error" => PetActivity.Error,
            "idle" => PetActivity.Idle, "unknown" => PetActivity.Unknown,
            _ => old?.State ?? PetActivity.Unknown
        };
        sessions[key] = new(e.Provider, e.Session, state, e.At, e.Child ?? old?.Child ?? false,
            e.Tokens ?? old?.Tokens, e.Context ?? old?.Context, e.Parent ?? old?.Parent,
            e.Kind == "usage" ? old?.ActivityAt ?? e.At : e.At);
        if (e.Primary is not null || e.Secondary is not null)
        {
            if (!limits.TryGetValue(e.Provider, out var previous) || e.At >= previous.At)
                limits[e.Provider] = (e.Primary, e.Secondary, e.At);
        }
        if (e.Kind is "idle" or "error")
            foreach (var child in sessions.Where(p => p.Value.Provider == e.Provider && p.Value.Parent == e.Session).ToArray())
                if (child.Value.State is PetActivity.Working or PetActivity.Waiting)
                    sessions[child.Key] = child.Value with { State = PetActivity.Unknown };
        if (sessions.Count > 64) sessions.Remove(sessions.MinBy(p => p.Value.At).Key);
    }
    public void Expire(DateTimeOffset now)
    {
        foreach (var pair in sessions.ToArray())
        {
            var s = pair.Value;
            if (s.State is PetActivity.Working or PetActivity.Waiting && now - s.ActivityAt > TimeSpan.FromMinutes(5))
                sessions[pair.Key] = s with { State = PetActivity.Unknown };
            else if (s.State == PetActivity.Done && now - s.ActivityAt > TimeSpan.FromSeconds(12))
                sessions[pair.Key] = s with { State = PetActivity.Idle };
        }
    }
    public PetActivity State => Sessions.Any(s => s.State == PetActivity.Working) ? PetActivity.Working
        : Sessions.Any(s => s.State == PetActivity.Waiting) ? PetActivity.Waiting
        : Sessions.Any(s => s.State == PetActivity.Done) ? PetActivity.Done
        : Sessions.FirstOrDefault()?.State ?? PetActivity.Idle;
    public static string Label(PetActivity s) => s switch
    {
        PetActivity.Working => "작업 중", PetActivity.Waiting => "확인 기다리는 중", PetActivity.Done => "작업 완료!",
        PetActivity.Error => "오류 확인", PetActivity.Unknown => "상태 확인 필요", _ => "쉬는 중"
    };
    public string Summary(string provider)
    {
        var active = Sessions.Where(s => s.Provider == provider && s.State is PetActivity.Working or PetActivity.Waiting).ToArray();
        var latest = Sessions.FirstOrDefault(s => s.Provider == provider);
        if (latest is null) return "연결 대기 · 아직 받은 이벤트가 없어요";
        return active.Length > 0 ? $"메인 {active.Count(s => !s.Child)} · 보조 {active.Count(s => s.Child)} 작업 중"
            : $"{Label(latest.State)} · 마지막 수신 {latest.At.ToLocalTime():HH:mm:ss}";
    }
    public string Usage(string provider)
    {
        var lines = new List<string>();
        if (limits.TryGetValue(provider, out var l))
        {
            foreach (var w in new[] { l.Primary, l.Secondary }.Where(w => w is not null))
            {
                string window = w!.Minutes >= 1440 ? $"{w.Minutes / 1440d:0.#}일" : w.Minutes >= 60 ? $"{w.Minutes / 60d:0.#}시간" : w.Minutes > 0 ? $"{w.Minutes}분" : "기간 미수신";
                lines.Add($"{window} 한도 {100 - w.Used:0.#}% 남음");
            }
            lines.Add($"한도 수신 {l.At.ToLocalTime():HH:mm}");
        }
        var s = Sessions.FirstOrDefault(s => s.Provider == provider && (s.Tokens.HasValue || s.Context.HasValue));
        if (s?.Tokens is long tokens) lines.Add($"최근 대화 누적 {tokens:N0} tokens (캐시 포함)");
        if (s?.Context is double context) lines.Add($"최근 대화 컨텍스트 {context:0.#}% 사용");
        if (!limits.ContainsKey(provider)) lines.Insert(0, "남은 한도 미수신 · 추정하지 않아요");
        return string.Join("\n", lines);
    }
}

internal static class AiJson
{
    public static JsonElement Get(this JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
    public static string? Str(this JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    public static long? Int(this JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n) && n >= 0 ? n : null;
    public static double? Number(this JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
    public static UsageWindow? Window(JsonElement e, bool claude = false, int minutes = 0)
    {
        double? used = e.Get(claude ? "used_percentage" : "used_percent").Number();
        return used is >= 0 and <= 100 ? new(used.Value, minutes > 0 ? minutes : (int)(e.Get("window_minutes").Int() ?? 0), e.Get("resets_at").Int()) : null;
    }
    public static AiEvent? Codex(string line, string id, bool child, string? parent)
    {
        if (!line.Contains("\"event_msg\"", StringComparison.Ordinal)) return null;
        try
        {
            using var d = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 48 });
            var root = d.RootElement;
            if (root.Get("type").Str() != "event_msg") return null;
            if (!DateTimeOffset.TryParse(root.Get("timestamp").Str(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)) return null;
            var p = root.Get("payload");
            var type = p.Get("type").Str();
            string? kind = type switch
            {
                "task_started" or "turn_started" => "working",
                "task_complete" or "task_completed" or "turn_complete" or "turn_completed" => "done",
                "turn_aborted" => "idle", "error" => "error", "token_count" => "usage",
                // Recent completed tool/message events prove activity, not a completed turn.
                "item_completed" => "working", _ => null
            };
            if (kind is null) return null;
            var usage = p.Get("info").Get("total_token_usage");
            var limits = p.Get("rate_limits");
            return new("Codex", id, kind, at, child, usage.Get("total_tokens").Int(), null,
                Window(limits.Get("primary")), Window(limits.Get("secondary")), parent);
        }
        catch (JsonException) { return null; }
    }
    public static AiEvent? Claude(string json, bool statusLine, DateTimeOffset at)
    {
        using var d = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
        var p = d.RootElement;
        string? session = p.Get("session_id").Str();
        if (string.IsNullOrWhiteSpace(session) || session.Length > 120) return null;
        var type = p.Get("hook_event_name").Str();
        bool child = type is "SubagentStart" or "SubagentStop";
        string? childId = p.Get("agent_id").Str();
        if (child && (string.IsNullOrEmpty(childId) || childId.Length > 120)) return null;
        string? kind = statusLine ? "usage" : type switch
        {
            "UserPromptSubmit" or "PreToolUse" or "PostToolUse" or "SubagentStart" => "working",
            "Stop" or "SubagentStop" => "done", "StopFailure" => "error",
            "PermissionRequest" => "waiting",
            "Notification" when p.Get("notification_type").Str() is "permission_prompt" or "idle_prompt" => "waiting",
            "SessionStart" or "SessionEnd" => "idle", _ => null
        };
        if (kind is null) return null;
        var limits = p.Get("rate_limits");
        double? context = p.Get("context_window").Get("used_percentage").Number();
        return new("Claude Code", child ? childId! : session, kind, at, child, null,
            context is >= 0 and <= 100 ? context : null,
            Window(limits.Get("five_hour"), true, 300), Window(limits.Get("seven_day"), true, 10080), child ? session : null);
    }
}
