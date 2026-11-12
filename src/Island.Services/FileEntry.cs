using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Island.Native;

namespace Island.Services;

public sealed class FileEntry : INotifyPropertyChanged
{
	private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

	private ImageSource? _icon;

	private string? _title;

	public string Path { get; }

	public bool IsLink { get; }

	public ImageSource? Icon => _icon;

	public string Name
	{
		get
		{
			if (IsLink)
			{
				return _title ?? LinkInfo.Host(Path);
			}
			string text = Path.TrimEnd('\\');
			if (text.Length <= 2)
			{
				return Path;
			}
			string text2 = System.IO.Path.GetExtension(text).ToLowerInvariant();
			if ((!(text2 == ".lnk") && !(text2 == ".url")) || 1 == 0)
			{
				return System.IO.Path.GetFileName(text);
			}
			return System.IO.Path.GetFileNameWithoutExtension(text);
		}
	}

	public bool Exists
	{
		get
		{
			if (!IsLink && !File.Exists(Path))
			{
				return Directory.Exists(Path);
			}
			return true;
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public FileEntry(string path, int iconSize, bool iconOnly)
	{
		Path = path;
		if (LinkInfo.IsLink(path))
		{
			IsLink = true;
			_icon = LinkInfo.CachedIcon(path) ?? LinkInfo.Placeholder;
			_title = LinkInfo.CachedTitle(path);
			if (_icon == LinkInfo.Placeholder || _title == null)
			{
				LoadLinkAsync();
			}
		}
		else
		{
			_icon = ShellIcons.Get(path, iconSize, iconOnly);
		}
	}

	private async Task LoadLinkAsync()
	{
		try
		{
			var (title, icon) = await Task.Run(() => LinkInfo.FetchAsync(Path));
			if (_ui != null)
			{
				_ui.Post((object? _) =>
				{
					Apply();
				}, null);
			}
			else
			{
				Apply();
			}
			void Apply()
			{
				if (icon != null)
				{
					_icon = icon;
				}
				if (title != null)
				{
					_title = title;
				}
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Icon"));
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Name"));
			}
		}
		catch (Exception ex)
		{
			Log.Error("link info", ex);
		}
	}
}
