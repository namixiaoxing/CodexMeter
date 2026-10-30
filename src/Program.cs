using System.Diagnostics;
using System.Text.Json;
namespace CodexMeter;

internal static class Program
{
    internal static bool TraceAttachment { get; private set; }
    [STAThread]
    private static void Main(string[] args)
    {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        if(args.Contains("--enable-autofollow")||args.Contains("--disable-autofollow"))
        {
            try
            {
                AutoFollow.Enabled=args.Contains("--enable-autofollow");
                if(args.Contains("--enable-autofollow"))AutoFollow.Start();
                if(File.Exists(AutoFollow.ErrorFile))File.Delete(AutoFollow.ErrorFile);
            }
            catch(Exception e) when(e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                Directory.CreateDirectory(Settings.Folder); File.WriteAllText(AutoFollow.ErrorFile,e.Message); Environment.ExitCode=1;
            }
            return;
        }
        if (args.Contains("--probe")) { Probe(home, args.Contains("--online")).GetAwaiter().GetResult(); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        TraceAttachment = args.Contains("--trace-attachment");
        if (args.Contains("--preview")) { UiDiagnostics.Preview(); return; }
        if (args.Contains("--attachment-check")) { UiDiagnostics.AttachmentCheck(); return; }
        if (args.Contains("--follow-benchmark")) { UiDiagnostics.FollowBenchmark(); return; }
        using var singleton = new Mutex(true, "Local\\CodexMeter", out var first);
        if (!first) return;
        if (args.Contains("--host-probe"))
        {
            var host = HostWindow.Find();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "host-probe.json"), JsonSerializer.Serialize(new { handle = host?.Handle.ToInt64(), process = host?.Process, client = host?.Client, visible = host?.Visible, minimized = host?.Minimized, dpi = host?.Dpi }));
            return;
        }
        try { using var process=Process.GetCurrentProcess(); process.PriorityClass=ProcessPriorityClass.BelowNormal; }
        catch(System.ComponentModel.Win32Exception) { }
        using var app = new HostSupervisor(home, args.Contains("--show"));
        Application.Run(app);
        GC.KeepAlive(singleton);
    }

    private static async Task Probe(string home, bool online)
    {
        using var reader = new SessionReader(home);
        using var ipc = new CodexIpcActiveThreadMonitor();
        await Task.Delay(1200);
        var route = ipc.GetStatus();
        var selection=new ThreadTitleIndex(home).Select(new PageThreadReader().Read(HostWindow.Find()),route.ThreadId);
        reader.Poll(selection.PollTarget);
        var s = reader.Snapshot;
        var q = reader.Quota;
        var estimator=new BudgetMonitor(home); estimator.Poll(reader.SelectedThread,true);
        string? error = null;
        if (online) { var live = await QuotaClient.ReadAsync(CancellationToken.None); q = live.Quota ?? q; error = live.Error; estimator.SelectAccount(q?.AccountId); estimator.Poll(reader.SelectedThread,true); }
        // No account IDs, conversation content or credentials are exported.
        var report = new { ipcConnected = route.IsConnected, followsSelection = selection.ThreadId is not null,
            quotaOnlyOverlay=!selection.CanShowConversationMetrics(reader.SelectedThread),
            selectionSource=selection.Label, ipcThreadId=route.ThreadId,
            snapshot = s, quota = q, budgetEstimate=estimator.Snapshot, error,
            privateMemoryMiB = Math.Round(Process.GetCurrentProcess().PrivateMemorySize64 / 1048576d, 1) };
        var output = Path.Combine(AppContext.BaseDirectory, "probe.json");
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class MeterContext : ApplicationContext
{
    internal bool EventFollowing{get;set;}
    internal long MotionEvents{get;set;}
    internal long MotionUpdates{get;set;}
    internal double FollowRefreshHz{get;set;}
    internal bool HighResolutionFollowing{get;set;}
    internal bool NativeRefreshRate{get;set;}
    private readonly Settings settings = Settings.Load();
    private readonly SessionReader reader;
    private readonly BudgetMonitor budgetMonitor;
    private readonly ThreadTitleIndex titles;
    private readonly PageThreadReader page=new();
    private readonly CodexIpcActiveThreadMonitor ipc = new();
    private readonly NotifyIcon tray;
    private readonly Icon icon;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly CancellationTokenSource cancellation = new();
    private readonly MeterPanel panel;
    private readonly OverlayForm overlay;
    private readonly ToolStripMenuItem overlayItem;
    private readonly Action exit;
    private HostSurface? host;
    private bool resumePanel;
    private string lastTrace = "";
    private bool busy, disposed;
    private int refreshing;
    private DateTime nextQuota;
    private QuotaSnapshot? quota;
    private string? quotaError;
    private string routeLabel = "等待当前对话";
    private ThreadSelection? currentSelection;
    private readonly bool initialShow;

    public MeterContext(string home, bool show, Action exit)
    {
        this.exit = exit;
        initialShow = show;
        Ui.SetTheme(settings.Theme);
        reader = new(home);
        budgetMonitor = new(home);
        titles = new(home);
        panel = new MeterPanel(settings, RefreshQuota, OpenBudget);
        overlay = new OverlayForm(settings, ShowPanel);
        icon = Ui.CreateIcon();
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开指标面板", null, (_, _) => ShowPanel());
        overlayItem = new("显示悬浮条") { Checked = settings.ShowOverlay, CheckOnClick = true };
        overlayItem.CheckedChanged += (_, _) => { settings.ShowOverlay = overlayItem.Checked; settings.Save(); Render(); };
        menu.Items.Add(overlayItem);
        var sizeMenu = new ToolStripMenuItem("托盘面板大小");
        foreach (var size in new[] { 67, 85, 100 })
        {
            var option = new ToolStripMenuItem($"{size}%") { Checked = settings.ScalePercent == size };
            option.Click += (_, _) =>
            {
                settings.ScalePercent = size; settings.Save();
                foreach (ToolStripMenuItem sibling in sizeMenu.DropDownItems) sibling.Checked = sibling == option;
                panel.AdaptToDesktop(); Render();
            };
            sizeMenu.DropDownItems.Add(option);
        }
        menu.Items.Add(sizeMenu);
        var themeMenu = new ToolStripMenuItem("主题");
        foreach (var (name, label) in new[] { ("white", "白色"), ("dark", "黑色"), ("miku", "Miku 青绿") })
        {
            var option = new ToolStripMenuItem(label) { Checked = settings.Theme == name };
            option.Click += (_, _) =>
            {
                settings.Theme = name; settings.Save(); Ui.SetTheme(name);
                foreach (ToolStripMenuItem sibling in themeMenu.DropDownItems) sibling.Checked = sibling == option;
                panel.ApplyTheme(); overlay.ApplyTheme();
                menu.BackColor = Ui.Card; menu.ForeColor = Ui.Text;
                Render();
            };
            themeMenu.DropDownItems.Add(option);
        }
        menu.Items.Add(themeMenu);
        menu.BackColor = Ui.Card; menu.ForeColor = Ui.Text;
        var autoFollow = new ToolStripMenuItem("登录后自动跟随 Codex") { Checked = AutoFollow.Enabled, CheckOnClick = true };
        var changingFollow=false;
        autoFollow.CheckedChanged += (_, _) =>
        {
            if(changingFollow)return;
            try { AutoFollow.Enabled=autoFollow.Checked; }
            catch(Exception e) when(e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
            {
                changingFollow=true; autoFollow.Checked=AutoFollow.Enabled; changingFollow=false;
                MessageBox.Show("自动启动设置未能保存："+e.Message,"Codex Meter",MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
        };
        menu.Items.Add(autoFollow);
        menu.Items.Add("刷新额度", null, (_, _) => RefreshQuota());
        menu.Items.Add("美元估算详情…", null, (_, _) => OpenBudget());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("完全退出（停止自动跟随）", null, (_, _) => this.exit());
        overlay.ContextMenuStrip = menu;
        panel.ContextMenuStrip = menu;
        tray = new NotifyIcon { Icon = icon, Text = "Codex Meter", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowPanel(); };
        panel.Icon = icon;
        panel.OverlayRequested += () => overlayItem.Checked = !overlayItem.Checked;
        timer.Tick += async (_, _) => await Tick();
        timer.Start();
        // Warm up the WinForms synchronization context before background results are applied.
        _ = panel.Handle;
        _ = Tick();
        resumePanel = initialShow;
    }

    private async Task Tick()
    {
        if (busy || disposed) return;
        busy = true;
        try
        {
            var route = ipc.GetStatus();
            var surface=host;
            var selection=await Task.Run(() =>
            {
                var selected=titles.Select(page.Read(surface),route.ThreadId);
                reader.Poll(selected.PollTarget); budgetMonitor.Poll(reader.SelectedThread);
                return selected;
            });
            routeLabel=selection.Label;
            currentSelection=selection;
            if (disposed) return;
            if (reader.Quota is { } log && (quota is null || log.Updated > quota.Updated)) quota = log;
            Render();
            if (DateTime.UtcNow >= nextQuota) RefreshQuota();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { busy = false; }
    }

    private async void RefreshQuota()
    {
        if (disposed || Interlocked.Exchange(ref refreshing, 1) != 0) return;
        nextQuota = DateTime.UtcNow.AddMinutes(5);
        Render();
        try
        {
            var result = await QuotaClient.ReadAsync(cancellation.Token);
            if (disposed) return;
            if (result.Quota is not null) { quota = result.Quota; budgetMonitor.SelectAccount(result.Quota.AccountId); }
            quotaError = result.Error;
        }
        finally { Interlocked.Exchange(ref refreshing, 0); if (!disposed) Render(); }
    }

    private void Render()
    {
        var s = reader.Snapshot;
        var status = quota is null ? "等待额度数据" : $"{quota.Source} · {quota.Updated.LocalDateTime:HH:mm:ss}";
        if (refreshing != 0) status += " · 查询中";
        else if (quotaError is not null) status += " · " + quotaError;
        panel.UpdateData(s, quota, routeLabel, status, budgetMonitor.Snapshot,reader.SelectedThread is null?null:reader.TurnCount);
        overlay.UpdateData(s, quota);
        if (host is not null) FollowHost(host);
        var tip = $"Codex · 5h余 {Display.Percent(quota?.FiveHour)} · 周余 {Display.Percent(quota?.Weekly)}";
        tray.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    private void ShowPanel()
    {
        if (host is null || host.Minimized || !host.Visible) return;
        if (!panel.Visible)
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            panel.AdaptTo(area,(uint)panel.DeviceDpi);
            panel.TopMost=false;
            panel.Bounds=WindowLayout.PanelCorner(area,panel.Size,(uint)panel.DeviceDpi);
            panel.Show();
            panel.AlignToDesktopCorner(area);
        }
        panel.Activate();
    }
    public void FollowHost(HostSurface surface,bool positionOnly=false)
    {
        host = surface;
        if (surface.Minimized || !surface.Visible) { HideForHost();if(!positionOnly)TraceHost(surface);return; }
        var active = HostWindow.IsForeground(surface, panel.Handle, overlay.Handle);
        overlay.SetQuotaOnly(currentSelection?.CanShowConversationMetrics(reader.SelectedThread)!=true);
        var fits = settings.ShowOverlay && overlay.DockTo(surface,!positionOnly);
        if (fits && active)
        {
            if (!overlay.Visible) { overlay.Show(); overlay.DockTo(surface,!positionOnly); HostWindow.ShowPassive(overlay.Handle); }
        }
        else overlay.Hide();
        if (!positionOnly&&active&&resumePanel) { resumePanel=false;ShowPanel(); }
        if(!positionOnly)TraceHost(surface);
    }
    private void TraceHost(HostSurface surface)
    {
        if (!Program.TraceAttachment) return;
        var actual = HostWindow.ActualBounds(overlay.Handle); var frame = HostWindow.VisibleBounds(surface);
        var report = JsonSerializer.Serialize(new { hostVisible = surface.Visible, hostMinimized = surface.Minimized,
            overlayVisible = overlay.Visible, hostDpi = surface.Dpi, visibleFrame = frame, actualBounds = actual,
            rightMargin = frame.Right - actual.Right, bottomMargin = frame.Bottom - actual.Bottom,
            ownerMatchesHost = HostWindow.OwnerOf(overlay.Handle) == surface.Handle, alignment=overlay.Alignment, overlayScale=overlay.DrawingScale,
            quotaOnly=overlay.QuotaOnly,placementReason=overlay.PlacementReason,composerBounds=overlay.ComposerBounds,
            panelScalePercent=settings.ScalePercent,panelTopmost=panel.TopMost,panelOwner=HostWindow.OwnerOf(panel.Handle).ToInt64(),panelLayout=panel.LayoutInfo,
            panelBounds=panel.Bounds,panelVisible=panel.Visible,desktopWorkingArea=Screen.FromRectangle(panel.Bounds).WorkingArea,
            eventFollowing=EventFollowing,motionEvents=MotionEvents,motionUpdates=MotionUpdates,overlayPaints=overlay.Paints,
            followRefreshHz=FollowRefreshHz,highResolutionFollowing=HighResolutionFollowing,nativeRefreshRate=NativeRefreshRate });
        if (report != lastTrace) { lastTrace = report; File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "attachment-live.json"), report); }
    }
    public void HideForHost()
    {
        overlay.Hide();
        panel.Hide();
    }
    public void StopMonitoring() => ExitThreadCore();
    private void OpenBudget()
    {
        using var dialog = new BudgetDialog(budgetMonitor.Snapshot,quota);
        if (dialog.ShowDialog(panel.Visible ? panel : null) == DialogResult.OK) Render();
    }
    protected override void ExitThreadCore()
    {
        if (!disposed)
        {
            disposed = true; timer.Stop(); cancellation.Cancel(); tray.Visible = false;
            ipc.Dispose(); reader.Dispose(); overlay.Dispose(); panel.Dispose(); tray.Dispose(); icon.Dispose(); timer.Dispose();
        }
        base.ExitThreadCore();
    }
}
