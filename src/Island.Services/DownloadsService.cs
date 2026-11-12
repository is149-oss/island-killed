using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace Island.Services;

public sealed class DownloadsService : IDisposable
{
	public static readonly string[] TempExtensions = new string[9] { ".crdownload", ".part", ".partial", ".download", ".opdownload", ".~", ".tmp", ".!ut", ".!qb" };

	private static readonly TimeSpan PlainIdle = TimeSpan.FromSeconds(5L);

	private readonly Dictionary<string, DownloadItem> _active = new Dictionary<string, DownloadItem>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, DateTime> _finished = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

	private readonly DispatcherTimer _poll = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(1L)
	};

	private FileSystemWatcher? _watcher;

	private SynchronizationContext? _ctx;

	public string Folder { get; } = KnownDownloadsFolder();

	public IReadOnlyCollection<DownloadItem> Active => _active.Values.Where((DownloadItem i) => DateTime.Now - i.LastGrowth < TimeSpan.FromSeconds(45L)).ToList();

	public event Action? Changed;

	public event Action<string>? Completed;

	private static bool IsTemp(string path)
	{
		return TempExtensions.Any((string e) => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
	}

	public void Init()
	{
		if (ReviewSafety.Enabled) return;
		_ctx = SynchronizationContext.Current;
		if (!Directory.Exists(Folder))
		{
			return;
		}
		foreach (string item in Directory.EnumerateFiles(Folder).Where(IsTemp))
		{
			if (DateTime.Now - File.GetLastWriteTime(item) < TimeSpan.FromMinutes(2L))
			{
				Track(item);
			}
		}
		_watcher = new FileSystemWatcher(Folder)
		{
			NotifyFilter = (NotifyFilters.FileName | NotifyFilters.Size),
			IncludeSubdirectories = true,
			InternalBufferSize = 65536
		};
		_watcher.Created += (object _, FileSystemEventArgs e) =>
		{
			Post(() =>
			{
				if (IsTemp(e.FullPath))
				{
					Track(e.FullPath);
				}
			});
		};
		_watcher.Changed += (object _, FileSystemEventArgs e) =>
		{
			Post(() =>
			{
				OnGrew(e.FullPath);
			});
		};
		_watcher.Deleted += (object _, FileSystemEventArgs e) =>
		{
			Post(() =>
			{
				if (_active.Remove(e.FullPath))
				{
					Changed?.Invoke();
				}
			});
		};
		_watcher.Renamed += (object _, RenamedEventArgs e) =>
		{
			Post(() =>
			{
				OnRenamed(e.OldFullPath, e.FullPath);
			});
		};
		_watcher.EnableRaisingEvents = true;
		_poll.Tick += (object? _, EventArgs _) =>
		{
			Poll();
		};
		if (_active.Count > 0)
		{
			_poll.Start();
		}
	}

	private void Post(Action a)
	{
		_ctx?.Post((object? _) =>
		{
			a();
		}, null);
	}

	private void Track(string path, bool plain = false)
	{
		if (!_active.ContainsKey(path))
		{
			_active[path] = new DownloadItem
			{
				TempPath = path,
				Plain = plain
			};
			_poll.Start();
			Poll();
		}
	}

	private void OnGrew(string path)
	{
		if (_active.ContainsKey(path) || IsTemp(path) || (_finished.TryGetValue(path, out var value) && DateTime.Now - value < TimeSpan.FromSeconds(30L)))
		{
			return;
		}
		try
		{
			FileInfo fileInfo = new FileInfo(path);
			if (!fileInfo.Exists || DateTime.Now - fileInfo.CreationTime > TimeSpan.FromSeconds(60L) || fileInfo.Length < 262144)
			{
				return;
			}
		}
		catch (Exception)
		{
			return;
		}
		Track(path, plain: true);
	}

	private void Finish(DownloadItem item, string finalPath)
	{
		_active.Remove(item.TempPath);
		_finished[finalPath] = DateTime.Now;
		Changed?.Invoke();
		if (File.Exists(finalPath))
		{
			Completed?.Invoke(finalPath);
		}
	}

	private void OnRenamed(string oldPath, string newPath)
	{
		bool flag = IsTemp(oldPath);
		bool flag2 = IsTemp(newPath);
		if ((flag & flag2) && _active.Remove(oldPath, out DownloadItem? value))
		{
			value.TempPath = newPath;
			_active[newPath] = value;
			Changed?.Invoke();
		}
		else if (flag && !flag2)
		{
			if (_active.TryGetValue(oldPath, out DownloadItem? value2))
			{
				Finish(value2, newPath);
				return;
			}
			_finished[newPath] = DateTime.Now;
			if (File.Exists(newPath))
			{
				Completed?.Invoke(newPath);
			}
		}
		else if (!flag & flag2)
		{
			Track(newPath);
		}
	}

	private void Poll()
	{
		DateTime now = DateTime.Now;
		foreach (DownloadItem item in _active.Values.ToList())
		{
			try
			{
				FileInfo fileInfo = new FileInfo(item.TempPath);
				if (!fileInfo.Exists)
				{
					_active.Remove(item.TempPath);
					continue;
				}
				double num = Math.Max(0.2, (now - item.LastCheck).TotalSeconds);
				double num2 = (double)Math.Max(0L, fileInfo.Length - item.LastSize) / num;
				item.Speed = ((item.LastSize == 0L) ? num2 : (item.Speed * 0.6 + num2 * 0.4));
				if (fileInfo.Length != item.LastSize)
				{
					item.LastGrowth = now;
				}
				item.Size = fileInfo.Length;
				item.LastSize = fileInfo.Length;
				item.LastCheck = now;
				if (item.Plain && now - item.LastGrowth > PlainIdle)
				{
					Finish(item, item.TempPath);
				}
			}
			catch (Exception)
			{
			}
		}
		if (_active.Count == 0)
		{
			_poll.Stop();
		}
		foreach (string item2 in (from f in _finished
			where now - f.Value > TimeSpan.FromMinutes(1L)
			select f.Key).ToList())
		{
			_finished.Remove(item2);
		}
		Changed?.Invoke();
	}

	public static string FormatSize(double bytes)
	{
		if (!(bytes >= 1073741824.0))
		{
			if (!(bytes >= 1048576.0))
			{
				if (bytes >= 1024.0)
				{
					return $"{bytes / 1024.0:0} КБ";
				}
				return $"{bytes:0} Б";
			}
			return $"{bytes / 1048576.0:0.#} МБ";
		}
		return $"{bytes / 1073741824.0:0.0} ГБ";
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, nint token, out nint path);

	private static string KnownDownloadsFolder()
	{
		try
		{
			if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0u, IntPtr.Zero, out var path) == 0)
			{
				try
				{
					string? folder = Marshal.PtrToStringUni(path);
					if (!string.IsNullOrWhiteSpace(folder)) return folder;
				}
				finally
				{
					Marshal.FreeCoTaskMem(path);
				}
			}
		}
		catch (Exception)
		{
		}
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
	}

	public void Dispose()
	{
		_watcher?.Dispose();
		_poll.Stop();
	}
}
