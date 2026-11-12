using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Island.Services;

public sealed class CalendarService
{
	private static readonly string FilePath = Path.Combine(Settings.Folder, "calendar.json");

	private static readonly HttpClient Http = CreateClient();

	private readonly List<LocalEvent> _local = new List<LocalEvent>();

	private List<string> _feedTexts = new List<string>();

	private (DateTime From, DateTime To, List<CalendarEvent> Events)? _rangeCache;

	private bool _loading;

	public IReadOnlyList<CalendarEvent> Events { get; private set; } = Array.Empty<CalendarEvent>();

	public string? Problem { get; private set; }

	public DateTime? UpdatedAt { get; private set; }

	public event Action? Changed;

	private static HttpClient CreateClient()
	{
		HttpClient httpClient = ReviewSafety.Client();
		httpClient.Timeout = TimeSpan.FromSeconds(20L);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Island/1.0");
		return httpClient;
	}

	public CalendarService()
	{
		try
		{
			if (File.Exists(FilePath))
			{
				List<LocalEvent>? list = JsonSerializer.Deserialize<List<LocalEvent>>(File.ReadAllText(FilePath));
				if (list != null)
				{
					_local.AddRange(list);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("load calendar", ex);
		}
		Rebuild();
	}

	private static (DateTime From, DateTime To) Window()
	{
		return (From: DateTime.Today.AddDays(-1.0), To: DateTime.Today.AddDays(32.0));
	}

	public async Task RefreshAsync()
	{
		if (_loading)
		{
			return;
		}
		_loading = true;
		List<string> texts = new List<string>();
		int failed = 0;
		foreach (string item in Settings.Current.CalendarFeeds.ToList())
		{
			try
			{
				string text = item.Trim();
				if (text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
				{
					text = "https://" + text.Substring(9);
				}
				List<string> list = texts;
				list.Add(await Http.GetStringAsync(text));
			}
			catch (Exception ex)
			{
				failed++;
				Log.Error("calendar feed", ex);
			}
		}
		_feedTexts = texts;
		Problem = ((failed > 0) ? $"Не удалось загрузить календарей: {failed}" : null);
		UpdatedAt = DateTime.Now;
		_loading = false;
		Rebuild();
	}

	public void AddLocal(LocalEvent e)
	{
		_local.Add(e);
		SaveLocal();
		Rebuild();
	}

	public void RemoveLocal(string id)
	{
		_local.RemoveAll((LocalEvent e) => e.Id == id);
		SaveLocal();
		Rebuild();
	}

	private void SaveLocal()
	{
		try
		{
			Directory.CreateDirectory(Settings.Folder);
			File.WriteAllText(FilePath, JsonSerializer.Serialize(_local, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			Log.Error("save calendar", ex);
		}
	}

	private void Rebuild()
	{
		_rangeCache = null;
		(DateTime From, DateTime To) tuple = Window();
		DateTime item = tuple.From;
		DateTime item2 = tuple.To;
		Events = Expand(item, item2);
		Changed?.Invoke();
	}

	public IReadOnlyList<CalendarEvent> Between(DateTime from, DateTime to)
	{
		(DateTime, DateTime, List<CalendarEvent>)? rangeCache = _rangeCache;
		if (rangeCache.HasValue)
		{
			(DateTime, DateTime, List<CalendarEvent>) valueOrDefault = rangeCache.GetValueOrDefault();
			if (valueOrDefault.Item1 == from && valueOrDefault.Item2 == to)
			{
				return valueOrDefault.Item3;
			}
		}
		List<CalendarEvent> list = Expand(from, to);
		_rangeCache = (from, to, list);
		return list;
	}

	private List<CalendarEvent> Expand(DateTime from, DateTime to)
	{
		List<CalendarEvent> list = new List<CalendarEvent>();
		foreach (string feedText in _feedTexts)
		{
			try
			{
				list.AddRange(Ics.Parse(feedText, from, to));
			}
			catch (Exception ex)
			{
				Log.Error("calendar parse", ex);
			}
		}
		foreach (LocalEvent item in _local)
		{
			List<DateTime> list2 = new List<DateTime>();
			if (item.Birthday)
			{
				for (int i = from.Year; i <= to.Year; i++)
				{
					int day = Math.Min(item.Start.Day, DateTime.DaysInMonth(i, item.Start.Month));
					list2.Add(new DateTime(i, item.Start.Month, day));
				}
			}
			else
			{
				list2.Add(item.Start);
			}
			foreach (DateTime item2 in list2)
			{
				DateTime dateTime = ((item.AllDay || item.Birthday) ? item2.Date : item2);
				DateTime dateTime2 = ((item.AllDay || item.Birthday) ? dateTime.AddDays(1.0) : dateTime.AddMinutes(Math.Max(1, item.DurationMinutes)));
				if (!(dateTime2 <= from) && !(dateTime >= to))
				{
					list.Add(new CalendarEvent(item.Title, dateTime, dateTime2, item.AllDay || item.Birthday, item.Link, item.Birthday || Ics.IsBirthdayTitle(item.Title), item.Id));
				}
			}
		}
		return (from e in list
			where e.End > @from && e.Start < to
			orderby e.Start, e.Title
			select e).ToList();
	}

	public IEnumerable<CalendarEvent> BirthdaysToday()
	{
		return Events.Where((CalendarEvent e) => e.IsBirthday && e.Start.Date <= DateTime.Today && e.End > DateTime.Today);
	}
}
