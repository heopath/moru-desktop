using Moru;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;

internal static class AiChecks
{
    public static void Run(string scratch, Action<string, bool> check)
    {
        var now = DateTimeOffset.UtcNow;
        var ai = new AiActivity();
        ai.Apply(new("Codex", "main", "usage", now, Tokens: 123));
        check("Usage alone does not imply work or completion", ai.State == PetActivity.Unknown);
        ai.Apply(new("Codex", "main", "working", now));
        ai.Apply(new("Codex", "child", "working", now, true, Parent: "main"));
        ai.Apply(new("Codex", "main", "done", now.AddSeconds(1)));
        check("Parent completion preserves a running background child", ai.State == PetActivity.Working);
        ai.Apply(new("Codex", "child", "done", now.AddSeconds(2), true, Parent: "main"));
        check("Completion is visible", ai.State == PetActivity.Done);
        ai.Expire(now.AddSeconds(20));
        check("Completion returns to idle", ai.Sessions.First(s => s.Id == "main").State == PetActivity.Idle);
        ai.Apply(new("Codex", "main", "working", now.AddSeconds(30)));
        ai.Apply(new("Codex", "main", "usage", now.AddMinutes(6), Tokens: 456));
        ai.Expire(now.AddMinutes(6));
        check("Quota updates cannot keep a stale task working", ai.State == PetActivity.Unknown);
        ai.Apply(new("Codex", "main", "done", now));
        check("Old events cannot overwrite newer state", ai.State == PetActivity.Unknown);
        ai.Apply(new("Codex", "main", "usage", now.AddMinutes(7), Primary: new(42, 300, null)));
        check("Quota percentage has an explicit window", ai.Usage("Codex").Contains("5시간 한도 42%"));
        for (int i = 0; i < 100; i++) ai.Apply(new("Codex", "session" + i, "idle", now.AddMinutes(10).AddSeconds(i)));
        check("Session history is bounded", ai.Sessions.Count() == 64);
        ai.Clear("Codex");
        check("Disconnect clears state and usage", !ai.Sessions.Any() && ai.Usage("Codex").Contains("미수신"));

        string Event(string kind) => JsonSerializer.Serialize(new { type = "event_msg", timestamp = now, payload = new { type = kind } });
        check("Codex completed turn maps to done", AiJson.Codex(Event("task_complete"), "a", false, null)?.Kind == "done");
        check("Codex interruption is not success", AiJson.Codex(Event("turn_aborted"), "a", false, null)?.Kind == "idle");
        check("Codex malformed and unrelated records are ignored", AiJson.Codex("{bad", "a", false, null) is null && AiJson.Codex("{\"type\":\"response_item\"}", "a", false, null) is null);
        var child = AiJson.Claude("{\"session_id\":\"main\",\"hook_event_name\":\"SubagentStart\",\"agent_id\":\"sub\",\"prompt\":\"DO NOT RETAIN\"}", false, now);
        check("Claude child is associated with parent", child is { Session: "sub", Parent: "main", Child: true, Kind: "working" });
        check("Prompt content is removed by bridge", !JsonSerializer.Serialize(child).Contains("DO NOT RETAIN"));
        var status = AiJson.Claude("{\"session_id\":\"main\",\"context_window\":{\"used_percentage\":25},\"rate_limits\":{\"five_hour\":{\"used_percentage\":13,\"resets_at\":1900000000}}}", true, now);
        check("Claude context and account quota are separate", status is { Kind: "usage", Context: 25, Primary.Used: 13, Primary.Minutes: 300 });
        check("Claude out-of-range context is unknown", AiJson.Claude("{\"session_id\":\"main\",\"context_window\":{\"used_percentage\":-1}}", true, now)?.Context is null);
        check("Claude failure is not a successful completion", AiJson.Claude("{\"session_id\":\"main\",\"hook_event_name\":\"StopFailure\"}", false, now)?.Kind == "error");

        string dir = Path.Combine(scratch, "claude-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string config = Path.Combine(dir, "settings.json");
        File.WriteAllText(config, "{\"env\":{\"CUSTOM\":\"keep\"},\"statusLine\":{\"command\":\"existing\"},\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"existing-hook\"}]}]}}");
        ClaudeBridge.Configure(config, "C:/Moru Test/Moru.exe", "moru-test", true);
        var installed = JsonNode.Parse(File.ReadAllText(config))!;
        check("Claude installer preserves existing settings/status line", installed["env"]?["CUSTOM"]?.GetValue<string>() == "keep" && installed["statusLine"]?["command"]?.GetValue<string>() == "existing");
        check("Claude configuration is backed up", Directory.GetFiles(dir, "*.moru-backup-*").Length == 1);
        ClaudeBridge.Configure(config, "C:/Moru Test/Moru.exe", "moru-test", true);
        installed = JsonNode.Parse(File.ReadAllText(config))!;
        check("Claude repeated setup does not duplicate hooks", installed["hooks"]?["Stop"]?.AsArray().Count == 2);
        ClaudeBridge.Configure(config, "C:/Moru Test/Moru.exe", "moru-test", false);
        string removed = File.ReadAllText(config);
        check("Disconnect removes only Moru hooks", removed.Contains("existing-hook") && removed.Contains("existing") && !removed.Contains(ClaudeBridge.HookFlag));

        string logs = Path.Combine(scratch, "rollouts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logs);
        string file = Path.Combine(logs, "rollout-test.jsonl");
        File.WriteAllText(file, "{\"type\":\"session_meta\",\"payload\":{\"id\":\"child\",\"source\":{\"subagent\":{\"thread_spawn\":{\"parent_thread_id\":\"parent\"}}}}}\n" + Event("task_started") + "\n", new UTF8Encoding(false));
        var events = new ConcurrentQueue<AiEvent>();
        using (var observer = new CodexObserver(logs, batch => { foreach (var e in batch) events.Enqueue(e); }))
        {
            check("Codex observer reads actual file metadata and startup events", SpinWait.SpinUntil(() => events.Any(e => e is { Kind: "working", Child: true, Parent: "parent" }), 5000));
            string done = Event("task_complete");
            File.AppendAllText(file, done[..20]);
            Thread.Sleep(1000);
            check("Partial JSON lines do not emit completion", !events.Any(e => e.Kind == "done"));
            File.AppendAllText(file, done[20..] + "\n");
            check("Appended partial line is reconstructed once", SpinWait.SpinUntil(() => events.Count(e => e.Kind == "done") == 1, 5000));
            observer.Recover(); Thread.Sleep(1000);
            check("Unchanged file does not replay usage or completion", events.Count(e => e.Kind == "done") == 1);
            File.WriteAllText(file, Event("turn_aborted") + "\n");
            check("Truncated rollout resets cursor", SpinWait.SpinUntil(() => events.Any(e => e.Kind == "idle"), 5000));
        }
    }
}
