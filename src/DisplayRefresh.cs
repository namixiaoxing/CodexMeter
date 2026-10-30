using System.Runtime.InteropServices;
namespace CodexMeter;

internal static class DisplayRefresh
{
    public static double ForWindow(IntPtr window)=>ForWindow(window,out _);
    public static double ForWindow(IntPtr window,out bool native)
    {
        native=false;
        var device=Screen.FromHandle(window).DeviceName;
        const uint flags=2|0x10; // Active desktop paths, with virtual mode indices understood.
        for(var attempt=0;attempt<3;attempt++)
        {
            if(GetDisplayConfigBufferSizes(flags,out var paths,out var modes)!=0||paths==0||paths>4096||modes>4096)break;
            var pathSize=Marshal.SizeOf<PathInfo>();
            var pathBuffer=Marshal.AllocHGlobal(checked((int)paths*pathSize));
            var modeBuffer=Marshal.AllocHGlobal(checked((int)Math.Max(1,modes)*64));
            try
            {
                var result=QueryDisplayConfig(flags,ref paths,pathBuffer,ref modes,modeBuffer,IntPtr.Zero);
                if(result==122)continue;
                if(result!=0)break;
                for(var i=0;i<paths;i++)
                {
                    var path=Marshal.PtrToStructure<PathInfo>(pathBuffer+i*pathSize);
                    var name=new SourceName{Header=new(){Type=1,Size=(uint)Marshal.SizeOf<SourceName>(),Adapter=path.Source.Adapter,Id=path.Source.Id},Device=""};
                    if(DisplayConfigGetDeviceInfo(ref name)!=0||!device.Equals(name.Device,StringComparison.OrdinalIgnoreCase))continue;
                    var index=(path.Flags&8)!=0?path.Target.ModeIndex>>16:path.Target.ModeIndex;
                    // Target mode's vSyncFreq preserves fractional rates and the physical rate used by DRR.
                    if(index<modes)
                    {
                        var mode=modeBuffer+(int)index*64;
                        if(Marshal.ReadInt32(mode)==2)
                        {
                            var rate=Rate((uint)Marshal.ReadInt32(mode,32),(uint)Marshal.ReadInt32(mode,36));
                            if(rate>0){native=true;return rate;}
                        }
                    }
                    var fallback=Rate(path.Target.Numerator,path.Target.Denominator);
                    if(fallback>0){native=true;return fallback;}
                }
            }
            finally{Marshal.FreeHGlobal(modeBuffer);Marshal.FreeHGlobal(pathBuffer);}
        }
        return 60;
    }
    internal static double Rate(uint numerator,uint denominator)
    {
        var value=denominator==0?0:numerator/(double)denominator;
        return value>=10&&value<=1000?value:0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Luid{public uint Low;public int High;}
    [StructLayout(LayoutKind.Sequential)] private struct Source{public Luid Adapter;public uint Id,ModeIndex,Status;}
    [StructLayout(LayoutKind.Sequential)] private struct Target
    {
        public Luid Adapter;public uint Id,ModeIndex;public int Technology;
        public uint Rotation,Scaling,Numerator,Denominator,ScanOrder,Available,Status;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PathInfo{public Source Source;public Target Target;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)] private struct Header{public uint Type,Size;public Luid Adapter;public uint Id;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct SourceName
    {public Header Header;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags,ref uint paths,IntPtr pathArray,ref uint modes,IntPtr modeArray,IntPtr topology);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern int DisplayConfigGetDeviceInfo(ref SourceName request);
}
