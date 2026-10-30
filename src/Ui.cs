using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace CodexMeter;

internal static class Ui
{
    public static Palette Palette { get; private set; } = Theme.Get("miku");
    public static Color Background => Palette.Background;
    public static Color Card => Palette.Card;
    public static Color Text => Palette.Text;
    public static Color Muted => Palette.Muted;
    public static Color Accent => Palette.Accent;
    public static Color Blue => Palette.Blue;
    public static Color Border => Palette.Border;
    public static void SetTheme(string name) => Palette = Theme.Get(name);
    public static PaletteScope WhiteScope() => new();
    internal readonly struct PaletteScope : IDisposable
    {
        private readonly Palette before;
        public PaletteScope() { before=Palette; Palette=Theme.Get("white"); }
        public void Dispose() => Palette=before;
    }
    public static readonly Color Warn = Color.FromArgb(181, 100, 37);
    // Drawing uses a 96-DPI coordinate transform; pixel fonts avoid applying DPI twice.
    public static readonly Font Small = new("Microsoft YaHei UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Normal = new("Microsoft YaHei UI", 13.333f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Medium = new("Microsoft YaHei UI", 14.667f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Large = new("Segoe UI", 30.667f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Number = new("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font ControlFont = new("Microsoft YaHei UI", 9, FontStyle.Regular, GraphicsUnit.Point);
    public static void TextAt(Graphics g, string text, Font font, Color color, float x, float y, float width = 1000)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(text, font, brush, new RectangleF(x, y, width, 40), format);
    }
    public static void Fill(Graphics g, Color c, float x, float y, float w, float h)
    { using var b = new SolidBrush(c); g.FillRectangle(b, x, y, w, h); }
    // Mixed sizes share a typographic baseline, rather than independent top coordinates.
    public static void BaselineText(Graphics g,string text,Font font,Color color,float x,float baseline,float width=1000)
    {
        var ascent=font.Size*font.FontFamily.GetCellAscent(font.Style)/font.FontFamily.GetEmHeight(font.Style);
        using var brush=new SolidBrush(color);
        using var format=(StringFormat)StringFormat.GenericTypographic.Clone();
        format.Trimming=StringTrimming.EllipsisCharacter; format.FormatFlags|=StringFormatFlags.NoWrap;
        g.DrawString(text,font,brush,new RectangleF(x,baseline-ascent,width,40),format);
    }
    public static RectangleF InkBounds(string text, Font font)
    {
        using var path=new GraphicsPath();
        path.AddString(text,font.FontFamily,(int)font.Style,font.Size,PointF.Empty,StringFormat.GenericTypographic);
        return path.GetBounds();
    }
    public static void RowText(Graphics g,string text,Font font,Color color,float left,float top)
    {
        var ink=InkBounds(text,font);
        using var brush=new SolidBrush(color);
        // Only correct the horizontal bearing. Every run shares this same font origin/baseline.
        g.DrawString(text,font,brush,new PointF(left-ink.Left,top),StringFormat.GenericTypographic);
    }
    public static void Bar(Graphics g, float x, float y, float w, double percent, Color color)
    { Fill(g, Palette.Track, x, y, w, 4); Fill(g, color, x, y, w * (float)Math.Clamp(percent / 100, 0, 1), 4); }
    public static void RemainingBar(Graphics g, float x, float y, float width, double remaining)
    {
        var track = new RectangleF(x, y, width, 5);
        using var path = Rounded(track, 2.5f);
        using var background = new SolidBrush(Palette.Track); g.FillPath(background, path);
        var length = width * (float)Math.Clamp(remaining / 100, 0, 1);
        if (length < 1) return;
        var color = Theme.RemainingColor(remaining);
        using var fillPath = Rounded(new RectangleF(x, y, length, 5), Math.Min(2.5f, length / 2));
        using var brush = new LinearGradientBrush(new RectangleF(x, y, Math.Max(1, length), 5), Color.FromArgb(Math.Min(255, color.R + 18), Math.Min(255, color.G + 18), Math.Min(255, color.B + 18)), color, 0f);
        g.FillPath(brush, fillPath);
    }
    public static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath(); var diameter = Math.Max(0.1f, radius * 2);
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }
    public static void StyleButton(Button b)
    { b.BackColor = Card; b.ForeColor = Text; b.FlatAppearance.BorderColor = Border; }
    public static Button Button(string title, Action click)
    {
        var b = new Button { Text = title, FlatStyle = FlatStyle.Flat, BackColor = Card, ForeColor = Text, Font = ControlFont, Cursor = Cursors.Hand, TabStop = true };
        b.FlatAppearance.BorderColor = Border;
        b.Click += (_, _) => click(); return b;
    }
    public static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            using var b = new SolidBrush(Background); g.FillEllipse(b, 0, 0, 31, 31);
            using var p = new Pen(Accent, 3); g.DrawArc(p, 6, 6, 19, 19, 40, 280);
            g.DrawLine(p, 16, 16, 23, 16);
        }
        var handle = bmp.GetHicon();
        try { using var original = Icon.FromHandle(handle); return (Icon)original.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
}

