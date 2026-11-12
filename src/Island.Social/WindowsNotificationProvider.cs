using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.ApplicationModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Island.Social;

// Official Notification Listener API. Capability + identity + explicit OS permission required.
public sealed class WindowsNotificationProvider : INotificationProvider
{
    public string Service=>"Windows";
    public string Status {get;private set;}="Не подключён";
    public Dictionary<string,string> AvailableApps {get;}=new(StringComparer.Ordinal);
    public event Action<SocialNotification>? NotificationReceived;
    public event Action? AccessLost;
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly HashSet<string> _known=new(StringComparer.Ordinal);
    private bool _busy,_baseline;
    private int _generation;
    public long PollCount {get;private set;}
    public int SnapshotCount {get;private set;}
    public long MatchedCount {get;private set;}
    public long EmittedCount {get;private set;}
    public string LastFailure {get;private set;}="";
    public WindowsNotificationProvider(){_timer.Tick+=async (_,_)=>await PollAsync();}
    public static bool HasIdentity
    {get{try{return !string.IsNullOrEmpty(Package.Current.Id.FullName);}catch{return false;}}}
    public async Task<bool> ConnectAsync(bool requestPermission)
    {
        Stop();
        if(!HasIdentity){Status="Нужна установленная MSIX-версия с capability";return false;}
        try
        {
            var listener=UserNotificationListener.Current;
            var access=requestPermission?await listener.RequestAccessAsync():listener.GetAccessStatus();
            if(access!=UserNotificationListenerAccessStatus.Allowed){Status="Нет разрешения Windows на уведомления";return false;}
            Status="Подключено: Windows Toast";
            // Explicit diagnostic replay reads existing OS notifications; never enabled on normal startup.
            _baseline=!(Array.IndexOf(Environment.GetCommandLineArgs(),"--social-diagnostics")>=0
                && Array.IndexOf(Environment.GetCommandLineArgs(),"--social-replay-existing")>=0);
            _timer.Start();await PollAsync();return true;
        }
        catch{Status="Notification Listener недоступен; проверьте пакет и разрешение";return false;}
    }
    public void Stop(){_generation++;_timer.Stop();_known.Clear();AvailableApps.Clear();Status="Отключён";}
    private async Task PollAsync()
    {
        if(_busy || !_timer.IsEnabled)return;
        int generation=_generation;_busy=true;
        PollCount++;
        try
        {
            var listener=UserNotificationListener.Current;
            if(listener.GetAccessStatus()!=UserNotificationListenerAccessStatus.Allowed)
            {Stop();Status="Разрешение Windows отозвано";AccessLost?.Invoke();return;}
            var snapshot=await listener.GetNotificationsAsync(NotificationKinds.Toast);
            SnapshotCount=snapshot.Count;
            if(generation!=_generation)return;
            var live=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in snapshot.OrderBy(n=>n.CreationTime))
            {
                try
                {
                    string appId=item.AppInfo.AppUserModelId;
                    AvailableApps[appId]=item.AppInfo.DisplayInfo.DisplayName;
                    // Read bodies only for user-pinned, enabled identities.
                    bool selected=SocialHub.Services.Any(s=>SocialHub.Options(s).Enabled && SocialHub.Options(s).AppId==appId);
                    if(!selected)continue;
                    MatchedCount++;
                    var binding=item.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                    if(binding is null)continue;
                    var texts=binding.GetTextElements().Select(t=>t.Text??"").ToArray();
                    string title=texts.FirstOrDefault()??item.AppInfo.DisplayInfo.DisplayName;
                    string body=string.Join("\n",texts.Skip(1));
                    string id=appId+":"+item.Id+":"+item.CreationTime.UtcTicks+":"+NotificationManager.Fingerprint(title+"\n"+body);
                    live.Add(id);
                    if(!_known.Add(id) || _baseline)continue;
                    // Listener exposes app logo, not a reliable sender avatar or chat activation args.
                    BitmapImage? logo=null;
                    try
                    {
                        using var ras=await item.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(48,48)).OpenReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                        using var stream=ras.AsStreamForRead();
                        if(stream.Length<=1024*1024){logo=new BitmapImage();logo.BeginInit();logo.CacheOption=BitmapCacheOption.OnLoad;logo.DecodePixelWidth=48;logo.StreamSource=stream;logo.EndInit();logo.Freeze();}
                    }catch{/* app logo optional; no remote image fetch */}
                    if(generation!=_generation)return;
                    if(listener.GetAccessStatus()!=UserNotificationListenerAccessStatus.Allowed)
                    {Stop();Status="Разрешение Windows отозвано";AccessLost?.Invoke();return;}
                    NotificationReceived?.Invoke(new(id,appId,title,title,body,item.CreationTime,Logo:logo));
                    EmittedCount++;
                }
                catch(Exception ex){LastFailure=ex.GetType().Name+":"+ex.HResult;}
            }
            _known.IntersectWith(live);_baseline=false;
        }
        catch(Exception ex){Status="Не удалось прочитать Windows Toast";LastFailure=ex.GetType().Name+":"+ex.HResult;}
        finally{_busy=false;}
    }
    public void Dispose()=>Stop();
}
