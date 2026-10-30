using System.Text.Json;
namespace CodexMeter;

internal sealed record ThreadSelection(string? ThreadId,string Label,bool HasPage)
{
    // A known page without a unique local match must not fall back to another conversation.
    public string? PollTarget=>ThreadId??(HasPage?"@unresolved-current-page":null);
    public bool CanShowConversationMetrics(string? loggedThread)=>ThreadId is not null&&ThreadId.Equals(loggedThread,StringComparison.OrdinalIgnoreCase);
}

internal sealed class ThreadTitleIndex
{
    private readonly string file;
    private Dictionary<string,string> names=new(StringComparer.OrdinalIgnoreCase);
    private DateTime write,nextRead;
    private long length=-1;
    public ThreadTitleIndex(string home)=>file=Path.Combine(home,"session_index.jsonl");

    public ThreadSelection Select(string? pageTitle,string? ipcThread)
    {
        Refresh();
        if(!string.IsNullOrWhiteSpace(pageTitle))
        {
            var matches=names.Where(p=>p.Value.Equals(pageTitle.Trim(),StringComparison.Ordinal)).Select(p=>p.Key).Take(2).ToArray();
            return matches.Length==1?new(matches[0],"跟随当前页面对话",true)
                :new(null,matches.Length>1?"同名对话，暂无法定位":"等待当前页面对话记录",true);
        }
        return new(ipcThread,ipcThread is null?"回退：最近活跃的桌面对话":"回退：IPC 消息流对话",false);
    }

    private void Refresh()
    {
        try
        {
            var info=new FileInfo(file);
            if(!info.Exists)return;
            if(info.Length==length&&info.LastWriteTimeUtc==write&&DateTime.UtcNow<nextRead)return;
            var updated=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var reader=new StreamReader(stream);
            while(reader.ReadLine() is {} line)
            {
                if(line.Length>65536)continue;
                try
                {
                    using var json=JsonDocument.Parse(line); var root=json.RootElement;
                    var id=root.At("id").Str(); var name=root.At("thread_name").Str();
                    if(Guid.TryParse(id,out _)&&!string.IsNullOrWhiteSpace(name))updated[id!]=name.Trim();
                }
                catch(JsonException) { }
            }
            names=updated; length=info.Length; write=info.LastWriteTimeUtc; nextRead=DateTime.UtcNow.AddSeconds(30);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
    }
}
