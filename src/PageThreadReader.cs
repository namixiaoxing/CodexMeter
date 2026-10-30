using System.Windows.Automation;
namespace CodexMeter;

// Read only the current web document's title. No message text, editor values, or input actions.
internal sealed class PageThreadReader
{
    private AutomationElement? document;
    private IntPtr handle;
    public string? Read(HostSurface? host)
    {
        if(host is null)return null;
        if(handle!=host.Handle) { document=null; handle=host.Handle; }
        for(var attempt=0;attempt<2;attempt++)
        {
            try
            {
                document??=AutomationElement.FromHandle(host.Handle).FindFirst(TreeScope.Descendants,
                    new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Document),
                        new PropertyCondition(AutomationElement.AutomationIdProperty,"RootWebArea")));
                return document?.Current.Name;
            }
            catch(Exception e) when(e is ElementNotAvailableException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            { document=null; }
        }
        return null;
    }
}
