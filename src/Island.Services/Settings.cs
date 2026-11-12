using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Island.Services;

public sealed class Settings
{
	public static readonly string Folder = (DeveloperAccess.Enabled || ReviewSafety.Enabled) ? ReviewSafety.Profile : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island");

	private static readonly string FilePath = Path.Combine(Folder, "settings.json");

	private const int CurrentVersion = 5;

	private string? _logged;

	public static Settings Current { get; } = Load();

	public int Version { get; set; }

	public bool SocialListenerEnabled { get; set; }
	public Dictionary<string, Island.Social.SocialOptions> SocialIntegrations { get; set; } = new();

	public bool HideInFullscreen { get; set; } = true;

	public List<string> HideInApps { get; set; } = new List<string>();

	public int HoverDelayMs { get; set; } = 110;

	public bool ReduceMotion { get; set; }

	public bool WaterEffects { get; set; } = true;

	public int MaxFps { get; set; }

	public double WaterStrength { get; set; } = 1.0;

	public double AttractStrength { get; set; } = 1.0;

	public double ToastSeconds { get; set; } = 1.8;

	public bool IdleShowWeather { get; set; } = true;

	public bool IdleShowBattery { get; set; } = true;

	public bool IdleShowKeyboard { get; set; } = true;

	public bool ToastVolume { get; set; } = true;

	public bool ReplaceVolumeOsd { get; set; } = true;

	public bool ToastBattery { get; set; } = true;

	public bool ToastDevices { get; set; } = true;

	public bool ToastNetwork { get; set; } = true;

	public bool ToastCapsLock { get; set; } = true;

	public bool ToastCopy { get; set; } = true;

	public bool Downloads { get; set; } = true;

	public bool CalendarReminders { get; set; } = true;

	public bool Lyrics { get; set; } = true;

	public string MusicVisual { get; set; } = "pulse";

	public double MusicTint { get; set; } = 0.4;

	public string MusicBackdrop { get; set; } = "none";

	public bool BackdropCompact { get; set; } = true;

	public double BackdropStrength { get; set; } = 1.0;

	public bool PlayerVolume { get; set; } = true;

	public bool LyricsInCompact { get; set; } = true;

	public int LyricsOffsetMs { get; set; }

	public bool TrackHistory { get; set; } = true;

	public bool GameMode { get; set; } = true;

	public bool Detached { get; set; }

	public double DetachedX { get; set; }

	public double DetachedY { get; set; }

	public List<string> GameBarItems { get; set; } = new List<string> { "fps", "cpu", "ram", "gpu", "disk" };

	public string AiProvider { get; set; } = "openrouter";

	public string? AiModel { get; set; }

	public string? AiCustomUrl { get; set; }

	public bool AiShortAnswers { get; set; } = true;

	public List<string> CalendarFeeds { get; set; } = new List<string>();

	public bool SeasonalEffects { get; set; } = true;

	public string? MyBirthday { get; set; }

	public string Font { get; set; } = "aminah";

	public string IslandColor { get; set; } = "#000000";

	public double CornerRadius { get; set; } = 0.75;

	public double UiScale { get; set; } = 1.0;

	public double? UiScaleCollapsed { get; set; }

	public string AnimationStyle { get; set; } = "drop";

	public double NeckScale { get; set; } = 1.0;

	public string Accent { get; set; } = "auto";

	public bool Glow { get; set; }

	public double GlowStrength { get; set; } = 0.75;

	public bool Outline { get; set; } = true;

	public bool Shadow { get; set; } = true;

	public bool OpenOnClick { get; set; }

	public bool OpenOnHomeTab { get; set; }

	public string OpenTab { get; set; } = "last";

	public bool HideAnimation { get; set; } = true;

	public string HideStyle { get; set; } = "drop";

	public bool RimAnimation { get; set; } = true;

	public bool BrowserAudio { get; set; } = true;

	public bool ExpRipples { get; set; }

	public int HotkeyVk { get; set; }

	public int HotkeyMods { get; set; }

	public int HoldHotkeyVk { get; set; }

	public int HoldHotkeyMods { get; set; }

	public string HotkeyMode { get; set; } = "toggle";

	public bool Halloween { get; set; } = true;

	public bool HalloweenPumpkins { get; set; } = true;

	public bool HalloweenSpider { get; set; } = true;

	public bool HalloweenVisitors { get; set; } = true;

	public bool HalloweenLogo { get; set; } = true;

	public bool PhoneLink { get; set; }

