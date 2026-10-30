using System.Text.Json;
namespace CodexMeter;

// A response's non-reasoning output includes tool arguments. It is text-only only when
// the response contains assistant messages and no generated tool calls.
internal sealed class VisibleTps
{
    private readonly HashSet<string> seen = new();
    private readonly HashSet<string> messages = new();
    private readonly Dictionary<string, (long Start, long End)> timings = new();
    private bool boundary, toolCall;
    public double? Value { get; private set; }
    public long Tokens { get; private set; }
    public long Milliseconds { get; private set; }
    public long MeasuredAtMs { get; private set; }
    public void Reset() { seen.Clear(); messages.Clear(); timings.Clear(); boundary = false; toolCall = false; Value = null; Tokens = Milliseconds = MeasuredAtMs = 0; }
    public void StartTurn() { messages.Clear(); timings.Clear(); boundary = true; toolCall = false; }
    public void Item(JsonElement p)
    {
        var kind = p.At("type").Str();
        if (kind?.EndsWith("_call", StringComparison.Ordinal) == true) toolCall = true;
        else if (kind == "message" && p.At("role").Str() == "assistant")
        {
            // An interrupted commentary can have partially generated tool arguments that never
            // become a response_item. Only finalized answer messages give a safe text-only sample.
            if (p.At("phase").Str() == "final_answer" && p.At("id").Str() is { Length: > 0 } id) messages.Add(id);
            else toolCall = true;
        }
    }
    public void Completed(JsonElement p)
    {
        var item = p.At("item");
        if (item.At("type").Str() != "AgentMessage" || item.At("id").Str() is not { Length: > 0 } id) return;
        timings[id] = (p.At("started_at_ms").Long(), p.At("completed_at_ms").Long());
    }
    public void Usage(JsonElement p)
    {
        var id = p.At("response_id").Str();
        if (string.IsNullOrEmpty(id) || !seen.Add(id)) return;
        var usage = p.At("usage");
        var reasoning = usage.At("reasoning_output_tokens").Num();
        var nonReasoning = usage.At("output_tokens").Long() - (long)(reasoning ?? 0);
        if (boundary && reasoning is >= 0 && !toolCall && messages.Count > 0 && nonReasoning > 0 && messages.All(id => timings.ContainsKey(id)))
        {
            var windows = messages.Select(id => timings[id]).OrderBy(w => w.Start).ToArray();
            if (windows.All(w => w.Start > 0 && w.End > w.Start && w.End - w.Start <= 600000))
            {
                long elapsed = 0, end = 0;
                foreach (var w in windows) { elapsed += Math.Max(0, w.End - Math.Max(end, w.Start)); end = Math.Max(end, w.End); }
                // Keep short answer samples, but the UI explicitly labels their limited precision.
                if (elapsed >= 250 && nonReasoning >= 8) { Tokens = nonReasoning; Milliseconds = elapsed; MeasuredAtMs = windows.Max(w => w.End); Value = 1000d * nonReasoning / elapsed; }
            }
        }
        boundary = true; messages.Clear(); timings.Clear(); toolCall = false;
    }
}
