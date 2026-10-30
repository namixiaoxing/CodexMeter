using System.Globalization;
using System.Text.Json;
namespace CodexMeter;

internal static class J
{
    public static JsonElement At(this JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) ? v : default;
    public static string? Str(this JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    public static double? Num(this JsonElement e)
    {
        double v;
        if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out v) && double.IsFinite(v)) return v;
        if (double.TryParse(e.Str(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v)) return v;
        return null;
    }
    public static long Long(this JsonElement e) => (long)(e.Num() ?? 0);
}

internal sealed record QuotaWindow(double Used, int Minutes, long Reset)
{
    public double Remaining => Math.Clamp(100 - Used, 0, 100);
    public bool Expired => Reset > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= Reset;
    public static QuotaWindow? Parse(JsonElement e, bool camel)
    {
        var used = e.At(camel ? "usedPercent" : "used_percent").Num();
        if (used is null) return null;
        return new(used.Value, (int)e.At(camel ? "windowDurationMins" : "window_minutes").Long(), e.At(camel ? "resetsAt" : "resets_at").Long());
    }
}

internal sealed record QuotaSnapshot(QuotaWindow? FiveHour, QuotaWindow? Weekly, DateTimeOffset Updated, string Source, [property: System.Text.Json.Serialization.JsonIgnore] string? AccountId=null)
{
    public static QuotaSnapshot? Parse(JsonElement e, bool camel, DateTimeOffset time, string source)
    {
        var windows = new[] { QuotaWindow.Parse(e.At("primary"), camel), QuotaWindow.Parse(e.At("secondary"), camel) };
        var five = windows.FirstOrDefault(x => x?.Minutes == 300);
        var week = windows.FirstOrDefault(x => x?.Minutes == 10080);
        if (five is null && week is null) return null;
        return new(five, week, time, source);
    }
}

internal sealed record TokenSnapshot(string Thread, string Model, long Input, long Output, long Cached, long Context, long Capacity, double? Tps, DateTimeOffset Updated, long TpsTokens = 0, long TpsMilliseconds = 0, long TpsMeasuredAtMs = 0, int Turns=0)
{
    public double? RecentTps => Tps is not null && TpsMeasuredAtMs > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= TpsMeasuredAtMs ? Tps : null;
    public bool TpsShort => TpsMilliseconds<3000 || TpsTokens<128;
    public bool TpsOld => TpsMeasuredAtMs>0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-TpsMeasuredAtMs>120000;
    public string TpsKind => TpsOld ? "旧样本" : TpsShort ? "短样本" : "上次正文";
    public string TpsCompact => RecentTps is {} t ? $"{(TpsOld?"旧":TpsShort?"短":"≈")}{t:0}t/s" : "TPS—";
    public double CacheRate => Input > 0 ? Math.Clamp(100d * Cached / Input, 0, 100) : 0;
    public double ContextRate => Capacity > 0 ? Math.Clamp(100d * Context / Capacity, 0, 100) : 0;
}

internal sealed class Settings
{
    public bool ShowOverlay { get; set; } = true;
    public int ScalePercent { get; set; } = 67;
    public string Theme { get; set; } = "miku";
    public int? X { get; set; }
    public int? Y { get; set; }
    public decimal? FiveHourBudgetUsd { get; set; }
    public decimal? WeeklyBudgetUsd { get; set; }
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMeter");
    public static readonly string FilePath = Path.Combine(Folder, "settings.json");
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

internal static class Display
{
    public static string Count(long n) => n.ToString("N0");
    public static string Compact(long n) => n >= 1000000 ? $"{n / 1000000d:0.00}M" : n >= 1000 ? $"{n / 1000d:0.0}k" : n.ToString();
    public static string Percent(QuotaWindow? w) => w is null ? "—" : w.Expired ? "待刷新" : $"{w.Remaining:0.#}%";
    public static string Reset(QuotaWindow? w)
    {
        if (w is null || w.Reset <= 0) return "等待额度数据";
        try
        {
            var at = DateTimeOffset.FromUnixTimeSeconds(w.Reset);
            var left = at - DateTimeOffset.UtcNow;
            var countdown = left <= TimeSpan.Zero ? "已到重置时间，等待查询" : left.TotalDays >= 1 ? $"{(int)left.TotalDays}天 {left.Hours}时" : $"{(int)left.TotalHours}时 {left.Minutes}分";
            return $"{at.LocalDateTime:MM-dd HH:mm} 重置 · {countdown}";
        }
        catch (ArgumentOutOfRangeException) { return "重置时间不可用"; }
    }
    public static string Money(QuotaWindow? w, decimal? budget) => w is null || budget is null || w.Expired ? "未配置" : $"≈ ${(double)budget * w.Remaining / 100:0.00}";
}
