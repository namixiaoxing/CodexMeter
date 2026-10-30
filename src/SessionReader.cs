using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
namespace CodexMeter;

// Only session_meta, turn_context, token_count, token_usage_record and item timing are retained.
internal sealed class SessionReader : IDisposable
{
    private readonly string root;
    private readonly FileSystemWatcher? watcher;
    private readonly ConcurrentDictionary<string, byte> changed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Path, DateTime Write)> index = new(StringComparer.OrdinalIgnoreCase);
    private DateTime nextScan;
    private string? path;
    private long position;
    private DateTime write;
    private readonly List<byte> partial = new();
    private bool discardLongLine;
    private string thread = "", model = "";
    private long input, output, cached, context, capacity;
    private DateTimeOffset updated;
    private readonly VisibleTps visibleTps = new();
    private readonly TurnCounter turnCounter=new();
    public int TurnCount=>turnCounter.Count;
    public TokenSnapshot? Snapshot { get; private set; }
    public QuotaSnapshot? Quota { get; private set; }
    public string? SelectedThread => path is null ? null : thread;

    public SessionReader(string codexHome)
    {
        root = Path.Combine(codexHome, "sessions");
        if (Directory.Exists(root))
        {
            watcher = new(root, "*.jsonl") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            watcher.Changed += (_, e) => changed.TryAdd(e.FullPath, 0);
            watcher.Created += (_, e) => changed.TryAdd(e.FullPath, 0);
            watcher.Renamed += (_, e) => changed.TryAdd(e.FullPath, 0);
            watcher.Error += (_, _) => nextScan = DateTime.MinValue;
            watcher.EnableRaisingEvents = true;
        }
    }

    private void Index(string candidate)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(candidate);
            if (name.Length < 36 || !Guid.TryParse(name[^36..], out _)) return;
            var id = name[^36..];
            if (!index.ContainsKey(id))
            {
                using var f = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                // Read only the first physical line; subagent replay must not qualify as a desktop root.
                using var r = new StreamReader(f);
                var first = new StringBuilder();
                for (var i = 0; i < 2 * 1024 * 1024; i++) { var c = r.Read(); if (c < 0 || c == '\n') break; first.Append((char)c); }
                using var doc = JsonDocument.Parse(first.ToString());
                var meta = doc.RootElement.At("payload");
                if (doc.RootElement.At("type").Str() != "session_meta" || meta.At("originator").Str() != "Codex Desktop" || meta.At("source").Str() != "vscode") return;
            }
            index[id] = (candidate, File.GetLastWriteTimeUtc(candidate));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public void Poll(string? preferred)
    {
        if (!Directory.Exists(root)) return;
        if (DateTime.UtcNow >= nextScan)
        {
            nextScan = DateTime.UtcNow.AddMinutes(5);
            try { foreach (var f in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)) Index(f); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        foreach (var f in changed.Keys) if (changed.TryRemove(f, out _)) Index(f);
        string? candidate = null;
        if (!string.IsNullOrEmpty(preferred))
        {
            if (index.TryGetValue(preferred, out var found)) candidate = found.Path;
            // Never silently show another thread while an IPC-selected thread is waiting for a log.
        }
        else candidate = index.OrderByDescending(k => k.Value.Write).Select(k => k.Value.Path).FirstOrDefault();
        if (candidate != path)
        {
            path = candidate; thread = preferred ?? (path is null ? "" : Path.GetFileNameWithoutExtension(path)[^36..]);
            position = 0; write = default; partial.Clear(); discardLongLine = false;
            Snapshot = null; input = output = cached = context = capacity = 0; model = ""; updated = default;
            visibleTps.Reset();
            turnCounter.Clear();
        }
        if (path is null) return;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (position > f.Length) { position = 0; partial.Clear(); visibleTps.Reset(); turnCounter.Clear(); Snapshot = null; updated = default; }
            if (position == f.Length && stamp == write) return;
            if (position == 0 && f.Length > 16 * 1024 * 1024)
            {
                position = turnCounter.ReadPrefix(f,f.Length - 16 * 1024 * 1024); discardLongLine = false;
            }
            f.Seek(position, SeekOrigin.Begin);
            var buffer = new byte[64 * 1024];
            // Bound work per poll; the initial tail is at most 16 MiB, later appends at most 2 MiB.
            var budget = position == 0 || Snapshot is null ? 16 * 1024 * 1024 : 2 * 1024 * 1024;
            while (budget > 0)
            {
                var count = f.Read(buffer, 0, Math.Min(buffer.Length, budget)); if (count == 0) break;
                position += count; budget -= count;
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] == 10)
                    {
                        if (!discardLongLine && partial.Count > 0) Consume(Encoding.UTF8.GetString(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(partial)));
                        partial.Clear(); discardLongLine = false;
                    }
                    else if (!discardLongLine)
                    {
                        if (partial.Count < 2 * 1024 * 1024) partial.Add(buffer[i]);
                        else { partial.Clear(); discardLongLine = true; }
                    }
                }
            }
            write = stamp;
            if (updated != default) Snapshot = new(thread, model, input, output, cached, context, capacity, visibleTps.Value, updated, visibleTps.Tokens, visibleTps.Milliseconds, visibleTps.MeasuredAtMs,turnCounter.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // Internal for synthetic, content-free parser tests.
    internal void Consume(string line)
    {
        // Skip conversation text and tool arguments before JSON allocation.
        if (!line.Contains("\"event_msg\"", StringComparison.Ordinal) && !line.Contains("\"token_usage_record\"", StringComparison.Ordinal) && !line.Contains("\"turn_context\"", StringComparison.Ordinal) && !line.Contains("\"response_item\"", StringComparison.Ordinal)) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement; var p = root.At("payload");
            var time = DateTimeOffset.TryParse(root.At("timestamp").Str(), out var t) ? t : DateTimeOffset.UtcNow;
            switch (root.At("type").Str())
            {
                case "turn_context": model = p.At("model").Str() ?? model; turnCounter.Add(p); break;
                case "response_item": visibleTps.Item(p); break;
                case "token_usage_record":
                    visibleTps.Usage(p);
                    SetUsage(p.At("thread_token_usage"));
                    context = p.At("usage").At("total_tokens").Long(); updated = time;
                    break;
                case "event_msg":
                    switch (p.At("type").Str())
                    {
                        case "task_started": visibleTps.StartTurn(); turnCounter.Add(p); capacity = p.At("model_context_window").Long(); break;
                        case "item_completed":
                            visibleTps.Completed(p);
                            break;
                        case "token_count":
                            var info = p.At("info");
                            if (info.ValueKind == JsonValueKind.Object)
                            {
                                SetUsage(info.At("total_token_usage")); context = info.At("last_token_usage").At("total_tokens").Long();
                                var cap = info.At("model_context_window").Long(); if (cap > 0) capacity = cap;
                                updated = time;
                            }
                            var q = QuotaSnapshot.Parse(p.At("rate_limits"), false, time, "会话日志");
                            if (q is not null && (Quota is null || q.Updated >= Quota.Updated)) Quota = q;
                            break;
                    }
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or OverflowException) { }
    }
    private void SetUsage(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return;
        input = e.At("input_tokens").Long(); output = e.At("output_tokens").Long(); cached = e.At("cached_input_tokens").Long();
    }
    internal double? TestTps => visibleTps.Value;
    public void Dispose() => watcher?.Dispose();
}
