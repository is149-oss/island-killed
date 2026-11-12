// Synthetic HTTP contract fixtures. This is NOT an AI model or a real forecast provider.
// Only the original clients run; no production feature is replaced with a fixture.
using Island.Services;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
    throw new InvalidOperationException("Use an ordinary/restricted token.");
if (!DeveloperAccess.Enabled || ReviewSafety.Enabled)
    throw new InvalidOperationException("DeveloperPro and --enable-integrations required for loopback-only test process.");
var results=new Dictionary<string,bool>();
var requests=new List<string>();
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
var server=new TcpListener(IPAddress.Loopback,0); server.Start();
string root="http://127.0.0.1:"+((IPEndPoint)server.LocalEndpoint).Port;
string date=DateTime.Now.ToString("yyyy-MM-dd"), time=date+"T12:00";
string forecast=JsonSerializer.Serialize(new {
    current=new {time,temperature_2m=12.5,apparent_temperature=11.0,relative_humidity_2m=55,weather_code=0,wind_speed_10m=2.0,is_day=1},
    hourly=new {time=new[]{time},temperature_2m=new[]{12.5},weather_code=new[]{0},is_day=new[]{1}},
    daily=new {time=new[]{date},temperature_2m_max=new[]{15.0},temperature_2m_min=new[]{5.0},weather_code=new[]{0},precipitation_probability_max=new[]{10}}});
var serving=Task.Run(async ()=> {
    try {
        while(!timeout.IsCancellationRequested) {
            using var client=await server.AcceptTcpClientAsync(timeout.Token);
            using var stream=client.GetStream();
            using var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true);
            string first=await reader.ReadLineAsync(timeout.Token)??"";
            string target=first.Split(' ')[1]; int length=0;
            string? line;
            while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync(timeout.Token)))
                if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)) length=int.Parse(line.Split(':')[1]);
            var body=new char[length]; int offset=0;
            while(offset<length) { int n=await reader.ReadAsync(body.AsMemory(offset),timeout.Token); if(n==0) break; offset+=n; }
            lock(requests) requests.Add(first);
            string reply, mime="application/json";
            if(target=="/v1/models") reply="{\"data\":[{\"id\":\"local-test-model\"}]}";
            else if(target=="/v1/chat/completions") {
                using var json=JsonDocument.Parse(new string(body));
                results["ai_request_contract"]=json.RootElement.GetProperty("stream").GetBoolean() && json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()=="synthetic prompt";
                reply="data: {\"choices\":[{\"delta\":{\"content\":\"fixture \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"response\"}}]}\n\ndata: [DONE]\n\n";mime="text/event-stream";
            }
            else if(target.StartsWith("/forecast?")) reply=forecast;
            else if(target.StartsWith("/geocode?")) reply="{\"results\":[{\"latitude\":1.0,\"longitude\":2.0,\"name\":\"Synthetic City\"}]}";
            else if(target.StartsWith("/lyrics/api/get?")) reply=JsonSerializer.Serialize(new{syncedLyrics="[00:01.00]fixture first\n[00:02.00]fixture second"});
            else throw new InvalidOperationException("Unexpected fixture request: "+target);
            byte[] bytes=Encoding.UTF8.GetBytes(reply);
            byte[] header=Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {mime}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header,timeout.Token);await stream.WriteAsync(bytes,timeout.Token);await stream.FlushAsync(timeout.Token);
        }
    } catch(OperationCanceledException) { }
});
try {
    var p=new AiProvider("custom","Synthetic loopback fixture",root+"/v1","","",Free:false);
    results["ai_models_local_endpoint"]=(await AiService.ListModelsAsync(p,"",timeout.Token)).SequenceEqual(new[]{"local-test-model"});
    var response=new StringBuilder();
    await foreach(var delta in AiService.StreamAsync(p,"","local-test-model",new[]{("user","synthetic prompt")},timeout.Token))response.Append(delta);
    results["ai_sse_original_parser"]=response.ToString()=="fixture response";
    Environment.SetEnvironmentVariable("ISLAND_WEATHER_FORECAST_URL",root+"/forecast");
    Environment.SetEnvironmentVariable("ISLAND_WEATHER_GEOCODING_URL",root+"/geocode");
    var flags=BindingFlags.NonPublic|BindingFlags.Static;
    var weather=await (Task<WeatherNow>)typeof(WeatherService).GetMethod("OpenMeteoAsync",flags)!.Invoke(null,new object[]{1.0,2.0,"Synthetic City"})!;
    results["weather_original_forecast_parser"]=weather.Temp==12.5 && weather.Hours.Count==1 && weather.Days.Count==1;
    var geocodeTask=(Task)typeof(WeatherService).GetMethod("GeocodeAsync",flags)!.Invoke(null,new object[]{"Synthetic City"})!;
    await geocodeTask;
    results["weather_custom_geocode"]=geocodeTask.GetType().GetProperty("Result")!.GetValue(geocodeTask) is not null;
    Environment.SetEnvironmentVariable("ISLAND_LYRICS_LRCLIB_URL",root+"/lyrics");
    Settings.Current.Lyrics=true;
    var lyrics=new LyricsService();
    await lyrics.LoadAsync("Synthetic Song","Synthetic Artist",TimeSpan.FromSeconds(3));
    results["lyrics_custom_endpoint_original_loader"]=lyrics.Lines?.Count==2 && lyrics.IndexAt(TimeSpan.FromSeconds(1.5))==0;
    results["access_without_review_network_block"]=License.IsPro && License.Gate() && !License.IsTrial && License.Expires is null;
    results["update_check_no_request"]=!await UpdateService.CheckAsync();
    try {await UpdateService.StartDownload();results["update_download_blocked_with_integrations"]=false;}
    catch(InvalidOperationException){results["update_download_blocked_with_integrations"]=true;}
    try {UpdateService.Apply();results["update_apply_blocked_with_integrations"]=false;}
    catch(InvalidOperationException){results["update_apply_blocked_with_integrations"]=true;}
} finally {timeout.Cancel();server.Stop();await serving;}
results["only_expected_fixture_requests"]=requests.Count==5;
File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"loopback-results.json"),JsonSerializer.Serialize(new{elevated=false,syntheticFixtures=true,requests,results,passed=results.Values.All(x=>x)},new JsonSerializerOptions{WriteIndented=true}));
return results.Values.All(x=>x)?0:1;
