using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Island.Services;

namespace Island.Social;

public static class SocialHub
{
    public static readonly string[] Services={"Discord","Steam","Telegram"};
    public static NotificationManager Manager {get;}=new();
    public static WindowsNotificationProvider Windows {get;}=new();
    public static event Action<string>? PolicyChanged;
    public static string ServiceStatus(string service)
    {
        var o=Options(service);
        if(!o.Enabled)return "Отключено";
        if(string.IsNullOrWhiteSpace(o.AppId))return "Не выбран источник Windows";
        if(!Windows.Status.StartsWith("Подключено",StringComparison.Ordinal))return Windows.Status;
        return Windows.AvailableApps.ContainsKey(o.AppId)
            ? "Источник обнаружен Windows"
            : "Мост подключён · ждём уведомление источника";
    }
    private static readonly INotificationProvider[] Providers={new DiscordNotificationProvider(Windows),new SteamNotificationProvider(Windows),new TelegramNotificationProvider(Windows)};
    public static SocialOptions Options(string service)
    {
        Settings.Current.SocialIntegrations ??= new();
        if(!Settings.Current.SocialIntegrations.TryGetValue(service,out var o) || o is null)
            Settings.Current.SocialIntegrations[service]=o=new();
        return o;
    }
    static SocialHub()
    {
        foreach(var p in Providers)p.NotificationReceived+=n=>Manager.Enqueue(n,Options(n.Service));
        Windows.AccessLost+=()=>{Manager.Clear();foreach(var s in Services)PolicyChanged?.Invoke(s);};
    }
    public static async Task StartAsync()
    {
        if(Settings.Current.SocialListenerEnabled && Services.Any(s=>Options(s).Enabled))
            await Windows.ConnectAsync(false); // Never request OS permission automatically.
    }
    public static void Changed(string service)
    {
        Manager.Forget(service);Settings.Current.Save();
        PolicyChanged?.Invoke(service);
        if(!Services.Any(s=>Options(s).Enabled)){Windows.Stop();return;}
        _ = StartAsync(); // Reset the baseline after identity/privacy changes: don't replay old toasts.
    }
    public static void Test(string service,SocialKind kind=SocialKind.Message)
        =>Manager.Enqueue(new(Guid.NewGuid().ToString(),service,"test","Тест · "+service,
            "Синтетическое уведомление Island — не сообщение из аккаунта",DateTimeOffset.Now,kind,Test:true),
            new SocialOptions{Enabled=true,HideContent=false,DurationSeconds=6});
    public static async Task<bool> OpenAsync(string service)
    {
        // Fixed allowlist. Never execute a URI or toast action supplied by notification text.
        string? uri=service switch{"Discord"=>"discord://-/channels/@me","Steam"=>"steam://open/friends","Telegram"=>"tg://",_=>null};
        if(uri is null)return false;
        try{return await global::Windows.System.Launcher.LaunchUriAsync(new Uri(uri));}catch{return false;}
    }
    public static void Stop(){Windows.Stop();Manager.Clear();foreach(var s in Services)PolicyChanged?.Invoke(s);}
}
