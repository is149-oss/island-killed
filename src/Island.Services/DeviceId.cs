using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Island.Services;

public static class DeviceId
{
	private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

	public static readonly byte[] Hash = Compute();

	public static string Code { get; } = Format(Hash);

	private static byte[] Compute()
	{
		string text2;
		try
		{
			string? text = BoardUuid();
			text2 = ((text != null) ? ("board:" + text) : ("windows:" + WindowsId()));
		}
		catch (Exception ex)
		{
			Log.Error("device id", ex);
			text2 = "windows:" + WindowsId();
		}
		return SHA256.HashData(Encoding.UTF8.GetBytes("Island device|" + text2))[..5];
	}

	private static string Format(byte[] bytes)
	{
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		int num2 = 0;
		foreach (byte b in bytes)
		{
			num = (num << 8) | b;
			for (num2 += 8; num2 >= 5; num2 -= 5)
			{
				stringBuilder.Append("ABCDEFGHJKLMNPQRSTUVWXYZ23456789"[(num >> num2 - 5) & 0x1F]);
			}
		}
		return stringBuilder.ToString(0, 4) + "-" + stringBuilder.ToString(4, 4);
	}

	private static string? BoardUuid()
	{
		uint systemFirmwareTable = GetSystemFirmwareTable(1381190978u, 0u, null, 0u);
		if (systemFirmwareTable == 0)
		{
			return null;
		}
		byte[] array = new byte[systemFirmwareTable];
		if (GetSystemFirmwareTable(1381190978u, 0u, array, systemFirmwareTable) != systemFirmwareTable)
		{
			return null;
		}
		int num = BitConverter.ToInt32(array, 4);
		int num2 = 8;
		int num3 = Math.Min(array.Length, 8 + num);
		while (num2 + 4 <= num3)
		{
			byte b = array[num2];
			byte b2 = array[num2 + 1];
			if (b2 < 4)
			{
				break;
			}
			if (b == 1 && b2 >= 25 && num2 + 24 <= num3)
			{
				Span<byte> span = array.AsSpan(num2 + 8, 16);
				bool flag = ((ReadOnlySpan<byte>)span).IndexOfAnyExcept((byte)0) < 0;
				bool flag2 = ((ReadOnlySpan<byte>)span).IndexOfAnyExcept(byte.MaxValue) < 0;
				if (!(flag | flag2))
				{
					return Convert.ToHexString(span);
				}
				return null;
			}
			if (b == 127)
			{
				break;
			}
			int i;
			for (i = num2 + b2; i + 1 < num3 && (array[i] != 0 || array[i + 1] != 0); i++)
			{
			}
			num2 = i + 2;
		}
		return null;
	}

	private static string WindowsId()
	{
		try
		{
			using RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
			using RegistryKey? registryKey2 = registryKey.OpenSubKey("SOFTWARE\\Microsoft\\Cryptography");
			return registryKey2?.GetValue("MachineGuid")?.ToString() ?? Environment.MachineName;
		}
		catch (Exception ex)
		{
			Log.Error("machine guid", ex);
			return Environment.MachineName;
		}
	}

	[DllImport("kernel32.dll")]
	private static extern uint GetSystemFirmwareTable(uint provider, uint table, byte[]? buffer, uint size);
}
