using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;

namespace Island.Services;

public static class SystemMonitor
{
	private struct FILETIME
	{
		public uint Low;

		public uint High;

		public ulong Value => ((ulong)High << 32) | Low;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct MemoryStatus
	{
		public uint Length;

		public uint MemoryLoad;

		public ulong TotalPhys;

		public ulong AvailPhys;

		public ulong TotalPageFile;

		public ulong AvailPageFile;

		public ulong TotalVirtual;

		public ulong AvailVirtual;

		public ulong AvailExtendedVirtual;
	}

	private static readonly object Gate = new object();

	private static int _users;

	private static Timer? _timer;

	private static SynchronizationContext? _ctx;

	private static (ulong Idle, ulong Total)? _prevTimes;

	private static (long Up, long Down, long At)? _prevNet;

	private static readonly Dictionary<string, CounterSample> _gpuPrev = new Dictionary<string, CounterSample>();

	private static PerformanceCounter? _diskCounter;

	private static bool _diskFailed;

	private static bool _gpuFailed;

	private static int _tick;

	public static MonitorSnapshot? Latest { get; private set; }

	public static FpsMeter Fps { get; } = new FpsMeter();

	public static event Action<MonitorSnapshot>? Updated;

	public static void Acquire(bool wantFps = false)
	{
		lock (Gate)
		{
			if (_ctx == null)
			{
				_ctx = SynchronizationContext.Current;
			}
			if (_users++ == 0)
			{
				_prevTimes = null;
				_timer = new Timer((object? _) =>
				{
					Tick();
				}, null, 0, 1000);
			}
			if (wantFps)
			{
				Fps.Acquire();
			}
		}
	}

	public static void Release(bool hadFps = false)
	{
		lock (Gate)
		{
			if (hadFps)
			{
				Fps.Release();
			}
			if (--_users <= 0)
			{
				_users = 0;
				_timer?.Dispose();
				_timer = null;
				_gpuPrev.Clear();
				_diskCounter?.Dispose();
				_diskCounter = null;
				_diskFailed = false;
			}
		}
	}

	[DllImport("kernel32.dll")]
	private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

	private static void Tick()
	{
		try
		{
			MonitorSnapshot snap = Read();
			Latest = snap;
			if (_ctx != null)
			{
				_ctx.Post((object? _) =>
				{
					Updated?.Invoke(snap);
				}, null);
			}
			else
			{
				Updated?.Invoke(snap);
			}
		}
		catch (Exception ex)
		{
			Log.Caught("system monitor", ex);
		}
	}

	private static MonitorSnapshot Read()
	{
		_tick++;
		double value = 0.0;
		if (GetSystemTimes(out var idle, out var kernel, out var user))
		{
			ulong num = kernel.Value + user.Value;
			(ulong, ulong)? prevTimes = _prevTimes;
			if (prevTimes.HasValue)
			{
				(ulong, ulong) valueOrDefault = prevTimes.GetValueOrDefault();
				if (num > valueOrDefault.Item2)
				{
					value = 100.0 * (1.0 - (double)(idle.Value - valueOrDefault.Item1) / (double)(num - valueOrDefault.Item2));
				}
			}
			_prevTimes = (idle.Value, num);
		}
		MemoryStatus status = new MemoryStatus
		{
			Length = (uint)Marshal.SizeOf<MemoryStatus>()
		};
		GlobalMemoryStatusEx(ref status);
		double num2 = (double)status.TotalPhys / 1073741824.0;
		double num3 = (double)(status.TotalPhys - status.AvailPhys) / 1073741824.0;
		var (netUpMbps, netDownMbps) = ReadNet();
		return new MonitorSnapshot(Math.Clamp(value, 0.0, 100.0), (num2 > 0.0) ? (100.0 * num3 / num2) : 0.0, num3, num2, ReadGpu(), netUpMbps, netDownMbps, ReadDisk(), Fps.Current);
	}

