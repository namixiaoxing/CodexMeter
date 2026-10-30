namespace CodexMeter;

// Keeps only the small lifecycle listener alive when Desktop is closed. The session watcher,
// IPC connection, quota polling, forms and tray are all disposed with the last host window.
internal sealed class HostSupervisor : ApplicationContext
{
    private readonly string home;
    private bool show;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 150 };
    private readonly HostMotionHook motion;
    private MeterContext? meter;
    private DateTime absent;
    private DateTime initializeAt;
    public HostSupervisor(string home, bool show)
    {
        this.home = home; this.show = show;
        // Ensure async continuations have a WinForms synchronization context even before a host exists.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        motion=new(surface=>meter?.FollowHost(surface,true),SynchronizationContext.Current!);
        timer.Tick += (_, _) => Tick(); timer.Start(); Tick();
    }
    private void Tick()
    {
        var host = HostWindow.Find();
        if (host is not null)
        {
            absent = default;
            if(meter is null)
            {
                if(initializeAt==default)initializeAt=DateTime.UtcNow.AddSeconds(2);
                if(DateTime.UtcNow<initializeAt)return;
                meter=new MeterContext(home, show, ExitThread);
                initializeAt=default;
            }
            motion.Watch(host);
            meter.EventFollowing=motion.Active;meter.MotionEvents=motion.Events;meter.MotionUpdates=motion.Updates;
            meter.FollowRefreshHz=motion.RefreshHz;meter.HighResolutionFollowing=motion.HighResolution;
            meter.NativeRefreshRate=motion.NativeRefreshRate;
            timer.Interval=motion.Active?150:16;
            show = false; meter.FollowHost(host);
        }
        else
        {
            timer.Interval=1000; initializeAt=default;motion.Unwatch();
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
        timer.Stop(); timer.Dispose(); motion.Dispose();meter?.StopMonitoring(); meter?.Dispose(); meter = null;
        base.ExitThreadCore();
    }
}
