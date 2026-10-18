using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Moru;

internal static class ClaudeBridge
{
    internal const string HookFlag = "--moru-claude-hook";
    internal const string StatusFlag = "--moru-claude-status";
    public static int Send(string pipe, bool status)
    {
        try
        {
            // Hook input can contain prompts/tool results. Reduce it before crossing the pipe.
            var chars = new char[1024 * 1024];
            int length = 0, read;
            using var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            while (length < chars.Length && (read = stdin.Read(chars, length, chars.Length - length)) > 0) length += read;
            if (length == chars.Length) return 0;
            var e = AiJson.Claude(new string(chars, 0, length), status, DateTimeOffset.UtcNow);
            if (e is null) return 0;
            if (status)
            {
                using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
                output.WriteLine(e.Context is double c ? $"Moru | context {c:0.#}%" : "Moru connected");
            }
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.Asynchronous);
            client.Connect(200);
            byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(e) + "\n");
            using var deadline = new CancellationTokenSource(500);
            client.WriteAsync(payload, deadline.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or JsonException or UnauthorizedAccessException or OperationCanceledException or ArgumentException) { }
        // Observers must never block Claude, request tools, or fail the user's task.
        return 0;
    }
    public static async Task Listen(string pipe, Action<AiEvent> publish, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(1000);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var buffer = new char[8192];
                int length = 0;
                while (length < buffer.Length)
                {
                    int n = await reader.ReadAsync(buffer.AsMemory(length, buffer.Length - length), timeout.Token).ConfigureAwait(false);
                    if (n == 0) break;
                    length += n;
                    if (Array.IndexOf(buffer, '\n', 0, length) >= 0) break;
                }
                if (length == 0 || length == buffer.Length) continue;
                var e = JsonSerializer.Deserialize<AiEvent>(new string(buffer, 0, length));
                if (e?.Provider == "Claude Code" && Math.Abs((DateTimeOffset.UtcNow - e.At).TotalMinutes) < 2) publish(e);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException) { }
        }
    }
    public static string Configure(string path, string executable, string pipe, bool enable)
    {
        if (enable && !HasGitBash()) throw new NotSupportedException("현재 Claude 연결에는 Git for Windows의 Git Bash가 필요해요. 설치 후 다시 연결해 주세요.");
        if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) throw new IOException("설정 파일이 너무 커요.");
        string original = File.Exists(path) ? File.ReadAllText(path) : "{}";
        var root = JsonNode.Parse(original) as JsonObject ?? throw new JsonException();
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        if (root["hooks"] is not null && root["hooks"] is not JsonObject) throw new JsonException();
        if (root["hooks"] is null) root["hooks"] = hooks;
        foreach (var pair in hooks.ToArray())
        {
            if (pair.Value is not JsonArray groups) continue;
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                if (groups[i]?["hooks"] is not JsonArray commands) continue;
                for (int j = commands.Count - 1; j >= 0; j--)
                    if (commands[j]?["command"]?.GetValue<string>().Contains(HookFlag, StringComparison.Ordinal) == true) commands.RemoveAt(j);
                if (commands.Count == 0) groups.RemoveAt(i);
            }
        }
        string exe = executable.Replace('\\', '/');
        if (exe.IndexOfAny(new[] { '"', '\n', '\r', '$', '`' }) >= 0) throw new ArgumentException("실행 경로에 지원하지 않는 문자가 있어요.");
        bool ownsStatus = root["statusLine"]?["command"]?.GetValue<string>().Contains(StatusFlag, StringComparison.Ordinal) == true;
        if (ownsStatus) root.Remove("statusLine");
        bool statusInstalled = false;
        if (enable)
        {
            foreach (string name in new[] { "SessionStart", "SessionEnd", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest", "Notification", "Stop", "StopFailure", "SubagentStart", "SubagentStop" })
            {
                if (hooks[name] is null) hooks[name] = new JsonArray();
                if (hooks[name] is not JsonArray groups) throw new JsonException();
                groups.Add(new JsonObject { ["hooks"] = new JsonArray(new JsonObject
                { ["type"] = "command", ["shell"] = "bash", ["command"] = $"\"{exe}\" {HookFlag} {pipe}", ["timeout"] = 2 }) });
            }
            if (root["statusLine"] is null)
            {
                root["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = $"\"{exe}\" {StatusFlag} {pipe}" };
                statusInstalled = true;
            }
        }
        string result = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            if (File.ReadAllText(path) != original) throw new IOException("설정이 변경되었어요. 다시 시도해 주세요.");
            File.Copy(path, path + ".moru-backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
        }
        string temp = path + ".moru-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, result, new UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return !enable ? "모루 연결을 해제했어요. Claude Code를 다시 열어 주세요."
            : statusInstalled ? "연결 설정 완료 · 새 Claude Code 세션부터 적용돼요."
            : "작업 훅 연결 완료 · 기존 상태줄을 유지해서 사용량 연결은 생략했어요.";
    }
    internal static bool HasGitBash()
    {
        string? configured = Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH");
        if (!string.IsNullOrEmpty(configured)) return File.Exists(configured);
        foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            if (File.Exists(Path.Combine(root, "Git", "bin", "bash.exe")) || File.Exists(Path.Combine(root, "Programs", "Git", "bin", "bash.exe"))) return true;
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                if (File.Exists(Path.Combine(dir, "git.exe")) && File.Exists(Path.GetFullPath(Path.Combine(dir, "..", "bin", "bash.exe")))) return true;
            }
            catch (ArgumentException) { }
        }
        return false;
    }
}
