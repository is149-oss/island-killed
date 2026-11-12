using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Island.Services;

public sealed class TrackStore
{
	private static readonly string FilePath = Path.Combine(Settings.Folder, "tracks.json");

	private const int HistoryMax = 100;

	public static TrackStore Current { get; } = Load();

	public List<TrackEntry> History { get; set; } = new List<TrackEntry>();

	private static string Key(string title, string artist)
	{
		return (title.Trim() + "|" + artist.Trim()).ToLowerInvariant();
	}

	public void Played(string title, string artist, string app)
	{
		if (History.Count <= 0 || !(Key(History[0].Title, History[0].Artist) == Key(title, artist)))
		{
			History.Insert(0, new TrackEntry
			{
				Title = title,
				Artist = artist,
				App = app,
				At = DateTime.Now
			});
			if (History.Count > 100)
			{
				History.RemoveRange(100, History.Count - 100);
			}
			Save();
		}
	}

	public void ClearHistory()
	{
		History.Clear();
		Save();
	}

	private static TrackStore Load()
	{
		try
		{
			if (File.Exists(FilePath))
			{
				TrackStore? trackStore = JsonSerializer.Deserialize<TrackStore>(File.ReadAllText(FilePath));
				if (trackStore != null)
				{
					TrackStore trackStore2 = trackStore;
					if (trackStore2.History == null)
					{
						List<TrackEntry> list = (trackStore2.History = new List<TrackEntry>());
					}
					return trackStore;
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("tracks.json unreadable", ex);
		}
		return new TrackStore();
	}

	private void Save()
	{
		try
		{
			Directory.CreateDirectory(Settings.Folder);
			File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			Log.Error("save tracks", ex);
		}
	}
}
