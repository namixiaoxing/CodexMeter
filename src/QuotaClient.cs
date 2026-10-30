using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace CodexMeter;

internal static class QuotaClient
{
    internal static string? FindCodex()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir.Trim('"'), "codex.exe");
            if (File.Exists(candidate)) return candidate;
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        try { return Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // Short-lived official read-only RPC. No browser cookies, login-token files or refresh-token handling.
    public static async Task<(QuotaSnapshot? Quota, string? Error)> ReadAsync(CancellationToken cancellation)
    {
        var exe = FindCodex();
        if (exe is null) return (null, "未找到 Codex CLI，使用日志额度");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var process = new Process();
        process.StartInfo = new(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in new[] { "app-server", "--stdio", "-c", "analytics.enabled=false" }) process.StartInfo.ArgumentList.Add(a);
        try
        {
            if (!process.Start()) return (null, "额度查询未启动");
            // Discard diagnostics rather than storing potentially sensitive app-server logs.
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            async Task Send(object value) => await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), timeout.Token);
            await Send(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "codex_meter", title = "Codex Meter", version = "0.1.0" } } });
            while (!timeout.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                if (line is null) break;
                using var doc = JsonDocument.Parse(line);
                var msg = doc.RootElement;
                if (msg.At("id").Long() == 1)
                {
                    if (msg.At("error").ValueKind == JsonValueKind.Object) return (null, "Codex 初始化失败，使用日志额度");
                    await Send(new { method = "initialized", @params = new { } });
                    await Send(new { id = 2, method = "account/rateLimits/read" });
                }
                if (msg.At("id").Long() == 2)
                {
                    if (msg.At("error").ValueKind == JsonValueKind.Object) return (null, "额度接口暂不可用，使用日志额度");
                    return (ParseResult(msg.At("result")), null);
                }
            }
            return (null, "未收到额度数据，使用日志额度");
        }
        catch (OperationCanceledException) { return (null, "额度查询超时，使用日志额度"); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception) { return (null, "额度查询失败，使用日志额度"); }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

    internal static QuotaSnapshot? ParseResult(JsonElement result)
    {
        var limits = result.At("rateLimitsByLimitId").At("codex");
        if (limits.ValueKind != JsonValueKind.Object) limits = result.At("rateLimits");
        var quota=QuotaSnapshot.Parse(limits, true, DateTimeOffset.UtcNow, "Codex 额度接口");
        return quota is null ? null : quota with { AccountId=result.At("accountId").Str() };
    }
}