	public bool PhoneCards { get; set; } = true;

	public string ProxyAddress { get; set; } = "";

	public bool PetEnabled { get; set; }

	public string PetKind { get; set; } = "black";

	public string PetPlace { get; set; } = "auto";

	public double PetSize { get; set; } = 1.0;

	public bool PetEvents { get; set; } = true;

	public bool PetMusic { get; set; } = true;

	public bool PetTyping { get; set; } = true;

	public bool PetLook { get; set; } = true;

	public bool PetSleep { get; set; } = true;

	public int PetSleepMinutes { get; set; } = 3;

	public bool PhoneDnd { get; set; }

	public bool PhoneOnCollapsed { get; set; } = true;

	public bool CollapsedMusic { get; set; } = true;

	public bool MiniPill { get; set; }

	public bool TwoIslands { get; set; }

	public string TwoIslandsStyle { get; set; } = "drop";

	public bool TabBadges { get; set; } = true;

	public bool PhoneClipboard { get; set; } = !DeveloperAccess.Enabled;

	public List<string> HiddenTabs { get; set; } = new List<string>();

	public List<string> TabOrder { get; set; } = new List<string>();

	public List<string> QuickActions { get; set; } = new List<string> { "screenshot", "explorer", "downloads", "calc" };

	public string? WeatherCity { get; set; }

	public List<string> Shelf { get; set; } = new List<string>();

	public List<string> Pins { get; set; } = new List<string>();

	public List<string> SeenTips { get; set; } = new List<string>();

	public bool? ClipPersist { get; set; }

	public string? KnownMinVersion { get; set; }

	public DateTime? LastUpdateCheck { get; set; }

	public DateTime? UpdateRemindAt { get; set; }

	public string? LicenseKey { get; set; }

	public static event Action? Changed;

	public static void NotifyChanged()
	{
		Changed?.Invoke();
	}

	public void ResetAll()
	{
		Settings obj = new Settings();
		HashSet<string> hashSet = new HashSet<string> { "LicenseKey", "Shelf", "Pins", "CalendarFeeds", "ClipPersist", "SeenTips", "KnownMinVersion", "LastUpdateCheck", "UpdateRemindAt", "Version" };
		PropertyInfo[] properties = typeof(Settings).GetProperties();
		foreach (PropertyInfo propertyInfo in properties)
		{
			if (propertyInfo.CanRead && propertyInfo.CanWrite && !hashSet.Contains(propertyInfo.Name))
			{
				propertyInfo.SetValue(this, propertyInfo.GetValue(obj));
			}
		}
		Save();
		NotifyChanged();
	}

	public void ResetTips()
	{
		SeenTips.Clear();
		Save();
	}

	public bool IsTabHidden(string id)
	{
		return HiddenTabs.Contains(id);
	}

