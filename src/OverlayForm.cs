namespace CodexMeter;

internal sealed class OverlayForm : Form
{
    private TokenSnapshot? token;
    private QuotaSnapshot? quota;
    private string lastSignature="";
    private float drawingScale=1;
    private bool quotaRequested;
    private IntPtr owner;
    private readonly ComposerAnchor composer=new();
    private readonly ToolTip tip=new(){AutoPopDelay=12000};
    public bool QuotaOnly{get;private set;}
    public string Alignment{get;private set;}="底部边距";
    internal float DrawingScale=>drawingScale;
    internal long Paints{get;private set;}
    public string PlacementReason{get;private set;}="等待输入框位置";
    public Rectangle? ComposerBounds{get;private set;}
    public void SetQuotaOnly(bool value)=>quotaRequested=value;
    private void SetMode(bool quotaOnly)
    {
        if(QuotaOnly==quotaOnly)return;
        QuotaOnly=quotaOnly;lastSignature="";Invalidate();
    }
    public OverlayForm(Settings settings,Action open)
    {
        BackColor=Color.White;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=false;
        AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.Manual;DoubleBuffered=true;
        ClientSize=DesiredSize(96);
        MouseClick+=(_,e)=>{if(e.Button==MouseButtons.Left)open();};
    }
    public static Size DesiredSize(uint dpi,bool quotaOnly=false)=>new((int)Math.Round((quotaOnly?WindowLayout.QuotaWidth:WindowLayout.OverlayWidth)*dpi/96f),(int)Math.Round((quotaOnly?WindowLayout.QuotaHeight:WindowLayout.OverlayHeight)*dpi/96f));
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x00000080;return p;}}
    public bool DockTo(HostSurface host,bool requestMeasurement=true)=>DockTo(host,composer.ReadBounds(host,requestMeasurement));
    internal bool DockTo(HostSurface host,ComposerGeometry? geometry)
    {
        var placement=WindowLayout.PlaceOverlay(HostWindow.VisibleBounds(host),host.Dpi,!quotaRequested,geometry);
        var bounds=placement.Bounds;
        SetMode(placement.QuotaOnly);PlacementReason=placement.Reason;ComposerBounds=geometry?.Bounds;
        if(bounds.IsEmpty){Hide();return false;}
        var repaint=drawingScale!=placement.Scale||Size!=bounds.Size;
        drawingScale=placement.Scale;
        if(owner!=host.Handle||HostWindow.OwnerOf(Handle)!=host.Handle){owner=host.Handle;HostWindow.Own(Handle,owner);}
        Alignment="聊天输入框底部";
        var previous=HostWindow.ActualBounds(Handle);
        if(previous!=bounds)
        {
            if(previous.Size==bounds.Size)HostWindow.Move(Handle,bounds.Location);
            else HostWindow.Position(Handle,bounds);
        }
        if(repaint)Invalidate();
        return true;
    }
    public void PreviewSize(int dpi,Rectangle? viewport=null)
    {
        SetMode(quotaRequested);
        var size=DesiredSize((uint)dpi,QuotaOnly);
        if(viewport is {} area)size=WindowLayout.Overlay(area,(uint)dpi,QuotaOnly,area.Bottom-20*dpi/96f).Size;
        if(size.IsEmpty){Hide();return;}
        drawingScale=size.Width/(float)(QuotaOnly?WindowLayout.QuotaWidth:WindowLayout.OverlayWidth);
        _=Handle;HostWindow.Position(Handle,new Rectangle(Point.Empty,size));
    }
    public void ApplyTheme(){BackColor=Color.White;var white=Theme.Get("white");tip.BackColor=white.Card;tip.ForeColor=white.Text;lastSignature="";Invalidate();}
    public void UpdateData(TokenSnapshot? token,QuotaSnapshot? quota)
    {
        var signature=Display.Percent(quota?.FiveHour)+Display.Percent(quota?.Weekly)+token?.TpsCompact;
        if(this.token==token&&this.quota==quota&&signature==lastSignature)return;
        lastSignature=signature;this.token=token;this.quota=quota;if(Visible)Invalidate();
        var speed=token?.RecentTps is {} t?$"{token.TpsKind} · 正文日志估算 ≈{t:0.0} tok/s\n{token.TpsTokens} 正文 token / {token.TpsMilliseconds/1000d:0.000} 秒，采样于 {DateTimeOffset.FromUnixTimeMilliseconds(token.TpsMeasuredAtMs).LocalDateTime:HH:mm:ss}\n短样本可能因缓冲交付显著偏高；旧样本不代表当前速度。":"TPS：等待带有效计时的完整最终回答。";
        var counts=token is null?"":$"\n累计输入 {Display.Count(token.Input)} · 输出 {Display.Count(token.Output)}\n缓存命中 {Display.Count(token.Cached)}";
        var resets=$"5小时：{Display.Reset(quota?.FiveHour)}\n周额度：{Display.Reset(quota?.Weekly)}";
        tip.SetToolTip(this,QuotaOnly?$"{resets}\n单击查看详情。":$"{resets}\n{speed}{counts}\n排除推理与工具；不是实时解码速度。单击查看详情。");
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Paints++;
        using var palette=Ui.WhiteScope();base.OnPaint(e);
        var g=e.Graphics;g.ScaleTransform(drawingScale,drawingScale);
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var height=QuotaOnly?WindowLayout.QuotaHeight:WindowLayout.OverlayHeight;
        var canvasWidth=QuotaOnly?ClientSize.Width/drawingScale:WindowLayout.OverlayWidth;
        using var shape=Ui.Rounded(new RectangleF(.5f,.5f,canvasWidth-1,height-1),9);
        using var background=new SolidBrush(Color.White);g.FillPath(background,shape);
        using var border=new Pen(Ui.Border);g.DrawPath(border,shape);
        if(QuotaOnly)
        {
            const float margin=6,gap=4;
            var column=(canvasWidth-2*margin-gap)/2;
            void VerticalQuota(QuotaWindow? window,string title,float left)
            {
                Ui.BaselineText(g,title,Ui.Small,Ui.Muted,left,20,column,StringAlignment.Center);
                // Keep text upright while the remaining amount fills upward from the bottom.
                Ui.VerticalRemainingBar(g,left+column/2,32,120,window is null||window.Expired?0:window.Remaining);
                var value=window?.Expired==true?"到期":Display.Percent(window);
                var measured=g.MeasureString(value,Ui.Small,PointF.Empty,StringFormat.GenericTypographic).Width;
                using var number=new Font(Ui.Small.FontFamily,Math.Min(11,Ui.Small.Size*column/Math.Max(column,measured)),FontStyle.Regular,GraphicsUnit.Pixel);
                Ui.BaselineText(g,value,number,Ui.Text,left,175,column,StringAlignment.Center);
            }
            VerticalQuota(quota?.FiveHour,"5h",margin);
            VerticalQuota(quota?.Weekly,"周",margin+column+gap);
            return;
        }
        const float left=12,valueLeft=65,right=176;
        void Label(string text,float baseline)=>Ui.BaselineText(g,text,Ui.Small,Ui.Muted,left,baseline,valueLeft-left-4);
        void Value(string text,float baseline,Color color)=>Ui.BaselineText(g,text,Ui.Small,color,valueLeft,baseline,right-valueLeft,StringAlignment.Far);
        void Quota(QuotaWindow? window,string title,float baseline)
        {
            Label(title,baseline);Value(Display.Percent(window),baseline,Ui.Text);
            Ui.RemainingBar(g,left,baseline+6,right-left,window is null||window.Expired?0:window.Remaining);
        }
        Quota(quota?.FiveHour,"5 小时",22);Quota(quota?.Weekly,"周额度",53);
        Ui.Fill(g,Ui.Border,left,70,right-left,.6f);
        Label("上下文",92);Value(token?.Capacity>0?$"{token.ContextRate:0.0}%":"—",92,Ui.Muted);
        Label("缓存",117);Value(token is null?"—":$"{token.CacheRate:0.0}%",117,Ui.Text);
        Label("↓ 输入",142);Value(token is null?"—":Display.Compact(token.Input),142,Color.FromArgb(16,185,129));
        Label("↑ 输出",167);Value(token is null?"—":Display.Compact(token.Output),167,Color.FromArgb(139,92,246));
        Label("TPS",192);Value(token?.RecentTps is {} speed?$"{speed:0.0} t/s":"— t/s",192,Ui.Blue);
    }
    protected override void Dispose(bool disposing){if(disposing)tip.Dispose();base.Dispose(disposing);}
}
