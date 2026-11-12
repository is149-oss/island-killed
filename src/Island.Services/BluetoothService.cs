using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Island.Services;

public sealed class BluetoothService : IDisposable
{
	private const string IsConnected = "System.Devices.Aep.IsConnected";

	private readonly List<DeviceWatcher> _watchers = new List<DeviceWatcher>();

	private readonly Dictionary<string, (string Name, bool On)> _devices = new Dictionary<string, (string, bool)>();

	private SynchronizationContext? _ctx;

	private int _pending;

	public event Action<string, bool>? ConnectionChanged;

	public void Init()
	{
		if (ReviewSafety.Enabled) return;
		_ctx = SynchronizationContext.Current;
		string[] array = new string[2]
		{
			BluetoothDevice.GetDeviceSelectorFromPairingState(pairingState: true),
			BluetoothLEDevice.GetDeviceSelectorFromPairingState(pairingState: true)
		};
		for (int i = 0; i < array.Length; i++)
		{
			DeviceWatcher deviceWatcher = DeviceInformation.CreateWatcher(array[i], new string[1] { "System.Devices.Aep.IsConnected" }, DeviceInformationKind.AssociationEndpoint);
			deviceWatcher.Added += (DeviceWatcher _, DeviceInformation info) =>
			{
				bool on = info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var value) && value is bool && (bool)value;
				Post(() =>
				{
					_devices[info.Id] = (info.Name, on);
				});
			};
			deviceWatcher.Updated += (DeviceWatcher _, DeviceInformationUpdate update) =>
			{
				Post(() =>
				{
					OnUpdated(update);
				});
			};
			deviceWatcher.Removed += (DeviceWatcher _, DeviceInformationUpdate update) =>
			{
				Post(() =>
				{
					_devices.Remove(update.Id);
				});
			};
			deviceWatcher.EnumerationCompleted += (DeviceWatcher _, object _) =>
			{
				Post(() =>
				{
					_pending--;
				});
			};
			_pending++;
			deviceWatcher.Start();
			_watchers.Add(deviceWatcher);
		}
	}

	private void Post(Action a)
	{
		_ctx?.Post((object? _) =>
		{
			a();
		}, null);
	}

	private void OnUpdated(DeviceInformationUpdate update)
	{
		if (_devices.TryGetValue(update.Id, out (string, bool) value) && update.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var value2) && value2 is bool flag && flag != value.Item2)
		{
			_devices[update.Id] = (value.Item1, flag);
			if (_pending <= 0 && value.Item1.Length > 0)
			{
				ConnectionChanged?.Invoke(value.Item1, flag);
			}
		}
	}

	public void Dispose()
	{
		foreach (DeviceWatcher watcher in _watchers)
		{
			try
			{
				DeviceWatcherStatus status = watcher.Status;
				if ((uint)(status - 1) <= 1u)
				{
					watcher.Stop();
				}
			}
			catch (Exception)
			{
			}
		}
	}
}
