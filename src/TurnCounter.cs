using System.Text;
using System.Text.Json;
namespace CodexMeter;

internal sealed class TurnCounter
{
    private readonly HashSet<string> ids=new(StringComparer.Ordinal);
    public int Count=>ids.Count;
    public void Clear()=>ids.Clear();
    public void Add(JsonElement payload)
    {
        if(payload.At("turn_id").Str() is { Length:>0 } id) ids.Add(id);
    }
    // Large logs normally use a bounded tail for metrics. Count the older prefix once, keeping
    // at most 4 KiB per physical line, so embedded images and tool results are never buffered.
    public long ReadPrefix(Stream stream,long until)
    {
        stream.Position=0; var buffer=new byte[65536]; var line=new byte[4096]; int length=0; long position=0;
        while(position<stream.Length)
        {
            var n=stream.Read(buffer); if(n==0)break;
            for(var i=0;i<n;i++)
            {
                position++;
                if(buffer[i]==10)
                {
                    if(length>0)
                    {
                        try
                        {
                            var prefix=line.AsSpan(0,length);
                            if(prefix.IndexOf("task_started"u8)>=0 || prefix.IndexOf("turn_context"u8)>=0)
                            {
                                using var doc=JsonDocument.Parse(line.AsMemory(0,length)); var root=doc.RootElement; var p=root.At("payload");
                                if(root.At("type").Str()=="turn_context" || root.At("type").Str()=="event_msg" && p.At("type").Str()=="task_started") Add(p);
                            }
                        }
                        catch(JsonException) { }
                    }
                    length=0; if(position>=until)return position;
                }
                else if(length<line.Length) line[length++]=buffer[i];
            }
        }
        return position;
    }
}
