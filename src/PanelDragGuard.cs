namespace CodexMeter;

// Tracks only an in-window horizontal drag. No hook, input history, or background UIA polling.
internal sealed class PanelDragGuard
{
    private bool down,candidate;
    private Point origin;
    public bool Suppressed{get;private set;}
    public bool Update(bool pressed,Point cursor,Rectangle host,Rectangle? editor,uint dpi)
    {
        var before=Suppressed;
        if(!pressed){down=false;candidate=false;Suppressed=false;return before;}
        var scale=Math.Max(96u,dpi)/96f;
        if(!down)
        {
            down=true;origin=cursor;
            // Exclude the title bar, sidebars and text editing gestures.
            candidate=host.Contains(cursor)&&cursor.Y>=host.Top+80*scale&&cursor.X>=host.Left+host.Width*.35
                &&(editor is null||!editor.Value.Contains(cursor));
        }
        if(candidate&&Math.Abs(cursor.X-origin.X)>=4*scale)Suppressed=true;
        return before!=Suppressed;
    }
}
