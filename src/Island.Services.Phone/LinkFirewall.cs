using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Island.Services.Phone;

public static class LinkFirewall
{
	private const string RuleName = "Island";

	private static readonly string[] OldNames = new string[1] { "Island Link" };

	private static string ExePath => Environment.ProcessPath ?? "";

	public static Task<bool?> IsAllowedAsync()
	{
		return Task.Run(() =>
		{
			try
			{
				Type? typeFromProgID = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
				if (typeFromProgID == null)
				{
					return (bool?)null;
				}
				dynamic? val = Activator.CreateInstance(typeFromProgID);
				foreach (dynamic item in val.Rules)
				{
					if (!((string)item.Name != "Island") && (bool)item.Enabled && (int)item.Direction == 1 && (int)item.Action == 1 && string.Equals((string)item.ApplicationName, ExePath, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
				return false;
			}
			catch (Exception ex)
			{
				Log.Error("firewall check", ex);
				return (bool?)null;
			}
		});
	}

	public static async Task<bool> AllowAsync()
	{
		string text = "netsh advfirewall firewall delete rule name=\"Island\"";
		string[] oldNames = OldNames;
		foreach (string text2 in oldNames)
		{
			text = text + " & netsh advfirewall firewall delete rule name=\"" + text2 + "\"";
		}
		text += $" & netsh advfirewall firewall add rule name=\"{"Island"}\" dir=in action=allow enable=yes profile=any remoteip=localsubnet program=\"{ExePath}\" description=\"Island: phone link on the local network\"";
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo("cmd.exe", "/c " + text)
			{
				UseShellExecute = true,
				Verb = "runas",
				WindowStyle = ProcessWindowStyle.Hidden
			};
			using Process? process = ReviewSafety.Start(startInfo);
			if (process == null)
			{
				return false;
			}
			await process.WaitForExitAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Win32Exception)
		{
			return false;
		}
		catch (Exception ex2)
		{
			Log.Error("firewall add", ex2);
			return false;
		}
		bool valueOrDefault = await IsAllowedAsync().ConfigureAwait(continueOnCapturedContext: false) == true;
		Log.Write(valueOrDefault ? "firewall: Island allowed" : "firewall: rule not added");
		return valueOrDefault;
	}
}
