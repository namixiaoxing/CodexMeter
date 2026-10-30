using System.Windows.Automation;
namespace CodexMeter;

// Passive accessibility geometry only: never reads edit values, sends input or changes focus.
internal sealed class ComposerAnchor
{
    private int running;
    private DateTime next;
    private (IntPtr Handle, Rectangle Host, uint Dpi, Rectangle Bounds, DateTime At)? result;
    private IntPtr observedHandle;
    private Rectangle observedClient;
    private uint observedDpi;
    private DateTime settledAt;
    private readonly object sync=new();
    private AutomationElement? cached;
    private IntPtr cachedHost;
    private double cachedPadding;
    private bool releasedInitialTree;
    private long revision;
    public event Action? Changed;
    public void Refresh()
    {
        lock(sync){revision++;result=null;next=settledAt=DateTime.MinValue;}
    }
    public ComposerGeometry? ReadBounds(HostSurface host,bool requestMeasurement=true)
    {
        lock(sync)
        {
            var now=DateTime.UtcNow;
            if(observedHandle!=host.Handle||observedClient!=host.Client||observedDpi!=host.Dpi)
            {
                observedHandle=host.Handle;observedClient=host.Client;observedDpi=host.Dpi;
                settledAt=now.AddMilliseconds(200);
                if(next>settledAt)next=settledAt;
            }
            if(requestMeasurement&&now>=next&&now>=settledAt&&Interlocked.CompareExchange(ref running,1,0)==0)
            {
                next=DateTime.UtcNow.AddMilliseconds(750);
                _=Task.Run(()=>Read(host));
            }
            if(result is not {} r||r.Handle!=host.Handle)return null;
            var sameSize=r.Host.Size==host.Client.Size&&r.Dpi==host.Dpi;
            var translating=sameSize&&now<settledAt.AddSeconds(1);
            if(now-r.At>=TimeSpan.FromSeconds(3)&&!translating)return null;
            var box=r.Bounds;
            box.Offset(host.Client.Right-r.Host.Right,host.Client.Bottom-r.Host.Bottom);
            return new(box,sameSize);
        }
    }
    private static Rectangle Bounds(System.Windows.Rect box,HostSurface host,double padding=0)
    {
        var bounds=Rectangle.FromLTRB((int)Math.Floor(box.Left-padding),(int)Math.Floor(box.Top-padding),
            (int)Math.Ceiling(box.Right+padding),(int)Math.Ceiling(box.Bottom+padding));
        // A lone Send/Stop button gives a safe right edge but not the editor's top edge.
        // Protect the lower half until a container rectangle is available.
        if(padding>0)bounds=Rectangle.FromLTRB(host.Client.Left,Math.Min(bounds.Top,host.Client.Top+(int)(host.Client.Height*.45)),bounds.Right,bounds.Bottom);
        return bounds;
    }
    private void Read(HostSurface host)
    {
        long generation;lock(sync)generation=revision;
        try
        {
            var current=HostWindow.ReadSurface(host.Handle,host.Process);
            if(current is null)return;
            host=current;
            void Publish(Rectangle? bounds)
            {
                var after=HostWindow.ReadSurface(host.Handle,host.Process);
                if(after is null||after.Client!=host.Client||after.Dpi!=host.Dpi)return;
                bool changed;
                lock(sync)
                {
                    if(generation!=revision)return;
                    changed=bounds is {} box?result is not {} old||old.Handle!=host.Handle||old.Host!=host.Client||old.Dpi!=host.Dpi||old.Bounds!=box:result is not null;
                    result=bounds is {} measured?(host.Handle,host.Client,host.Dpi,measured,DateTime.UtcNow):null;
                }
                if(changed)Changed?.Invoke();
            }
            if(cached is not null && cachedHost==host.Handle)
            {
                var box=cached.Current.BoundingRectangle;
                if(!box.IsEmpty && box.Bottom<=host.Client.Bottom && box.Top>host.Client.Top+host.Client.Height*.5)
                { Publish(Bounds(box,host,cachedPadding));return; }
                cached=null;
            }
            var root=AutomationElement.FromHandle(host.Handle);
            var buttons=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
            Rectangle? best=null;
            void Consider(AutomationElement element,bool isButton)
            {
                var r=element.Current.BoundingRectangle;
                if(r.IsEmpty||r.Top<host.Client.Top+host.Client.Height*.5)return;
                var padding=isButton?12*host.Dpi/96d:0;
                var bounds=Bounds(r,host,padding);var chosen=element;
                var parent=element;
                for(var i=0;i<12;i++)
                {
                    parent=TreeWalker.RawViewWalker.GetParent(parent); if(parent is null)break;
                    var box=parent.Current.BoundingRectangle;
                    if(box.Width>=300*host.Dpi/96d&&box.Height>=50*host.Dpi/96d&&box.Height<=400*host.Dpi/96d&&box.Top>=host.Client.Top+host.Client.Height*.45&&box.Bottom>=r.Bottom&&box.Bottom<=host.Client.Bottom)
                    { bounds=Bounds(box,host);chosen=parent;padding=0;break; }
                }
                if(best is null||bounds.Bottom>best.Value.Bottom)
                { best=bounds;cached=chosen;cachedHost=host.Handle;cachedPadding=padding; }
            }
            foreach(AutomationElement button in buttons)
            {
                var name=button.Current.Name;
                if(name.Contains("发送")||name.Contains("停止")||name.Contains("Send",StringComparison.OrdinalIgnoreCase)||name.Contains("Stop",StringComparison.OrdinalIgnoreCase))Consider(button,true);
            }
            if(best is null)
            {
                // Empty/new-chat pages may expose an editor without a named Send button. Geometry only; never inspect values.
                var edits=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit));
                foreach(AutomationElement edit in edits)
                {
                    if(edit.Current.BoundingRectangle.Width>=200*host.Dpi/96d)Consider(edit,false);
                }
            }
            Publish(best);
        }
        catch(Exception e) when(e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { cached=null; }
        finally { Interlocked.Exchange(ref running,0); if(!releasedInitialTree) { releasedInitialTree=true; GC.Collect(2,GCCollectionMode.Forced,false); } }
    }
}
