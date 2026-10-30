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
    public static void BaselineText(Graphics g,string text,Font font,Color color,float x,float baseline,float width=1000,StringAlignment alignment=StringAlignment.Near)
    {
        var ascent=font.Size*font.FontFamily.GetCellAscent(font.Style)/font.FontFamily.GetEmHeight(font.Style);
        using var brush=new SolidBrush(color);
        using var format=(StringFormat)StringFormat.GenericTypographic.Clone();
        format.Alignment=alignment;
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
    public static void VerticalRemainingBar(Graphics g,float center,float top,float height,double remaining)
    {
        const float width=6;
        using var track=Rounded(new(center-width/2,top,width,height),width/2);
        using var background=new SolidBrush(Palette.Track);g.FillPath(background,track);
        var length=height*(float)Math.Clamp(remaining/100,0,1);
        if(length<1)return;
        using var fill=Rounded(new(center-width/2,top+height-length,width,length),Math.Min(width/2,length/2));
        using var brush=new SolidBrush(Theme.RemainingColor(remaining));g.FillPath(brush,fill);
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
