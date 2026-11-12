using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Island.Services;
using Island.Social;

namespace Island;

public partial class SettingsWindow
{
    private string _socialService="Discord";
    private void BuildIntegrations()
    {
        Card(Row("Social Hub · Windows Toast", "Сообщения остаются на этом ПК. Чтение требует установленного MSIX-пакета и отдельного разрешения Windows. Видны только события, которые оригинальные приложения передали в Центр уведомлений. История — только в памяти, до 100 записей.",null,"\ue8f2"));
        Card(ButtonRow("Доступ Windows", SocialHub.Windows.Status,
            "Запросить доступ",async ()=>
            {
                // Explicit UI choice; permission dialog is completed by the user, never automation.
                bool allowed=await SocialHub.Windows.ConnectAsync(true);
                S.SocialListenerEnabled=allowed;S.Save();RebuildPage();
            },"\ue72e"));
        Card(ButtonRow("Отключить всё", "Остановит чтение и очистит очередь/историю; разрешение Windows можно отозвать отдельно в настройках Windows.","Отключить",()=>
        {
            S.SocialListenerEnabled=false;
            foreach(var name in SocialHub.Services)SocialHub.Options(name).Enabled=false;
            SocialHub.Stop();S.Save();RebuildPage();
        }));
        foreach(string name in SocialHub.Services)
            Card(ButtonRow(name,SocialHub.ServiceStatus(name),"Тест",()=>SocialHub.Test(name),name=="Telegram"?"\ue724":name=="Steam"?"\ue7fc":"\ue8f2"));
        Card(ChoiceRow("Настроить сервис", "Тест выше — искусственное уведомление, не подтверждение подключения",new[]{("Discord","Discord"),("Steam","Steam"),("Telegram","Telegram")},_socialService,v=>{_socialService=v;RebuildPage();}));
        foreach(string service in SocialHub.Services.Where(s=>s==_socialService))
        {
            var o=SocialHub.Options(service);
            string glyph=service switch{"Discord"=>"\ue8f2","Steam"=>"\ue7fc",_=>"\ue724"};
            Card(Row(service,SocialHub.ServiceStatus(service),null,glyph));
            Card(SwitchRow("Уведомления · "+service,"Включает только выбранный точный AppUserModelId",o.Enabled,v=>
            {o.Enabled=v;SocialHub.Changed(service);}));
            var appId=new TextBox{Text=o.AppId,MinWidth=180,MaxWidth=350,ToolTip="Точный AppUserModelId из Windows",Style=(Style)FindResource("T.TextBox")};
            appId.LostKeyboardFocus+=(_,_)=>{o.AppId=appId.Text.Trim();SocialHub.Changed(service);};
            Card(Row("Идентификатор приложения", "Не определяется по тексту сообщения или имени отправителя.",appId));
            var apps=new Button{Content="Выбрать приложение",Style=(Style)FindResource("T.Button")};
            var list=new StackPanel();
            var popup=new Popup{PlacementTarget=apps,Placement=PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true};
            popup.Child=new Border{Background=(Brush)FindResource("T.Field"),Padding=new Thickness(8),CornerRadius=new CornerRadius(8),Child=new ScrollViewer{MaxHeight=240,MaxWidth=480,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=list}};
            foreach(var app in SocialHub.Windows.AvailableApps.OrderBy(p=>p.Value))
            {
                string id=app.Key;
                var select=new Button{Content=app.Value+" · "+app.Key,Style=(Style)FindResource("T.Button"),Margin=new Thickness(2)};
                select.Click+=(_,_)=>{o.AppId=id;appId.Text=id;popup.IsOpen=false;SocialHub.Changed(service);};list.Children.Add(select);
            }
            if(list.Children.Count==0)list.Children.Add(new TextBlock{Text="Нет доступных приложений",Foreground=(Brush)FindResource("T.Text")});
            apps.Click+=(_,_)=>popup.IsOpen=true;
            Card(Row("Приложения в Центре уведомлений", "После доступа нажмите «Обновить список». Выберите настоящий клиент; один AppId назначайте одному сервису.",apps));
            Card(SwitchRow("Скрывать отправителя и текст","Скрытие применяется до очереди и истории",o.HideContent,v=>{o.HideContent=v;SocialHub.Changed(service);}));
            Card(SwitchRow("Не беспокоить","Новые события этого сервиса не сохраняются и не показываются",o.DoNotDisturb,v=>{o.DoNotDisturb=v;SocialHub.Changed(service);}));
            Card(SwitchRow("Звук","Системный короткий сигнал Island; возможен второй звук оригинального клиента",o.Sound,v=>{o.Sound=v;SocialHub.Changed(service);}));
            Card(SwitchRow("Хранить текст в истории сеанса","Только RAM, до закрытия; выключение очищает прежнюю историю сервиса",o.RetainContentInMemory,v=>{o.RetainContentInMemory=v;SocialHub.Changed(service);}));
            Card(SliderRow("Длительность карточки","Существующая анимация Island",3,15,1,o.DurationSeconds,v=>v+" сек",v=>{o.DurationSeconds=(int)v;SocialHub.Changed(service);}));
            Card(ButtonRow("Отключить · "+service,"Очистит очередь и историю этого сервиса", "Отключить",()=>{o.Enabled=false;SocialHub.Changed(service);RebuildPage();}));
        }
        Card(ButtonRow("Список и история","Обновляет только отображение текущего состояния", "Обновить список",RebuildPage));
        Card(Row("Ограничения Windows-моста","Нет ответа на сообщение, гарантированного аватара, типа звонка/приглашения и точного перехода в чат. Кнопка открывает оригинальный клиент. Steam overlay-only события недоступны. Запросы к Discord/Steam/Telegram API и чтение сессий не используются.",null));
        foreach(var item in SocialHub.Manager.History.Take(30))
            Card(Row(item.Service+(item.Test?" · ТЕСТ":"")+" · "+item.Time.ToLocalTime().ToString("HH:mm"),item.Title+"\n"+item.Body,null));
    }
}
