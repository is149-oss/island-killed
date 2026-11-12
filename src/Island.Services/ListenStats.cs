using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Island.Services;

public sealed class ListenStats
{
	private static readonly string FilePath = Path.Combine(Settings.Folder, "listening.json");

	private const int KeepDays = 60;

	private bool _dirty;

	private DateTime _saved = DateTime.Now;

	public static ListenStats Current { get; } = Load();

	public List<DayStat> Days { get; set; } = new List<DayStat>();

	public void Add(string artist, string title, int seconds)
	{
		DateTime now = DateTime.Now;
		string key = now.ToString("yyyy-MM-dd");
		DayStat? dayStat = Days.LastOrDefault((DayStat d) => d.Day == key);
		if (dayStat == null)
		{
			dayStat = new DayStat
			{
				Day = key
			};
			Days.Add(dayStat);
			string oldest = now.Date.AddDays(-60.0).ToString("yyyy-MM-dd");
			Days.RemoveAll((DayStat d) => string.CompareOrdinal(d.Day, oldest) < 0);
		}
		dayStat.Hours[now.Hour] += seconds;
		if (artist.Length > 0)
		{
			dayStat.Artists[artist] = dayStat.Artists.GetValueOrDefault(artist) + seconds;
		}
		string key2 = ((artist.Length > 0) ? (artist + " — " + title) : title);
		dayStat.Tracks[key2] = dayStat.Tracks.GetValueOrDefault(key2) + seconds;
		_dirty = true;
		if (now - _saved > TimeSpan.FromMinutes(1L))
		{
			Flush();
		}
	}

	public void Flush()
	{
		if (!_dirty)
		{
			return;
		}
		_dirty = false;
		_saved = DateTime.Now;
		try
		{
			Directory.CreateDirectory(Settings.Folder);
			File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
		}
		catch (Exception ex)
		{
			Log.Error("save listening", ex);
		}
	}

	public void Clear()
	{
		Days.Clear();
		_dirty = true;
		Flush();
	}

	public ListenSummary Summary(int days = 7)
	{
		DateTime today = DateTime.Today;
		List<DateTime> source = (from i in Enumerable.Range(0, days)
			select today.AddDays(i - days + 1)).ToList();
		List<DayStat?> source2 = source.Select((DateTime k) => Days.LastOrDefault((DayStat d) => d.Day == k.ToString("yyyy-MM-dd"))).ToList();
		int[] array = source2.Select((DayStat d) => d?.Hours.Sum() ?? 0).ToArray();
		int[] array2 = new int[24];
		Dictionary<string, int> dictionary = new Dictionary<string, int>();
		Dictionary<string, int> dictionary2 = new Dictionary<string, int>();
		foreach (DayStat? item in source2.Where((DayStat d) => d != null))
		{
			for (int num = 0; num < 24; num++)
			{
				array2[num] += item.Hours[num];
			}
			string key;
			int value;
			foreach (KeyValuePair<string, int> artist in item.Artists)
			{
				artist.Deconstruct(out key, out value);
				string key2 = key;
				int num2 = value;
				dictionary[key2] = dictionary.GetValueOrDefault(key2) + num2;
			}
			foreach (KeyValuePair<string, int> track in item.Tracks)
			{
				track.Deconstruct(out key, out value);
				string key3 = key;
				int num3 = value;
				dictionary2[key3] = dictionary2.GetValueOrDefault(key3) + num3;
			}
		}
		int? bestHour = null;
		int num4 = 0;
		for (int num5 = 0; num5 < 24; num5++)
		{
			int num6 = array2[num5] + array2[(num5 + 1) % 24] + array2[(num5 + 2) % 24];
			if (num6 > num4)
			{
				num4 = num6;
				bestHour = num5;
			}
		}
		return new ListenSummary(array.Sum(), array.Count((int s) => s > 60), array, source.Select((DateTime k) => k.ToString("ddd")).ToArray(), (from p in dictionary.OrderByDescending((KeyValuePair<string, int> p) => p.Value).Take(5)
			select (Key: p.Key, Value: p.Value)).ToList(), (from p in dictionary2.OrderByDescending((KeyValuePair<string, int> p) => p.Value).Take(5)
			select (Key: p.Key, Value: p.Value)).ToList(), bestHour);
	}

	private static ListenStats Load()
	{
		try
		{
			if (File.Exists(FilePath))
			{
				ListenStats? listenStats = JsonSerializer.Deserialize<ListenStats>(File.ReadAllText(FilePath));
				if (listenStats != null)
				{
					ListenStats listenStats2 = listenStats;
					if (listenStats2.Days == null)
					{
						List<DayStat> list = (listenStats2.Days = new List<DayStat>());
					}
					foreach (DayStat day in listenStats.Days)
					{
						int[] hours = day.Hours;
						if (hours == null || hours.Length != 24)
						{
							day.Hours = new int[24];
						}
					}
					return listenStats;
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("listening.json unreadable", ex);
		}
		return new ListenStats();
	}
}
