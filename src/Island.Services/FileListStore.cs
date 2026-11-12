using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Island.Services;

public sealed class FileListStore
{
	private readonly Func<Settings, List<string>> _select;

	private readonly int _max;

	private readonly int _iconSize;

	private readonly bool _iconOnly;

	public ObservableCollection<FileEntry> Items { get; } = new ObservableCollection<FileEntry>();

	public bool IsFull => Items.Count >= _max;

	public FileListStore(Func<Settings, List<string>> select, int max, int iconSize, bool iconOnly)
	{
		_select = select;
		_max = max;
		_iconSize = iconSize;
		_iconOnly = iconOnly;
		foreach (string item in select(Settings.Current))
		{
			FileEntry fileEntry = new FileEntry(item, iconSize, iconOnly);
			if (fileEntry.Exists)
			{
				Items.Add(fileEntry);
			}
		}
	}

	public void Add(IEnumerable<string> paths)
	{
		foreach (string path in paths)
		{
			if (IsFull)
			{
				break;
			}
			if (!Items.Any((FileEntry i) => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
			{
				Items.Add(new FileEntry(path, _iconSize, _iconOnly));
			}
		}
		Save();
	}

	public void Remove(FileEntry entry)
	{
		Items.Remove(entry);
		Save();
	}

	public void Clear()
	{
		Items.Clear();
		Save();
	}

	private void Save()
	{
		List<string> list = _select(Settings.Current);
		list.Clear();
		list.AddRange(Items.Select((FileEntry i) => i.Path));
		Settings.Current.Save();
	}
}
