using System.Text.Json;
using CodexMeter;
using System.Xml.Linq;

var count = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
using var json = JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":90,"windowDurationMins":300,"resetsAt":2000000000}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":12,"windowDurationMins":300,"resetsAt":2000000000},"secondary":{"usedPercent":30,"windowDurationMins":10080,"resetsAt":2000000001}}}}""");
var quota = QuotaClient.ParseResult(json.RootElement)!;
Check(quota.FiveHour?.Remaining == 88 && quota.Weekly?.Remaining == 70, "multi-bucket precedence and remaining percent");
using var unknown = JsonDocument.Parse("""{"primary":{"usedPercent":20,"windowDurationMins":15}}""");
Check(QuotaSnapshot.Parse(unknown.RootElement, true, DateTimeOffset.UtcNow, "test") is null, "do not mislabel unknown window as 5h");
Check(new QuotaWindow(120, 300, 2000000000).Remaining == 0, "clamp over-limit percent");
Check(new QuotaWindow(-10, 300, 2000000000).Remaining == 100, "clamp negative percent");
Check(Display.Percent(new(30, 300, 1)) == "待刷新", "expired quota is not falsely reset to 100%");
Check(Display.Money(new(25, 300, 2000000000), 100m).Contains("75.00"), "explicit dollar baseline only");
Check(Display.Money(new(25, 300, 2000000000), null) == "未配置", "no invented default dollar budget");
var startup=XDocument.Parse(StartupTaskXml.Create(@"D:\Meter & tools\CodexMeter.exe","S-1-5-21-fixture"));
XNamespace scheduler="http://schemas.microsoft.com/windows/2004/02/mit/task";
string? Startup(string name)=>startup.Descendants(scheduler+name).FirstOrDefault()?.Value;
Check(Startup("Command")==@"D:\Meter & tools\CodexMeter.exe"&&Startup("WorkingDirectory")==@"D:\Meter & tools","startup registration preserves executable paths and XML special characters");
Check(Startup("LogonTrigger") is not null&&Startup("LogonType")=="InteractiveToken"&&Startup("RunLevel")=="LeastPrivilege","independent Windows startup uses the interactive current user without elevation or a password");
Check(Startup("MultipleInstancesPolicy")=="IgnoreNew"&&Startup("ExecutionTimeLimit")=="PT0S"&&Startup("StopIfGoingOnBatteries")=="false","startup task does not duplicate, expire after three days or stop on battery power");

var layout = HostWindow.DockBounds(new(100, 200, 1600, 900), new Size(517,86), 96);
Check(layout.Height == 86 && layout.Right == 1688 && layout.Bottom == 1088, "two-row overlay keeps a fixed 12px right/bottom margin");
var moved = HostWindow.DockBounds(new(300, 250, 1600, 900), new Size(517,86), 96);
Check(moved.X - layout.X == 200 && moved.Y - layout.Y == 50, "host move preserves bottom-right attachment");
var resized = HostWindow.DockBounds(new(100, 200, 1800, 1000), new Size(517,86), 96);
Check(resized.X - layout.X == 200 && resized.Y - layout.Y == 100, "host resize follows right and bottom edges");

JsonElement Element(object p) => JsonSerializer.SerializeToElement(p);
var partialTps = new VisibleTps();
partialTps.Item(Element(new { type = "message", role = "assistant", id = "incomplete", phase = "final_answer" }));
partialTps.Completed(Element(new { item = new { type = "AgentMessage", id = "incomplete" }, started_at_ms = 1000, completed_at_ms = 2000 }));
partialTps.Usage(Element(new { response_id = "partial", usage = new { output_tokens = 400 } }));
Check(partialTps.Value is null, "tail read without a response boundary cannot invent text TPS");
partialTps.Item(Element(new { type = "message", role = "assistant", id = "clean", phase = "final_answer" }));
partialTps.Completed(Element(new { item = new { type = "AgentMessage", id = "clean" }, started_at_ms = 3000, completed_at_ms = 7000 }));
partialTps.Usage(Element(new { response_id = "clean", usage = new { output_tokens = 300, reasoning_output_tokens = 100 } }));
Check(partialTps.Value == 50, "first complete pure-text sample recovers after partial history");
partialTps.Reset(); Check(partialTps.Value is null, "thread switch discards another thread's TPS");
partialTps.StartTurn();
partialTps.Item(Element(new { type = "message", role = "assistant", id = "aborted", phase = "commentary" }));
partialTps.Completed(Element(new { item = new { type = "AgentMessage", id = "aborted" }, started_at_ms = 1000, completed_at_ms = 3010 }));
partialTps.Usage(Element(new { response_id = "interrupted", usage = new { output_tokens = 2503, reasoning_output_tokens = 913 } }));
Check(partialTps.Value is null, "interrupted commentary with uncommitted tool arguments cannot produce 791 tok/s");
partialTps.StartTurn();
partialTps.Item(Element(new { type = "message", role = "assistant", id = "short", phase = "final_answer" }));
partialTps.Completed(Element(new { item = new { type = "AgentMessage", id = "short" }, started_at_ms = 1000, completed_at_ms = 1784 }));
partialTps.Usage(Element(new { response_id = "short", usage = new { output_tokens = 567, reasoning_output_tokens = 417 } }));
Check(Math.Abs(partialTps.Value!.Value-150000d/784)<.00001, "short answer samples remain available instead of disappearing");
var stale = new TokenSnapshot("test", "test", 0,0,0,0,0,53.2,DateTimeOffset.UtcNow,188,3536,DateTimeOffset.UtcNow.AddMinutes(-3).ToUnixTimeMilliseconds());
Check(stale.RecentTps==53.2 && stale.TpsOld && stale.TpsCompact.StartsWith("旧"), "old TPS remains visible with an explicit old-sample label");
var fresh = stale with { TpsMeasuredAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
Check(fresh.RecentTps == 53.2, "recent reliable output sample remains visible");
var brief=fresh with { TpsTokens=150,TpsMilliseconds=784,Tps=150000d/784 };
Check(brief.TpsShort && brief.TpsCompact.StartsWith("短"),"short TPS is visibly marked instead of being presented as sustained speed");
Check(Theme.RemainingColor(80) == Theme.Miku && Theme.RemainingColor(0) == Theme.Low, "quota color is Miku teal with capacity and red when exhausted");
Check(!HostWindow.CanFit(new(0,0,400,400),new(517,86),96), "narrow hosts hide the overlay instead of shrinking its font");

var costJson=Element(new { input_tokens=100000,cached_input_tokens=50000,cache_write_input_tokens=10000,output_tokens=10000,reasoning_output_tokens=5000 });
Check(ApiRate.Cost("gpt-6.1-sol",costJson)==.21m,"API cost separates cached reads/writes and does not charge reasoning twice");
Check(ApiRate.Cost("gpt-6.1-terra",costJson)==.105m && ApiRate.Cost("gpt-6-astra",costJson)==1.075m,"only three requested model rate cards are applied");
Check(ApiRate.Cost("other-model",costJson) is null,"unsupported models cannot contaminate quota-dollar calibration");
var longJson=Element(new { input_tokens=1000000,cached_input_tokens=500000,cache_write_input_tokens=100000,output_tokens=100000 });
Check(ApiRate.Cost("gpt-6.1-sol",longJson)==3.7m,"long-context API input/cache and output multipliers are distinct");
var moment=DateTimeOffset.UtcNow.AddHours(-1);
var costSamples=new[] {new CostPoint("r","a","gpt-6.1-sol",moment.AddSeconds(30),.5m)};
var quotaSamples=new[] { new QuotaPoint("a",moment,new(10,300,2000000000),new(20,10080,2000000001)), new QuotaPoint("a",moment.AddMinutes(1),new(15,300,2000000000),new(25,10080,2000000001)) };
var fitted=BudgetMath.Fit(costSamples,quotaSamples,false)!;
Check(fitted.Total==10m && fitted.Low<10m && fitted.High>10m,"dollar budget is cost delta divided by percent delta with rounding bounds");
Check(BudgetMath.Fit(new[]{costSamples[0] with {Cost=null}},quotaSamples,false) is null,"unknown-cost calls invalidate the affected calibration interval");
var resetSamples=new[] {quotaSamples[0],quotaSamples[1] with {Five=new(0,300,2000000002)}};
Check(BudgetMath.Fit(costSamples,resetSamples,false) is null,"quota resets are not treated as consumption");

var temp = Path.Combine(Path.GetTempPath(), "codex-meter-test-" + Guid.NewGuid());
var turnPrefix=JsonSerializer.Serialize(new{type="event_msg",payload=new{type="task_started",turn_id="prefix-turn"}})+"\n";
var largeToolLine=JsonSerializer.Serialize(new{type="response_item",payload=new{type="function_call_output",output=new string('x',20000)}})+"\n";
var turnTail=JsonSerializer.Serialize(new{type="event_msg",payload=new{type="task_started",turn_id="tail-turn"}})+"\n";
using(var stream=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(turnPrefix+largeToolLine+turnTail)))
{
    var counter=new TurnCounter(); var boundary=counter.ReadPrefix(stream,1000);
    Check(counter.Count==1 && boundary==System.Text.Encoding.UTF8.GetByteCount(turnPrefix+largeToolLine),"large-log prefix counting stops at a complete line without buffering a tool body");
    stream.Position=boundary; using var tailReader=new StreamReader(stream); using var tailDoc=JsonDocument.Parse(tailReader.ReadLine()!); counter.Add(tailDoc.RootElement.At("payload"));
    Check(counter.Count==2,"joining the counted prefix and metric tail loses no turn");
}
Directory.CreateDirectory(Path.Combine(temp, "sessions"));
try
{
    using var reader = new SessionReader(temp);
    void Event(object p) => reader.Consume(JsonSerializer.Serialize(new { type = "event_msg", timestamp = "2026-10-30T00:00:00Z", payload = p }));
    Event(new { type = "task_started", turn_id = "turn-1", model_context_window = 100000 });
    Event(new { type = "task_started", turn_id = "turn-1", model_context_window = 100000 });
    reader.Consume(JsonSerializer.Serialize(new {type="turn_context",payload=new {turn_id="turn-1",model="gpt-6.1-sol"}}));
    Check(reader.TurnCount==1,"duplicate start/context records count as one conversation turn");
    Event(new { type = "item_completed", item = new { type = "Reasoning", id = "reason" }, started_at_ms = 1000, completed_at_ms = 3000 });
    Event(new { type = "item_completed", item = new { type = "AgentMessage", id = "m1" }, started_at_ms = 3000, completed_at_ms = 6536 });
    reader.Consume(JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "message", id = "m1", role = "assistant", phase = "final_answer" } }));
    var usage = JsonSerializer.Serialize(new { type = "token_usage_record", payload = new { turn_id = "turn-1", response_id = "r1", usage = new { output_tokens = 670, reasoning_output_tokens = 482 } } });
    reader.Consume(usage); reader.Consume(usage);
    Check(Math.Abs(reader.TestTps!.Value - 188000d / 3536) < 0.00001, "visible TPS subtracts reasoning; 188 text tokens / 3.536 sec = 53.17 tok/s");
    var measured = reader.TestTps;
    Event(new { type = "task_started", turn_id = "turn-2", model_context_window = 100000 });
    Check(reader.TurnCount==2,"a new user turn increments the counter independently of tool and token events");
    reader.Consume(JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "custom_tool_call" } }));
    Event(new { type = "item_completed", item = new { type = "AgentMessage", id = "m2" }, started_at_ms = 1000, completed_at_ms = 4000 });
    reader.Consume(JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "message", id = "m2", role = "assistant" } }));
    reader.Consume(JsonSerializer.Serialize(new { type = "token_usage_record", payload = new { response_id = "r2", usage = new { output_tokens = 12000, reasoning_output_tokens = 100 } } }));
    Check(reader.TestTps == measured, "mixed text + tool-argument responses cannot inflate TPS");
    Event(new { type = "item_completed", item = new { type = "AgentMessage", id = "m3" }, started_at_ms = 1000, completed_at_ms = 1000 });
    reader.Consume(JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "message", id = "m3", role = "assistant" } }));
    reader.Consume(JsonSerializer.Serialize(new { type = "token_usage_record", payload = new { response_id = "r3", usage = new { output_tokens = 500 } } }));
    Check(reader.TestTps == measured, "zero-duration text cannot produce a huge TPS");
    reader.Consume("{malformed\"event_msg\"");
    Check(reader.TestTps == measured, "malformed record does not kill reader");

    const string a = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", b = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    var titleFile=Path.Combine(temp,"session_index.jsonl");
    string Title(string id,string name)=>JsonSerializer.Serialize(new{id,thread_name=name})+"\n";
    File.WriteAllText(titleFile,Title(a,"当前页面")+Title(b,"旧的消息流"));
    var titles=new ThreadTitleIndex(temp);
    Check(titles.Select("当前页面",b).ThreadId==a,"current page overrides a stale IPC stream from another conversation");
    Check(titles.Select("旧的消息流",a).ThreadId==b&&titles.Select("当前页面",b).ThreadId==a,"switching away and back follows the displayed page even without new stream events");
    var unresolved=titles.Select("尚无记录的新对话",b);
    Check(!unresolved.CanShowConversationMetrics(b),"new pages use quota-only mode even when IPC retains an old conversation");
    Check(!titles.Select("当前页面",b).CanShowConversationMetrics(null),"a conversation without a local session yet uses quota-only mode");
    Check(!titles.Select("当前页面",b).CanShowConversationMetrics(b),"loading another conversation cannot briefly display stale overlay metrics");
    Check(titles.Select("当前页面",b).CanShowConversationMetrics(a),"returning to an indexed conversation restores the full overlay");
    using(var missingReader=new SessionReader(temp))
    {
        missingReader.Poll(unresolved.PollTarget);
        Check(unresolved.ThreadId is null&&missingReader.SelectedThread is null,"a known unmatched page cannot display the previous conversation's metrics");
    }
    File.AppendAllText(titleFile,Title(b,"当前页面"));
    Check(titles.Select("当前页面",b).ThreadId is null,"identical titles are not resolved by guessing from a stale stream");
    File.AppendAllText(titleFile,Title(b,"重新命名的页面"));
    Check(titles.Select("当前页面",b).ThreadId==a&&titles.Select("重新命名的页面",a).ThreadId==b,"renames replace old titles in the local index");
    Check(titles.Select(null,b).ThreadId==b&&!titles.Select(null,b).HasPage,"unavailable page information explicitly falls back to IPC");
    var noIndex=new ThreadTitleIndex(Path.Combine(temp,"missing-index")).Select("新页面",b);
    Check(noIndex.ThreadId is null&&noIndex.HasPage,"a missing local title index cannot select a stale IPC conversation for a known page");
    var file = Path.Combine(temp, "sessions", "rollout-" + a + ".jsonl");
    var meta = JsonSerializer.Serialize(new { type = "session_meta", payload = new { originator = "Codex Desktop", source = "vscode" } });
    var tokens = JsonSerializer.Serialize(new { type = "event_msg", timestamp = "2026-10-30T00:00:00Z", payload = new { type = "token_count", info = new { total_token_usage = new { input_tokens = 1000, cached_input_tokens = 800, output_tokens = 50 }, last_token_usage = new { total_tokens = 350 }, model_context_window = 1000 } } });
    var started=JsonSerializer.Serialize(new {type="event_msg",payload=new{type="task_started",turn_id="file-turn"}});
    File.WriteAllText(file, meta + "\n" + started+"\n"+tokens[..^2]);
    reader.Poll(a); Check(reader.Snapshot is null, "incomplete final JSONL record is not committed");
    Check(reader.TurnCount==1,"turn count is available before the first token snapshot");
    File.AppendAllText(file, tokens[^2..] + "\n"); reader.Poll(a);
    var s = reader.Snapshot!;
    Check(s.Turns==1,"token snapshot carries the selected conversation turn count");
    Check(s.Input == 1000 && s.Output == 50 && s.Context == 350 && s.ContextRate == 35 && s.CacheRate == 80, "input/output/context/cache formulas");
    reader.Poll(b); Check(reader.Snapshot is null && reader.TurnCount==0, "selected idle/missing thread never displays another thread's tokens or turns");
    reader.Poll(a); Check(reader.Snapshot?.Input == 1000, "switching back to an idle thread reparses immediately");
    File.WriteAllText(Path.Combine(temp, "sessions", "rollout-" + b + ".jsonl"), JsonSerializer.Serialize(new { type = "session_meta", payload = new { originator = "Codex Desktop", source = new { subagent = "parent" } } }) + "\n" + tokens + "\n");
    reader.Poll(b); Check(reader.Snapshot is null, "subagent replay is excluded by first-line root filter");

    reader.Poll(a);
    var compacted=JsonSerializer.Serialize(new{type="compacted",payload=new{latest_token_usage_record=new{thread_token_usage=new{input_tokens=1000,cached_input_tokens=800,output_tokens=50}}}});
    var afterCompaction=JsonSerializer.Serialize(new {type="event_msg",timestamp="2026-10-30T00:01:00Z",payload=new{type="token_count",info=new{total_token_usage=new{input_tokens=1080,cached_input_tokens=840,output_tokens=55},last_token_usage=new{total_tokens=85},model_context_window=1000}}});
    File.AppendAllText(file,compacted+"\n"+afterCompaction+"\n");
    reader.Poll(a); var compactSnapshot=reader.Snapshot!;
    Check(compactSnapshot.Context==85&&compactSnapshot.Input==1080&&compactSnapshot.Output==55&&compactSnapshot.Cached==840&&compactSnapshot.Turns==1,"compaction lowers current context while lifetime usage and turns remain intact");
    reader.Poll(b); reader.Poll(a);
    Check(reader.Snapshot==compactSnapshot,"switching back after compaction restores the same counts, cache, context and turns");

    var calibrationHome=Path.Combine(temp,"budget-fixtures");
    Directory.CreateDirectory(Path.Combine(calibrationHome,"sessions")); Directory.CreateDirectory(Path.Combine(calibrationHome,"archived_sessions"));
    var at=DateTimeOffset.UtcNow.AddMinutes(-30);
    string Entry(string type,object payload,DateTimeOffset time)=>JsonSerializer.Serialize(new{type,payload,timestamp=time});
    var costMeta=Entry("session_meta",new{creator_account_id="fixture-account"},at);
    var contextMeta=Entry("turn_context",new{model="gpt-6.1-sol"},at);
    string Point(int used,DateTimeOffset time)=>Entry("event_msg",new{type="token_count",rate_limits=new{primary=new{used_percent=used,window_minutes=300,resets_at=2000000000},secondary=new{used_percent=used,window_minutes=10080,resets_at=2000000001}}},time);
    var priceRecord=Entry("token_usage_record",new{response_id="unique-response",usage=new{input_tokens=100000,cached_input_tokens=50000,cache_write_input_tokens=10000,output_tokens=10000}},at.AddMinutes(1));
    var history=string.Join("\n",costMeta,contextMeta,Point(10,at),priceRecord,Point(15,at.AddMinutes(2)))+"\n";
    File.WriteAllText(Path.Combine(calibrationHome,"sessions","rollout-"+a+".jsonl"),history);
    File.WriteAllText(Path.Combine(calibrationHome,"archived_sessions","replay.jsonl"),history);
    var foreignHistory=history.Replace("fixture-account","other-account").Replace("unique-response","other-response").Replace("\"input_tokens\":100000","\"input_tokens\":200000");
    File.WriteAllText(Path.Combine(calibrationHome,"sessions","other.jsonl"),foreignHistory);
    var bindingFile=Path.Combine(temp,"account-bindings.json");
    var calibration=new BudgetMonitor(calibrationHome,bindingFile); calibration.Poll(a,true);
    Check(calibration.Snapshot.Calls==1 && calibration.Snapshot.RecordedCost==.21m,"history scan deduplicates replayed response IDs and isolates accounts");
    Check(calibration.Snapshot.Five?.Total==4.2m && calibration.Snapshot.Week?.Total==4.2m,"history scanner calibrates both quota windows from observed cost deltas");
    calibration.SelectAccount("other-account");
    Check(calibration.Snapshot.Five is null,"changing login immediately hides the previous account's dollar estimate");
    calibration.Poll(a);
    Check(calibration.Snapshot.RecordedCost==.41m && calibration.Snapshot.Five?.Total==8.2m,"active login takes priority over an old conversation's creator account");
    var future=DateTimeOffset.UtcNow.AddMilliseconds(1);
    var newPrice=priceRecord.Replace("unique-response","new-login-response");
    using(var priceDoc=JsonDocument.Parse(newPrice)) newPrice=Entry("token_usage_record",priceDoc.RootElement.At("payload"),future);
    File.AppendAllText(Path.Combine(calibrationHome,"sessions","rollout-"+a+".jsonl"),newPrice+"\n");
    calibration.Poll(a,true);
    Check(calibration.Snapshot.RecordedCost==.62m,"new usage in an old conversation is attributed to the confirmed current login");
    var reloaded=new BudgetMonitor(calibrationHome,bindingFile); reloaded.SelectAccount("other-account"); reloaded.Poll(a,true);
    Check(reloaded.Snapshot.RecordedCost==.62m && !File.ReadAllText(bindingFile).Contains("other-account"),"login attribution survives restart using hashed account keys only");
}
finally { Directory.Delete(temp, true); }
Console.WriteLine($"{count} checks passed.");
