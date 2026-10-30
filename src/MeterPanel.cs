namespace CodexMeter;

internal sealed class MeterPanel : Form
{
    private readonly Settings settings;
    private TokenSnapshot? token;
    private QuotaSnapshot? quota;
    private BudgetSnapshot money=BudgetSnapshot.Waiting;
    private int? turns;
    private string route="等待当前对话",status="正在读取 Codex 数据…";
    private readonly Button refresh,overlay,budget;
    private readonly ToolTip detailTip=new(){AutoPopDelay=15000};
    private string lastDetail="";
    private Font? buttonFont;
    private Rectangle? viewport;
    private uint layoutDpi=96;
    private Padding invisibleFrame;
    private PanelLayout layout=new(2,new(360,484),1);
    public event Action? OverlayRequested;
    internal PanelLayout LayoutInfo=>layout;

    public MeterPanel(Settings settings,Action refreshAction,Action budgetAction)
    {
        this.settings=settings;
        Text="Codex Meter"; BackColor=Ui.Background; ForeColor=Ui.Text;
        FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false; MinimizeBox=false;
        ShowInTaskbar=false; TopMost=false; StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None; DoubleBuffered=true; ClientSize=layout.ClientSize;
        refresh=Ui.Button("刷新额度",refreshAction);
        overlay=Ui.Button("悬浮窗开关",()=>OverlayRequested?.Invoke());
        budget=Ui.Button("美元估算…",budgetAction);
        Controls.AddRange(new Control[]{refresh,overlay,budget});
        DpiChanged+=(_,_)=>AdaptTo(Screen.FromRectangle(Bounds).WorkingArea,(uint)DeviceDpi);
        FormClosing+=(_,e)=>{if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
        Deactivate+=(_,_)=>QueueDismiss();
        ApplyScale();
    }
    protected override void OnContextMenuStripChanged(EventArgs e)
    {
        base.OnContextMenuStripChanged(e);
        if(ContextMenuStrip is { } menu)menu.Closed+=(_,_)=>QueueDismiss();
    }
    private void QueueDismiss()
    {
        if(!IsHandleCreated||IsDisposed)return;
        BeginInvoke((Action)(()=>
        {
            if(!IsDisposed&&Visible&&!ContainsFocus&&ContextMenuStrip?.Visible!=true&&!OwnedForms.Any(f=>f.Visible))Hide();
        }));
    }
    public void AdaptTo(Rectangle available,uint dpi)
    {
        viewport=available; layoutDpi=dpi; ApplyScale();
    }
    public void AdaptToDesktop()=>AdaptTo(Screen.FromRectangle(Bounds).WorkingArea,(uint)DeviceDpi);
    private void ReadFrameInsets()
    {
        if(!Visible||!IsHandleCreated)return;
        var outer=HostWindow.ActualBounds(Handle);var frame=HostWindow.FrameBounds(Handle);
        var insets=new[]{frame.Left-outer.Left,frame.Top-outer.Top,outer.Right-frame.Right,outer.Bottom-frame.Bottom};
        // DWM can briefly return the previous frame during resizing; keep the last valid border measurement.
        if(insets.All(value=>value>=0&&value<=32*layoutDpi/96f))invisibleFrame=new(insets[0],insets[1],insets[2],insets[3]);
    }
    public void AlignToDesktopCorner(Rectangle area)
    {
        ReadFrameInsets();Bounds=WindowLayout.PanelCorner(area,Size,layoutDpi,invisibleFrame);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if(viewport is null)layoutDpi=(uint)DeviceDpi;
        ApplyScale();
    }
    public void ApplyScale()
    {
        ReadFrameInsets();
        var area=viewport??Screen.FromRectangle(Bounds).WorkingArea;
        var frame=SizeFromClientSize(Size.Empty);
        var next=WindowLayout.Panel(area,layoutDpi,settings.ScalePercent,frame);
        var changed=layout!=next||ClientSize!=next.ClientSize;
        layout=next;
        if(changed)ClientSize=layout.ClientSize;
        var full=layout.Level==2;
        refresh.Visible=overlay.Visible=budget.Visible=full;
        if(full)
        {
            var s=layout.Scale;
            refresh.SetBounds((int)(16*s),(int)(440*s),(int)(96*s),(int)(28*s));
            overlay.SetBounds((int)(120*s),(int)(440*s),(int)(110*s),(int)(28*s));
            budget.SetBounds((int)(238*s),(int)(440*s),(int)(106*s),(int)(28*s));
            if(buttonFont is null||changed)
            {
                var old=buttonFont;
                buttonFont=new Font("Microsoft YaHei UI",12*s*96/Math.Max(96,DeviceDpi),FontStyle.Regular,GraphicsUnit.Pixel);
                refresh.Font=overlay.Font=budget.Font=buttonFont;old?.Dispose();
            }
        }
        if(Visible)Bounds=WindowLayout.PanelCorner(area,Size,layoutDpi,invisibleFrame);
        if(changed)Invalidate();
    }
    public void ApplyTheme()
    {
        BackColor=Ui.Background;ForeColor=Ui.Text;
        foreach(var button in new[]{refresh,overlay,budget})Ui.StyleButton(button);
        Invalidate();
    }
    public void UpdateData(TokenSnapshot? token,QuotaSnapshot? quota,string route,string status,BudgetSnapshot? money=null,int? turns=null)
    {
        this.token=token;this.quota=quota;this.route=route;this.status=status;this.turns=turns??token?.Turns;
        if(money is not null)this.money=money;
        var detail=$"{route}\n5小时：{Display.Reset(quota?.FiveHour)}\n周额度：{Display.Reset(quota?.Weekly)}\n"+
            $"输入 {Display.Count(token?.Input??0)} · 输出（含推理）{Display.Count(token?.Output??0)}\n"+
            $"上下文 {Display.Count(token?.Context??0)} / {Display.Count(token?.Capacity??0)} · 缓存 {Display.Count(token?.Cached??0)}\n"+
            $"TPS：{token?.TpsKind} · {token?.RecentTps:0.0} tok/s\n{status}\n右键：刷新额度、美元详情、面板大小。";
        if(detail!=lastDetail){lastDetail=detail;detailTip.SetToolTip(this,detail);}
        if(Visible)Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;g.ScaleTransform(layout.Scale,layout.Scale);
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var width=layout.Canvas.Width;const float margin=16,gap=12;
        var column=(width-2*margin-gap)/2f;var right=margin+column+gap;
        var full=layout.Level==2;var minimal=layout.Level==0;
        void Text(string text,Font font,Color color,float x,float baseline,float length,StringAlignment alignment=StringAlignment.Near)
            =>Ui.BaselineText(g,text,font,color,x,baseline,length,alignment);
        void Right(string text,Font font,Color color,float x,float baseline,float length)=>Text(text,font,color,x,baseline,length,StringAlignment.Far);
        Text("Codex Meter",Ui.Medium,Ui.Text,margin,27,width-110);
        Right(turns is {} n?$"轮次 {n}":"轮次 —",Ui.Small,Ui.Muted,width-margin-90,27,90);
        string Remaining(WindowBudget? sample,QuotaWindow? window)=>sample is null?"美元采样中":window is null||window.Expired?"美元待刷新":$"余 ≈${sample.Total*(decimal)window.Remaining/100:0.00}";
        void Card(QuotaWindow? window,WindowBudget? sample,string title,float x)
        {
            Ui.Fill(g,Ui.Card,x,40,column,68);
            Text(title,Ui.Small,Ui.Muted,x+12,60,column-24);
            Text(Display.Percent(window),Ui.Large,window?.Remaining<20?Ui.Warn:Ui.Accent,x+12,91,column-24);
            Ui.RemainingBar(g,x+12,99,column-24,window is null||window.Expired?0:window.Remaining);
            var date="重置时间 —";var countdown="等待数据";
            if(window?.Reset>0)
            {
                try{date=$"{DateTimeOffset.FromUnixTimeSeconds(window.Reset).LocalDateTime:MM-dd HH:mm} 重置";countdown=Display.Reset(window).Split(" · ").Last().Replace("已到重置时间，等待查询","待刷新");}
                catch(ArgumentOutOfRangeException){}
            }
            Text(date,Ui.Small,Ui.Muted,x,128,column);
            Text(countdown,Ui.Small,Ui.Muted,x,148,column);
            if(minimal)Text(Remaining(sample,window),Ui.Small,Ui.Text,x,168,column);
        }
        Card(quota?.FiveHour,money.Five,"5 小时剩余",margin);
        Card(quota?.Weekly,money.Week,"周额度剩余",right);
        if(!minimal)
        {
            void Money(string label,WindowBudget? sample,QuotaWindow? window,float baseline)
            {
                Text(label,Ui.Small,Ui.Muted,margin,baseline,28);
                Text(Remaining(sample,window),Ui.Small,Ui.Text,margin+32,baseline,column-32);
                Text(sample is null?"总 —":$"总 ≈${sample.Total:0.00}",Ui.Small,Ui.Text,right,baseline,column);
            }
            Money("5h",money.Five,quota?.FiveHour,172);Money("周",money.Week,quota?.Weekly,194);
            if(full)Text(money.Status,Ui.Small,Ui.Muted,margin,216,width-2*margin);
        }
        var divider=minimal?180:full?228:210;
        Ui.Fill(g,Ui.Border,margin,divider,width-2*margin,1);
        if(!minimal)
        {
            var heading=full?251:232;
            Text("当前对话",Ui.Medium,Ui.Text,margin,heading,86);
            Right(route,Ui.Small,Ui.Muted,margin+92,heading,width-2*margin-92);
        }
        var contextBase=minimal?202:full?276:254;
        Text("上下文",Ui.Small,Ui.Muted,margin,contextBase,56);
        var context=token?.Capacity>0?minimal?$"{token.ContextRate:0.0}%":$"{Display.Count(token.Context)} / {Display.Count(token.Capacity)} · {token.ContextRate:0.0}%":"—";
        Right(context,Ui.Small,Ui.Text,margin+58,contextBase,width-2*margin-58);
        Ui.Bar(g,margin,contextBase+10,width-2*margin,token?.ContextRate??0,Ui.Blue);
        var inputLabel=minimal?234:full?310:286;var inputValue=minimal?255:full?334:310;
        Text("累计输入",Ui.Small,Ui.Muted,margin,inputLabel,column);
        Text(minimal?"累计输出":"输出（含推理）",Ui.Small,Ui.Muted,right,inputLabel,column);
        string Count(long count)=>minimal?Display.Compact(count):Display.Count(count);
        Text(token is null?"—":Count(token.Input),Ui.Number,Ui.Text,margin,inputValue,column);
        Text(token is null?"—":Count(token.Output),Ui.Number,Ui.Text,right,inputValue,column);
        var cacheLabel=minimal?278:full?359:334;var cacheValue=minimal?299:full?383:358;
        Text(full?"缓存率":"缓存命中",Ui.Small,Ui.Muted,margin,cacheLabel,full?60:column);
        if(full)Right(token is null?"命中量 —":$"命中量 {Display.Compact(token.Cached)}",Ui.Small,Ui.Muted,margin+64,cacheLabel,column-64);
        Text("TPS · 日志估算",Ui.Small,Ui.Muted,right,cacheLabel,column);
        var cache=token is null?"—":$"{token.CacheRate:0.0}%";
        Text(cache,Ui.Number,Ui.Accent,margin,cacheValue,column);
        Text(token?.RecentTps is {} speed?$"≈{speed:0.0} t/s":"—",Ui.Number,Ui.Blue,right,cacheValue,column);
        if(full)
        {
            Text(token is null?"等待 token 记录":$"{token.Model} · {token.Thread[..Math.Min(8,token.Thread.Length)]}",Ui.Small,Ui.Muted,margin,407,width-2*margin);
            Text(status,Ui.Small,Ui.Muted,margin,428,width-2*margin);
        }
    }
    protected override void Dispose(bool disposing){if(disposing){buttonFont?.Dispose();detailTip.Dispose();}base.Dispose(disposing);}
}
