using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Island.Services;

public static class License
{
	public const string BotName = "dislandbot";

	private const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEEfGPwefNd80Qjil5Gecvs3bllljz+VCmTBRrKejCWVRWP6w7dplFGbKBZRKqrnClcfeg/MBDmczxTEntFNB0Fg==";

	private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

	private static readonly DateTime Epoch = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	private static readonly string KeyFile = Path.Combine(Settings.Folder, "license.key");

	public static readonly TimeSpan TrialLength = TimeSpan.FromDays(7);

	private const string TrialRegKey = "Software\\Island";

	private static readonly string TrialFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Island", "first-run");

	public static readonly DateTime TrialStart = DeveloperAccess.Enabled ? DateTime.MinValue : LoadTrialStart();

	private static DateTime? _expires;

	private static DispatcherTimer? _expiryTimer;

	private static bool _valid = !DeveloperAccess.Enabled && Load();

	private static long _gateAt;

	private static bool _gate;

	public static string? BotUrl
	{
		get
		{
			if ("dislandbot".Length != 0)
			{
				return "https://t.me/dislandbot";
			}
			return null;
		}
	}

	public static string? BuyUrl
	{
		get
		{
			if (BotUrl != null)
			{
				return BotUrl + "?start=hw_" + DeviceId.Code.Replace("-", "");
			}
			return null;
		}
	}

	public static DateTime TrialEnds => TrialStart + TrialLength;

	public static bool TrialJustStarted { get; private set; }

	private static bool KeyActive
	{
		get
		{
			if (_valid)
			{
				if (_expires.HasValue)
				{
					DateTime utcNow = DateTime.UtcNow;
					DateTime? expires = _expires;
					return utcNow < expires;
				}
				return true;
			}
			return false;
		}
	}

	public static bool IsTrial
	{
		get
		{
			if (DeveloperAccess.Enabled) return false;
			if (!KeyActive)
			{
				return DateTime.UtcNow < TrialEnds;
			}
			return false;
		}
	}

	public static bool IsPro
	{
		get
		{
			if (DeveloperAccess.Enabled) return true;
			if (Integrity.SelfIntact)
			{
				if (!KeyActive)
				{
					return IsTrial;
				}
				return true;
			}
			return false;
		}
	}

	public static DateTime? Expires
	{
		get
		{
			if (DeveloperAccess.Enabled) return null;
			if (!KeyActive)
			{
				if (!(DateTime.UtcNow < TrialEnds))
				{
					return _expires;
				}
				return TrialEnds;
			}
			return _expires;
		}
	}

	public static bool HasKey => KeyActive;

	public static event Action? Ended;

