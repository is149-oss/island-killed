using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Island.Services;

public static class AiKeyStore
{
	private struct Blob
	{
		public int Size;

		public nint Data;
	}

	private static string PathFor(AiProvider p)
	{
		return Path.Combine(Settings.Folder, "ai-" + p.Id + ".key");
	}

	public static string? Load(AiProvider p)
	{
		try
		{
			string path = PathFor(p);
			return File.Exists(path) ? Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(path))) : null;
		}
		catch (Exception ex)
		{
			Log.Error("load ai key", ex);
			return null;
		}
	}

	public static void Save(AiProvider p, string? key)
	{
		try
		{
			string path = PathFor(p);
			if (string.IsNullOrWhiteSpace(key))
			{
				File.Delete(path);
				return;
			}
			Directory.CreateDirectory(Settings.Folder);
			File.WriteAllBytes(path, Protect(Encoding.UTF8.GetBytes(key.Trim())));
		}
		catch (Exception ex)
		{
			Log.Error("save ai key", ex);
		}
	}

	[DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);

	[DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);

	[DllImport("kernel32.dll")]
	private static extern nint LocalFree(nint mem);

	internal static byte[] Protect(byte[] data)
	{
		return Run(data, protect: true);
	}

	internal static byte[] Unprotect(byte[] data)
	{
		return Run(data, protect: false);
	}

	private static byte[] Run(byte[] data, bool protect)
	{
		GCHandle gCHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
		try
		{
			Blob input = new Blob
			{
				Size = data.Length,
				Data = gCHandle.AddrOfPinnedObject()
			};
			if (!(protect ? CryptProtectData(ref input, "Island AI key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			try
			{
				byte[] array = new byte[output.Size];
				Marshal.Copy(output.Data, array, 0, output.Size);
				return array;
			}
			finally
			{
				LocalFree(output.Data);
			}
		}
		finally
		{
			gCHandle.Free();
		}
	}
}
