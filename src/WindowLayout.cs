namespace CodexMeter;

internal sealed record PanelLayout(int Level, Size Canvas, float Scale)
{
    public Size ClientSize=>new((int)Math.Round(Canvas.Width*Scale),(int)Math.Round(Canvas.Height*Scale));
}

internal sealed record OverlayPlacement(Rectangle Bounds,bool QuotaOnly,float Scale,string Reason);
internal sealed record ComposerGeometry(Rectangle Bounds,bool MatchesSize);

internal static class WindowLayout
{
    public const int OverlayWidth=188, OverlayHeight=206, QuotaWidth=72, QuotaHeight=188;
    public const int RightInset=28; // Scrollbar/resize strip plus a separate click-safe gap, in DIP.

    public static OverlayPlacement PlaceOverlay(Rectangle available,uint dpi,bool conversationAvailable,ComposerGeometry? composer)
    {
        var native=Math.Max(96u,dpi)/96f;
        OverlayPlacement Hidden(string reason)=>new(Rectangle.Empty,true,native,reason);
        if(composer is null)return Hidden("等待输入框边界，隐藏");
        if(!composer.MatchesSize)return Hidden("窗口尺寸变化，等待边界确认");
        var gap=(int)Math.Round(8*native);
        var full=Overlay(available,dpi,false,composer.Bounds.Bottom);
        if(conversationAvailable&&!full.IsEmpty&&full.Left>=composer.Bounds.Right+gap)
            return new(full,false,native,"输入框右侧空间充足");
        var quota=Overlay(available,dpi,true,composer.Bounds.Bottom);
        if(!quota.IsEmpty&&quota.Left>=composer.Bounds.Right+gap)
            return new(quota,true,native,"固定双竖条额度窗");
        return Hidden("正常锚点放不下，隐藏");
    }

    public static PanelLayout Panel(Rectangle available,uint dpi,int percent,Size frame)
    {
        var native=Math.Max(96u,dpi)/96f;
        var margin=(int)Math.Round(12*native);
        var maxWidth=Math.Max(1,available.Width-2*margin-frame.Width);
        var maxHeight=Math.Max(1,Math.Min(available.Height-2*margin-frame.Height,(int)(available.Height*.72)-frame.Height));
        var level=percent>=100?2:percent>=80?1:0;
        Size Canvas(int detail)=>detail switch {2=>new(360,484),1=>new(332,374),_=>new(300,316)};
        while(level>0&&(Canvas(level).Width*native>maxWidth||Canvas(level).Height*native>maxHeight))level--;
        var canvas=Canvas(level);
        var scale=Math.Min(native,Math.Min(maxWidth/(float)canvas.Width,maxHeight/(float)canvas.Height));
        return new(level,canvas,scale);
    }

    public static Rectangle Overlay(Rectangle available,uint dpi,bool quotaOnly,double? composerBottom=null)
    {
        var native=Math.Max(96u,dpi)/96f;
        var right=(int)Math.Round(RightInset*native);
        var padding=(int)Math.Round(8*native);
        if(composerBottom is not {} y||!double.IsFinite(y))return Rectangle.Empty;
        var bottom=(int)Math.Round(y);
        var height=quotaOnly?QuotaHeight:OverlayHeight;
        var width=quotaOnly?QuotaWidth:OverlayWidth;
        var size=new Size((int)Math.Round(width*native),(int)Math.Round(height*native));
        var bounds=new Rectangle(available.Right-right-size.Width,bottom-size.Height,size.Width,size.Height);
        return available.Contains(bounds)&&bounds.Left>=available.Left+padding&&bounds.Top>=available.Top+padding?bounds:Rectangle.Empty;
    }

    public static Rectangle PanelCorner(Rectangle workingArea,Size size,uint dpi,Padding invisibleFrame=default)
    {
        var visibleSize=new Size(size.Width-invisibleFrame.Horizontal,size.Height-invisibleFrame.Vertical);
        var visible=Clamp(new(workingArea.Right-visibleSize.Width,workingArea.Bottom-visibleSize.Height,visibleSize.Width,visibleSize.Height),workingArea);
        return new(visible.Left-invisibleFrame.Left,visible.Top-invisibleFrame.Top,size.Width,size.Height);
    }

    public static Rectangle Clamp(Rectangle bounds,Rectangle available)=>new(
        Math.Clamp(bounds.X,available.Left,Math.Max(available.Left,available.Right-bounds.Width)),
        Math.Clamp(bounds.Y,available.Top,Math.Max(available.Top,available.Bottom-bounds.Height)),bounds.Width,bounds.Height);
}
