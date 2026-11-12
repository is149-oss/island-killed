using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Island.Services.Phone;

public sealed class PhoneLinkStore
{
	public static readonly string DefaultPath = Path.Combine(Settings.Folder, "phones.json");

	private static readonly object Gate = new object();

	private string _path = DefaultPath;

	public string PcId { get; set; } = "";

	public List<PhoneDevice> Devices { get; set; } = new List<PhoneDevice>();

	public static PhoneLinkStore Load(string path)
	{
		PhoneLinkStore phoneLinkStore;
		try
		{
			phoneLinkStore = (File.Exists(path) ? (JsonSerializer.Deserialize<PhoneLinkStore>(File.ReadAllText(path)) ?? new PhoneLinkStore()) : new PhoneLinkStore());
		}
		catch (Exception ex)
		{
			Log.Error("load phones", ex);
			phoneLinkStore = new PhoneLinkStore();
		}
		PhoneLinkStore phoneLinkStore2 = phoneLinkStore;
		if (phoneLinkStore2.Devices == null)
		{
			List<PhoneDevice> list = (phoneLinkStore2.Devices = new List<PhoneDevice>());
		}
		phoneLinkStore._path = path;
		if (string.IsNullOrEmpty(phoneLinkStore.PcId))
		{
			phoneLinkStore.PcId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
			phoneLinkStore.Save();
		}
		return phoneLinkStore;
	}

	public void Save()
	{
		try
		{
			lock (Gate)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
				File.WriteAllText(_path, JsonSerializer.Serialize(this, new JsonSerializerOptions
				{
					WriteIndented = true
				}));
			}
		}
		catch (Exception ex)
		{
			Log.Error("save phones", ex);
		}
	}

	public PhoneDevice? Find(string id)
	{
		return Devices.FirstOrDefault((PhoneDevice d) => d.Id == id);
	}

	public static string ProtectKey(byte[] key)
	{
		return Convert.ToBase64String(AiKeyStore.Protect(key));
	}

	public static byte[]? UnprotectKey(PhoneDevice device)
	{
		try
		{
			return AiKeyStore.Unprotect(Convert.FromBase64String(device.Key));
		}
		catch (Exception ex)
		{
			Log.Error("phone key", ex);
			return null;
		}
	}
}
