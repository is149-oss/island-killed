using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Island.Services;

public sealed class MediaService
{
	private GlobalSystemMediaTransportControlsSessionManager? _manager;

	private GlobalSystemMediaTransportControlsSession? _session;

	private SynchronizationContext? _ctx;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	public static readonly Color DefaultAccent = Color.FromRgb(139, 233, 168);

	private string? _coverKey;

	private string? _coverHash;

	private bool _artworkDirty = true;

	private BitmapImage? _cover;

	private Color _accent = DefaultAccent;

	private TimeSpan _startTime;

	private (TimeSpan, DateTimeOffset) _lastReport;

	private MediaState? _sessionState;

	private MediaState? _browserAudio;

	private readonly List<GlobalSystemMediaTransportControlsSession> _watched = new List<GlobalSystemMediaTransportControlsSession>();

	private string? _watchedIds;

	private (string App, int Nth)? _chosen;

	private string? _windowsAtChoice;

	private (TimeSpan Target, DateTimeOffset At)? _seekGuard;

	public MediaState? Current { get; private set; }

	public bool IsBrowserFallback
	{
		get
		{
			if (_browserAudio != null)
			{
				return (object)Current == _browserAudio;
			}
			return false;
		}
	}

	public bool HasBrowserAudio => _browserAudio != null;

	public bool BrowserControllable { get; private set; }

	public int PlayerCount { get; private set; }

	public int PlayerIndex { get; private set; }

	public event Action? Changed;

	private void Publish()
	{
		MediaState? current;
		if (_browserAudio != null)
		{
			MediaState? sessionState = _sessionState;
			if ((object)sessionState == null || !sessionState.IsPlaying)
			{
				current = _browserAudio;
				goto IL_002f;
			}
		}
		current = _sessionState;
		goto IL_002f;
		IL_002f:
		Current = current;
		Changed?.Invoke();
	}

	public void SetBrowserAudio(string? processName, string? title, string artist = "", bool playing = true, bool controllable = false)
	{
		if (processName == null)
		{
			if (!(_browserAudio == null))
			{
				_browserAudio = null;
				BrowserControllable = false;
				Publish();
			}
			return;
		}
		string text = (string.IsNullOrWhiteSpace(title) ? "Звук в браузере" : title);
		bool flag = controllable != BrowserControllable;
		BrowserControllable = controllable;
		MediaState? browserAudio = _browserAudio;
		if ((object)browserAudio != null && browserAudio.Title == text && browserAudio.Artist == artist && browserAudio.SourceId == processName)
		{
			if (browserAudio.IsPlaying != playing)
			{
				DateTimeOffset now = DateTimeOffset.Now;
				TimeSpan position = browserAudio.Position + (browserAudio.IsPlaying ? (now - browserAudio.PositionUpdatedAt) : TimeSpan.Zero);
				_browserAudio = browserAudio with
				{
					IsPlaying = playing,
					Position = position,
					PositionUpdatedAt = now
				};
				flag = true;
			}
			if (flag)
			{
				Publish();
			}
		}
		else
		{
			_browserAudio = new MediaState(text, artist, BrowserName(processName), playing, null, DefaultAccent, TimeSpan.Zero, TimeSpan.Zero, DateTimeOffset.Now, processName);
			Publish();
		}
	}

	private static Task<bool> PressInBrowser(string which)
	{
		return Task.Run(() => BrowserTabs.Press(which));
	}

	private static string BrowserName(string process)
	{
		switch (process)
		{
		case "browser":
		case "yandex":
			return "Яндекс Браузер";
		case "chrome":
			return "Chrome";
		case "msedge":
			return "Edge";
		case "firefox":
			return "Firefox";
		case "opera":
			return "Opera";
		case "vivaldi":
			return "Vivaldi";
		case "brave":
			return "Brave";
		default:
			return process;
		}
	}