internal sealed class MeterPanel : Form
{
    private readonly Settings settings;
    private TokenSnapshot? token;
    private QuotaSnapshot? quota;
    private BudgetSnapshot money=BudgetSnapshot.Waiting;
    private int? turns;
    private string route = "等待当前对话", status = "正在读取 Codex 数据…";
    private readonly Button refresh, overlay, budget;
    private float DrawingScale => DeviceDpi / 96f * Math.Clamp(settings.ScalePercent, 50, 130) / 100f;
    private bool Compact=>settings.ScalePercent<100;
    private bool Minimal=>settings.ScalePercent<=70;
    private readonly ToolTip detailTip=new(){AutoPopDelay=15000};
    private string lastDetail="";
    private Font? buttonFont;
    public event Action? OverlayRequested;
    public MeterPanel(Settings settings, Action refreshAction, Action budgetAction)
    {
        this.settings = settings;
        Text = "Codex Meter"; BackColor = Ui.Background; ForeColor = Ui.Text;
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true; ClientSize = new(460, 590);
        refresh = Ui.Button("刷新额度", refreshAction);
        overlay = Ui.Button("显示 / 隐藏悬浮条", () => OverlayRequested?.Invoke());
        budget = Ui.Button("美元估算…", budgetAction);
        Controls.AddRange(new Control[] { refresh, overlay, budget });
        LayoutButtons();
        DpiChanged += (_, _) => ApplyScale();
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyScale();
    }
    public void ApplyScale()
    {
        var anchor=new Point(Right,Bottom); var keepAnchor=Visible;
        var dpi=DeviceDpi/96f;
        ClientSize = new((int)Math.Round(460*Math.Clamp(settings.ScalePercent,50,130)/100f*dpi),
            Compact?(int)Math.Round((Minimal?349:408)*dpi):(int)Math.Round(630*DrawingScale));
        LayoutButtons();
        var old = buttonFont;
        buttonFont = new Font("Microsoft YaHei UI", 9f * Math.Clamp(settings.ScalePercent, 50, 130) / 100, FontStyle.Regular, GraphicsUnit.Point);
        refresh.Font = overlay.Font = budget.Font = buttonFont;
        old?.Dispose();
        if(keepAnchor)
        {
            var area=Screen.FromRectangle(Bounds).WorkingArea;
            Location=new Point(Math.Clamp(anchor.X-Width,area.Left,Math.Max(area.Left,area.Right-Width)),Math.Clamp(anchor.Y-Height,area.Top,Math.Max(area.Top,area.Bottom-Height)));
        }
        Invalidate();
    }
    public void ApplyTheme()
    {
        BackColor = Ui.Background; ForeColor = Ui.Text;
        foreach (var b in new[] { refresh, overlay, budget }) Ui.StyleButton(b);
        Invalidate();
    }
    private void LayoutButtons()
    {
        refresh.Visible=overlay.Visible=budget.Visible=!Compact;
        if(Compact)return;
        var s = DrawingScale;
        refresh.SetBounds((int)(22 * s), (int)(579 * s), (int)(102 * s), (int)(32 * s));
        overlay.SetBounds((int)(132 * s), (int)(579 * s), (int)(178 * s), (int)(32 * s));
        budget.SetBounds((int)(318 * s), (int)(579 * s), (int)(120 * s), (int)(32 * s));
    }
    public void UpdateData(TokenSnapshot? token, QuotaSnapshot? quota, string route, string status, BudgetSnapshot? money=null,int? turns=null)
    {
        this.token=token; this.quota=quota; this.route=route; this.status=status; this.turns=turns??token?.Turns;
        if(money is not null)this.money=money;
        var detail=$"{route}\n5小时：{Display.Reset(quota?.FiveHour)}\n周额度：{Display.Reset(quota?.Weekly)}\nTPS：{token?.TpsKind} · {token?.RecentTps:0.0} tok/s\n右键：刷新额度、美元详情；100%显示完整信息";
        if(detail!=lastDetail) { lastDetail=detail; detailTip.SetToolTip(this,detail); }
        if(Visible)Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g=e.Graphics;
        if(Compact) { PaintCompact(g); return; }
        g.ScaleTransform(DrawingScale,DrawingScale);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        void Text(string text,Font font,Color color,float x,float baseline,float width=1000)
            =>Ui.BaselineText(g,text,font,color,x,baseline,width);
        Text("Codex Meter",Ui.Medium,Ui.Text,22,34);
        Text("额度与当前对话",Ui.Small,Ui.Muted,285,34,155);
        QuotaCard(g,quota?.FiveHour,"5 小时额度",22);
        QuotaCard(g,quota?.Weekly,"周额度",236);
        Text(Display.Reset(quota?.FiveHour),Ui.Small,Ui.Muted,22,194,416);
        Text(Display.Reset(quota?.Weekly),Ui.Small,Ui.Muted,22,216,416);
        MoneyRow(g,"5h 美元估算",money.Five,quota?.FiveHour,249);
        MoneyRow(g,"周美元估算",money.Week,quota?.Weekly,273);
        Text(money.Status,Ui.Small,Ui.Muted,22,295,416);
        Ui.Fill(g,Ui.Border,22,309,416,1);
        Text("当前对话",Ui.Medium,Ui.Text,22,340);
        Text(turns is {} n?$"轮次 {n}":"轮次 —",Ui.Small,Ui.Text,127,340,100);
        Text(route,Ui.Small,Ui.Muted,230,340,208);
        Text("上下文占用",Ui.Small,Ui.Muted,22,373);
        var ctx=token is null || token.Capacity<=0?"—":$"{Display.Count(token.Context)} / {Display.Count(token.Capacity)}  ({token.ContextRate:0.0}%)";
        Text(ctx,Ui.Normal,Ui.Text,135,373,304);
        Ui.Bar(g,22,388,416,token?.ContextRate??0,Ui.Blue);
        Text("累计输入",Ui.Small,Ui.Muted,22,420);
        Text("累计输出（含推理）",Ui.Small,Ui.Muted,236,420);
        Text(token is null?"—":Display.Count(token.Input),Ui.Number,Ui.Text,22,451,190);
        Text(token is null?"—":Display.Count(token.Output),Ui.Number,Ui.Text,236,451,200);
        Text("缓存命中",Ui.Small,Ui.Muted,22,482);
        Text($"TPS · {token?.TpsKind??"等待正文"} · 估算",Ui.Small,Ui.Muted,236,482,203);
        Text(token is null?"—":$"{token.CacheRate:0.0}% · {Display.Compact(token.Cached)}",Ui.Number,Ui.Accent,22,513,200);
        Text(token?.RecentTps is {} tps?$"≈{tps:0.0} tok/s":"—",Ui.Number,Ui.Blue,236,513,201);
        var shortId=token is null?"等待产生 token 记录":$"{token.Model} · {token.Thread[..Math.Min(8,token.Thread.Length)]}";
        Text(shortId,Ui.Small,Ui.Muted,22,546,416);
        Text(status,Ui.Small,Ui.Muted,22,567,416);
    }
    private static void MoneyRow(Graphics g,string label,WindowBudget? estimate,QuotaWindow? window,float baseline)
    {
        Ui.BaselineText(g,label,Ui.Small,Ui.Muted,22,baseline,108);
        if(estimate is null || window is null || window.Expired)
        {
            Ui.BaselineText(g,estimate is null?"采样中":"等待新窗口数据",Ui.Normal,Ui.Text,135,baseline,303);
            return;
        }
        Ui.BaselineText(g,$"余 ≈\u0024{estimate.Total*(decimal)window.Remaining/100:0.00}",Ui.Normal,Ui.Text,135,baseline,132);
        Ui.BaselineText(g,"/",Ui.Normal,Ui.Muted,270,baseline,12);
        Ui.BaselineText(g,$"总 ≈\u0024{estimate.Total:0.00}",Ui.Normal,Ui.Text,286,baseline,152);
    }
    private static void QuotaCard(Graphics g,QuotaWindow? window,string title,int x)
    {
        Ui.Fill(g,Ui.Card,x,60,202,110);
        Ui.BaselineText(g,title,Ui.Small,Ui.Muted,x+14,84,170);
        Ui.BaselineText(g,Display.Percent(window),Ui.Large,window?.Remaining<20?Ui.Warn:Ui.Accent,x+14,128,182);
        Ui.BaselineText(g,"剩余",Ui.Small,Ui.Muted,x+139,128,55);
        Ui.RemainingBar(g,x+14,150,174,window is null || window.Expired?0:window.Remaining);
    }
    private void PaintCompact(Graphics g)
    {
        var scale=DeviceDpi/96f; g.ScaleTransform(scale,scale);
        g.SmoothingMode=SmoothingMode.AntiAlias; g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var width=ClientSize.Width/scale; const float margin=16,gap=12;
        var column=(width-margin*2-gap)/2; var right=margin+column+gap;
        void Text(string value,Font font,Color color,float x,float baseline,float maxWidth=1000)=>Ui.BaselineText(g,value,font,color,x,baseline,maxWidth);
        Text("Codex Meter",Ui.Medium,Ui.Text,margin,Minimal?27:29);
        Text(turns is {} n?$"轮次 {n}":"轮次 —",Ui.Small,Ui.Muted,width-margin-64,Minimal?27:29,64);
        var top=Minimal?39:43; var cardHeight=Minimal?88:92;
        string Remaining(WindowBudget? sample,QuotaWindow? window)=>sample is null?"美元采样中":window is null||window.Expired?"美元待刷新":$"余 ≈\u0024{sample.Total*(decimal)window.Remaining/100:0.00}";
        void Card(QuotaWindow? window,WindowBudget? sample,string label,float x)
        {
            Ui.Fill(g,Ui.Card,x,top,column,cardHeight);
            Text(label,Ui.Small,Ui.Muted,x+12,top+22,column-24);
            Text(Display.Percent(window),Ui.Large,window?.Remaining<20?Ui.Warn:Ui.Accent,x+12,top+66,column-24);
            Ui.RemainingBar(g,x+12,top+78,column-24,window is null||window.Expired?0:window.Remaining);
            string date="重置时间 —",countdown="等待数据";
            if(window?.Reset>0)
            {
                try { date=$"{DateTimeOffset.FromUnixTimeSeconds(window.Reset).LocalDateTime:MM-dd HH:mm} 重置"; countdown=Display.Reset(window).Split(" · ").Last().Replace("已到重置时间，等待查询","已到重置，待刷新"); }
                catch(ArgumentOutOfRangeException) { }
            }
            Text(date,Ui.Small,Ui.Muted,x,Minimal?148:154,column);
            if(Minimal)
            {
                if(window?.Expired==true)countdown="待刷新";
                var remaining=Remaining(sample,window).Replace("余 ≈","余≈");
                var countdownWidth=g.MeasureString(countdown,Ui.Small,PointF.Empty,StringFormat.GenericTypographic).Width;
                var amountWidth=g.MeasureString(remaining,Ui.Small,PointF.Empty,StringFormat.GenericTypographic).Width;
                if(countdownWidth+amountWidth+8>column)
                {
                    remaining=remaining.Replace("余≈","≈");
                    amountWidth=g.MeasureString(remaining,Ui.Small,PointF.Empty,StringFormat.GenericTypographic).Width;
                }
                amountWidth=Math.Min(amountWidth,Math.Max(0,column-countdownWidth-8));
                Text(countdown,Ui.Small,Ui.Muted,x,166,countdownWidth+1);
                Text(remaining,Ui.Small,Ui.Text,x+column-amountWidth,166,amountWidth+1);
            }
            else Text(countdown,Ui.Small,Ui.Muted,x,174,column);
        }
        Card(quota?.FiveHour,money.Five,"5 小时剩余",margin); Card(quota?.Weekly,money.Week,"周额度剩余",right);
        if(!Minimal)
        {
            void DollarRow(string label,WindowBudget? sample,QuotaWindow? window,float baseline)
            {
                Text(label,Ui.Small,Ui.Muted,margin,baseline,30);
                Text(Remaining(sample,window),Ui.Normal,Ui.Text,margin+35,baseline,column-35);
                Text(sample is null?"总 —":$"总 ≈\u0024{sample.Total:0.00}",Ui.Normal,Ui.Text,right,baseline,column);
            }
            DollarRow("5h",money.Five,quota?.FiveHour,198); DollarRow("周",money.Week,quota?.Weekly,222);
        }
        Ui.Fill(g,Ui.Border,margin,Minimal?182:238,width-margin*2,1);
        var contextBase=Minimal?206:260;
        Text("上下文",Ui.Small,Ui.Muted,margin,contextBase,64);
        var context=token?.Capacity>0?Minimal?$"{token.ContextRate:0.0}%":$"{Display.Count(token.Context)} / {Display.Count(token.Capacity)} · {token.ContextRate:0.0}%":"—";
        Text(context,Ui.Normal,Ui.Text,Minimal?width-margin-72:margin+65,contextBase,Minimal?72:width-margin*2-65);
        Ui.Bar(g,margin,Minimal?219:273,width-margin*2,token?.ContextRate??0,Ui.Blue);
        var inputLabel=Minimal?245:299; var inputValue=Minimal?273:327;
        Text("累计输入",Ui.Small,Ui.Muted,margin,inputLabel,column);
        Text(Minimal?"累计输出":"累计输出（含推理）",Ui.Small,Ui.Muted,right,inputLabel,column);
        string Count(long value)=>Minimal?Display.Compact(value):Display.Count(value);
        Text(token is null?"—":Count(token.Input),Ui.Number,Ui.Text,margin,inputValue,column);
        Text(token is null?"—":Count(token.Output),Ui.Number,Ui.Text,right,inputValue,column);
        var lastLabel=Minimal?302:357; var lastValue=Minimal?330:385;
        Text("缓存命中",Ui.Small,Ui.Muted,margin,lastLabel,column);
        Text("TPS · 日志估算",Ui.Small,Ui.Muted,right,lastLabel,column);
        var cache=token is null?"—":Minimal?$"{token.CacheRate:0.0}%":$"{token.CacheRate:0.0}% · {Display.Compact(token.Cached)}";
        Text(cache,Ui.Number,Ui.Accent,margin,lastValue,column);
        Text(token?.RecentTps is {} speed?$"≈{speed:0.0} t/s":"—",Ui.Number,Ui.Blue,right,lastValue,column);
    }
    protected override void Dispose(bool disposing) { if (disposing) { buttonFont?.Dispose(); detailTip.Dispose(); } base.Dispose(disposing); }
}

