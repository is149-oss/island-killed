// Local regression probe. Synthetic test data, no accounts, real clipboard or device listeners.
using Island.Services;
using Island.Services.Phone;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
    throw new InvalidOperationException("Use an ordinary/restricted token.");
if (!ReviewSafety.Enabled) throw new InvalidOperationException("Local review is required.");
bool expectedDeveloper = args.Contains("--expect-developer");
var results = new Dictionary<string,bool>();
results["configuration"] = DeveloperAccess.Enabled == expectedDeveloper;
var check = typeof(License).GetMethod("Check", BindingFlags.NonPublic|BindingFlags.Static)!;
object?[] invalidKey = {"ISLAND-INVALID", null};
results["invalid_key_rejected"] = check.Invoke(null, invalidKey) is string;
if (expectedDeveloper)
{
    results["permanent_access"] = License.IsPro && License.Gate() && !License.IsTrial && License.Expires is null && !License.HasKey;
    results["expired_trial_independent"] = License.TrialEnds < DateTime.UtcNow && License.Gate();
    typeof(License).GetField("_gate", BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null, false);
    typeof(License).GetField("_gateAt", BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null, Environment.TickCount64);
    results["old_gate_cache_independent"] = License.Gate();
    Settings.Current.KnownMinVersion = "9999.0.0";
    var before = Settings.Current.LastUpdateCheck;
    results["author_version_cannot_lock"] = !UpdateService.Outdated;
    results["update_check_disabled"] = !await UpdateService.CheckAsync() && Settings.Current.LastUpdateCheck == before;
    results["self_update_disabled"] = !UpdateService.CanSelfUpdate && UpdateService.Latest is null && UpdateService.ReadyExe is null;
    try { await UpdateService.StartDownload(); results["download_blocked"] = false; }
    catch (InvalidOperationException) { results["download_blocked"] = true; }
    try { UpdateService.Apply(); results["apply_blocked"] = false; }
    catch (InvalidOperationException) { results["apply_blocked"] = true; }
    results["developer_label"] = Island.DeveloperBranding.CardTitle == "island killed" && Island.DeveloperBranding.Subtitle == "999999d";
    results["separate_profile"] = Settings.Folder == Path.Combine(AppContext.BaseDirectory,Island.Social.SocialBuild.Enabled?"SocialHubProfile":"DeveloperProProfile");
    results["clipboard_sync_default_off"] = !new Settings().PhoneClipboard;
}
else results["release_original_branch"] = !Island.DeveloperBranding.IsDeveloper && License.TrialStart != DateTime.MinValue;

Directory.CreateDirectory(Settings.Folder);
var todos = new TodoService();
string taskText = "DeveloperPro synthetic persistence task";
if (!todos.Items.Any(x=>x.Text==taskText)) todos.Add(taskText);
todos.Items.First(x=>x.Text==taskText).Done = true;
results["todo_persisted"] = new TodoService().Items.Any(x=>x.Text==taskText && x.Done);
byte[] qr = PhoneLinkService.QrPng("DeveloperPro local QR test");
results["qr_png"] = qr.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) && qr.Length > 100;
var parse = typeof(LyricsService).GetMethod("Parse", BindingFlags.NonPublic|BindingFlags.Static)!;
var lines = (List<LyricLine>)parse.Invoke(null, new object[]{"[00:02.00]two\n[00:01.00]<00:01.00>one <00:01.50>word"})!;
results["lyrics_lrc_order_and_words"] = lines.Count==2 && lines[0].Time==TimeSpan.FromSeconds(1) && lines[0].Words?.Count==2 && lines[1].Text=="two";
results["weather_local_format"] = WeatherService.FormatTemp(5.2).Contains("5") && !string.IsNullOrEmpty(WeatherService.Describe(0,true).Text);

byte[] key=RandomNumberGenerator.GetBytes(32), pn=RandomNumberGenerator.GetBytes(16), cn=RandomNumberGenerator.GetBytes(16);
using var pc=LinkCipher.ForPc(key,pn,cn);
using var phone=LinkCipher.ForPhone(key,pn,cn);
byte[] text=Encoding.UTF8.GetBytes("synthetic phone message");
byte[] frame=pc.Seal(text);
results["phone_aesgcm_both_directions"] = phone.Open(frame).SequenceEqual(text) && pc.Open(phone.Seal(text)).SequenceEqual(text);
try { phone.Open(frame); results["phone_replay_rejected"]=false; }
catch(CryptographicException) { results["phone_replay_rejected"]=true; }
using var receiver=LinkCipher.ForPhone(key,pn,cn);
frame[0]^=1;
try { receiver.Open(frame); results["phone_tampering_rejected"]=false; }
catch(CryptographicException) { results["phone_tampering_rejected"]=true; }
using var stream=new MemoryStream();
await LinkProtocol.WriteFrameAsync(stream,text,default); stream.Position=0;
results["phone_frame_roundtrip"]=(await LinkProtocol.ReadFrameAsync(stream,default)).SequenceEqual(text);
using var badFrame=new MemoryStream(new byte[]{127,255,255,255});
try { await LinkProtocol.ReadFrameAsync(badFrame,default);results["oversized_frame_rejected"]=false; }
catch(InvalidDataException){results["oversized_frame_rejected"]=true;}
using var http=ReviewSafety.Client();
try { await http.GetAsync("https://example.invalid/never-sent"); results["http_denied"]=false; }
catch(System.Net.Http.HttpRequestException){results["http_denied"]=true;}
if(expectedDeveloper)
{
    Environment.SetEnvironmentVariable("ISLAND_WEATHER_FORECAST_URL","http://127.0.0.1:12345/forecast");
    results["custom_weather_loopback"] = DeveloperEndpoints.WeatherUrl("ISLAND_WEATHER_FORECAST_URL","unused").Contains("127.0.0.1");
    Environment.SetEnvironmentVariable("ISLAND_WEATHER_FORECAST_URL","http://example.invalid/forecast");
    try { DeveloperEndpoints.WeatherUrl("ISLAND_WEATHER_FORECAST_URL","unused");results["custom_weather_insecure_rejected"]=false; }
    catch(InvalidOperationException){results["custom_weather_insecure_rejected"]=true;}
}
File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"probe-results.json"),JsonSerializer.Serialize(new {configuration=expectedDeveloper?"DeveloperPro":"Release",elevated=false,review=true,results,passed=results.Values.All(v=>v)},new JsonSerializerOptions{WriteIndented=true}));
return results.Values.All(v=>v)?0:1;
