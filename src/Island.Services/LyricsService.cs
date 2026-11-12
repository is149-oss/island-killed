using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Island.Services;

public sealed class LyricsService
{
	private sealed record Track(string Title, string Artist, TimeSpan Duration);

	private static readonly HttpClient Http = CreateClient();

	private static readonly Regex Stamp = new Regex("\\[(\\d+):(\\d+(?:[.:]\\d+)?)\\]", RegexOptions.Compiled);

	private static readonly Regex Noise = new Regex("\\s*[\\(\\[][^\\)\\]]*(official|video|lyric|audio|visualizer|клип|видео|mv)[^\\)\\]]*[\\)\\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex Feat = new Regex("\\s*[\\(\\[]?\\s*\\b(feat\\.?|ft\\.?|featuring|при уч\\.?|при участии)\\s+[^\\)\\]]*[\\)\\]]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex Remaster = new Regex("\\s*[-–—]\\s*[^-–—]*remaster[^-–—]*$|\\s*[\\(\\[][^\\)\\]]*remaster[^\\)\\]]*[\\)\\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex ArtistSplit = new Regex("\\s*(?:,|;|/|&|×|\\s(?:feat\\.?|ft\\.?|featuring|x|и|and|vs\\.?)\\s)\\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private string? _key;

	private static DateTime _lrclibDownUntil;

	private static readonly TimeSpan[] RetryDelays = new TimeSpan[4]
	{
		TimeSpan.FromSeconds(4L),
		TimeSpan.FromSeconds(12L),
		TimeSpan.FromSeconds(30L),
		TimeSpan.FromSeconds(60L)
	};

	private static readonly Regex WordStamp = new Regex("<(\\d+):(\\d+(?:[.:]\\d+)?)>", RegexOptions.Compiled);

	public IReadOnlyList<LyricLine>? Lines { get; private set; }

	public event Action? Changed;

	private static HttpClient CreateClient()
	{
		HttpClient httpClient = ReviewSafety.Client(NetProxy.Handler());
		httpClient.Timeout = TimeSpan.FromSeconds(10L);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Island/1.0 (Windows desktop island)");
		return httpClient;
	}

	public async Task LoadAsync(string title, string artist, TimeSpan duration)
	{
		string key = title + "|" + artist;
		if (key == _key)
		{
			return;
		}
		_key = key;
		Lines = null;
		Changed?.Invoke();
		if (!License.IsPro || !Settings.Current.Lyrics || string.IsNullOrWhiteSpace(title) || !License.Gate())
		{
			return;
		}
		string text = Noise.Replace(title, "").Trim();
		string bareTitle = Remaster.Replace(Feat.Replace(text, ""), "").Trim();
		if (bareTitle.Length == 0)
		{
			bareTitle = text;
		}
		string artist2 = ArtistSplit.Split(artist.Trim())[0].Trim();
		Track full = new Track(text, artist, duration);
		Track bare = new Track(bareTitle, artist, duration);
		Track main = new Track(bareTitle, artist2, duration);
		List<(string Name, Func<Task<string?>> Find)> sources = new List<(string, Func<Task<string>>)> { ("lrclib", () => LrclibGet(full)) };
		if (bare != full)
		{
			sources.Add(("lrclib bare", () => LrclibGet(bare)));
		}
		if (main != bare)
		{
			sources.Add(("lrclib main artist", () => LrclibGet(main)));
		}
		sources.Add(("lrclib search", () => LrclibSearch(main)));
		sources.Add(("lrclib query", () => LrclibQuery(main)));
		sources.Add(("lrclib title", () => LrclibHits("https://lrclib.net/api/search?track_name=" + Q(bareTitle), bare)));
		if (!DeveloperEndpoints.CustomLyrics) sources.Add(("netease", () => NetEase(main)));
		if (!DeveloperEndpoints.CustomLyrics && main.Artist != artist)
		{
			sources.Add(("netease title", () => NetEase(bare with
			{
				Artist = ""
			}, bare)));
		}
		for (int round = 0; round < RetryDelays.Length + 1; round++)
		{
			if (round > 0)
			{
				await Task.Delay(RetryDelays[round - 1]);
				if (key != _key || !Settings.Current.Lyrics)
				{
					break;
				}
			}
			string? lrc = null;
			bool failed = false;
			foreach (var item2 in sources)
			{
				string name = item2.Name;
				Func<Task<string>> item = item2.Find;
				bool lrclib = name.StartsWith("lrclib", StringComparison.Ordinal);
				if (lrclib && DateTime.UtcNow < _lrclibDownUntil)
				{
					failed = true;
					continue;
				}
				try
				{
					lrc = await item();
				}
				catch (Exception ex)
				{
					failed = true;
					bool flag = lrclib;
					if (flag)
					{
						bool flag2 = ((ex is TaskCanceledException || ex is HttpRequestException) ? true : false);
						flag = flag2;
					}
					if (flag)
					{
						_lrclibDownUntil = DateTime.UtcNow.AddMinutes(5.0);
						Log.Write($"lyrics {name}: {ex.GetType().Name}: {ex.Message}. lrclib is skipped for 5 minutes");
					}
					else
					{
						Log.Write($"lyrics {name}: {ex.GetType().Name}: {ex.Message}");
					}
				}
				if (key != _key)
				{
					return;
				}
				if (lrc == null || !Stamp.IsMatch(lrc))
				{
					lrc = null;
					continue;
				}
				break;
			}
			List<LyricLine>? list = ((lrc != null) ? Parse(lrc) : null);
			if (list != null && list.Count > 0)
			{
				Lines = list;
				Changed?.Invoke();
				break;
			}
			if (!failed && round >= 1)
			{
				break;
			}
		}
	}

	private static string Q(string s)
	{
		return Uri.EscapeDataString(s);
	}

	private static async Task<JsonDocument?> GetJson(string url, string? referer = null)
	{
		url = DeveloperEndpoints.LyricsUrl(url);
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
		if (referer != null)
		{
			request.Headers.Referrer = new Uri(referer);
		}
		using HttpResponseMessage response = await Http.SendAsync(request);
		if (response.StatusCode >= HttpStatusCode.InternalServerError || response.StatusCode == HttpStatusCode.TooManyRequests)
		{
			throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
		}
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}
		return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
	}

	private static string? Str(JsonElement e, string name)
	{
		if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static double Num(JsonElement e, string name)
	{
		if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
		{
			return 0.0;
		}
		return value.GetDouble();
	}

	private static async Task<string?> LrclibGet(Track t)
	{
		string text = "https://lrclib.net/api/get?track_name=" + Q(t.Title) + "&artist_name=" + Q(t.Artist);
		if (t.Duration > TimeSpan.Zero)
		{
			text += $"&duration={(int)t.Duration.TotalSeconds}";
		}
		using JsonDocument? jsonDocument = await GetJson(text);
		return (jsonDocument == null) ? null : Str(jsonDocument.RootElement, "syncedLyrics");
	}

	private static Task<string?> LrclibSearch(Track t)
	{
		return LrclibHits("https://lrclib.net/api/search?track_name=" + Q(t.Title) + "&artist_name=" + Q(t.Artist), t);
	}

	private static Task<string?> LrclibQuery(Track t)
	{
		return LrclibHits("https://lrclib.net/api/search?q=" + Q((t.Artist + " " + t.Title).Trim()), t);
	}

	private static async Task<string?> LrclibHits(string url, Track t)
	{
		using JsonDocument? jsonDocument = await GetJson(url);
		if (jsonDocument == null || jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
		{
			return null;
		}
		foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
		{
			string? text = Str(item, "syncedLyrics");
			if (text != null && Matches(t, Str(item, "trackName"), Str(item, "artistName"), Num(item, "duration")))
			{
				return text;
			}
		}
		return null;
	}

	private static async Task<string?> NetEase(Track query, Track? check = null)
	{
		Track t = check ?? query;
		using JsonDocument? search = await GetJson("https://music.163.com/api/search/get?s=" + Q((query.Title + " " + query.Artist).Trim()) + "&type=1&limit=8", "https://music.163.com/");
		if (search == null || !search.RootElement.TryGetProperty("result", out var value) || !value.TryGetProperty("songs", out var value2) || value2.ValueKind != JsonValueKind.Array)
		{
			return null;
		}
		foreach (JsonElement item in value2.EnumerateArray())
		{
			string artist = ((item.TryGetProperty("artists", out var value3) && value3.ValueKind == JsonValueKind.Array) ? string.Join(" ", from x in value3.EnumerateArray()
				select Str(x, "name")) : "");
			if (!Matches(t, Str(item, "name"), artist, Num(item, "duration") / 1000.0))
			{
				continue;
			}
			using JsonDocument? jsonDocument = await GetJson($"https://music.163.com/api/song/lyric?id={Num(item, "id"):0}&lv=1&kv=1&tv=-1", "https://music.163.com/");
			if (jsonDocument != null && jsonDocument.RootElement.TryGetProperty("lrc", out var value4))
			{
				string? text = Str(value4, "lyric");
				if (text != null && text.Length > 0)
				{
					return text;
				}
			}
		}
		return null;
	}

	private static bool Matches(Track t, string? title, string? artist, double seconds)
	{
		string text = norm(t.Title);
		string text2 = norm(Noise.Replace(title ?? "", ""));
		if (text2.Length == 0 || (!text.Contains(text2) && !text2.Contains(text)))
		{
			return false;
		}
		string wantArtist = norm(t.Artist);
		string gotArtist = norm(artist);
		if (wantArtist.Length > 0 && gotArtist.Length > 0 && !wantArtist.Contains(gotArtist) && !gotArtist.Contains(wantArtist) && !(from n in ArtistSplit.Split(t.Artist).Concat(ArtistSplit.Split(artist ?? "")).Select(norm)
			where n.Length > 1
			select n).Any((string n) => gotArtist.Contains(n) && wantArtist.Contains(n)))
		{
			return false;
		}
		if (!(seconds <= 0.0) && !(t.Duration <= TimeSpan.Zero))
		{
			return Math.Abs(seconds - t.Duration.TotalSeconds) <= 6.0;
		}
		return true;
		static string norm(string? s)
		{
			return new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
		}
	}

	private static List<LyricLine> Parse(string lrc)
	{
		List<LyricLine> list = new List<LyricLine>();
		string[] array = lrc.Split('\n');
		foreach (string text in array)
		{
			MatchCollection matchCollection = Stamp.Matches(text);
			if (matchCollection.Count == 0)
			{
				continue;
			}
			string text2 = text.Substring(matchCollection[matchCollection.Count - 1].Index + matchCollection[matchCollection.Count - 1].Length).Trim();
			List<LyricWord>? words = ParseWords(text2);
			string input = WordStamp.Replace(text2, "").Trim();
			input = Regex.Replace(input, "\\s+", " ");
			foreach (Match item in matchCollection)
			{
				list.Add(new LyricLine(ToTime(item), input, words));
			}
		}
		return list.OrderBy((LyricLine l) => l.Time).ToList();
	}

	private static List<LyricWord>? ParseWords(string body)
	{
		MatchCollection matchCollection = WordStamp.Matches(body);
		if (matchCollection.Count == 0)
		{
			return null;
		}
		List<LyricWord> list = new List<LyricWord>();
		for (int i = 0; i < matchCollection.Count; i++)
		{
			int num = matchCollection[i].Index + matchCollection[i].Length;
			int num2 = ((i + 1 < matchCollection.Count) ? matchCollection[i + 1].Index : body.Length);
			int num3 = num;
			string text = body.Substring(num3, num2 - num3).Trim();
			if (text.Length > 0)
			{
				list.Add(new LyricWord(ToTime(matchCollection[i]), text));
			}
		}
		if (list.Count <= 0)
		{
			return null;
		}
		return list;
	}

	private static TimeSpan ToTime(Match m)
	{
		int num = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
		double num2 = double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
		return TimeSpan.FromSeconds((double)(num * 60) + num2);
	}

	public int IndexAt(TimeSpan position)
	{
		if (Lines == null)
		{
			return -1;
		}
		int num = 0;
		int num2 = Lines.Count - 1;
		int result = -1;
		while (num <= num2)
		{
			int num3 = (num + num2) / 2;
			if (Lines[num3].Time <= position)
			{
				result = num3;
				num = num3 + 1;
			}
			else
			{
				num2 = num3 - 1;
			}
		}
		return result;
	}
}
