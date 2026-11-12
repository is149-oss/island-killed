using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Island.Services;

public sealed class TodoService
{
	private static readonly string FilePath = Path.Combine(Settings.Folder, "todo.json");

	public ObservableCollection<TodoItem> Items { get; } = new ObservableCollection<TodoItem>();

	public int Remaining => Enumerable.Count(Items, (TodoItem i) => !i.Done);

	public TodoService()
	{
		try
		{
			if (!File.Exists(FilePath))
			{
				return;
			}
			List<TodoItem>? list = JsonSerializer.Deserialize<List<TodoItem>>(File.ReadAllText(FilePath));
			if (list == null)
			{
				return;
			}
			foreach (TodoItem item in list)
			{
				Attach(item);
			}
		}
		catch (Exception ex)
		{
			Log.Error("load todo", ex);
		}
	}

	public void Add(string text)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			Attach(new TodoItem
			{
				Text = text.Trim()
			});
			Save();
		}
	}

	public void Remove(TodoItem item)
	{
		Items.Remove(item);
		Save();
	}

	public void ClearDone()
	{
		foreach (TodoItem item in Items.Where((TodoItem i) => i.Done).ToList())
		{
			Items.Remove(item);
		}
		Save();
	}

	private void Attach(TodoItem item)
	{
		item.PropertyChanged += (object? _, PropertyChangedEventArgs _) =>
		{
			Save();
		};
		Items.Add(item);
	}

	private void Save()
	{
		try
		{
			Directory.CreateDirectory(Settings.Folder);
			File.WriteAllText(FilePath, JsonSerializer.Serialize(Items.ToList()));
		}
		catch (Exception ex)
		{
			Log.Error("save todo", ex);
		}
	}
}
