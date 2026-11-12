using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Island.Services;

public static class UpdateService
{
	private const string Repo = "qxezecwry/Island";

	private static readonly HttpClient Http = CreateClient();

	private static readonly HttpClient Download;

	private static Task? _downloading;

	public static Version Current { get; }

	public static ReleaseInfo? Latest { get; private set; }

	public static bool UpdateAvailable
	{
		get
		{
			if (Latest != null)
			{
				return Latest.Version > Current;
			}
			return false;
		}
	}

	public static bool Outdated
	{
		get
		{
			if (DeveloperAccess.Enabled) return false;
			if (Version.TryParse(Settings.Current.KnownMinVersion, out Version? result))
			{
				return Current < result;
			}
			return false;
		}
	}

	public static bool CanSelfUpdate
	{
		get
		{
			if (DeveloperAccess.Enabled || ReviewSafety.Enabled || !Integrity.Enforced) return false;
			string? processPath = Environment.ProcessPath;
			if (processPath != null)
			{
				return !File.Exists(Path.Combine(Path.GetDirectoryName(processPath), "Island.dll"));
			}
			return false;
		}
	}

	public static bool Downloading
	{
		get
		{
			Task? downloading = _downloading;
			if (downloading != null)
			{
				return !downloading.IsCompleted;
			}
			return false;
		}
	}

	public static double DownloadProgress { get; private set; }

	public static string? ReadyExe { get; private set; }

	public static Version? ReadyVersion { get; private set; }

	private static string WorkDir => Path.Combine(Path.GetTempPath(), "IslandUpdate");

	public static event Action? Changed;

	public static event Action? DownloadChanged;

	private static HttpClient CreateClient()
	{
		HttpClient httpClient = ReviewSafety.Client();
		httpClient.Timeout = TimeSpan.FromSeconds(20L);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Island-Updater");
		return httpClient;
	}

