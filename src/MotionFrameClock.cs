using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace CodexMeter;

// Only runs while native move events are arriving. The idle thread sleeps indefinitely.
internal sealed class MotionFrameClock : IDisposable
{
    private readonly AutoResetEvent wake=new(false);
    private readonly ManualResetEvent stop=new(false);
    private readonly NativeTimer timer;
    private readonly Thread thread;
    private readonly Action pulse;
    private double hertz=60;
    private long activeUntil;
    private int disposed;
    public bool HighResolution{get;}
    public double Hertz=>Volatile.Read(ref hertz);
    public MotionFrameClock(Action pulse)
    {
        this.pulse=pulse;
        var handle=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x100002);
        HighResolution=handle!=IntPtr.Zero;
        if(handle==IntPtr.Zero)handle=CreateWaitableTimerEx(IntPtr.Zero,null,0,0x100002);
        timer=new(handle);
        thread=new Thread(Run){IsBackground=true,Name="CodexMeter window frames"};thread.Start();
    }
    public void SetRate(double value)=>Volatile.Write(ref hertz,double.IsFinite(value)&&value>=10&&value<=1000?value:60);
    public void Request()
    {
        if(Volatile.Read(ref disposed)!=0)return;
        Interlocked.Exchange(ref activeUntil,Stopwatch.GetTimestamp()+Stopwatch.Frequency/4);wake.Set();
    }
    public void Pause()=>Interlocked.Exchange(ref activeUntil,0);
    private void Run()
    {
        WaitHandle[] idle={stop,wake};WaitHandle[] timed={stop,timer};
        while(WaitHandle.WaitAny(idle)!=0)
        {
            var next=(double)Stopwatch.GetTimestamp();
            while(Stopwatch.GetTimestamp()<Interlocked.Read(ref activeUntil)&&!stop.WaitOne(0))
            {
                var now=Stopwatch.GetTimestamp();
                if(next>now)
                {
                    var due=-(long)Math.Max(1,Math.Ceiling((next-now)*10000000/Stopwatch.Frequency));
                    if(!timer.SafeWaitHandle.IsInvalid&&SetWaitableTimerEx(timer.SafeWaitHandle,ref due,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,0))
                    {if(WaitHandle.WaitAny(timed)==0)return;}
                    else if(stop.WaitOne(Math.Max(1,(int)((next-now)*1000/Stopwatch.Frequency))))return;
                }
                if(Stopwatch.GetTimestamp()>=Interlocked.Read(ref activeUntil))break;
                pulse();
                var period=Stopwatch.Frequency/Hertz;
                next+=period;
                if(next<Stopwatch.GetTimestamp()-period)next=Stopwatch.GetTimestamp()+period;
            }
        }
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        stop.Set();wake.Set();thread.Join();timer.Dispose();wake.Dispose();stop.Dispose();
    }
    private sealed class NativeTimer : WaitHandle{public NativeTimer(IntPtr handle)=>SafeWaitHandle=new SafeWaitHandle(handle,true);}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr CreateWaitableTimerEx(IntPtr attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll")] private static extern bool SetWaitableTimerEx(SafeWaitHandle timer,ref long due,int period,IntPtr callback,IntPtr state,IntPtr reason,uint tolerance);
}
