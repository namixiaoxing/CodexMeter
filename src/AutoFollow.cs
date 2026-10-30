using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
namespace CodexMeter;

internal static class AutoFollow
{
    private const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string UserSid { get { using var user=WindowsIdentity.GetCurrent(); return user.User!.Value; } }
    public static string TaskName=>"CodexMeter.AutoFollow-"+UserSid;
    public static string ErrorFile=>Path.Combine(Settings.Folder,"startup-error.txt");
    public static bool Enabled
    {
        get
        {
            return ReadEnabled(()=>WithFolder(folder=>
            {
                object? task=null;
                try { task=((dynamic)folder).GetTask(TaskName); return (bool)((dynamic)task).Enabled; }
                finally { Release(task); }
            }),()=>
            {
                using var key=Registry.CurrentUser.OpenSubKey(Key);
                return key?.GetValue("CodexMeter") is string;
            });
        }
        set
        {
            WithFolder(folder=>
            {
                if(value)
                {
                    var executable=Environment.ProcessPath??throw new InvalidOperationException("无法取得程序路径");
                    object task=((dynamic)folder).RegisterTask(TaskName,StartupTaskXml.Create(executable,UserSid),6,UserSid,null,3,null);
                    Release(task);
                }
                else
                {
                    DeleteTaskIfPresent(()=>((dynamic)folder).DeleteTask(TaskName,0));
                }
                return true;
            });
            // Remove legacy sign-in registration only after the independent task succeeds.
            using var key=Registry.CurrentUser.OpenSubKey(Key,true); key?.DeleteValue("CodexMeter",false);
        }
    }
    // COM interop maps ERROR_FILE_NOT_FOUND to FileNotFoundException on a fresh user profile.
    // Startup registration is optional; inspecting its state must not prevent the meter from opening.
    internal static bool ReadEnabled(Func<bool> readTask,Func<bool> readLegacy)
    {
        try { if(readTask())return true; }
        catch(Exception e) when(IsReadFailure(e)) { }
        try { return readLegacy(); }
        catch(Exception e) when(IsReadFailure(e)) { return false; }
    }
    private static bool IsReadFailure(Exception e)=>e is COMException or IOException or UnauthorizedAccessException or SecurityException;

    internal static void DeleteTaskIfPresent(Action delete)
    {
        try { delete(); }
        catch(Exception e) when((e is COMException or IOException) && e.HResult==unchecked((int)0x80070002)) { }
    }
    public static void Start()=>WithFolder(folder=>
    {
        object? task=null,running=null;
        try { task=((dynamic)folder).GetTask(TaskName); running=((dynamic)task).Run(null); return true; }
        finally { Release(running); Release(task); }
    });
    private static T WithFolder<T>(Func<object,T> action)
    {
        object? service=null,folder=null;
        try
        {
            service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true)!)!;
            ((dynamic)service).Connect(); folder=((dynamic)service).GetFolder(@"\");
            return action(folder);
        }
        finally { Release(folder); Release(service); }
    }
    private static void Release(object? value) { if(value is not null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value); }
}
