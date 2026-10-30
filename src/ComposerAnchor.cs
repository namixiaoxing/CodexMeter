using System.Windows.Automation;
namespace CodexMeter;

// Passive accessibility geometry only: never reads edit values, sends input or changes focus.
internal sealed class ComposerAnchor
{
    private int running;
    private DateTime next;
    private (IntPtr Handle, Rectangle Host, double Bottom, DateTime At)? result;
    private readonly object sync=new();
    private AutomationElement? cached;
    private IntPtr cachedHost;
    private double cachedPadding;
    private bool releasedInitialTree;
    public double? Bottom(HostSurface host)
    {
        lock(sync)
        {
            if(DateTime.UtcNow>=next && Interlocked.CompareExchange(ref running,1,0)==0)
            {
                next=DateTime.UtcNow.AddSeconds(2);
                _=Task.Run(()=>Read(host));
            }
            return result is { } r && r.Handle==host.Handle && r.Host==host.Client && DateTime.UtcNow-r.At<TimeSpan.FromSeconds(8) ? r.Bottom : null;
        }
    }
    private void Read(HostSurface host)
    {
        try
        {
            if(cached is not null && cachedHost==host.Handle)
            {
                var box=cached.Current.BoundingRectangle;
                if(!box.IsEmpty && box.Bottom<=host.Client.Bottom && box.Top>host.Client.Top+host.Client.Height*.5)
                { lock(sync) result=(host.Handle,host.Client,box.Bottom+cachedPadding,DateTime.UtcNow); return; }
                cached=null;
            }
            var root=AutomationElement.FromHandle(host.Handle);
            var buttons=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
            double? best=null;
            foreach(AutomationElement button in buttons)
            {
                var r=button.Current.BoundingRectangle; var name=button.Current.Name;
                if(r.IsEmpty || r.Top<host.Client.Top+host.Client.Height*.6 || r.Height>100 || !(name.Contains("发送") || name.Contains("停止") || name.Contains("Send",StringComparison.OrdinalIgnoreCase) || name.Contains("Stop",StringComparison.OrdinalIgnoreCase))) continue;
                var bottom=r.Bottom+12*host.Dpi/96d;
                var chosen=button; var padding=12*host.Dpi/96d;
                var parent=button;
                for(var i=0;i<7;i++)
                {
                    parent=TreeWalker.ControlViewWalker.GetParent(parent); if(parent is null)break;
                    var box=parent.Current.BoundingRectangle;
                    if(box.Width>=300*host.Dpi/96d && box.Height>=50*host.Dpi/96d && box.Height<=280*host.Dpi/96d && box.Bottom>=r.Bottom && box.Bottom<=host.Client.Bottom) { bottom=box.Bottom; chosen=parent; padding=0; break; }
                }
                if(best is null || bottom>best) { best=bottom; cached=chosen; cachedHost=host.Handle; cachedPadding=padding; }
            }
            lock(sync) result=best is { } y ? (host.Handle,host.Client,y,DateTime.UtcNow) : null;
        }
        catch(Exception e) when(e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { cached=null; }
        finally { Interlocked.Exchange(ref running,0); if(!releasedInitialTree) { releasedInitialTree=true; GC.Collect(2,GCCollectionMode.Forced,false); } }
    }
}