	public async Task InitAsync()
	{
		if (!ReviewSafety.MediaEnabled) return;
		_ctx = SynchronizationContext.Current;
		_manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
		_manager.CurrentSessionChanged += (GlobalSystemMediaTransportControlsSessionManager _, CurrentSessionChangedEventArgs _) =>
		{
			Post(Attach);
		};
		_manager.SessionsChanged += (GlobalSystemMediaTransportControlsSessionManager _, SessionsChangedEventArgs _) =>
		{
			try
			{
				Log.Write("media sessions: [" + string.Join(", ", from s in _manager.GetSessions()
					select s.SourceAppUserModelId) + "]");
			}
			catch (Exception ex)
			{
				Log.Caught("media sessions", ex);
			}
			Post(Attach);
		};
		Attach();
	}

	private int PickAutomatically(List<GlobalSystemMediaTransportControlsSession> sessions, GlobalSystemMediaTransportControlsSession? current)
	{
		string? currentId = current?.SourceAppUserModelId;
		string? shownId = _session?.SourceAppUserModelId;
		int num = Index((GlobalSystemMediaTransportControlsSession s) => s.SourceAppUserModelId == currentId && IsPlaying(s));
		if (num < 0)
		{
			num = Index((GlobalSystemMediaTransportControlsSession s) => s.SourceAppUserModelId == shownId && IsPlaying(s));
		}
		if (num < 0)
		{
			num = Index(IsPlaying);
		}
		if (num < 0)
		{
			num = Index((GlobalSystemMediaTransportControlsSession s) => s.SourceAppUserModelId == currentId);
		}
		if (num < 0)
		{
			num = Index((GlobalSystemMediaTransportControlsSession s) => s.SourceAppUserModelId == shownId);
		}
		if (num < 0 && sessions.Count > 0)
		{
			num = 0;
		}
		return num;
		int Index(Func<GlobalSystemMediaTransportControlsSession, bool> match)
		{
			return sessions.FindIndex((GlobalSystemMediaTransportControlsSession s) => match(s));
		}
	}

	private void Watch(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
	{
		foreach (GlobalSystemMediaTransportControlsSession item in _watched)
		{
			item.PlaybackInfoChanged -= OnAnyPlaybackChanged;
		}
		_watched.Clear();
		foreach (GlobalSystemMediaTransportControlsSession session in sessions)
		{
			session.PlaybackInfoChanged += OnAnyPlaybackChanged;
			_watched.Add(session);
		}
	}

	private void OnAnyPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, object args)
	{
		Post(Attach);
	}

	private void Post(Action action)
	{
		if (_ctx != null)
		{
			_ctx.Post((object? _) =>
			{
				action();
			}, null);
		}
		else
		{
			action();
		}
	}

	private static (string App, int Nth) Identify(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions, int index)
	{
		return (App: sessions[index].SourceAppUserModelId, Nth: sessions.Take(index).Count((GlobalSystemMediaTransportControlsSession s) => s.SourceAppUserModelId == sessions[index].SourceAppUserModelId));
	}

	private static int Find(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions, (string App, int Nth) id)
	{
		int i = 0;
		int num = 0;
		for (; i < sessions.Count; i++)
		{
			if (sessions[i].SourceAppUserModelId == id.App && num++ == id.Nth)
			{
				return i;
			}
		}
		return -1;
	}