internal sealed class OverlayForm : Form
{
    public const int WidthDip = 355;
    public const int QuotaWidthDip = 178;
    public const int HeightDip = 78;
    public const float FixedScale = 1.10f;
    private TokenSnapshot? token;
    private QuotaSnapshot? quota;
    private string lastSignature = "";
    private float drawingScale = FixedScale;
    public bool QuotaOnly { get; private set; }
    public void SetQuotaOnly(bool value) { if(QuotaOnly==value)return; QuotaOnly=value; lastSignature=""; Invalidate(); }
    private IntPtr owner;
    private readonly ComposerAnchor composer=new();
    public string Alignment { get; private set; }="底部边距";
    private readonly ToolTip tip = new() { AutoPopDelay = 12000 };
    public OverlayForm(Settings settings, Action open)
    {
        BackColor = Color.White; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.Manual; DoubleBuffered = true;
        ClientSize = DesiredSize(96);
        MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) open(); };
    }
    public static Size DesiredSize(uint dpi,bool quotaOnly=false) => new((int)Math.Round((quotaOnly?QuotaWidthDip:WidthDip) * dpi / 96f * FixedScale), (int)Math.Round(HeightDip * dpi / 96f * FixedScale));
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080; return p; } }
    public bool DockTo(HostSurface host)
    {
        drawingScale = host.Dpi / 96f * FixedScale;
        var area = HostWindow.VisibleBounds(host);
        var desired = DesiredSize(host.Dpi,QuotaOnly);
        if (!HostWindow.CanFit(area, desired, host.Dpi)) { Hide(); return false; }
        if (owner != host.Handle || HostWindow.OwnerOf(Handle) != host.Handle) { owner = host.Handle; HostWindow.Own(Handle, owner); }
        var bounds = HostWindow.DockBounds(area, desired, host.Dpi);
        var composerBottom=composer.Bottom(host);
        Alignment=composerBottom is null ? "底部边距（等待输入框位置）" : "聊天输入框底部";
        var bottom=composerBottom is { } y ? (int)Math.Round(y) : area.Bottom-(int)Math.Round(20*host.Dpi/96d);
        bounds.Y=Math.Clamp(bottom-desired.Height,area.Top,bounds.Y);
        if (HostWindow.ActualBounds(Handle) != bounds) HostWindow.Position(Handle, bounds);
        return true;
    }
    public void PreviewSize(int dpi)
    {
        drawingScale = dpi / 96f * FixedScale;
        _ = Handle;
        HostWindow.Position(Handle, new Rectangle(Point.Empty, DesiredSize((uint)dpi,QuotaOnly)));
    }
    public void ApplyTheme() { BackColor = Color.White; var white=Theme.Get("white"); tip.BackColor = white.Card; tip.ForeColor = white.Text; lastSignature = ""; Invalidate(); }
    public void UpdateData(TokenSnapshot? token, QuotaSnapshot? quota)
    {
        var signature = Display.Percent(quota?.FiveHour) + Display.Percent(quota?.Weekly) + token?.TpsCompact;
        if (this.token == token && this.quota == quota && signature == lastSignature) return;
        lastSignature = signature; this.token = token; this.quota = quota;
        if (Visible) Invalidate();
        var speed = token?.RecentTps is { } t
            ? $"{token.TpsKind} · 正文日志估算 ≈{t:0.0} tok/s\n{token.TpsTokens} 正文 token / {token.TpsMilliseconds / 1000d:0.000} 秒，采样于 {DateTimeOffset.FromUnixTimeMilliseconds(token.TpsMeasuredAtMs).LocalDateTime:HH:mm:ss}\n短样本可能因缓冲交付显著偏高；旧样本不代表当前速度。"
            : "TPS：等待带有效计时的完整最终回答。";
        var counts = token is null ? "" : $"\n累计输入 {Display.Count(token.Input)} · 输出 {Display.Count(token.Output)}";
        var resetTip=$"5小时：{Display.Reset(quota?.FiveHour)}\n周额度：{Display.Reset(quota?.Weekly)}";
        tip.SetToolTip(this,QuotaOnly?$"{resetTip}\n单击查看详情。":$"{resetTip}\n{speed}{counts}\n排除推理与工具；不是实时解码速度。单击查看详情。");
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var palette=Ui.WhiteScope();
        base.OnPaint(e); var g = e.Graphics; g.ScaleTransform(drawingScale, drawingScale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var shape = Ui.Rounded(new RectangleF(.5f,.5f,(QuotaOnly?QuotaWidthDip:WidthDip)-1,HeightDip-1),9);
        using var background = new SolidBrush(Ui.Background); g.FillPath(background, shape);
        using var border = new Pen(Ui.Border); g.DrawPath(border, shape);
        if(QuotaOnly)
        {
            void Row(QuotaWindow? window,string label,int baseline,int barY)
            {
                const int left=12,right=QuotaWidthDip-12;
                var remaining=$"余 {Display.Percent(window)}";
                var size=g.MeasureString(remaining,Ui.Normal,PointF.Empty,StringFormat.GenericTypographic);
                Ui.BaselineText(g,label,Ui.Small,Ui.Muted,left,baseline,70);
                Ui.BaselineText(g,remaining,Ui.Normal,Ui.Text,right-size.Width,baseline,size.Width+1);
                Ui.RemainingBar(g,left,barY,right-left,window is null||window.Expired?0:window.Remaining);
            }
            Row(quota?.FiveHour,"5 小时",23,30);
            Row(quota?.Weekly,"周额度",61,68);
            return;
        }
        QuotaCell(g, quota?.FiveHour, "5 小时", 12, 158);
        QuotaCell(g, quota?.Weekly, "周额度", 185, 158);
        Ui.Fill(g, Ui.Border, 12, 43, 331, .6f);
        string Short(long n)=>n>=1000000?$"{n/1000000d:0.#}M":n>=1000?$"{n/1000d:0.#}k":n.ToString();
        var context=token?.Capacity>0?$"上下文{token.ContextRate:0}%":"上下文—";
        var cache=token is null?"缓存—":$"缓存{token.CacheRate:0}%";
        var input=token is null?"↓—":$"↓{Short(token.Input)}";
        var output=token is null?"↑—":$"↑{Short(token.Output)}";
        var speed=token?.RecentTps is {} tps?$"{tps:0}":"—";
        const string unit="t/s";
        float W(string text)=>Ui.InkBounds(text,Ui.Small).Width;
        var widths=new[]{W(context),W(cache),W(input),W(output),W(speed)+3+W(unit)};
        const float left=12,right=343,rowTop=54;
        var gap=Math.Max(2,(right-left-widths.Sum())/4);
        float x=left;
        Ui.RowText(g,context,Ui.Small,Ui.Muted,x,rowTop); x+=widths[0]+gap;
        Ui.RowText(g,cache,Ui.Small,Ui.Text,x,rowTop); x+=widths[1]+gap;
        Ui.RowText(g,input,Ui.Small,Color.FromArgb(16,185,129),x,rowTop); x+=widths[2]+gap;
        Ui.RowText(g,output,Ui.Small,Color.FromArgb(139,92,246),x,rowTop);
        // Keep the unit's right ink edge on the progress bar edge; digit changes expand left.
        var unitLeft=right-W(unit);
        Ui.RowText(g,speed,Ui.Small,Ui.Blue,unitLeft-3-W(speed),rowTop);
        Ui.RowText(g,unit,Ui.Small,Ui.Blue,unitLeft,rowTop);
    }
    private static void QuotaCell(Graphics g, QuotaWindow? window, string label, int x, int width)
    {
        Ui.TextAt(g, label, Ui.Small, Ui.Muted, x, 9, 80);
        Ui.TextAt(g, $"余 {Display.Percent(window)}", Ui.Normal, Ui.Text, x + width - 78, 7, 80);
        Ui.RemainingBar(g, x, 30, width, window is null || window.Expired ? 0 : window.Remaining);
    }
    protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
}

