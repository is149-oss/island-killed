using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Island.Services.Phone;

public sealed class PhoneNotification
{
	public string DeviceId { get; init; } = "";

	public string DeviceName { get; init; } = "";

	public string Key { get; init; } = "";

	public string Package { get; init; } = "";

	public string AppName { get; init; } = "";

	public byte[]? AppIcon { get; init; }

	public byte[]? Avatar { get; init; }

	public DateTime Time { get; init; }

	public string Title { get; init; } = "";

	public string Text { get; init; } = "";

	public string? BigText { get; init; }

	public string? SubText { get; init; }

	public string? Category { get; init; }

	public bool Silent { get; init; }

	public bool Ongoing { get; init; }

	public bool IsUpdate { get; init; }

	public bool IsSync { get; init; }

	public IReadOnlyList<PhoneMessage> Messages { get; init; } = Array.Empty<PhoneMessage>();

	public IReadOnlyList<PhoneAction> Actions { get; init; } = Array.Empty<PhoneAction>();

	internal static PhoneNotification Parse(JsonElement j, PhoneDevice device, string appName, byte[]? appIcon)
	{
		List<PhoneMessage> list = new List<PhoneMessage>();
		if (j.TryGetProperty("messages", out var value) && value.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in value.EnumerateArray())
			{
				list.Add(new PhoneMessage(Str(item, "from") ?? "", Str(item, "text") ?? "", UnixTime(item)));
			}
		}
		List<PhoneAction> list2 = new List<PhoneAction>();
		if (j.TryGetProperty("actions", out var value2) && value2.ValueKind == JsonValueKind.Array)
		{
			int num = 0;
			foreach (JsonElement item2 in value2.EnumerateArray())
			{
				list2.Add(new PhoneAction(num++, Str(item2, "title") ?? "", Bool(item2, "reply")));
			}
		}
		return new PhoneNotification
		{
			DeviceId = device.Id,
			DeviceName = device.Name,
			Key = (Str(j, "key") ?? ""),
			Package = (Str(j, "pkg") ?? ""),
			AppName = appName,
			AppIcon = appIcon,
			Avatar = Bytes(Str(j, "avatar")),
			Time = UnixTime(j),
			Title = (Str(j, "title") ?? ""),
			Text = (Str(j, "text") ?? ""),
			BigText = Str(j, "big"),
			SubText = Str(j, "sub"),
			Category = Str(j, "category"),
			Silent = Bool(j, "silent"),
			Ongoing = Bool(j, "ongoing"),
			IsUpdate = Bool(j, "update"),
			IsSync = Bool(j, "sync"),
			Messages = list,
			Actions = list2
		};
	}

	internal static string? Str(JsonElement j, string name)
	{
		if (!j.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static bool Bool(JsonElement j, string name)
	{
		if (j.TryGetProperty(name, out var value))
		{
			return value.ValueKind == JsonValueKind.True;
		}
		return false;
	}

	private static DateTime UnixTime(JsonElement j)
	{
		if (!j.TryGetProperty("time", out var value) || !value.TryGetInt64(out var value2) || value2 <= 0)
		{
			return DateTime.Now;
		}
		return DateTimeOffset.FromUnixTimeMilliseconds(value2).LocalDateTime;
	}

	internal static byte[]? Bytes(string? base64)
	{
		if (string.IsNullOrEmpty(base64))
		{
			return null;
		}
		try
		{
			return Convert.FromBase64String(base64);
		}
		catch (FormatException)
		{
			return null;
		}
	}
}
