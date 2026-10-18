using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Moru;

// Read-only compatibility adapter for local Codex Desktop/CLI rollout files.
// File notifications are coalesced; at most 1 MiB per file is read per pass.
internal sealed class CodexObserver : IDisposable
{
    private const int Limit = 1024 * 1024;
    private sealed class Cursor
    {
        public long Offset;
        public byte[] Pending = Array.Empty<byte>();
        public bool SkipLine;
        public bool Child;
        public string? Parent;
        public string Id = "";
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Cursor> cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<IReadOnlyList<AiEvent>> publish;
    private readonly Timer timer;
    private FileSystemWatcher? watcher;
    private bool disposed;
    public CodexObserver(string root, Action<IReadOnlyList<AiEvent>> publish)
    {
        this.publish = publish;
        timer = new Timer(_ => Drain(), null, Timeout.Infinite, Timeout.Infinite);
        if (!Directory.Exists(root)) return;
        watcher = new(root, "*.jsonl") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        watcher.Changed += (_, e) => Queue(e.FullPath);
        watcher.Created += (_, e) => Queue(e.FullPath);
        watcher.Renamed += (_, e) => Queue(e.FullPath);
        watcher.Error += (_, _) => Recover();
        watcher.EnableRaisingEvents = true;
        _ = Task.Run(() =>
        {
            try
            {
                // One metadata-only discovery; old, large conversations are never read in full.
                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                    .OrderByDescending(f => f.LastWriteTimeUtc).Take(12)) Queue(file.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
    }
    private void Queue(string path)
    {
        lock (gate)
        {
            if (disposed || pending.Count >= 64) return;
            bool schedule = pending.Count == 0;
            pending.Add(path);
            if (schedule) timer.Change(750, Timeout.Infinite);
        }
    }
    public void Recover()
    {
        lock (gate)
        {
            if (disposed) return;
            foreach (var path in cursors.Keys) pending.Add(path);
            timer.Change(750, Timeout.Infinite);
        }
    }
    private void Drain()
    {
        var events = new List<AiEvent>();
        lock (gate)
        {
            if (disposed) return;
            foreach (var path in pending.ToArray())
            {
                pending.Remove(path);
                try
                {
                    using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (!cursors.TryGetValue(path, out var c))
                    {
                        c = new Cursor { Id = Path.GetFileNameWithoutExtension(path) };
                        var header = new byte[Math.Min(65536, (int)Math.Min(f.Length, 65536))];
                        int n = f.Read(header);
                        int newline = Array.IndexOf(header, (byte)'\n', 0, n);
                        if (newline > 0) ReadMetadata(Encoding.UTF8.GetString(header, 0, newline), c);
                        if (cursors.Count >= 32) cursors.Remove(cursors.Keys.First());
                        cursors[path] = c;
                    }
                    if (f.Length < c.Offset) { c.Offset = 0; c.Pending = Array.Empty<byte>(); c.SkipLine = false; }
                    if (f.Length - c.Offset > Limit)
                    {
                        c.Offset = f.Length - Limit;
                        c.Pending = Array.Empty<byte>();
                        c.SkipLine = true;
                    }
                    f.Position = c.Offset;
                    int count = (int)Math.Min(Limit, f.Length - c.Offset);
                    if (count == 0) continue;
                    var bytes = new byte[c.Pending.Length + count];
                    c.Pending.CopyTo(bytes, 0);
                    int read = f.Read(bytes, c.Pending.Length, count);
                    c.Offset += read;
                    int end = c.Pending.Length + read, start = 0;
                    for (int i = 0; i < end; i++)
                    {
                        if (bytes[i] != '\n') continue;
                        if (!c.SkipLine && i - start <= 65536)
                        {
                            string line = Encoding.UTF8.GetString(bytes, start, i - start);
                            var ev = AiJson.Codex(line, c.Id, c.Child, c.Parent);
                            if (ev is not null) events.Add(ev);
                        }
                        c.SkipLine = false;
                        start = i + 1;
                    }
                    // Preserve split UTF-8 and partial JSON only up to the metadata line bound.
                    if (end - start > 65536) { c.Pending = Array.Empty<byte>(); c.SkipLine = true; }
                    else c.Pending = bytes.AsSpan(start, end - start).ToArray();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
        }
        if (events.Count > 0) publish(events.OrderBy(e => e.At).ToArray());
    }
    private static void ReadMetadata(string line, Cursor c)
    {
        try
        {
            using var d = JsonDocument.Parse(line);
            var p = d.RootElement.Get("payload");
            c.Id = p.Get("id").Str() ?? c.Id;
            var source = p.Get("source");
            var subagent = source.Get("subagent");
            c.Child = subagent.ValueKind != JsonValueKind.Undefined;
            c.Parent = subagent.Get("thread_spawn").Get("parent_thread_id").Str();
        }
        catch (JsonException) { }
    }
    public void Dispose()
    {
        lock (gate) { disposed = true; watcher?.Dispose(); timer.Dispose(); pending.Clear(); cursors.Clear(); }
    }
}