	private static Settings Load()
	{
		try
		{
			if (File.Exists(FilePath))
			{
				Settings? settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
				if (settings != null)
				{
					Settings settings2 = settings;
					if (settings2.HiddenTabs == null)
					{
						List<string> list = (settings2.HiddenTabs = new List<string>());
					}
					settings2 = settings;
					if (settings2.QuickActions == null)
					{
						List<string> list = (settings2.QuickActions = new List<string>());
					}
					settings2 = settings;
					if (settings2.Shelf == null)
					{
						List<string> list = (settings2.Shelf = new List<string>());
					}
					settings2 = settings;
					if (settings2.Pins == null)
					{
						List<string> list = (settings2.Pins = new List<string>());
					}
					settings2 = settings;
					if (settings2.CalendarFeeds == null)
					{
						List<string> list = (settings2.CalendarFeeds = new List<string>());
					}
					settings2 = settings;
					if (settings2.SeenTips == null)
					{
						List<string> list = (settings2.SeenTips = new List<string>());
					}
					settings2 = settings;
					if (settings2.HideInApps == null)
					{
						List<string> list = (settings2.HideInApps = new List<string>());
					}
					settings2 = settings;
					if (settings2.TabOrder == null)
					{
						List<string> list = (settings2.TabOrder = new List<string>());
					}
					settings2 = settings;
					if (settings2.GameBarItems == null)
					{
						List<string> list = (settings2.GameBarItems = new List<string>());
					}
					if (settings.Version < 2)
					{
						settings.MusicVisual = "pulse";
					}
					if (settings.Version < 3 && Math.Abs(settings.UiScale - 1.0) > 0.001)
					{
						settings.UiScale = Math.Round(settings.UiScale / 1.1, 2);
					}
					if (settings.Version < 4 && settings.HotkeyMode == "hold" && settings.HotkeyVk != 0)
					{
						settings2 = settings;
						Settings settings3 = settings;
						int hotkeyVk = settings.HotkeyVk;
						int hotkeyMods = settings.HotkeyMods;
						settings2.HoldHotkeyVk = hotkeyVk;
						settings3.HoldHotkeyMods = hotkeyMods;
						hotkeyMods = (settings.HotkeyMods = 0);
						settings.HotkeyVk = hotkeyMods;
					}
					if (settings.Version < 5)
					{
						int hotkeyVk;
						if (settings.HotkeyVk == 0 && settings.HoldHotkeyVk != 0)
						{
							Settings settings3 = settings;
							settings2 = settings;
							int hotkeyMods = settings.HoldHotkeyVk;
							hotkeyVk = settings.HoldHotkeyMods;
							settings3.HotkeyVk = hotkeyMods;
							settings2.HotkeyMods = hotkeyVk;
						}
						hotkeyVk = (settings.HoldHotkeyMods = 0);
						settings.HoldHotkeyVk = hotkeyVk;
						if (settings.OpenOnHomeTab)
						{
							settings.OpenTab = "home";
						}
					}
					if (settings.MusicBackdrop == "ripple")
					{
						settings.ExpRipples = true;
					}
					bool flag;
					switch (settings.MusicBackdrop)
					{
					case "blur":
					case "aurora":
					case "none":
						flag = true;
						break;
					default:
						flag = false;
						break;
					}
					if (!flag)
					{
						settings.MusicBackdrop = "blur";
					}
					settings.BackdropStrength = Math.Clamp(settings.BackdropStrength, 0.3, 1.0);
					settings.Version = 5;
					settings.HiddenTabs.Remove("controls");
					if (settings.HiddenTabs.Remove("notes"))
					{
						settings.HiddenTabs.Add("text");
					}
					if (settings.CornerRadius > 1.0)
					{
						settings.CornerRadius = Math.Clamp(settings.CornerRadius / 48.0, 0.1, 1.0);
					}
					settings.UiScale = Math.Clamp(settings.UiScale, 0.75, 1.5);
					settings.UiScaleCollapsed = Math.Clamp(settings.UiScaleCollapsed ?? settings.UiScale, 0.75, 1.5);
					return settings;
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("settings.json unreadable, defaults used", ex);
			try
			{
				File.Copy(FilePath, FilePath + ".broken", overwrite: true);
			}
			catch (Exception)
			{
			}
		}
		return new Settings();
	}

	public string SafeJson(bool indented)
	{
		return Safe().ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = indented
		});
	}

	private JsonObject Safe()
	{
		JsonObject jsonObject = JsonSerializer.SerializeToNode(this).AsObject();
		string[] array = new string[5] { "LicenseKey", "ProxyAddress", "AiCustomUrl", "WeatherCity", "MyBirthday" };
		foreach (string propertyName in array)
		{
			JsonNode? jsonNode = jsonObject[propertyName];
			if (jsonNode != null && !string.IsNullOrEmpty(jsonNode.ToString()))
			{
				jsonObject[propertyName] = "<set>";
			}
		}
		array = new string[4] { "Shelf", "Pins", "CalendarFeeds", "SeenTips" };
		foreach (string propertyName2 in array)
		{
			if (jsonObject[propertyName2] is JsonArray jsonArray)
			{
				jsonObject[propertyName2] = $"<{jsonArray.Count}>";
			}
		}
		return jsonObject;
	}

	public void MarkLogged()
	{
		_logged = Safe().ToJsonString();
	}

	private void LogChanges()
	{
		try
		{
			JsonObject jsonObject = Safe();
			JsonObject? before = ((_logged == null) ? null : JsonNode.Parse(_logged).AsObject());
			_logged = jsonObject.ToJsonString();
			if (before != null)
			{
				List<string> list = (from p in jsonObject
					where before[p.Key]?.ToJsonString() != p.Value?.ToJsonString()
					select $"{p.Key}: {before[p.Key]?.ToJsonString()} -> {p.Value?.ToJsonString()}").ToList();
				if (list.Count > 0)
				{
					Log.Write("settings changed: " + string.Join("; ", list));
				}
			}
		}
		catch (Exception)
		{
		}
	}

	public void Save()
	{
		LogChanges();
		try
		{
			Directory.CreateDirectory(Folder);
			File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			Log.Error("save settings", ex);
		}
	}
}
