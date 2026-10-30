namespace CodexMeter;

// Keeps only the small lifecycle listener alive when Desktop is closed. The session watcher,
// IPC connection, quota polling, forms and tray are all disposed with the last host window.
internal sealed class HostSupervisor : ApplicationContext
{
    private readonly string home;
    private bool show;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 150 };
    private MeterContext? meter;
    private DateTime absent;
    private DateTime initializeAt;
    public HostSupervisor(string home, bool show)
    {
        this.home = home; this.show = show;
        // Ensure async continuations have a WinForms synchronization context even before a host exists.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        timer.Tick += (_, _) => Tick(); timer.Start(); Tick();
    }
    private void Tick()
    {
        var host = HostWindow.Find();
        if (host is not null)
        {
            timer.Interval=150;
            absent = default;
            if(meter is null)
            {
                if(initializeAt==default)initializeAt=DateTime.UtcNow.AddSeconds(2);
                if(DateTime.UtcNow<initializeAt)return;
                meter=new MeterContext(home, show, ExitThread);
                initializeAt=default;
            }
            show = false; meter.FollowHost(host);
        }
        else
        {
            timer.Interval=1000; initializeAt=default;
            if(meter is not null)
            {
                meter.HideForHost();
                if (absent == default) absent = DateTime.UtcNow;
                if (DateTime.UtcNow - absent > TimeSpan.FromSeconds(3))
                {
                    meter.StopMonitoring(); meter.Dispose(); meter = null;
                    GC.Collect();
                }
            }
        }
    }
    protected override void ExitThreadCore()
    {
        timer.Stop(); timer.Dispose(); meter?.StopMonitoring(); meter?.Dispose(); meter = null;
        base.ExitThreadCore();
    }
}
