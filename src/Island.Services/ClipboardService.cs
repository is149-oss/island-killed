using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Island.Native;

namespace Island.Services;

public sealed class ClipboardService : IDisposable
{
	private sealed record Saved(ClipKind Kind, string? Text, string[]? Files, string? Image, DateTime Time);

	private const int MaxItems = 25;

	private const int MaxImages = 5;

	private static readonly string SavePath = Path.Combine(Settings.Folder, "clipboard.json");

	private static readonly string ImageFolder = Path.Combine(Settings.Folder, "clipboard");

	private nint _hwnd;

	private DateTime _ignoreUntil;

	private readonly DispatcherTimer _saveTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(1L)
	};

	private static readonly ConditionalWeakTable<BitmapSource, string> PictureNames = new ConditionalWeakTable<BitmapSource, string>();

	public ObservableCollection<ClipItem> Items { get; } = new ObservableCollection<ClipItem>();

	public event Action<ClipItem>? Captured;

	public event Action<string>? TextCopied;

	public event Action<BitmapSource>? ImageCopied;

	public ClipboardService()
	{
		_saveTimer.Tick += (object? _, EventArgs _) =>
		{
			_saveTimer.Stop();
			Save();
		};
		Items.CollectionChanged += (object? _, NotifyCollectionChangedEventArgs _) =>
		{
			if (Settings.Current.ClipPersist == true)
			{
				_saveTimer.Stop();
				_saveTimer.Start();
			}
		};
	}

	public void Load()
	{
		if (Settings.Current.ClipPersist != true || !File.Exists(SavePath))
		{
			return;
		}
		try
		{
			foreach (Saved item in (JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(SavePath)) ?? new List<Saved>()).Take(25))
			{
				ClipItem clipItem;
				switch (item.Kind)
				{
				case ClipKind.Image:
					if (item.Image != null && File.Exists(Path.Combine(ImageFolder, item.Image)))
					{
						clipItem = ClipItem.FromImage(LoadPicture(Path.Combine(ImageFolder, item.Image)));
						break;
					}
					goto default;
				case ClipKind.Text:
					if (item.Text != null)
					{
						clipItem = ClipItem.FromSaved(ClipKind.Text, item.Text, null, item.Time);
						break;
					}
					goto default;
				case ClipKind.Files:
				{
					string[]? files = item.Files;
					if (files != null && files.Length > 0)
					{
						clipItem = ClipItem.FromSaved(ClipKind.Files, null, item.Files, item.Time);
						break;
					}
					goto default;
				}
				default:
					clipItem = null;
					break;
				}
				ClipItem? clipItem2 = clipItem;
				if (clipItem2 != null)
				{
					clipItem2.Time = item.Time;
					Items.Add(clipItem2);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("load clipboard history", ex);
		}
	}

	private static BitmapSource LoadPicture(string path)
	{
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		bitmapImage.UriSource = new Uri(path);
		bitmapImage.EndInit();
		bitmapImage.Freeze();
		return bitmapImage;
	}

	public void Save()
	{
		if (Settings.Current.ClipPersist != true)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(ImageFolder);
			HashSet<string> hashSet = new HashSet<string>();
			List<Saved> list = new List<Saved>();
			foreach (ClipItem item in Items)
			{
				string? text = null;
				if (item.Kind == ClipKind.Image)
				{
					text = PictureName(item.Image);
					string path = Path.Combine(ImageFolder, text);
					if (!File.Exists(path))
					{
						PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
						pngBitmapEncoder.Frames.Add(BitmapFrame.Create(item.Image));
						using FileStream stream = File.Create(path);
						pngBitmapEncoder.Save(stream);
					}
					hashSet.Add(text);
				}
				list.Add(new Saved(item.Kind, item.Text, item.Files, text, item.Time));
			}
			File.WriteAllText(SavePath, JsonSerializer.Serialize(list));
			string[] files = Directory.GetFiles(ImageFolder, "*.png");
			foreach (string path2 in files)
			{
				if (!hashSet.Contains(Path.GetFileName(path2)))
				{
					File.Delete(path2);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("save clipboard history", ex);
		}
	}

	private static string PictureName(BitmapSource image)
	{
		return PictureNames.GetValue(image, (BitmapSource img) =>
		{
			int num = img.PixelWidth * ((img.Format.BitsPerPixel + 7) / 8);
			byte[] array = new byte[num * img.PixelHeight];
			img.CopyPixels(array, num, 0);
			return Convert.ToHexString(SHA1.HashData(array)).Substring(0, 16) + ".png";
		});
	}

	public static void Forget()
	{
		try
		{
			if (File.Exists(SavePath))
			{
				File.Delete(SavePath);
			}
			if (Directory.Exists(ImageFolder))
			{
				Directory.Delete(ImageFolder, recursive: true);
			}
		}
		catch (Exception ex)
		{
			Log.Error("forget clipboard history", ex);
		}
	}

	public void Init(nint hwnd)
	{
		if (ReviewSafety.Enabled) return;
		_hwnd = hwnd;
		HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
		Win32.AddClipboardFormatListener(hwnd);
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg == 797)
		{
			CaptureAsync();
		}
		return IntPtr.Zero;
	}

	private async Task CaptureAsync()
	{
		if (ReviewSafety.Enabled) return;
		bool record = !Settings.Current.IsTabHidden("clip");
		if (DateTime.UtcNow < _ignoreUntil || (!record && TextCopied == null && ImageCopied == null))
		{
			return;
		}
		await Task.Delay(60);
		for (int attempt = 0; attempt < 5; attempt++)
		{
			try
			{
				ClipItem? clipItem = Read();
				if (clipItem != null)
				{
					if (clipItem.Kind == ClipKind.Text)
					{
						TextCopied?.Invoke(clipItem.Text);
					}
					if (clipItem.Kind == ClipKind.Image)
					{
						ImageCopied?.Invoke(clipItem.Image);
					}
					if (record)
					{
						Add(clipItem);
						Captured?.Invoke(clipItem);
					}
				}
				break;
			}
			catch (ExternalException)
			{
				await Task.Delay(80);
			}
			catch (Exception)
			{
				break;
			}
		}
	}

	private static ClipItem? Read()
	{
		IDataObject? dataObject = ReviewClipboard.GetDataObject();
		if (dataObject == null)
		{
			return null;
		}
		if (dataObject.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || dataObject.GetDataPresent("Clipboard Viewer Ignore"))
		{
			return null;
		}
		if (dataObject.GetDataPresent("CanIncludeInClipboardHistory") && dataObject.GetData("CanIncludeInClipboardHistory") is MemoryStream memoryStream && memoryStream.Length >= 4 && BitConverter.ToInt32(memoryStream.ToArray(), 0) == 0)
		{
			return null;
		}
		if (dataObject.GetDataPresent(DataFormats.FileDrop) && dataObject.GetData(DataFormats.FileDrop) is string[] array && array.Length > 0)
		{
			return ClipItem.FromFiles(array);
		}
		if (dataObject.GetDataPresent(DataFormats.UnicodeText) && dataObject.GetData(DataFormats.UnicodeText) is string text && !string.IsNullOrWhiteSpace(text))
		{
			return ClipItem.FromText(text);
		}
		if (ReviewClipboard.ContainsImage())
		{
			BitmapSource? image = ReviewClipboard.GetImage();
			if (image != null)
			{
				return ClipItem.FromImage(image);
			}
		}
		return null;
	}

	private void Add(ClipItem item)
	{
		ClipItem? clipItem = Items.FirstOrDefault((ClipItem i) => i.SameContent(item));
		if (clipItem != null)
		{
			Items.Remove(clipItem);
		}
		Items.Insert(0, item);
		foreach (ClipItem item2 in Items.Where((ClipItem i) => i.Kind == ClipKind.Image).Skip(5).ToList())
		{
			Items.Remove(item2);
		}
		while (Items.Count > 25)
		{
			Items.RemoveAt(Items.Count - 1);
		}
	}

	public async Task PasteAsync(ClipItem item)
	{
		if (ReviewSafety.Enabled) return;
		_ignoreUntil = DateTime.UtcNow.AddMilliseconds(400.0);
		for (int attempt = 0; attempt < 5; attempt++)
		{
			try
			{
				switch (item.Kind)
				{
				case ClipKind.Text:
					ReviewClipboard.SetText(item.Text);
					break;
				case ClipKind.Image:
					ReviewClipboard.SetImage(item.Image);
					break;
				case ClipKind.Files:
				{
					StringCollection stringCollection = new StringCollection();
					stringCollection.AddRange(item.Files);
					ReviewClipboard.SetFileDropList(stringCollection);
					break;
				}
				}
				item.Time = DateTime.Now;
				int num = Items.IndexOf(item);
				if (num > 0)
				{
					Items.Move(num, 0);
				}
				await Task.Delay(40);
				Win32.SendCtrlV();
				break;
			}
			catch (ExternalException)
			{
				await Task.Delay(80);
			}
		}
	}

	public Task SetTextAsync(string text)
	{
		return SetAsync(() =>
		{
			ReviewClipboard.SetText(text);
		});
	}

	public Task SetImageAsync(BitmapSource image)
	{
		return SetAsync(() =>
		{
			ReviewClipboard.SetImage(image);
		});
	}

	private static async Task SetAsync(Action set)
	{
		if (ReviewSafety.Enabled) return;
		for (int attempt = 0; attempt < 5; attempt++)
		{
			try
			{
				set();
				break;
			}
			catch (ExternalException)
			{
				await Task.Delay(80);
			}
		}
	}

	public void Remove(ClipItem item)
	{
		Items.Remove(item);
	}

	public void Dispose()
	{
		if (_saveTimer.IsEnabled)
		{
			Save();
		}
		if (_hwnd != IntPtr.Zero)
		{
			Win32.RemoveClipboardFormatListener(_hwnd);
		}
	}
}