internal sealed class BudgetDialog : Form
{
    public BudgetDialog(BudgetSnapshot estimate,QuotaSnapshot? quota)
    {
        Text="API 等价美元估算"; Font=Ui.ControlFont; BackColor=Ui.Background; ForeColor=Ui.Text;
        FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false; ShowInTaskbar=false;
        StartPosition=FormStartPosition.CenterScreen; AutoScaleMode=AutoScaleMode.Dpi; ClientSize=new(550,405);
        string Window(string name,WindowBudget? sample,QuotaWindow? window) => sample is null ? $"{name}：采样中，需要至少 3% 的同窗口额度变化。" :
            $"{name}：{sample.Summary(window)}\r\n总量区间 ≈\u0024{sample.Low:0.00}–\u0024{sample.High:0.00}；样本 {sample.PercentSpan:0.#}% / {sample.Calls} 次调用";
        var description=Window("5 小时",estimate.Five,quota?.FiveHour)+"\r\n\r\n"+Window("周",estimate.Week,quota?.Weekly)+
            $"\r\n\r\n{estimate.Status}\r\n已记录 {estimate.Calls} 次调用，API 等价成本 \u0024{estimate.RecordedCost:0.0000}\r\n\r\n标准美元单价 / 100万 token（2026-10-30核对）\r\n模型               输入 / 缓存读 / 缓存写 / 输出\r\n"+
            string.Join("\r\n",ApiRate.Rates.Select(r=>$"{r.Model}:  \u0024{r.Input} / \u0024{r.Cached} / \u0024{r.Write} / \u0024{r.Output}"))+
            "\r\n\r\n总量 ≈ 本机 API 成本增量 ÷ 额度百分比增量 × 100。\r\n普通输入扣除缓存读写；推理包含在输出内，不重复计价。\r\n>272k 输入按官方长上下文倍率；缓存写按30分钟价。\r\n取整、其他设备用量、缺失日志及模型混合都会造成误差。\r\n这是使用模式下的 API 等价美元估算，不是官方余额。";
        var text=new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,Text=description,
            BackColor=Ui.Background,ForeColor=Ui.Text,Bounds=new(18,16,514,332) };
        var close=Ui.Button("关闭",()=>Close()); close.Bounds=new(432,359,100,30);
        Controls.AddRange(new Control[]{text,close}); CancelButton=close;
    }
}
