using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Island.Services;

public static class GameDetector
{
	private struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct MONITORINFO
	{
		public int Size;

		public RECT Monitor;

		public RECT Work;

		public uint Flags;
	}

	private static readonly string[] NotGames = new string[34]
	{
		"explorer", "chrome", "msedge", "firefox", "opera", "browser", "vivaldi", "brave", "iexplore", "vlc",
		"mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "potplayer", "potplayermini", "potplayermini64", "wmplayer", "mpv", "kmplayer",
		"powerpnt", "teams", "zoom", "telegram", "applicationframehost", "searchhost", "lockapp", "shellexperiencehost", "island", "devenv",
		"code", "claude", "obs64", "streamlabs obs"
	};

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(nint hWnd, out RECT rect);

	[DllImport("user32.dll")]
	private static extern nint MonitorFromWindow(nint hWnd, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint hWnd, StringBuilder text, int count);

	[DllImport("shell32.dll")]
	private static extern int SHQueryUserNotificationState(out int state);

	public static bool IsGameInFront(out string? process)
	{
		process = null;
		try
		{
			nint foregroundWindow = GetForegroundWindow();
			if (foregroundWindow == IntPtr.Zero)
			{
				return false;
			}
			GetWindowThreadProcessId(foregroundWindow, out var pid);
			if (pid == 0 || pid == Environment.ProcessId)
			{
				return false;
			}
			string processName = Process.GetProcessById((int)pid).ProcessName;
			StringBuilder stringBuilder = new StringBuilder(64);
			GetClassName(foregroundWindow, stringBuilder, stringBuilder.Capacity);
			bool flag;
			switch (stringBuilder.ToString())
			{
			case "Progman":
			case "WorkerW":
			case "Shell_TrayWnd":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				return false;
			}
			if (SHQueryUserNotificationState(out var state) == 0 && state == 3)
			{
				process = processName;
				return true;
			}
			if (NotGames.Contains(processName.ToLowerInvariant()))
			{
				return false;
			}
			if (!GetWindowRect(foregroundWindow, out var rect))
			{
				return false;
			}
			MONITORINFO info = new MONITORINFO
			{
				Size = Marshal.SizeOf<MONITORINFO>()
			};
			if (!GetMonitorInfo(MonitorFromWindow(foregroundWindow, 2u), ref info))
			{
				return false;
			}
			int num;
			if (rect.Left <= info.Monitor.Left && rect.Top <= info.Monitor.Top && rect.Right >= info.Monitor.Right)
			{
				num = ((rect.Bottom >= info.Monitor.Bottom) ? 1 : 0);
				if (num != 0)
				{
					process = processName;
				}
			}
			else
			{
				num = 0;
			}
			return (byte)num != 0;
		}
		catch (Exception)
		{
			return false;
		}
	}
}
