using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace Island.Services;

internal static class Ics
{
	private sealed class Raw
	{
		public string? Uid;

		public string? Summary;

		public string? Location;

		public string? Description;

		public string? Url;

		public string? RRule;

		public DateTime? Start;

		public DateTime? End;

		public DateTime? RecurrenceId;

		public TimeSpan? Duration;

		public bool AllDay;

		public bool Cancelled;

		public readonly List<DateTime> ExDates = new List<DateTime>();
	}

	private sealed class Rule
	{
		public string Freq = "";

		public int Interval = 1;

		public int? Count;

		public DateTime? Until;

		public readonly List<DayOfWeek> ByDay = new List<DayOfWeek>();
	}

	private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

	private static readonly Regex MeetingLink = new Regex("https?://(?:[\\w-]+\\.)*(?:meet\\.google\\.com|zoom\\.us|teams\\.microsoft\\.com|teams\\.live\\.com|telemost\\.yandex\\.ru|telemost\\.360\\.yandex\\.ru|webex\\.com|whereby\\.com|discord\\.gg|jitsi\\.org|meet\\.jit\\.si)[^\\s<>\"'\\\\]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex AnyLink = new Regex("https?://[^\\s<>\"'\\\\]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public static bool IsBirthdayTitle(string title)
	{
		string text = title.ToLowerInvariant();
		if (!text.Contains("день рождения") && !text.Contains("birthday") && !text.Contains("днюх"))
		{
			return text.Contains("bday");
		}
		return true;
	}

	public static List<CalendarEvent> Parse(string text, DateTime from, DateTime to)
	{
		List<Raw> list = new List<Raw>();
		Raw? raw = null;
		foreach (string item in Unfold(text))
		{
			if (item.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
			{
				raw = new Raw();
			}
			else if (item.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
			{
				if (raw != null)
				{
					list.Add(raw);
				}
				raw = null;
			}
			else
			{
				if (raw == null)
				{
					continue;
				}
				var (text2, pars, text3) = SplitLine(item);
				try
				{
					if (text2 == null)
					{
						continue;
					}
					switch (text2.Length)
					{
					case 3:
						switch (text2[1])
						{
						case 'I':
							if (text2 == "UID")
							{
								raw.Uid = text3;
							}
							break;
						case 'R':
							if (text2 == "URL")
							{
								raw.Url = text3;
							}
							break;
						}
						break;
					case 7:
						switch (text2[0])
						{
						case 'S':
							if (text2 == "SUMMARY")
							{
								raw.Summary = Unescape(text3);
							}
							break;
						case 'D':
							if (text2 == "DTSTART")
							{
								Raw raw2 = raw;
								Raw raw3 = raw;
								(DateTime, bool) tuple2 = ParseDate(text3, pars);
								DateTime? start = tuple2.Item1;
								raw2.Start = start;
								raw3.AllDay = tuple2.Item2;
							}
							break;
						}
						break;
					case 8:
						switch (text2[0])
						{
						case 'L':
							if (text2 == "LOCATION")
							{
								raw.Location = Unescape(text3);
							}
							break;
						case 'D':
							if (text2 == "DURATION")
							{
								raw.Duration = XmlConvert.ToTimeSpan(text3);
							}
							break;
						}
						break;
					case 5:
						switch (text2[0])
						{
						case 'D':
							if (text2 == "DTEND")
							{
								raw.End = ParseDate(text3, pars).Date;
							}
							break;
						case 'R':
							if (text2 == "RRULE")
							{
								raw.RRule = text3;
							}
							break;
						}
						break;
					case 6:
						switch (text2[0])
						{
						case 'E':
							if (text2 == "EXDATE")
							{
								string[] array = text3.Split(',');
								foreach (string value in array)
								{
									raw.ExDates.Add(ParseDate(value, pars).Date);
								}
							}
							break;
						case 'S':
							if (text2 == "STATUS")
							{
								raw.Cancelled = text3.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);
							}
							break;
						}
						break;
					case 11:
						if (text2 == "DESCRIPTION")
						{
							raw.Description = Unescape(text3);
						}
						break;
					case 13:
						if (text2 == "RECURRENCE-ID")
						{
							raw.RecurrenceId = ParseDate(text3, pars).Date;
						}
						break;
					case 4:
					case 9:
					case 10:
					case 12:
						break;
					}
				}
				catch (Exception)
				{
				}
			}
		}
		foreach (Raw o in list.Where((Raw r) => r.RecurrenceId.HasValue && r.Uid != null).ToList())
		{
			list.FirstOrDefault((Raw m) => m.Uid == o.Uid && !m.RecurrenceId.HasValue)?.ExDates.Add(o.RecurrenceId.Value);
		}
		List<CalendarEvent> list2 = new List<CalendarEvent>();
		foreach (Raw item2 in list)
		{
			if (item2.Cancelled || !item2.Start.HasValue)
			{
				continue;
			}
			DateTime? start = item2.End;
			TimeSpan timeSpan;
			if (!start.HasValue)
			{
				timeSpan = item2.Duration ?? (item2.AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero);
			}
			else
			{
				DateTime valueOrDefault = start.GetValueOrDefault();
				timeSpan = valueOrDefault - item2.Start.Value;
			}
			TimeSpan timeSpan2 = timeSpan;
			if (timeSpan2 < TimeSpan.Zero)
			{
				timeSpan2 = TimeSpan.Zero;
			}
			string title = (string.IsNullOrWhiteSpace(item2.Summary) ? "(без названия)" : item2.Summary.Trim());
			string? link = FindLink(item2.Url, item2.Location, item2.Description);
			foreach (DateTime start2 in Occurrences(item2, from - timeSpan2, to))
			{
				if (!item2.ExDates.Any((DateTime x) => Math.Abs((x - start2).TotalMinutes) < 1.0))
				{
					list2.Add(new CalendarEvent(title, start2, start2 + timeSpan2, item2.AllDay, link, IsBirthdayTitle(title), null));
				}
			}
		}
		return list2;
	}

	private static IEnumerable<DateTime> Occurrences(Raw e, DateTime from, DateTime to)
	{
		DateTime start = e.Start.Value;
		if (e.RRule == null)
		{
			if (start < to && start >= from)
			{
				yield return start;
			}
			yield break;
		}
		Rule rule = ParseRule(e.RRule);
		int count = 0;
		if (rule.Freq == "WEEKLY" && rule.ByDay.Count > 0)
		{
			DateTime weekStart = start.Date.AddDays(-((int)(start.DayOfWeek + 6) % 7));
			if (!rule.Count.HasValue && weekStart < from.AddDays(-14.0))
			{
				int num = (int)((from - weekStart).TotalDays / (double)(7 * rule.Interval)) - 1;
				if (num > 0)
				{
					weekStart = weekStart.AddDays(7.0 * (double)rule.Interval * (double)num);
				}
			}
			List<DayOfWeek> days = rule.ByDay.OrderBy((DayOfWeek d) => (int)(d + 6) % 7).ToList();
			for (int guard = 0; guard < 3000; guard++)
			{
				foreach (DayOfWeek item in days)
				{
					DateTime dateTime = weekStart.AddDays((int)(item + 6) % 7) + start.TimeOfDay;
					if (dateTime < start)
					{
						continue;
					}
					DateTime? until = rule.Until;
					if (until.HasValue)
					{
						DateTime valueOrDefault = until.GetValueOrDefault();
						if (dateTime > valueOrDefault)
						{
							yield break;
						}
					}
					int? count2 = rule.Count;
					if (count2.HasValue)
					{
						int valueOrDefault2 = count2.GetValueOrDefault();
						if (count >= valueOrDefault2)
						{
							yield break;
						}
					}
					count++;
					if (dateTime >= to)
					{
						yield break;
					}
					if (dateTime >= from)
					{
						yield return dateTime;
					}
				}
				weekStart = weekStart.AddDays(7 * rule.Interval);
			}
			yield break;
		}
		DateTime current2 = start;
		string freq = rule.Freq;
		int num2 = ((freq == "DAILY") ? rule.Interval : ((freq == "WEEKLY") ? (7 * rule.Interval) : 0));
		double stepDays = num2;
		if (stepDays > 0.0 && !rule.Count.HasValue && current2 < from.AddDays(0.0 - stepDays))
		{
			int num3 = (int)((from - current2).TotalDays / stepDays) - 1;
			if (num3 > 0)
			{
				current2 = current2.AddDays(stepDays * (double)num3);
			}
		}
		for (int guard = 0; guard < 5000; guard++)
		{
			DateTime? until = rule.Until;
			if (until.HasValue)
			{
				DateTime valueOrDefault3 = until.GetValueOrDefault();
				if (current2 > valueOrDefault3)
				{
					break;
				}
			}
			int? count2 = rule.Count;
			if (count2.HasValue)
			{
				int valueOrDefault4 = count2.GetValueOrDefault();
				if (count >= valueOrDefault4)
				{
					break;
				}
			}
			count++;
			if (!(current2 >= to))
			{
				if (current2 >= from)
				{
					yield return current2;
				}
				DateTime dateTime2;
				switch (rule.Freq)
				{
				case "DAILY":
				case "WEEKLY":
					dateTime2 = current2.AddDays(stepDays);
					break;
				case "MONTHLY":
					dateTime2 = start.AddMonths(rule.Interval * count);
					break;
				case "YEARLY":
					dateTime2 = start.AddYears(rule.Interval * count);
					break;
				default:
					dateTime2 = DateTime.MaxValue;
					break;
				}
				current2 = dateTime2;
				if (current2 == DateTime.MaxValue)
				{
					break;
				}
				continue;
			}
			break;
		}
	}

	private static Rule ParseRule(string value)
	{
		Rule rule = new Rule();
		string[] array = value.Split(';');
		for (int i = 0; i < array.Length; i++)
		{
			string[] array2 = array[i].Split('=', 2);
			if (array2.Length != 2)
			{
				continue;
			}
			switch (array2[0].ToUpperInvariant())
			{
			case "FREQ":
				rule.Freq = array2[1].ToUpperInvariant();
				break;
			case "INTERVAL":
				rule.Interval = Math.Max(1, int.Parse(array2[1], Inv));
				break;
			case "COUNT":
				rule.Count = int.Parse(array2[1], Inv);
				break;
			case "UNTIL":
				rule.Until = ParseDate(array2[1], new Dictionary<string, string>()).Date;
				break;
			case "BYDAY":
			{
				string[] array3 = array2[1].Split(',');
				for (int j = 0; j < array3.Length; j++)
				{
					DayOfWeek? dayOfWeek = Regex.Replace(array3[j], "^[+-]?\\d+", "").ToUpperInvariant() switch
					{
						"MO" => (DayOfWeek?)DayOfWeek.Monday, 
						"TU" => DayOfWeek.Tuesday, 
						"WE" => DayOfWeek.Wednesday, 
						"TH" => DayOfWeek.Thursday, 
						"FR" => DayOfWeek.Friday, 
						"SA" => DayOfWeek.Saturday, 
						"SU" => DayOfWeek.Sunday, 
						_ => null, 
					};
					if (dayOfWeek.HasValue)
					{
						rule.ByDay.Add(dayOfWeek.Value);
					}
				}
				break;
			}
			}
		}
		return rule;
	}

	private static (DateTime Date, bool AllDay) ParseDate(string value, Dictionary<string, string> pars)
	{
		string text = value.Trim();
		if (text.Length == 8 || (pars.TryGetValue("VALUE", out string? value2) && value2.Equals("DATE", StringComparison.OrdinalIgnoreCase)))
		{
			return (Date: DateTime.ParseExact(text.Substring(0, 8), "yyyyMMdd", Inv), AllDay: true);
		}
		bool flag = text.EndsWith('Z');
		DateTime dateTime = DateTime.ParseExact(text.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", Inv);
		if (flag)
		{
			return (Date: DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).ToLocalTime(), AllDay: false);
		}
		if (pars.TryGetValue("TZID", out string? value3))
		{
			try
			{
				TimeZoneInfo sourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById(value3.Trim('"'));
				return (Date: TimeZoneInfo.ConvertTime(dateTime, sourceTimeZone, TimeZoneInfo.Local), AllDay: false);
			}
			catch (Exception)
			{
			}
		}
		return (Date: dateTime, AllDay: false);
	}

	private static IEnumerable<string> Unfold(string text)
	{
		string? text2 = null;
		string[] array = text.Replace("\r\n", "\n").Split('\n');
		foreach (string raw in array)
		{
			if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
			{
				text2 += raw.Substring(1);
				continue;
			}
			if (text2 != null)
			{
				yield return text2;
			}
			text2 = raw;
		}
		if (text2 != null)
		{
			yield return text2;
		}
	}

	private static (string Name, Dictionary<string, string> Params, string Value) SplitLine(string line)
	{
		int num = -1;
		bool flag = false;
		for (int i = 0; i < line.Length; i++)
		{
			if (line[i] == '"')
			{
				flag = !flag;
			}
			else if (line[i] == ':' && !flag)
			{
				num = i;
				break;
			}
		}
		if (num < 0)
		{
			return (Name: line.ToUpperInvariant(), Params: new Dictionary<string, string>(), Value: "");
		}
		string[] array = line.Substring(0, num).Split(';');
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in array.Skip(1))
		{
			string[] array2 = item.Split('=', 2);
			if (array2.Length == 2)
			{
				dictionary[array2[0]] = array2[1];
			}
		}
		return (Name: array[0].ToUpperInvariant(), Params: dictionary, Value: line.Substring(num + 1));
	}

	private static string Unescape(string v)
	{
		return v.Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",")
			.Replace("\\;", ";")
			.Replace("\\\\", "\\");
	}

	private static string? FindLink(params string?[] fields)
	{
		string[] array = fields;
		foreach (string text in array)
		{
			if (text != null)
			{
				Match match = MeetingLink.Match(text);
				if (match != null && match.Success)
				{
					return Clean(match.Value);
				}
			}
		}
		array = fields;
		foreach (string text2 in array)
		{
			if (text2 != null)
			{
				Match match2 = AnyLink.Match(text2);
				if (match2 != null && match2.Success)
				{
					return Clean(match2.Value);
				}
			}
		}
		return null;
	}

	private static string Clean(string url)
	{
		return url.TrimEnd(new char[6] { '.', ',', ')', ';', ']', '>' });
	}
}
