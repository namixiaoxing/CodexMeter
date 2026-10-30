using System.Runtime.InteropServices;
namespace CodexMeter;

// Read-only, out-of-process window events. No injected DLL and no high-rate idle polling.
internal sealed class HostMotionHook : IDisposable
{
    private readonly Action<HostSurface> changed;
    private readonly SynchronizationContext context;
    private readonly WinEventProc callback;
    private readonly MotionFrameClock clock;
    private GCHandle callbackRoot;
    private IntPtr hook,window;
    private uint process;
    private int queued;
    private volatile bool disposed;
    private IntPtr monitor;
    private readonly EventHandler displayChanged;
    public bool Active=>hook!=IntPtr.Zero;
    public double RefreshHz=>clock.Hertz;
    public bool HighResolution=>clock.HighResolution;
    public bool NativeRefreshRate{get;private set;}
    internal long Events{get;private set;}
    internal long Updates{get;private set;}

    public HostMotionHook(Action<HostSurface> changed,SynchronizationContext context)
    {
        this.changed=changed;this.context=context;callback=OnEvent;
        callbackRoot=GCHandle.Alloc(callback);
        clock=new(QueueFrame);
        displayChanged=(_,_)=>context.Post(_=>{if(!disposed&&window!=IntPtr.Zero){monitor=IntPtr.Zero;UpdateRefresh();}},null);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=displayChanged;
    }
    public void Watch(HostSurface host)
    {
        if(disposed)return;
        if(window==host.Handle&&process==host.Process){UpdateRefresh();return;}
        Unwatch();window=host.Handle;process=host.Process;
        hook=SetWinEventHook(0x800B,0x800B,IntPtr.Zero,callback,process,0,0);
        UpdateRefresh();
    }
    public void Unwatch()
    {
        window=IntPtr.Zero;process=0;
        monitor=IntPtr.Zero;clock.Pause();
        if(hook!=IntPtr.Zero){UnhookWinEvent(hook);hook=IntPtr.Zero;}
    }
    private void OnEvent(IntPtr eventHook,uint eventType,IntPtr handle,int objectId,int childId,uint thread,uint time)
    {
        if(disposed||handle!=window||objectId!=0||childId!=0)return;
        Events++;
        UpdateRefresh();clock.Request();
    }
    private void UpdateRefresh()
    {
        var current=MonitorFromWindow(window,2);
        if(current==monitor)return;
        monitor=current;
        clock.SetRate(DisplayRefresh.ForWindow(window,out var native));NativeRefreshRate=native;
    }
    private void QueueFrame()
    {
        if(disposed||Interlocked.Exchange(ref queued,1)!=0)return;
        // Read the latest native position once per display frame; do not replay stale events.
        context.Post(_=>
        {
            Interlocked.Exchange(ref queued,0);
            if(disposed||window==IntPtr.Zero)return;
            if(HostWindow.ReadSurface(window,process) is {} surface){Updates++;changed(surface);}
        },null);
    }
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;Unwatch();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=displayChanged;
        clock.Dispose();
        if(callbackRoot.IsAllocated)callbackRoot.Free();
        GC.KeepAlive(callback);
    }
    private delegate void WinEventProc(IntPtr hook,uint type,IntPtr window,int objectId,int childId,uint thread,uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventProc callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
}
