using System;
using System.Windows.Threading;
using Island.Social;

namespace Island;

public partial class MainWindow
{
    private readonly DispatcherTimer _socialTimer=new(){Interval=TimeSpan.FromMilliseconds(400)};
    private DateTime _socialUntil;
    private string? _socialActive;
    private DateTime _socialDiagnosticAt;
    private void InitSocial()
    {
        Loaded+=async (_,_)=>await SocialHub.StartAsync();
        _socialTimer.Tick+=(_,_)=>
        {
            WriteSocialDiagnostic();
            if(KeepOpen || _state==IslandState.Expanded || _toastTimer.IsEnabled || DateTime.UtcNow<_socialUntil)return;
            var item=SocialHub.Manager.Next(SocialHub.Options,
                !UserHidden && !_hiding && IsVisible && Root.Visibility==System.Windows.Visibility.Visible);
            if(item is null)return;
            var o=SocialHub.Options(item.Service);
            int seconds=Math.Clamp(o.DurationSeconds,3,15);
            _socialUntil=DateTime.UtcNow.AddSeconds(seconds);
            string title=(item.Test?"ТЕСТ · ":"")+item.Title+(item.Count>1?" · "+item.Count:"");
            ShowToast(item.Kind==SocialKind.Call?"\ue717":"\ue8f2",title,null,item.Service,FillNormal,
                ("Открыть",async ()=>
                {
                    bool opened=await SocialHub.OpenAsync(item.Service);
                    if(!opened)ShowToast("\ue783","Клиент не открылся",null,item.Service,FillNormal);
                }),seconds*1000,false,new ToastExtra(item.Body,Thumb:item.Logo));
            _socialActive=item.Service;
            if(o.Sound && !o.DoNotDisturb)System.Media.SystemSounds.Asterisk.Play();
        };
        _socialTimer.Start();
        SocialHub.PolicyChanged+=SocialPolicyChanged;
        Closed+=(_,_)=>{_socialTimer.Stop();SocialHub.PolicyChanged-=SocialPolicyChanged;SocialHub.Stop();};
    }
    private void WriteSocialDiagnostic()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--social-diagnostics")<0 || DateTime.UtcNow<_socialDiagnosticAt)return;
        _socialDiagnosticAt=DateTime.UtcNow.AddSeconds(2);
        try
        {
            var source=SocialHub.Windows;
            var state=new{at=DateTimeOffset.UtcNow,source.Status,source.PollCount,source.SnapshotCount,source.MatchedCount,source.EmittedCount,source.LastFailure,pending=SocialHub.Manager.PendingCount,history=SocialHub.Manager.History.Count,islandState=_state?.ToString(),userHidden=UserHidden,hiding=_hiding,keepOpen=KeepOpen,typing=_typing,menuOpen=_menuOpen,toastTimer=_toastTimer.IsEnabled};
            System.IO.Directory.CreateDirectory(Services.Settings.Folder);
            System.IO.File.WriteAllText(System.IO.Path.Combine(Services.Settings.Folder,"social-diagnostics.json"),System.Text.Json.JsonSerializer.Serialize(state));
        }catch{/* optional counters only, never notification content */}
    }
    private void SocialPolicyChanged(string service)
    {
        if(_socialActive!=service)return;
        _socialActive=null;_socialUntil=DateTime.MinValue;
        ToastLabel.Text="";ToastSub.Text="";ToastThumbBrush.ImageSource=null;
        _toastAction=_toastAction2=null;_toastInteractive=false;
        if(_state==IslandState.Toast){_toastTimer.Stop();ApplyState(BaseState());}
    }
}