	private static GlobalSystemMediaTransportControlsSessionPlaybackStatus? StatusOf(GlobalSystemMediaTransportControlsSession s)
	{
		try
		{
			return s.GetPlaybackInfo().PlaybackStatus;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static bool IsPlaying(GlobalSystemMediaTransportControlsSession s)
	{
		return StatusOf(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
	}

	private static bool IsLive(GlobalSystemMediaTransportControlsSession s)
	{
		bool flag;
		switch (StatusOf(s))
		{
		case null:
		case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed:
		case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		return !flag;
	}

	private List<GlobalSystemMediaTransportControlsSession> LiveSessions()
	{
		return _manager?.GetSessions().Where(IsLive).ToList() ?? new List<GlobalSystemMediaTransportControlsSession>();
	}

	public void SwitchPlayer()
	{
		if (!(_manager == null))
		{
			List<GlobalSystemMediaTransportControlsSession> list = LiveSessions();
			if (list.Count >= 2)
			{
				_chosen = Identify(list, (PlayerIndex + 1) % list.Count);
				_windowsAtChoice = _manager.GetCurrentSession()?.SourceAppUserModelId;
				Attach();
			}
		}
	}

	private void Attach()
	{
		if (_manager == null)
		{
			return;
		}
		IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions = _manager.GetSessions();
		string text = string.Join("\n", sessions.Select((GlobalSystemMediaTransportControlsSession s) => s.SourceAppUserModelId));
		if (text != _watchedIds || sessions.Any((GlobalSystemMediaTransportControlsSession s) => !_watched.Contains(s)))
		{
			_watchedIds = text;
			Watch(sessions);
		}
		List<GlobalSystemMediaTransportControlsSession> sessions2 = LiveSessions();
		GlobalSystemMediaTransportControlsSession currentSession = _manager.GetCurrentSession();
		(string, int)? chosen = _chosen;
		int index = ((!chosen.HasValue) ? (-1) : Find(id: chosen.GetValueOrDefault(), sessions: sessions2));
		if (index < 0 || (!IsPlaying(sessions2[index]) && sessions2.Any((GlobalSystemMediaTransportControlsSession s) => s.SourceAppUserModelId != sessions2[index].SourceAppUserModelId && s.SourceAppUserModelId != _windowsAtChoice && IsPlaying(s))))
		{
			_chosen = null;
			index = -1;
		}
		if (index < 0)
		{
			index = PickAutomatically(sessions2, currentSession);
		}
		PlayerCount = sessions2.Count;
		PlayerIndex = Math.Max(index, 0);
		GlobalSystemMediaTransportControlsSession? globalSystemMediaTransportControlsSession = ((index >= 0) ? sessions2[index] : null);
		if (globalSystemMediaTransportControlsSession == _session && globalSystemMediaTransportControlsSession != null)
		{
			RefreshAsync();
			return;
		}
		if (_session != null)
		{
			_session.MediaPropertiesChanged -= OnPropertiesChanged;
			_session.PlaybackInfoChanged -= OnSessionEvent;
			_session.TimelinePropertiesChanged -= OnSessionEvent;
		}
		_session = globalSystemMediaTransportControlsSession;
		if (_session != null)
		{
			_session.MediaPropertiesChanged += OnPropertiesChanged;
			_session.PlaybackInfoChanged += OnSessionEvent;
			_session.TimelinePropertiesChanged += OnSessionEvent;
		}
		_artworkDirty = true;
		RefreshAsync();
	}

	private void OnSessionEvent(GlobalSystemMediaTransportControlsSession sender, object args)
	{
		Post(() =>
		{
			RefreshAsync();
		});
	}

	private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
	{
		Post(() =>
		{
			_artworkDirty = true;
			RefreshAsync();
		});
	}

	private async Task RecheckArtworkAsync(string key)
	{
		int[] array = new int[3] { 600, 1500, 3500 };
		for (int i = 0; i < array.Length; i++)
		{
			await Task.Delay(array[i]);
			if (_coverKey != key)
			{
				break;
			}
			_artworkDirty = true;
			await RefreshAsync();
		}
	}

	private async Task RefreshAsync()
	{
		await _gate.WaitAsync();
		try
		{
			GlobalSystemMediaTransportControlsSession? s = _session;
			if (s == null)
			{
				_sessionState = null;
				Publish();
				return;
			}
			GlobalSystemMediaTransportControlsSessionMediaProperties props = await s.TryGetMediaPropertiesAsync();
			GlobalSystemMediaTransportControlsSessionPlaybackInfo playback = s.GetPlaybackInfo();
			GlobalSystemMediaTransportControlsSessionTimelineProperties timeline = s.GetTimelineProperties();
			string key = $"{s.SourceAppUserModelId}|{props.Title}|{props.Artist}|{props.AlbumTitle}";
			bool newTrack = key != _coverKey;
			if (newTrack || _artworkDirty || (_cover == null && props.Thumbnail != null))
			{
				_coverKey = key;
				_artworkDirty = false;
				byte[]? array = ((props.Thumbnail == null) ? null : (await ReadThumbnailAsync(props.Thumbnail)));
				byte[]? array2 = array;
				string? text = ((array2 != null) ? Convert.ToHexString(SHA1.HashData(array2)) : null);
				if (text != _coverHash)
				{
					_coverHash = text;
					_cover = ((array2 != null) ? DecodeCover(array2) : null);
					_accent = ((_cover != null) ? ExtractAccent(_cover) : DefaultAccent);
				}
				if (newTrack)
				{
					RecheckArtworkAsync(key);
				}
			}
			DateTimeOffset now = DateTimeOffset.Now;
			DateTimeOffset dateTimeOffset = timeline.LastUpdatedTime;
			if (dateTimeOffset.Year < 2000 || dateTimeOffset > now)
			{
				dateTimeOffset = now;
			}
			_startTime = timeline.StartTime;
			bool flag = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
			TimeSpan timeSpan = timeline.Position - timeline.StartTime;
			(TimeSpan, DateTimeOffset) tuple = (timeline.Position, timeline.LastUpdatedTime);
			(TimeSpan, DateTimeOffset) tuple2 = tuple;
			(TimeSpan, DateTimeOffset) lastReport = _lastReport;
			if ((tuple2.Item1 == lastReport.Item1 && tuple2.Item2 == lastReport.Item2) || StaleAfterSeek(timeSpan + (flag ? (now - dateTimeOffset) : TimeSpan.Zero), now))
			{
				MediaState? sessionState = _sessionState;
				if ((object)sessionState != null && sessionState.Title == (props.Title ?? ""))
				{
					timeSpan = sessionState.Position + (sessionState.IsPlaying ? (now - sessionState.PositionUpdatedAt) : TimeSpan.Zero);
					dateTimeOffset = now;
					goto IL_0454;
				}
			}
			if (!flag)
			{
				dateTimeOffset = now;
			}
			goto IL_0454;
			IL_0454:
			_lastReport = tuple;
			string title = props.Title ?? "";
			string artist = props.Artist ?? "";
			string sourceApp = PrettySource(s.SourceAppUserModelId);
			bool isPlaying = flag;
			BitmapImage? cover = _cover;
			Color accent = _accent;
			TimeSpan position = timeSpan;
			TimeSpan duration;
			if (timeline.EndTime > timeline.StartTime)
			{
				duration = timeline.EndTime - timeline.StartTime;
			}
			else
			{
				duration = ((timeline.MaxSeekTime > timeline.MinSeekTime) ? (timeline.MaxSeekTime - timeline.MinSeekTime) : TimeSpan.Zero);
			}
			DateTimeOffset positionUpdatedAt = dateTimeOffset;
			string sourceAppUserModelId = s.SourceAppUserModelId;
			bool isNextEnabled = playback.Controls.IsNextEnabled;
			bool isPreviousEnabled = playback.Controls.IsPreviousEnabled;
			string album = props.AlbumTitle ?? "";
			string albumArtist = props.AlbumArtist ?? "";
			IReadOnlyList<string> genres = props.Genres;
			string genre = ((genres != null && genres.Count > 0) ? string.Join(", ", genres) : "");
			int trackNumber = props.TrackNumber;
			_sessionState = new MediaState(title, artist, sourceApp, isPlaying, cover, accent, position, duration, positionUpdatedAt, sourceAppUserModelId, isNextEnabled, isPreviousEnabled, album, albumArtist, genre, trackNumber, props.PlaybackType switch
			{
				MediaPlaybackType.Music => "music", 
				MediaPlaybackType.Video => "video", 
				MediaPlaybackType.Image => "image", 
				_ => "", 
			});
			Publish();
		}
		catch (Exception)
		{
		}
		finally
		{
			_gate.Release();
		}
	}

	private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference thumbnail)
	{
		_ = 1;
		try
		{
			using IRandomAccessStreamWithContentType ras = await thumbnail.OpenReadAsync();
			using Stream src = ras.AsStreamForRead();
			MemoryStream ms = new MemoryStream();
			await src.CopyToAsync(ms);
			return (ms.Length > 0) ? ms.ToArray() : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static BitmapImage? DecodeCover(byte[] bytes)
	{
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.DecodePixelWidth = 160;
			bitmapImage.StreamSource = new MemoryStream(bytes);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			return bitmapImage;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static Color ExtractAccent(BitmapSource cover)
	{
		try
		{
			FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(new TransformedBitmap(cover, new ScaleTransform(24.0 / (double)cover.PixelWidth, 24.0 / (double)cover.PixelHeight)), PixelFormats.Bgra32, null, 0.0);
			int pixelWidth = formatConvertedBitmap.PixelWidth;
			int pixelHeight = formatConvertedBitmap.PixelHeight;
			byte[] array = new byte[pixelWidth * pixelHeight * 4];
			formatConvertedBitmap.CopyPixels(array, pixelWidth * 4, 0);
			double num = 0.0;
			double num2 = 0.0;
			double num3 = 0.0;
			double num4 = 0.0;
			for (int i = 0; i < array.Length; i += 4)
			{
				double num5 = (double)(int)array[i] / 255.0;
				double num6 = (double)(int)array[i + 1] / 255.0;
				double num7 = (double)(int)array[i + 2] / 255.0;
				double num8 = Math.Max(num7, Math.Max(num6, num5));
				double num9 = Math.Min(num7, Math.Min(num6, num5));
				double num10 = 0.02 + (num8 - num9) * (num8 - num9) * num8;
				num += num7 * num10;
				num2 += num6 * num10;
				num3 += num5 * num10;
				num4 += num10;
			}
			var (h, num11, _) = ToHsl(num / num4, num2 / num4, num3 / num4);
			if (num11 > 0.08)
			{
				num11 = Math.Max(num11, 0.55);
			}
			return FromHsl(h, num11, 0.7);
		}
		catch (Exception)
		{
			return DefaultAccent;
		}
	}

	internal static (double h, double s, double l) ToHsl(double r, double g, double b)
	{
		double num = Math.Max(r, Math.Max(g, b));
		double num2 = Math.Min(r, Math.Min(g, b));
		double num3 = (num + num2) / 2.0;
		double num4 = num - num2;
		if (num4 < 1E-06)
		{
			return (h: 0.0, s: 0.0, l: num3);
		}
		double item = num4 / (1.0 - Math.Abs(2.0 * num3 - 1.0));
		return (h: ((num == r) ? (((g - b) / num4 + 6.0) % 6.0) : ((num == g) ? ((b - r) / num4 + 2.0) : ((r - g) / num4 + 4.0))) * 60.0, s: item, l: num3);
	}

	internal static Color FromHsl(double h, double s, double l)
	{
		double num = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
		double num2 = num * (1.0 - Math.Abs(h / 60.0 % 2.0 - 1.0));
		double num3 = l - num / 2.0;
		var (num4, num5, num6) = ((h < 60.0) ? (num, num2, 0.0) : ((h < 120.0) ? (num2, num, 0.0) : ((h < 180.0) ? (0.0, num, num2) : ((h < 240.0) ? (0.0, num2, num) : ((!(h < 300.0)) ? (num, 0.0, num2) : (num2, 0.0, num))))));
		return Color.FromRgb((byte)((num4 + num3) * 255.0), (byte)((num5 + num3) * 255.0), (byte)((num6 + num3) * 255.0));
	}

	private static string PrettySource(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return "";
		}
		string text = id;
		int num = text.LastIndexOf('!');
		if (num >= 0)
		{
			text = text.Substring(num + 1);
		}
		if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			string text2 = text;
			text = text2.Substring(0, text2.Length - 4);
		}
		string text3 = id.ToLowerInvariant();
		if (text3.Contains("yandex") && text3.Contains("music"))
		{
			return "Яндекс Музыка";
		}
		if (text3.Contains("spotify"))
		{
			return "Spotify";
		}
		if (text3.Contains("msedge"))
		{
			return "Edge";
		}
		if (text3.Contains("chrome"))
		{
			return "Chrome";
		}
		if (text3.Contains("firefox"))
		{
			return "Firefox";
		}
		if (text3.Contains("telegram"))
		{
			return "Telegram";
		}
		return text;
	}

	public async Task PlayPauseAsync()
	{
		_ = 1;
		try
		{
			if (IsBrowserFallback)
			{
				bool flag = BrowserControllable;
				if (flag)
				{
					flag = await PressInBrowser("play");
				}
				if (flag)
				{
					MediaState? browserAudio = _browserAudio;
					if ((object)browserAudio != null)
					{
						SetBrowserAudio(browserAudio.SourceId, browserAudio.Title, browserAudio.Artist, !browserAudio.IsPlaying, controllable: true);
					}
				}
			}
			else if (_session != null)
			{
				await _session.TryTogglePlayPauseAsync();
			}
		}
		catch (Exception)
		{
		}
	}

	public async Task NextAsync()
	{
		_ = 1;
		try
		{
			if (IsBrowserFallback)
			{
				if (BrowserControllable)
				{
					await PressInBrowser("next");
				}
			}
			else if (_session != null)
			{
				await _session.TrySkipNextAsync();
			}
		}
		catch (Exception)
		{
		}
	}

	public async Task PreviousAsync()
	{
		_ = 1;
		try
		{
			if (IsBrowserFallback)
			{
				if (BrowserControllable)
				{
					await PressInBrowser("prev");
				}
			}
			else if (_session != null)
			{
				await _session.TrySkipPreviousAsync();
			}
		}
		catch (Exception)
		{
		}
	}

	public async Task<bool> SeekAsync(double fraction)
	{
		MediaState? sessionState = _sessionState;
		GlobalSystemMediaTransportControlsSession? s = _session;
		if (s == null || sessionState == null || sessionState.Duration <= TimeSpan.Zero || IsBrowserFallback)
		{
			return false;
		}
		TimeSpan timeSpan = TimeSpan.FromTicks((long)((double)sessionState.Duration.Ticks * Math.Clamp(fraction, 0.0, 1.0)));
		_sessionState = sessionState with
		{
			Position = timeSpan,
			PositionUpdatedAt = DateTimeOffset.Now
		};
		_seekGuard = (timeSpan, DateTimeOffset.Now);
		Publish();
		List<long> list = new List<long>();
		try
		{
			GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties = s.GetTimelineProperties();
			if (timelineProperties.EndTime > timelineProperties.StartTime)
			{
				list.Add((timelineProperties.StartTime + timeSpan).Ticks);
			}
			if (timelineProperties.MaxSeekTime > timelineProperties.MinSeekTime)
			{
				list.Add((timelineProperties.MinSeekTime + TimeSpan.FromTicks(Math.Min(timeSpan.Ticks, (timelineProperties.MaxSeekTime - timelineProperties.MinSeekTime).Ticks))).Ticks);
			}
		}
		catch (Exception)
		{
		}
		list.Add(timeSpan.Ticks);
		bool ok = false;
		foreach (long ticks in list.Distinct())
		{
			for (int attempt = 0; attempt < 2; attempt++)
			{
				if (ok)
				{
					break;
				}
				try
				{
					ok = await s.TryChangePlaybackPositionAsync(ticks);
				}
				catch (Exception)
				{
				}
				if (!ok)
				{
					await Task.Delay(120);
				}
			}
			if (ok)
			{
				break;
			}
		}
		if (!ok)
		{
			Log.Write("seek refused by " + s.SourceAppUserModelId);
			_seekGuard = null;
			_lastReport = default;
		}
		ResyncAfterSeekAsync();
		return ok;
	}

	private bool StaleAfterSeek(TimeSpan reported, DateTimeOffset now)
	{
		(TimeSpan, DateTimeOffset)? seekGuard = _seekGuard;
		if (seekGuard.HasValue)
		{
			(TimeSpan, DateTimeOffset) valueOrDefault = seekGuard.GetValueOrDefault();
			TimeSpan timeSpan = now - valueOrDefault.Item2;
			if (timeSpan > TimeSpan.FromSeconds(2.5))
			{
				_seekGuard = null;
				return false;
			}
			if ((reported - (valueOrDefault.Item1 + timeSpan)).Duration() < TimeSpan.FromSeconds(3L))
			{
				_seekGuard = null;
				return false;
			}
			return true;
		}
		return false;
	}

	private async Task ResyncAfterSeekAsync()
	{
		int[] array = new int[3] { 500, 1500, 3000 };
		for (int i = 0; i < array.Length; i++)
		{
			await Task.Delay(array[i]);
			await RefreshAsync();
		}
	}
}
