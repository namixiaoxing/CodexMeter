using System.Xml.Linq;
namespace CodexMeter;

internal static class StartupTaskXml
{
    public static string Create(string executable,string sid)
    {
        XNamespace ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement E(string name,object value)=>new(ns+name,value);
        return new XDocument(new XElement(ns+"Task",new XAttribute("version","1.2"),
            E("RegistrationInfo",E("Description","CodexMeter: current-user Codex-only lifecycle listener, started independently by Windows.")),
            E("Triggers",new XElement(ns+"LogonTrigger",E("Enabled","true"),E("UserId",sid))),
            E("Principals",new XElement(ns+"Principal",new XAttribute("id","CurrentUser"),E("UserId",sid),E("LogonType","InteractiveToken"),E("RunLevel","LeastPrivilege"))),
            new XElement(ns+"Settings",E("MultipleInstancesPolicy","IgnoreNew"),E("DisallowStartIfOnBatteries","false"),E("StopIfGoingOnBatteries","false"),
                E("AllowHardTerminate","true"),E("StartWhenAvailable","true"),E("RunOnlyIfNetworkAvailable","false"),E("AllowStartOnDemand","true"),
                E("Enabled","true"),E("ExecutionTimeLimit","PT0S"),E("Priority",7),
                new XElement(ns+"RestartOnFailure",E("Interval","PT1M"),E("Count",3))),
            new XElement(ns+"Actions",new XAttribute("Context","CurrentUser"),new XElement(ns+"Exec",E("Command",executable),E("WorkingDirectory",Path.GetDirectoryName(executable)??""))))).ToString();
    }
}