	public static Version? ParseVersion(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string text2 = new string((from c in text.Trim().TrimStart(new char[2] { 'v', 'V' })
			select (c != '-') ? c : '.').TakeWhile((char c) => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
		if (!Version.TryParse(text2.Contains('.') ? text2 : (text2 + ".0"), out Version? result))
		{
			return null;
		}
		return new Version(result.Major, result.Minor, Math.Max(0, result.Build));
	}

	public static async Task<bool> CheckAsync()
	{
		if (DeveloperAccess.Enabled) return false; // No release or minimum-version requests in this flavor.
		bool ok = false;
		try
		{
			JsonNode? jsonNode = JsonNode.Parse(await Http.GetStringAsync("https://api.github.com/repos/qxezecwry/Island/releases/latest"));
			Version? version = ParseVersion(jsonNode?["tag_name"]?.ToString()) ?? ParseVersion(jsonNode?["name"]?.ToString());
			if (version != null)
			{
				JsonNode? jsonNode2 = jsonNode?["assets"]?.AsArray().FirstOrDefault((JsonNode a) => a != null && a["name"]?.ToString().EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
				Latest = new ReleaseInfo(version, jsonNode?["name"]?.ToString() ?? $"Island {version}", jsonNode?["html_url"]?.ToString() ?? "https://github.com/qxezecwry/Island/releases/latest", jsonNode2?["browser_download_url"]?.ToString(), (jsonNode2?["size"]?.GetValue<long>()).GetValueOrDefault());
				ok = true;
			}
		}
		catch (Exception ex)
		{
			Log.Write("update check: " + ex.GetType().Name + ": " + ex.Message);
		}
		try
		{
			using HttpResponseMessage response = await Http.GetAsync("https://raw.githubusercontent.com/qxezecwry/Island/main/min-version.txt");
			if (response.IsSuccessStatusCode)
			{
				Settings current = Settings.Current;
				current.KnownMinVersion = ParseVersion(await response.Content.ReadAsStringAsync())?.ToString();
			}
			else if (response.StatusCode == HttpStatusCode.NotFound)
			{
				Settings.Current.KnownMinVersion = null;
			}
			ok = true;
		}
		catch (Exception ex2)
		{
			Log.Write("min-version check: " + ex2.GetType().Name + ": " + ex2.Message);
		}
		if (ok)
		{
			Settings.Current.LastUpdateCheck = DateTime.UtcNow;
			Settings.Current.Save();
			Changed?.Invoke();
		}
		return ok;
	}

	private static HttpClient CreateDownloadClient()
	{
		HttpClient httpClient = ReviewSafety.Client();
		httpClient.Timeout = Timeout.InfiniteTimeSpan;
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Island-Updater");
		return httpClient;
	}

	public static Task StartDownload()
	{
		ReviewSafety.RequireUpdates();
		Task? downloading = _downloading;
		if (downloading != null && !downloading.IsCompleted)
		{
			return downloading;
		}
		if (ReadyExe != null && ReadyVersion == Latest?.Version)
		{
			return Task.CompletedTask;
		}
		ReleaseInfo release = Latest ?? throw new InvalidOperationException("Нет данных о новой версии");
		if (release.ZipUrl == null)
		{
			throw new InvalidOperationException("В релизе нет архива");
		}
		SynchronizationContext? ui = SynchronizationContext.Current;
		DownloadProgress = 0.0;
		_downloading = Task.Run(async () =>
		{
			try
			{
				string text = await DownloadAsync(release, Notify);
				if (!Integrity.IsGenuine(text))
				{
					try
					{
						File.Delete(text);
					}
					catch (IOException)
					{
					}
					throw new InvalidOperationException("Подпись загруженной версии не подошла — обновление не установлено");
				}
				ReadyExe = text;
				ReadyVersion = release.Version;
			}
			finally
			{
				Notify();
			}
		});
		return _downloading;
		void Notify()
		{
			if (ui != null)
			{
				ui.Post((object? _) =>
				{
					DownloadChanged?.Invoke();
				}, null);
			}
			else
			{
				DownloadChanged?.Invoke();
			}
		}
	}

	private static async Task<string> DownloadAsync(ReleaseInfo release, Action notify)
	{
		if (Directory.Exists(WorkDir))
		{
			Directory.Delete(WorkDir, recursive: true);
		}
		Directory.CreateDirectory(WorkDir);
		string zipPath = Path.Combine(WorkDir, "update.zip");
		long total = release.ZipSize;
		long done = 0L;
		DateTime lastNotify = DateTime.MinValue;
		int attempt = 1;
		bool flag = default;
		while (true)
		{
			try
			{
				using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, release.ZipUrl);
				if (done > 0)
				{
					request.Headers.Range = new RangeHeaderValue(done, null);
				}
				using CancellationTokenSource headers = new CancellationTokenSource(TimeSpan.FromSeconds(30L));
				using HttpResponseMessage response = await Download.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token);
				response.EnsureSuccessStatusCode();
				if (done > 0 && response.StatusCode != HttpStatusCode.PartialContent)
				{
					done = 0L;
				}
				long? contentLength = response.Content.Headers.ContentLength;
				if (contentLength.HasValue)
				{
					long valueOrDefault = contentLength.GetValueOrDefault();
					total = done + valueOrDefault;
				}
				await using Stream source = await response.Content.ReadAsStreamAsync();
				await using FileStream target = new FileStream(zipPath, (done > 0) ? FileMode.Append : FileMode.Create, FileAccess.Write);
				byte[] buffer = new byte[81920];
				while (true)
				{
					using CancellationTokenSource stall = new CancellationTokenSource(TimeSpan.FromSeconds(30L));
					int read = await source.ReadAsync(buffer, stall.Token);
					if (read == 0)
					{
						break;
					}
					await target.WriteAsync(buffer.AsMemory(0, read));
					done += read;
					if (total > 0)
					{
						DownloadProgress = Math.Min(1.0, (double)done / (double)total);
					}
					if (DateTime.UtcNow - lastNotify > TimeSpan.FromMilliseconds(500L))
					{
						lastNotify = DateTime.UtcNow;
						notify();
					}
					continue;
				}
				if (total > 0 && done < total)
				{
					throw new IOException($"Скачано {done} из {total} байт");
				}
			}
			catch (Exception ex) when (((Func<bool>)delegate
			{
				// Could not convert BlockContainer to single expression
				flag = attempt < 6;
				if (flag)
				{
					flag = ((ex is HttpRequestException || ex is IOException || ex is OperationCanceledException) ? true : false);
				}
				return flag;
			}).Invoke())
			{
				Log.Write($"update download, try {attempt}: {ex.GetType().Name}: {ex.Message}");
				await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
				goto IL_085f;
			}
			break;
			IL_085f:
			attempt++;
		}
		string text = Path.Combine(WorkDir, "new");
		ZipFile.ExtractToDirectory(zipPath, text);
		File.Delete(zipPath);
		return Directory.GetFiles(text, "Island.exe", SearchOption.AllDirectories).FirstOrDefault() ?? throw new InvalidOperationException("В архиве нет Island.exe");
	}

	public static string Apply()
	{
		ReviewSafety.RequireUpdates();
		if (ReadyExe is null || !Integrity.IsGenuine(ReadyExe))
			throw new InvalidOperationException("Не подтверждена подпись подготовленного обновления");
		string sourceFileName = ReadyExe ?? throw new InvalidOperationException("Обновление ещё не скачано");
		string text = Environment.ProcessPath ?? throw new InvalidOperationException("Не найден файл программы");
		string text2 = Path.Combine(Path.GetDirectoryName(text), "Island.old.exe");
		if (File.Exists(text2))
		{
			File.Delete(text2);
		}
		File.Move(text, text2);
		try
		{
			File.Copy(sourceFileName, text);
		}
		catch
		{
			File.Move(text2, text);
			throw;
		}
		ReadyExe = null;
		return text;
	}

	public static void CleanUp()
	{
		if (DeveloperAccess.Enabled || ReviewSafety.Enabled) return;
		try
		{
			string? processPath = Environment.ProcessPath;
			if (processPath != null)
			{
				string path = Path.Combine(Path.GetDirectoryName(processPath), "Island.old.exe");
				for (int i = 0; i < 20; i++)
				{
					if (!File.Exists(path))
					{
						break;
					}
					try
					{
						File.Delete(path);
					}
					catch (IOException)
					{
						Thread.Sleep(250);
					}
					catch (UnauthorizedAccessException)
					{
						Thread.Sleep(250);
					}
				}
			}
			if (Directory.Exists(WorkDir))
			{
				Directory.Delete(WorkDir, recursive: true);
			}
		}
		catch (Exception ex3)
		{
			Log.Error("update cleanup", ex3);
		}
		NoteInstalledVersion();
	}

	private static void NoteInstalledVersion()
	{
		string[] array = new string[2] { "Island_is1", "Island" };
		foreach (string text in array)
		{
			try
			{
				using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + text, writable: true);
				if (!(registryKey?.GetValue("InstallLocation") is string path))
				{
					continue;
				}
				string? processPath = Environment.ProcessPath;
				if (processPath != null && string.Equals(Path.GetFullPath(path).TrimEnd('\\'), Path.GetDirectoryName(processPath), StringComparison.OrdinalIgnoreCase))
				{
					string text2 = Current.ToString();
					if (registryKey.GetValue("DisplayVersion") as string != text2)
					{
						registryKey.SetValue("DisplayVersion", text2);
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error("installed version", ex);
			}
		}
	}

	static UpdateService()
	{
		Version? version = Assembly.GetExecutingAssembly().GetName().Version;
		Current = (((object)version != null) ? new Version(version.Major, version.Minor, Math.Max(0, version.Build)) : new Version(1, 0, 0));
		Download = CreateDownloadClient();
	}
}
