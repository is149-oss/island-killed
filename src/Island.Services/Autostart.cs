using System;
using Microsoft.Win32;

namespace Island.Services;

public static class Autostart
{
	private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string Name = "Island";

	public static bool IsEnabled
	{
		get
		{
			using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			return registryKey?.GetValue("Island") is string;
		}
	}

	public static void Set(bool enabled)
	{
		if (ReviewSafety.Enabled) return;
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			if (enabled)
			{
				registryKey.SetValue("Island", "\"" + Environment.ProcessPath + "\"");
			}
			else
			{
				registryKey.DeleteValue("Island", throwOnMissingValue: false);
			}
		}
		catch (Exception ex)
		{
			Log.Error("autostart", ex);
		}
	}
}
