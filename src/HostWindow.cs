using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace CodexMeter;

internal sealed record HostSurface(IntPtr Handle, uint Process, Rectangle Client, bool Visible, bool Minimized, uint Dpi, Rectangle? Frame = null);

internal static class HostWindow
{
    private static IntPtr known;
    private static uint knownProcess;
    private static DateTime nextSearch;
    public static HostSurface? Find()
    {
        if (known != IntPtr.Zero && IsWindow(known))
        {
            GetWindowThreadProcessId(known, out var pid);
            if (pid == knownProcess) return Read(known, pid);
        }
        known = IntPtr.Zero;
        if (DateTime.UtcNow < nextSearch) return null;
        nextSearch = DateTime.UtcNow.AddSeconds(1);
        var candidates = new List<(IntPtr Handle, uint Pid, long Area)>();
        EnumWindows((h, _) =>
        {
            if (GetWindow(h, 4) != IntPtr.Zero) return true;
            var name = new StringBuilder(256); GetClassName(h, name, name.Capacity);
            if (!name.ToString().StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal)) return true;
            GetWindowThreadProcessId(h, out var pid);
            try
            {
                using var p = Process.GetProcessById((int)pid);
                var path = p.MainModule?.FileName ?? "";
                var executable = Path.GetFileName(path);
                // Current Windows package uses ChatGPT.exe; older Desktop builds used Codex.exe.
                if (!(executable.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase) || executable.Equals("Codex.exe", StringComparison.OrdinalIgnoreCase))) return true;
                if (!(path.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) || path.Contains("OpenAI\\Codex\\", StringComparison.OrdinalIgnoreCase))) return true;
                if (!GetClientRect(h, out var r)) return true;
                // Excludes pets, launch splashes and small auxiliary surfaces.
                var title = new StringBuilder(512); GetWindowText(h, title, title.Capacity);
                var isMinimized = IsIconic(h);
                if (!IsWindowVisible(h) || (!isMinimized && (r.Right < 420 || r.Bottom < 300)) || title.Length == 0) return true;
                candidates.Add((h, pid, (long)r.Right * r.Bottom));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return true;
        }, IntPtr.Zero);
        var foreground = GetForegroundWindow();
        var best = candidates.OrderByDescending(c => c.Handle == foreground).ThenByDescending(c => c.Area).FirstOrDefault();
        if (best.Handle == IntPtr.Zero) return null;
        known = best.Handle; knownProcess = best.Pid;
        return Read(known, knownProcess);
    }
    private static HostSurface? Read(IntPtr h, uint pid)
    {
        if (!GetClientRect(h, out var rect)) return null;
        var point = new NativePoint(); ClientToScreen(h, ref point);
        Rectangle? frame = null;
        if (DwmGetWindowAttribute(h, 9, out var extended, Marshal.SizeOf<NativeRect>()) == 0)
            frame = Rectangle.FromLTRB(extended.Left, extended.Top, extended.Right, extended.Bottom);
        return new(h, pid, new Rectangle(point.X, point.Y, rect.Right, rect.Bottom), IsWindowVisible(h), IsIconic(h), Math.Max(96u, GetDpiForWindow(h)), frame);
    }
    public static bool IsForeground(HostSurface host, params IntPtr[] own)
    {
        var f = GetForegroundWindow();
        if (f == host.Handle || own.Contains(f)) return true;
        GetWindowThreadProcessId(f, out var pid);
        // Menus and the settings dialog are in our process.
        return pid == Environment.ProcessId;
    }
    public static void Own(IntPtr child, IntPtr host) => SetWindowLongPtr(child, -8, host);
    public static IntPtr OwnerOf(IntPtr child) => GetWindow(child, 4);
    public static void ShowPassive(IntPtr child) => ShowWindow(child, 8);
    // Place only the passive overlay behind the panel, without activation or global raising.
    public static void KeepBelow(IntPtr lower,IntPtr upper)
    {
        if(GetWindow(lower,3)!=upper)SetWindowPos(lower,upper,0,0,0,0,0x0001|0x0002|0x0010|0x0200);
    }
    internal static bool ImmediatelyBelow(IntPtr lower,IntPtr upper)=>GetWindow(lower,3)==upper;
    internal static IntPtr ForegroundHandle=>GetForegroundWindow();
    public static void Position(IntPtr h, Rectangle bounds) => SetWindowPos(h, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0004 | 0x0200);
    public static Rectangle VisibleBounds(HostSurface host) => Rectangle.Intersect(host.Frame ?? host.Client, Screen.FromHandle(host.Handle).WorkingArea);
    public static Rectangle ActualBounds(IntPtr h) => GetWindowRect(h, out var r) ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : Rectangle.Empty;
    internal static bool CanFit(Rectangle visible, Size size, uint dpi) => visible.Width >= size.Width + 2 * (int)Math.Round(12 * dpi / 96f) && visible.Height >= size.Height + 2 * (int)Math.Round(12 * dpi / 96f);
    internal static Rectangle DockBounds(Rectangle visible, Size size, uint dpi)
    {
        var margin = (int)Math.Round(12 * dpi / 96f);
        return new(visible.Right - size.Width - margin, visible.Bottom - size.Height - margin, size.Width, size.Height);
    }
    private delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr p);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint command);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out NativeRect r);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out NativeRect r);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out NativeRect r, int size);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref NativePoint p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder text, int count);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
}

