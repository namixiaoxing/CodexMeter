using System.Text;
using System.Text.Json;
namespace CodexMeter;

internal sealed record ApiRate(string Model, decimal Input, decimal Cached, decimal Write, decimal Output)
{
    public static readonly ApiRate[] Rates = { new("gpt-6-astra",10,1,12.5m,50), new("gpt-6.1-sol",2,.1m,2.5m,10), new("gpt-6.1-terra",1,.05m,1.25m,5) };
    public static decimal? Cost(string model, JsonElement usage)
    {
        var rate = Rates.FirstOrDefault(r => r.Model == model); if (rate is null) return null;
        if(usage.At("input_tokens").Num() is null || usage.At("output_tokens").Num() is null) return null;
        var input = usage.At("input_tokens").Long(); var cached = usage.At("cached_input_tokens").Long();
        var write = usage.At("cache_write_input_tokens").Long(); var output = usage.At("output_tokens").Long();
        if (input < 0 || cached < 0 || write < 0 || output < 0 || cached + write > input) return null;
        var longInput = input > 272000 ? 2m : 1m; var longOutput = input > 272000 ? 1.5m : 1m;
        return ((input-cached-write)*rate.Input*longInput + cached*rate.Cached*longInput + write*rate.Write*longInput + output*rate.Output*longOutput)/1000000m;
    }
}
internal sealed record CostPoint(string Id, string Account, string Model, DateTimeOffset Time, decimal? Cost);
internal sealed record QuotaPoint(string Account, DateTimeOffset Time, QuotaWindow? Five, QuotaWindow? Week);
internal sealed record WindowBudget(decimal Total, decimal Low, decimal High, double PercentSpan, int Calls)
{
    public string Summary(QuotaWindow? current) => current is null || current.Expired ? "等待新窗口数据" : $"余 ≈${Total * (decimal)current.Remaining/100:0.00} / 总 ≈${Total:0.00}";
}
internal sealed record BudgetSnapshot(WindowBudget? Five, WindowBudget? Week, string Status, int Calls, decimal RecordedCost, [property: System.Text.Json.Serialization.JsonIgnore] string? AccountId=null)
{
    public static BudgetSnapshot Waiting = new(null,null,"正在读取本机额度样本…",0,0);
}

internal static class BudgetMath
{
    public static WindowBudget? Fit(IReadOnlyList<CostPoint> costs, IReadOnlyList<QuotaPoint> observations, bool weekly)
    {
        decimal totalCost = 0; double span = 0; var calls = 0; var chunks=0;
        foreach (var group in observations.GroupBy(q => (weekly ? q.Week : q.Five)?.Reset).Where(g => g.Key is > 0))
        {
            var ordered = group.OrderBy(q=>q.Time).ToArray();
            if (ordered.Length < 2) continue;
            var start = ordered[0];
            foreach (var end in ordered.Skip(1))
            {
                var a=(weekly?start.Week:start.Five)!; var b=(weekly?end.Week:end.Five)!;
                if (b.Used < a.Used) { start=end; continue; } // Reset credits / corrections are never spending.
                var delta=b.Used-a.Used;
                if (delta < 3) continue;
                var slice=costs.Where(c=>c.Time>start.Time && c.Time<=end.Time).ToArray();
                if (slice.Length>0 && slice.All(c=>c.Cost.HasValue))
                {
                    var amount=slice.Sum(c=>c.Cost!.Value);
                    if (amount>0) { totalCost+=amount; span+=delta; calls+=slice.Length; chunks++; }
                }
                start=end;
            }
        }
        if (span<3 || totalCost<=0) return null;
        // The percentage is rounded to whole points: expose that uncertainty rather than cents of precision.
        var estimate=totalCost*100/(decimal)span;
        return new(estimate,totalCost*100/(decimal)(span+chunks),totalCost*100/(decimal)Math.Max(1,span-chunks),span,calls);
    }
}

