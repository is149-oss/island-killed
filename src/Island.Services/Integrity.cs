using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Island.Services;

public static class Integrity
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct FileInfo
	{
		public uint Size;

		public string FilePath;

		public nint File;

		public nint KnownSubject;
	}

	private struct TrustData
	{
		public uint Size;

		public nint PolicyCallbackData;

		public nint SipClientData;

		public uint UiChoice;

		public uint RevocationChecks;

		public uint UnionChoice;

		public nint FileInfo;

		public uint StateAction;

		public nint StateData;

		public nint UrlReference;

		public uint ProvFlags;

		public uint UiContext;

		public nint SignatureSettings;
	}

	private const string SignerThumbprint = "";

	public static readonly bool SelfIntact;

	private static readonly Guid ActionGenericVerify;

	public static bool Enforced => "".Length > 0;

	public static bool IsGenuine(string path)
	{
		if (!Enforced)
		{
			return false;
		}
		try
		{
			if (!File.Exists(path) || !SignatureValid(path))
			{
				return false;
			}
			using X509Certificate2 x509Certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
			return string.Equals(x509Certificate.Thumbprint, "", StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception ex)
		{
			Log.Error("integrity", ex);
			return false;
		}
	}

	[DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	private static extern int WinVerifyTrust(nint hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref TrustData data);

	private static bool SignatureValid(string path)
	{
		FileInfo structure = new FileInfo
		{
			Size = (uint)Marshal.SizeOf<FileInfo>(),
			FilePath = path
		};
		nint num = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
		try
		{
			Marshal.StructureToPtr(structure, num, fDeleteOld: false);
			TrustData data = new TrustData
			{
				Size = (uint)Marshal.SizeOf<TrustData>(),
				UiChoice = 2u,
				RevocationChecks = 0u,
				UnionChoice = 1u,
				FileInfo = num,
				StateAction = 0u,
				ProvFlags = 4112u
			};
			return WinVerifyTrust(IntPtr.Zero, ActionGenericVerify, ref data) == 0;
		}
		finally
		{
			Marshal.FreeHGlobal(num);
		}
	}

	static Integrity()
	{
		ActionGenericVerify = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
		int selfIntact;
		if (Enforced)
		{
			string? processPath = Environment.ProcessPath;
			if (processPath != null)
			{
				selfIntact = (IsGenuine(processPath) ? 1 : 0);
				goto IL_0019;
			}
		}
		selfIntact = 1;
		goto IL_0019;
		IL_0019:
		SelfIntact = (byte)selfIntact != 0;
		ActionGenericVerify = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
	}
}
