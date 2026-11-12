using System;
using System.Runtime.InteropServices;

namespace Island.Native;

internal static class Win32
{
	public struct POINT
	{
		public int X;

		public int Y;
	}

	public const int GWL_EXSTYLE = -20;

	public const long WS_EX_TOOLWINDOW = 128L;

	public const long WS_EX_APPWINDOW = 262144L;

	public const long WS_EX_NOACTIVATE = 134217728L;

	public const int WM_CLIPBOARDUPDATE = 797;

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

	[DllImport("shell32.dll")]
	private static extern int SHQueryUserNotificationState(out int state);

	public static bool IsFullscreenAppRunning()
	{
		if (SHQueryUserNotificationState(out var state) != 0)
		{
			return false;
		}
		if ((uint)(state - 2) <= 2u)
		{
			return true;
		}
		return false;
	}

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool AddClipboardFormatListener(nint hwnd);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool RemoveClipboardFormatListener(nint hwnd);

	[DllImport("user32.dll")]
	private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

	public static void SendCtrlV()
	{
		keybd_event(17, 0, 0u, UIntPtr.Zero);
		keybd_event(86, 0, 0u, UIntPtr.Zero);
		keybd_event(86, 0, 2u, UIntPtr.Zero);
		keybd_event(17, 0, 2u, UIntPtr.Zero);
	}

	[DllImport("user32.dll")]
	public static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	public static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll")]
	public static extern bool LockWorkStation();

	[DllImport("user32.dll")]
	public static extern bool GetCursorPos(out POINT pt);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern nint SendMessageTimeout(nint hWnd, int msg, nint wParam, string lParam, int flags, int timeout, out nint result);

	public static void BroadcastSettingChange(string area)
	{
		SendMessageTimeout(65535, 26, IntPtr.Zero, area, 2, 100, out var _);
	}

	public static void SetNoActivate(nint hwnd, bool on)
	{
		long num = GetWindowLongPtr(hwnd, -20);
		num = (on ? (num | 0x8000000) : (num & -134217729));
		SetWindowLongPtr(hwnd, -20, (nint)num);
	}

	[DllImport("powrprof.dll")]
	private static extern bool GetPwrCapabilities([Out] byte[] capabilities);

	public static bool CanSleep()
	{
		try
		{
			byte[] array = new byte[256];
			if (!GetPwrCapabilities(array))
			{
				return false;
			}
			return array[3] != 0 || array[4] != 0 || array[5] != 0 || array[20] != 0;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static bool CanHibernate()
	{
		try
		{
			byte[] array = new byte[256];
			return GetPwrCapabilities(array) && array[6] != 0 && array[8] != 0;
		}
		catch (Exception)
		{
			return false;
		}
	}

	[DllImport("user32.dll")]
	private static extern nint GetDC(nint hWnd);

	[DllImport("user32.dll")]
	private static extern int ReleaseDC(nint hWnd, nint hdc);

	[DllImport("gdi32.dll")]
	private static extern int GetDeviceCaps(nint hdc, int index);

	public static int GetRefreshRate()
	{
		nint dC = GetDC(IntPtr.Zero);
		try
		{
			int deviceCaps = GetDeviceCaps(dC, 116);
			return (deviceCaps > 1) ? deviceCaps : 60;
		}
		finally
		{
			ReleaseDC(IntPtr.Zero, dC);
		}
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

	public static void UseDarkTitleBar(nint hwnd)
	{
		int value = 1;
		DwmSetWindowAttribute(hwnd, 20, ref value, 4);
	}

	public static void MakeOverlay(nint hwnd)
	{
		long num = GetWindowLongPtr(hwnd, -20);
		num |= 0x8000080;
		num &= -262145;
		SetWindowLongPtr(hwnd, -20, (nint)num);
	}
}
