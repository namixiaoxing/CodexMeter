using Microsoft.Win32;
using System.Runtime.InteropServices;
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
            try
            {
                if(WithFolder(folder=>
                {
                    object? task=null;
                    try { task=((dynamic)folder).GetTask(TaskName); return (bool)((dynamic)task).Enabled; }
                    finally { Release(task); }
                }))return true;
            }
            catch(COMException) { }
            using var key=Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue("CodexMeter") is string;
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
                    try { ((dynamic)folder).DeleteTask(TaskName,0); }
                    catch(COMException e) when(e.HResult==unchecked((int)0x80070002)) { }
                }
                return true;
            });
            // Remove legacy sign-in registration only after the independent task succeeds.
            using var key=Registry.CurrentUser.OpenSubKey(Key,true); key?.DeleteValue("CodexMeter",false);
        }
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