	private static (double Up, double Down) ReadNet()
	{
		long num = 0L;
		long num2 = 0L;
		NetworkInterface[] allNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
		foreach (NetworkInterface networkInterface in allNetworkInterfaces)
		{
			bool flag = networkInterface.OperationalStatus != OperationalStatus.Up;
			if (!flag)
			{
				NetworkInterfaceType networkInterfaceType = networkInterface.NetworkInterfaceType;
				bool flag2 = ((networkInterfaceType == NetworkInterfaceType.Loopback || networkInterfaceType == NetworkInterfaceType.Tunnel) ? true : false);
				flag = flag2;
			}
			if (!flag)
			{
				try
				{
					IPv4InterfaceStatistics iPv4Statistics = networkInterface.GetIPv4Statistics();
					num += iPv4Statistics.BytesSent;
					num2 += iPv4Statistics.BytesReceived;
				}
				catch (Exception)
				{
				}
			}
		}
		long tickCount = Environment.TickCount64;
		double item = 0.0;
		double item2 = 0.0;
		(long, long, long)? prevNet = _prevNet;
		if (prevNet.HasValue)
		{
			(long, long, long) valueOrDefault = prevNet.GetValueOrDefault();
			if (tickCount > valueOrDefault.Item3 && num >= valueOrDefault.Item1 && num2 >= valueOrDefault.Item2)
			{
				double num3 = (double)(tickCount - valueOrDefault.Item3) / 1000.0;
				item = (double)((num - valueOrDefault.Item1) * 8) / 1000000.0 / num3;
				item2 = (double)((num2 - valueOrDefault.Item2) * 8) / 1000000.0 / num3;
			}
		}
		_prevNet = (num, num2, tickCount);
		return (Up: item, Down: item2);
	}

	private static double ReadGpu()
	{
		if (_gpuFailed)
		{
			return -1.0;
		}
		try
		{
			InstanceDataCollectionCollection instanceDataCollectionCollection = new PerformanceCounterCategory("GPU Engine").ReadCategory();
			if (!instanceDataCollectionCollection.Contains("Utilization Percentage"))
			{
				return -1.0;
			}
			Dictionary<string, CounterSample> dictionary = new Dictionary<string, CounterSample>();
			Dictionary<string, double> dictionary2 = new Dictionary<string, double>();
			foreach (DictionaryEntry item in instanceDataCollectionCollection["Utilization Percentage"])
			{
				if (item.Key is not string text || item.Value is not InstanceData instance)
				{
					continue;
				}
				CounterSample nextCounterSample = (dictionary[text] = instance.Sample);
				if (_gpuPrev.TryGetValue(text, out var value))
				{
					double num = CounterSample.Calculate(value, nextCounterSample);
					int num2 = text.IndexOf("_luid_", StringComparison.Ordinal);
					string key = ((num2 >= 0) ? text.Substring(num2 + 1) : text);
					dictionary2[key] = dictionary2.GetValueOrDefault(key) + num;
				}
			}
			_gpuPrev.Clear();
			foreach (KeyValuePair<string, CounterSample> item2 in dictionary)
			{
				_gpuPrev[item2.Key] = item2.Value;
			}
			return (dictionary2.Count == 0) ? 0.0 : Math.Clamp(dictionary2.Values.Max(), 0.0, 100.0);
		}
		catch (Exception ex)
		{
			_gpuFailed = true;
			Log.Caught("gpu counters", ex);
			return -1.0;
		}
	}

	private static double ReadDisk()
	{
		if (_diskFailed)
		{
			return 0.0;
		}
		try
		{
			if (_diskCounter == null)
			{
				_diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", readOnly: true);
			}
			return Math.Clamp(_diskCounter.NextValue(), 0f, 100f);
		}
		catch (Exception ex)
		{
			_diskFailed = true;
			Log.Caught("disk counter", ex);
			return 0.0;
		}
	}

	public static string Speed(double mbps)
	{
		if (!(mbps >= 100.0))
		{
			if (mbps >= 10.0)
			{
				return $"{mbps:0.#} Мбит/с";
			}
			return $"{mbps:0.0} Мбит/с";
		}
		return $"{mbps:0} Мбит/с";
	}
}