	private static DateTime LoadTrialStart()
	{
		DateTime? dateTime = null;
		DateTime? dateTime2 = null;
		try
		{
			using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Software\\Island");
			if (registryKey?.GetValue("FirstRun") is long ticks)
			{
				dateTime = new DateTime(ticks, DateTimeKind.Utc);
			}
		}
		catch (Exception ex)
		{
			Log.Error("trial reg", ex);
		}
		try
		{
			if (File.Exists(TrialFile) && long.TryParse(File.ReadAllText(TrialFile).Trim(), out var result))
			{
				dateTime2 = new DateTime(result, DateTimeKind.Utc);
			}
		}
		catch (Exception ex2)
		{
			Log.Error("trial file", ex2);
		}
		DateTime? dateTime3;
		if (dateTime.HasValue)
		{
			DateTime valueOrDefault = dateTime.GetValueOrDefault();
			if (dateTime2.HasValue)
			{
				DateTime valueOrDefault2 = dateTime2.GetValueOrDefault();
				dateTime3 = ((valueOrDefault < valueOrDefault2) ? valueOrDefault : valueOrDefault2);
				goto IL_0107;
			}
		}
		dateTime3 = dateTime ?? dateTime2;
		goto IL_0107;
		IL_0107:
		DateTime? dateTime4 = dateTime3;
		if (!dateTime4.HasValue)
		{
			TrialJustStarted = true;
		}
		DateTime dateTime5;
		if (dateTime4.HasValue)
		{
			DateTime valueOrDefault3 = dateTime4.GetValueOrDefault();
			if (valueOrDefault3 <= DateTime.UtcNow)
			{
				dateTime5 = valueOrDefault3;
				goto IL_0156;
			}
		}
		dateTime5 = ((!dateTime4.HasValue) ? DateTime.UtcNow : DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
		goto IL_0156;
		IL_0156:
		DateTime result2 = dateTime5;
		try
		{
			using RegistryKey registryKey2 = Registry.CurrentUser.CreateSubKey("Software\\Island");
			registryKey2.SetValue("FirstRun", result2.Ticks, RegistryValueKind.QWord);
			Directory.CreateDirectory(Path.GetDirectoryName(TrialFile));
			File.WriteAllText(TrialFile, result2.Ticks.ToString());
		}
		catch (Exception ex3)
		{
			Log.Error("trial save", ex3);
		}
		return result2;
	}

	private static bool Load()
	{
		string? text = null;
		try
		{
			if (File.Exists(KeyFile))
			{
				text = File.ReadAllText(KeyFile).Trim();
			}
		}
		catch (Exception ex)
		{
			Log.Error("read license", ex);
		}
		string[] array = new string[2]
		{
			text,
			Settings.Current.LicenseKey
		};
		foreach (string text2 in array)
		{
			if (Check(text2, out var expires) == null)
			{
				_expires = expires;
				_valid = true;
				Store(Normalize(text2));
				WatchExpiry();
				return true;
			}
		}
		WatchExpiry();
		return false;
	}

	public static void WatchExpiry()
	{
		_expiryTimer?.Stop();
		if (DeveloperAccess.Enabled) return;
		DateTime? expires = Expires;
		if (!expires.HasValue)
		{
			return;
		}
		DateTime valueOrDefault = expires.GetValueOrDefault();
		if (valueOrDefault <= DateTime.UtcNow)
		{
			return;
		}
		TimeSpan timeSpan = valueOrDefault - DateTime.UtcNow;
		_expiryTimer = new DispatcherTimer
		{
			Interval = ((timeSpan < TimeSpan.FromDays(1)) ? (timeSpan + TimeSpan.FromSeconds(1L)) : TimeSpan.FromDays(1))
		};
		_expiryTimer.Tick += (object? _, EventArgs _) =>
		{
			_expiryTimer.Stop();
			if (IsPro)
			{
				WatchExpiry();
			}
			else
			{
				Settings.NotifyChanged();
				Ended?.Invoke();
			}
		};
		_expiryTimer.Start();
	}

	private static void Store(string? key)
	{
		try
		{
			if (key == null)
			{
				File.Delete(KeyFile);
			}
			else if (!File.Exists(KeyFile) || File.ReadAllText(KeyFile).Trim() != key)
			{
				Directory.CreateDirectory(Settings.Folder);
				File.WriteAllText(KeyFile, key);
			}
		}
		catch (Exception ex)
		{
			Log.Error("save license", ex);
		}
		if (!(Settings.Current.LicenseKey == key))
		{
			Settings.Current.LicenseKey = key;
			Settings.Current.Save();
		}
	}

	public static bool Gate()
	{
		if (DeveloperAccess.Enabled) return true;
		long tickCount = Environment.TickCount64;
		if (_gateAt != 0L && tickCount - _gateAt < 60000)
		{
			return _gate;
		}
		bool flag;
		try
		{
			flag = Integrity.SelfIntact && (KeyStillGood() || TrialStillOn());
		}
		catch (Exception ex)
		{
			Log.Error("license gate", ex);
			flag = IsPro;
		}
		_gate = flag;
		_gateAt = ((tickCount == 0L) ? 1 : tickCount);
		return flag;
	}

	private static bool KeyStillGood()
	{
		string? text = null;
		try
		{
			if (File.Exists(KeyFile))
			{
				text = File.ReadAllText(KeyFile).Trim();
			}
		}
		catch (IOException)
		{
		}
		string[] array = new string[2]
		{
			text,
			Settings.Current.LicenseKey
		};
		foreach (string text2 in array)
		{
			if (!string.IsNullOrWhiteSpace(text2) && Check(text2, out var _) == null)
			{
				return true;
			}
		}
		return false;
	}

	private static bool TrialStillOn()
	{
		DateTime? dateTime = null;
		try
		{
			using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Software\\Island");
			if (registryKey?.GetValue("FirstRun") is long ticks)
			{
				dateTime = new DateTime(ticks, DateTimeKind.Utc);
			}
		}
		catch (Exception)
		{
		}
		try
		{
			DateTime value;
			if (File.Exists(TrialFile) && long.TryParse(File.ReadAllText(TrialFile).Trim(), out var result))
			{
				DateTime dateTime2 = new DateTime(result, DateTimeKind.Utc);
				if (dateTime.HasValue)
				{
					DateTime valueOrDefault = dateTime.GetValueOrDefault();
					if (valueOrDefault < dateTime2)
					{
						value = valueOrDefault;
						goto IL_00ac;
					}
				}
				value = dateTime2;
				goto IL_00ac;
			}
			goto end_IL_005b;
			IL_00ac:
			dateTime = value;
			end_IL_005b:;
		}
		catch (IOException)
		{
		}
		DateTime dateTime3 = dateTime ?? TrialStart;
		if (dateTime3 <= DateTime.UtcNow)
		{
			return DateTime.UtcNow < dateTime3 + TrialLength;
		}
		return false;
	}

	public static string? Activate(string text)
	{
		string? text2 = Check(text, out var expires);
		if (text2 != null)
		{
			Log.Write("licence: key refused (" + text2 + ")");
			return text2;
		}
		Log.Write("licence: key accepted, expires " + (expires?.ToString("u") ?? "never"));
		_valid = true;
		_expires = expires;
		Store(Normalize(text));
		WatchExpiry();
		Settings.NotifyChanged();
		return null;
	}

	public static void Deactivate()
	{
		Log.Write("licence: key removed");
		_valid = false;
		_expires = null;
		Store(null);
		WatchExpiry();
		Settings.NotifyChanged();
	}

	private static string? Check(string? text, out DateTime? expires)
	{
		expires = null;
		if (string.IsNullOrWhiteSpace(text))
		{
			return "Введите ключ";
		}
		byte[]? array = Decode(Normalize(text));
		int num = ((array != null && array.Length > 0 && array[0] == 3) ? 14 : 9);
		bool flag = array == null || array.Length != num + 64;
		if (!flag)
		{
			byte b = array[0];
			bool flag2 = (uint)(b - 1) <= 2u;
			flag = !flag2;
		}
		if (flag)
		{
			return "Это не похоже на ключ Island — проверьте, что скопировали его целиком";
		}
		try
		{
			using ECDsa eCDsa = ECDsa.Create();
			eCDsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String("MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEEfGPwefNd80Qjil5Gecvs3bllljz+VCmTBRrKejCWVRWP6w7dplFGbKBZRKqrnClcfeg/MBDmczxTEntFNB0Fg=="), out var _);
			if (!eCDsa.VerifyData(array.AsSpan(0, num), array.AsSpan(num), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
			{
				return "Ключ недействителен";
			}
		}
		catch (CryptographicException ex)
		{
			Log.Error("license check", ex);
			return "Не удалось проверить ключ";
		}
		if (array[0] == 1)
		{
			int num2 = (array[7] << 8) | array[8];
			if (num2 > 0)
			{
				expires = Epoch.AddDays(num2);
			}
		}
		else
		{
			uint num3 = (uint)((array[5] << 24) | (array[6] << 16) | (array[7] << 8) | array[8]);
			if (num3 != 0)
			{
				expires = Epoch.AddMinutes(num3);
			}
		}
		if (array[0] == 3 && !((ReadOnlySpan<byte>)array.AsSpan(9, 5)).SequenceEqual((ReadOnlySpan<byte>)DeviceId.Hash))
		{
			return "Этот ключ выдан для другого компьютера. Код этого — " + DeviceId.Code + ": перенесите ключ на него в боте, «Мои ключи» → «Перенести на другой компьютер»";
		}
		DateTime utcNow = DateTime.UtcNow;
		DateTime? dateTime = expires;
		if (utcNow >= dateTime)
		{
			return $"Срок ключа истёк {expires.Value.ToLocalTime():dd.MM.yyyy HH:mm}";
		}
		return null;
	}

	private static string Normalize(string text)
	{
		List<char> list = new List<char>();
		string text2 = text.ToUpperInvariant();
		foreach (char c in text2)
		{
			if (char.IsLetterOrDigit(c))
			{
				list.Add(c);
			}
		}
		string text3 = new string(list.ToArray());
		if (!text3.StartsWith("ISLAND"))
		{
			return text3;
		}
		return text3.Substring(6);
	}

	private static byte[]? Decode(string text)
	{
		List<byte> list = new List<byte>();
		int num = 0;
		int num2 = 0;
		foreach (char value in text)
		{
			int num3 = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".IndexOf(value);
			if (num3 < 0)
			{
				return null;
			}
			num = (num << 5) | num3;
			num2 += 5;
			if (num2 >= 8)
			{
				list.Add((byte)(num >> num2 - 8));
				num2 -= 8;
			}
		}
		return list.ToArray();
	}
}
