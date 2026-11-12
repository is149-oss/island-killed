using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island.Native;

internal static class ShellIcons
{
	[ComImport]
	[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IShellItemImageFactory
	{
		[PreserveSig]
		int GetImage(SIZE size, int flags, out nint phbm);
	}

	private struct SIZE
	{
		public int cx;

		public int cy;
	}

	private struct BITMAP
	{
		public int bmType;

		public int bmWidth;

		public int bmHeight;

		public int bmWidthBytes;

		public ushort bmPlanes;

		public ushort bmBitsPixel;

		public nint bmBits;
	}

	private struct BITMAPINFOHEADER
	{
		public uint biSize;

		public int biWidth;

		public int biHeight;

		public ushort biPlanes;

		public ushort biBitCount;

		public uint biCompression;

		public uint biSizeImage;

		public int biXPelsPerMeter;

		public int biYPelsPerMeter;

		public uint biClrUsed;

		public uint biClrImportant;
	}

	private struct DIBSECTION
	{
		public BITMAP dsBm;

		public BITMAPINFOHEADER dsBmih;

		public uint dsBitfield0;

		public uint dsBitfield1;

		public uint dsBitfield2;

		public nint dshSection;

		public uint dsOffset;
	}

	private static readonly Dictionary<(string, int, bool), ImageSource?> Cache = new Dictionary<(string, int, bool), ImageSource>();

	private const int SIIGBF_ICONONLY = 4;

	public static ImageSource? Get(string path, int size, bool iconOnly)
	{
		(string, int, bool) key = (path.ToLowerInvariant(), size, iconOnly);
		if (Cache.TryGetValue(key, out ImageSource? value))
		{
			return value;
		}
		ImageSource? imageSource = Load(path, size, iconOnly);
		Cache[key] = imageSource;
		return imageSource;
	}

	private static ImageSource? Load(string path, int size, bool iconOnly)
	{
		try
		{
			SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out IShellItemImageFactory factory);
			int image;
			nint phbm;
			try
			{
				image = factory.GetImage(new SIZE
				{
					cx = size,
					cy = size
				}, iconOnly ? 4 : 0, out phbm);
			}
			finally
			{
				Marshal.ReleaseComObject(factory);
			}
			if (image != 0 || phbm == IntPtr.Zero)
			{
				return null;
			}
			try
			{
				DIBSECTION ds = default;
				if (GetObject(phbm, Marshal.SizeOf<DIBSECTION>(), ref ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32)
				{
					return null;
				}
				int bmWidth = ds.dsBm.bmWidth;
				int bmHeight = ds.dsBm.bmHeight;
				int bmWidthBytes = ds.dsBm.bmWidthBytes;
				BitmapSource bitmapSource = BitmapSource.Create(bmWidth, bmHeight, 96.0, 96.0, PixelFormats.Pbgra32, null, ds.dsBm.bmBits, bmWidthBytes * bmHeight, bmWidthBytes);
				if (ds.dsBmih.biHeight > 0)
				{
					bitmapSource = new TransformedBitmap(bitmapSource, new ScaleTransform(1.0, -1.0));
				}
				bitmapSource.Freeze();
				return bitmapSource;
			}
			finally
			{
				DeleteObject(phbm);
			}
		}
		catch (Exception)
		{
			return null;
		}
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
	private static extern void SHCreateItemFromParsingName(string path, nint pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory factory);

	[DllImport("gdi32.dll")]
	private static extern int GetObject(nint h, int c, ref DIBSECTION ds);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(nint h);
}
