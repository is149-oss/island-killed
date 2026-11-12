// Executes only selected local services. No windows, clipboard access, accounts or network.
using Island.Services;
using System.IO;
using System.Security.Principal;
using System.Text.Json;

if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
    throw new InvalidOperationException("Probe must run with restricted/ordinary user rights.");
if (!ReviewSafety.Enabled) throw new InvalidOperationException("Local review mode required.");
string path=Path.Combine(AppContext.BaseDirectory,"probe-results.json");
var samples=new List<MonitorSnapshot>();
var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
void OnUpdated(MonitorSnapshot sample) { lock(samples) { samples.Add(sample); if(samples.Count>=3)ready.TrySetResult(); } }
SystemMonitor.Updated+=OnUpdated;
SystemMonitor.Acquire(wantFps:false);
try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
finally { SystemMonitor.Release();SystemMonitor.Updated-=OnUpdated; }
bool memory=samples.All(s=>s.RamTotalGb>0&&s.RamUsedGb>=0&&s.RamUsedGb<=s.RamTotalGb);
bool cpu=samples.All(s=>double.IsFinite(s.Cpu)&&s.Cpu>=0&&s.Cpu<=100);
bool preview=ClipItem.FromText("Island local test").Preview=="Island local test";
bool invalidPreview=ClipItem.FromSaved(ClipKind.Text,null,null,DateTime.Now).Preview=="";
bool emptyFiles=ClipItem.FromFiles(Array.Empty<string>()).Preview=="";
File.WriteAllText(path,JsonSerializer.Serialize(new {elevated=false,review=true,memory,cpu,preview,invalidPreview,emptyFiles,samples},new JsonSerializerOptions{WriteIndented=true}));
return memory&&cpu&&preview&&invalidPreview&&emptyFiles?0:1;