// Numeric, local-only calibration. No messages are retained and no inference request is issued.
internal sealed class BudgetMonitor
{
    internal sealed record AccountBinding(DateTimeOffset From,string Account);
    private sealed class Cursor { public long Offset; public string Model="",Account=""; public DateTime Write; public List<AccountBinding> Bindings=new(); }
    private readonly string root;
    private readonly string archive;
    private readonly Dictionary<string,Cursor> cursors=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,CostPoint> costs=new();
    private readonly List<QuotaPoint> quotas=new();
    private DateTime nextPoll;
    private bool firstScan=true;
    private BudgetSnapshot snapshot=BudgetSnapshot.Waiting;
    private string? requestedAccount,appliedAccount;
    private long requestedAtMs;
    private readonly string bindingPath;
    private readonly Dictionary<string,List<AccountBinding>> bindings;
    private static string AccountKey(string raw)=>string.IsNullOrEmpty(raw)?"":Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    public BudgetSnapshot Snapshot => Volatile.Read(ref requestedAccount) is {} a && snapshot.AccountId!=a ? new(null,null,"已切换登录账号，正在重新采样…",0,0) : snapshot;
    public void SelectAccount(string? account)
    {
        if(string.IsNullOrEmpty(account))return;
        account=AccountKey(account);
        if(account==Volatile.Read(ref requestedAccount))return;
        Interlocked.Exchange(ref requestedAtMs,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Volatile.Write(ref requestedAccount,account);
    }
    public BudgetMonitor(string home,string? bindingsFile=null)
    {
        root=Path.Combine(home,"sessions"); archive=Path.Combine(home,"archived_sessions");
        bindingPath=bindingsFile??Path.Combine(Settings.Folder,"account-bindings.json");
        try { bindings=JsonSerializer.Deserialize<Dictionary<string,List<AccountBinding>>>(File.ReadAllText(bindingPath))??new(); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { bindings=new(); }
    }
    private void SaveBindings()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(bindingPath)!); var tmp=bindingPath+".tmp"; File.WriteAllText(tmp,JsonSerializer.Serialize(bindings)); File.Move(tmp,bindingPath,true); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
    }
    public void Poll(string? selectedThread, bool force=false)
    {
        var requested=Volatile.Read(ref requestedAccount);
        if (!force && requested==appliedAccount && DateTime.UtcNow<nextPoll) return;
        appliedAccount=requested;
        nextPoll=DateTime.UtcNow.AddSeconds(30);
        if (!Directory.Exists(root)) return;
        var since=DateTimeOffset.UtcNow.AddDays(-8);
        var budget=64*1024*1024;
        try
        {
            foreach (var file in new[]{root,archive}.Where(Directory.Exists).SelectMany(r=>Directory.EnumerateFiles(r,"*.jsonl",SearchOption.AllDirectories)).Select(p=>new FileInfo(p)).Where(f=>f.LastWriteTimeUtc>=since.UtcDateTime).OrderByDescending(f=>f.LastWriteTimeUtc))
            {
                if (budget<=0) break;
                try {
                if (!cursors.TryGetValue(file.FullName,out var cursor) || string.IsNullOrEmpty(cursor.Account))
                {
                    cursor=new(); cursors[file.FullName]=cursor;
                    using var header=new StreamReader(new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete));
                    var first=header.ReadLine(); if (first is null) continue;
                    using var meta=JsonDocument.Parse(first); cursor.Account=AccountKey(meta.RootElement.At("payload").At("creator_account_id").Str()??"");
                    var stem=Path.GetFileNameWithoutExtension(file.Name);
                    if(stem.Length>=36 && Guid.TryParse(stem[^36..],out _))
                    {
                        var thread=stem[^36..];
                        if(!bindings.TryGetValue(thread,out var history)) {history=new();bindings[thread]=history;}
                        cursor.Bindings=history;
                    }
                    // Large legacy logs are bounded: unknown model spans remain unpriced until a turn_context.
                    if (file.Length>16*1024*1024) cursor.Offset=file.Length-16*1024*1024;
                }
                if(requested is not null && selectedThread is not null && file.Name.EndsWith(selectedThread+".jsonl",StringComparison.OrdinalIgnoreCase) && (cursor.Bindings.Count==0 || cursor.Bindings[^1].Account!=requested))
                { cursor.Bindings.Add(new(DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref requestedAtMs)),requested)); SaveBindings(); }
                if (cursor.Write==file.LastWriteTimeUtc && cursor.Offset>=file.Length) continue;
                if (cursor.Offset>file.Length) { cursor.Offset=0; cursor.Model=""; }
                using var stream=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                stream.Position=cursor.Offset;
                var bytes=new byte[(int)Math.Min(Math.Min(stream.Length-cursor.Offset,16*1024*1024),budget)];
                var read=stream.Read(bytes); if(read==0) { cursor.Write=file.LastWriteTimeUtc; continue; } var end=Array.LastIndexOf(bytes,(byte)10,read-1,read);
                if (end<0) continue;
                budget-=end+1; cursor.Offset+=end+1; cursor.Write=file.LastWriteTimeUtc;
                var start=0;
                while(start<=end)
                {
                    var length=bytes.AsSpan(start,end+1-start).IndexOf((byte)10);
                    if(length<0)break;
                    var prefix=bytes.AsSpan(start,Math.Min(length,256));
                    // Never decode/copy chat bodies, base64 images or tool results during cost scans.
                    if(length<1024*1024 && (prefix.IndexOf("token_usage_record"u8)>=0 || prefix.IndexOf("turn_context"u8)>=0 || prefix.IndexOf("token_count"u8)>=0))
                        Consume(Encoding.UTF8.GetString(bytes,start,length),cursor,since);
                    start+=length+1;
                }
                } catch (Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { }
            }
            var selected=cursors.FirstOrDefault(c=>selectedThread is not null && c.Key.EndsWith(selectedThread+".jsonl",StringComparison.OrdinalIgnoreCase)).Value;
            var account=requested??selected?.Account;
            if (string.IsNullOrEmpty(account)) { snapshot=new(null,null,"等待当前账号的本机记录",0,0); return; }
            foreach(var key in costs.Where(p=>p.Value.Time<since).Select(p=>p.Key).ToArray()) costs.Remove(key);
            quotas.RemoveAll(q=>q.Time<since);
            var c=costs.Values.Where(c=>c.Account==account && c.Time>=since).OrderBy(c=>c.Time).ToArray();
            var q=quotas.Where(q=>q.Account==account && q.Time>=since).ToArray();
            var five=BudgetMath.Fit(c,q,false); var week=BudgetMath.Fit(c,q,true);
            var status=five is null && week is null ? "采样中：需要至少 3% 额度变化" : $"API 等价美元 · {(Math.Min(five?.PercentSpan??100,week?.PercentSpan??100)<10?"初步样本":"本机样本")}";
            snapshot=new(five,week,status+(requested is null?" · 会话账号":" · 当前登录账号"),c.Count(p=>p.Cost.HasValue),c.Sum(p=>p.Cost??0),account);
            if(firstScan) { firstScan=false; System.Runtime.GCSettings.LargeObjectHeapCompactionMode=System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce; GC.Collect(2,GCCollectionMode.Forced,true,true); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { snapshot=Snapshot with { Status="历史样本暂不可读，稍后重试" }; }
    }
    private void Consume(string line,Cursor cursor,DateTimeOffset since)
    {
        if (!line.Contains("\"turn_context\"") && !line.Contains("\"token_usage_record\"") && !(line.Contains("\"token_count\"") && line.Contains("\"rate_limits\""))) return;
        try
        {
            using var doc=JsonDocument.Parse(line); var e=doc.RootElement; var p=e.At("payload");
            var type=e.At("type").Str();
            if (type=="turn_context") { cursor.Model=p.At("model").Str()??""; return; }
            if (!DateTimeOffset.TryParse(e.At("timestamp").Str(),out var time) || time<since) return;
            var binding=cursor.Bindings.LastOrDefault(b=>b.From<=time);
            var account=string.IsNullOrEmpty(binding?.Account)?cursor.Account:binding.Account;
            if (type=="token_usage_record" && p.At("response_id").Str() is {Length:>0} id)
            {
                var key=account+"/"+id;
                var value=new CostPoint(id,account,cursor.Model,time,ApiRate.Cost(cursor.Model,p.At("usage")));
                if (!costs.TryGetValue(key,out var prior) || prior.Cost is null && value.Cost.HasValue) costs[key]=value;
            }
            if (type=="event_msg" && p.At("type").Str()=="token_count")
            {
                var q=QuotaSnapshot.Parse(p.At("rate_limits"),false,time,"本机样本");
                if(q is not null) quotas.Add(new(account,time,q.FiveHour,q.Weekly));
            }
        }
        catch (Exception e) when(e is JsonException or InvalidOperationException or OverflowException) { }
    }
}
