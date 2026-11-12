// Notification routing and in-memory history; no account tokens or message logging.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;

namespace Island.Social;

public enum SocialKind { Message, Call, Invitation, Mention }
public sealed record SocialNotification(string Id, string Service, string Group, string Title,
    string Body, DateTimeOffset Time, SocialKind Kind = SocialKind.Message,
    ImageSource? Logo = null, bool Test = false, int Count = 1);

public sealed class SocialOptions
{
    public bool Enabled { get; set; }
    public string AppId { get; set; } = "";
    public bool HideContent { get; set; } = true;
    public bool DoNotDisturb { get; set; }
    public bool Sound { get; set; }
    public bool RetainContentInMemory { get; set; }
    public int DurationSeconds { get; set; } = 6;
}

public interface INotificationProvider : IDisposable
{
    string Service { get; }
    event Action<SocialNotification>? NotificationReceived;
}

public abstract class AppNotificationProvider : INotificationProvider
{
    private readonly WindowsNotificationProvider _source;
    public abstract string Service { get; }
    protected AppNotificationProvider(WindowsNotificationProvider source)
    { _source=source; source.NotificationReceived+=Receive; }
    public event Action<SocialNotification>? NotificationReceived;
    private void Receive(SocialNotification n)
    {
        var o=SocialHub.Options(Service);
        if(o.Enabled && o.AppId.Length>0 && string.Equals(o.AppId,n.Service,StringComparison.Ordinal)
            && SocialHub.Services.FirstOrDefault(s=>SocialHub.Options(s).Enabled && SocialHub.Options(s).AppId==n.Service)==Service)
            NotificationReceived?.Invoke(n with {Service=Service});
    }
    public void Dispose() => _source.NotificationReceived-=Receive;
}
public sealed class DiscordNotificationProvider : AppNotificationProvider
{ public override string Service=>"Discord"; public DiscordNotificationProvider(WindowsNotificationProvider source):base(source){} }
public sealed class SteamNotificationProvider : AppNotificationProvider
{ public override string Service=>"Steam"; public SteamNotificationProvider(WindowsNotificationProvider source):base(source){} }
public sealed class TelegramNotificationProvider : AppNotificationProvider
{ public override string Service=>"Telegram"; public TelegramNotificationProvider(WindowsNotificationProvider source):base(source){} }

// UI-thread confined. Bounded pending queue/history; no disk history or content logging.
public sealed class NotificationManager
{
    private readonly List<SocialNotification> _pending=new();
    private readonly Queue<string> _seenOrder=new();
    private readonly HashSet<string> _seen=new(StringComparer.Ordinal);
    private readonly List<SocialNotification> _history=new();
    public IReadOnlyList<SocialNotification> History=>_history.AsReadOnly();
    public int PendingCount=>_pending.Count;
    public bool Enqueue(SocialNotification n, SocialOptions o)
    {
        if((!o.Enabled && !n.Test) || o.DoNotDisturb) return false;
        string id=n.Service+":"+n.Id;
        if(!_seen.Add(id))return false;
        _seenOrder.Enqueue(id);
        while(_seenOrder.Count>2048)_seen.Remove(_seenOrder.Dequeue());
        n=n with {Title=Limit(n.Title,120),Body=Limit(n.Body,600),Group=Limit(n.Group,180)};
        // Redact before retaining/queuing, not only at rendering time.
        if(o.HideContent)n=n with {Title=n.Service,Body="Содержимое скрыто",Group="private"};
        int existing=_pending.FindIndex(x=>x.Service==n.Service && x.Group==n.Group && x.Kind==n.Kind && n.Time>=x.Time && n.Time-x.Time<TimeSpan.FromSeconds(8));
        if(existing>=0){n=n with {Count=_pending[existing].Count+1};_pending.RemoveAt(existing);}
        _pending.Add(n);
        if(_pending.Count>100)_pending.RemoveAt(_pending.FindIndex(x=>x.Kind!=SocialKind.Call) is var i && i>=0?i:0);
        var history=o.RetainContentInMemory?n:n with{Title=n.Service,Body="Содержимое не сохраняется",Group="",Logo=null};
        _history.Insert(0,history); if(_history.Count>100)_history.RemoveAt(100);
        return true;
    }
    public SocialNotification? Next(Func<string,SocialOptions> options, bool canPresent=true)
    {
        _pending.RemoveAll(n=>options(n.Service).DoNotDisturb || (!n.Test && !options(n.Service).Enabled));
        if(!canPresent || _pending.Count==0)return null;
        int i=_pending.FindIndex(n=>n.Kind==SocialKind.Call);if(i<0)i=0;
        var next=_pending[i];_pending.RemoveAt(i);return next;
    }
    public void Forget(string service)
    { _pending.RemoveAll(n=>n.Service==service);_history.RemoveAll(n=>n.Service==service); }
    public void Clear(){_pending.Clear();_history.Clear();_seen.Clear();_seenOrder.Clear();}
    public static string Fingerprint(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Limit(string? value,int max)=>value is null?"":value.Length<=max?value:value.Substring(0,max);
}
