using System;
using System.Management;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Island.Native;
using Microsoft.Win32;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace Island.Services;

public sealed class SystemControls
{
	private const string PersonalizeKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";

	private SynchronizationContext? _ctx;

	private Radio? _wifi;

	private Radio? _bluetooth;

	private int _pendingBrightness = -1;

	private int _writingBrightness;

	private static readonly Regex VpnHint = new Regex("vpn|wireguard|wintun|openvpn|tap-|\\btun\\b|amnezia|outline|nordlynx|proton|forti|anyconnect|globalprotect|zerotier|tailscale|hamachi|radmin|softether|cloudflare warp", RegexOptions.IgnoreCase);

	private static readonly Regex NotVpn = new Regex("teredo|isatap|6to4|ip-https|loopback|kernel debug|hyper-v|vethernet|virtualbox|vmware|bluetooth", RegexOptions.IgnoreCase);

	public bool CanSleep { get; } = Win32.CanSleep();

	public NetworkKind Network { get; private set; }

	public string NetworkName { get; private set; } = "";

	public string? VpnName { get; private set; }

	public bool HasWifi => _wifi != null;

	public bool WifiOn
	{
		get
		{
			Radio? wifi = _wifi;
			if ((object)wifi == null)
			{
				return false;
			}
			return wifi.State == RadioState.On;
		}
	}

	public bool HasBluetooth => _bluetooth != null;

	public bool BluetoothOn
	{
		get
		{
			Radio? bluetooth = _bluetooth;
			if ((object)bluetooth == null)
			{
				return false;
			}
			return bluetooth.State == RadioState.On;
		}
	}

	public int? Brightness { get; private set; }

	public bool DarkTheme
	{
		get
		{
			using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
			return registryKey?.GetValue("AppsUseLightTheme") is int num && num == 0;
		}
	}

	public event Action? Changed;

	public async Task InitAsync()
	{
		if (ReviewSafety.Enabled) return;
		_ctx = SynchronizationContext.Current;
		RefreshNetwork();
		try
		{
			if (await Radio.RequestAccessAsync() == RadioAccessStatus.Allowed)
			{
				foreach (Radio item in await Radio.GetRadiosAsync())
				{
					if (item.Kind == RadioKind.WiFi && _wifi == null)
					{
						_wifi = item;
					}
					else if (item.Kind == RadioKind.Bluetooth && _bluetooth == null)
					{
						_bluetooth = item;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("radios", ex);
		}
		if (_wifi != null)
		{
			_wifi.StateChanged += (Radio _, object _) =>
			{
				Post();
			};
		}
		if (_bluetooth != null)
		{
			_bluetooth.StateChanged += (Radio _, object _) =>
			{
				Post();
			};
		}
		NetworkInformation.NetworkStatusChanged += (object _) =>
		{
			Post();
		};
		NetworkChange.NetworkAddressChanged += (object? _, EventArgs _) =>
		{
			Post();
		};
		await RefreshBrightnessAsync();
	}

	private void Post()
	{
		_ctx?.Post((object? _) =>
		{
			RefreshNetwork();
			Changed?.Invoke();
		}, null);
	}

	public void RefreshNetwork()
	{
		RefreshVpn();
		try
		{
			ConnectionProfile internetConnectionProfile = NetworkInformation.GetInternetConnectionProfile();
			if (internetConnectionProfile == null)
			{
				Network = NetworkKind.None;
				NetworkName = "";
			}
			else if (internetConnectionProfile.IsWlanConnectionProfile)
			{
				string? text = internetConnectionProfile.WlanConnectionProfileDetails?.GetConnectedSsid();
				string networkName = (string.IsNullOrEmpty(text) ? "Wi‑Fi" : text);
				Network = NetworkKind.Wifi;
				NetworkName = networkName;
			}
			else if (internetConnectionProfile.IsWwanConnectionProfile)
			{
				string networkName = internetConnectionProfile.ProfileName;
				Network = NetworkKind.Cellular;
				NetworkName = networkName;
			}
			else
			{
				NetworkAdapter networkAdapter = internetConnectionProfile.NetworkAdapter;
				string networkName = (((object)networkAdapter != null && networkAdapter.IanaInterfaceType == 6) ? "Ethernet" : internetConnectionProfile.ProfileName);
				Network = NetworkKind.Ethernet;
				NetworkName = networkName;
			}
		}
		catch (Exception ex)
		{
			Log.Error("network", ex);
			Network = NetworkKind.None;
			NetworkName = "";
		}
	}

	private void RefreshVpn()
	{
		try
		{
			NetworkInterface[] allNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface networkInterface in allNetworkInterfaces)
			{
				if (networkInterface.OperationalStatus != OperationalStatus.Up)
				{
					continue;
				}
				string input = networkInterface.Name + " " + networkInterface.Description;
				if (!NotVpn.IsMatch(input))
				{
					NetworkInterfaceType networkInterfaceType = networkInterface.NetworkInterfaceType;
					bool flag = ((networkInterfaceType == NetworkInterfaceType.Ppp || networkInterfaceType == NetworkInterfaceType.Tunnel) ? true : false);
					if (flag || VpnHint.IsMatch(input))
					{
						VpnName = networkInterface.Name;
						return;
					}
				}
			}
			VpnName = null;
		}
		catch (Exception ex)
		{
			Log.Error("vpn", ex);
		}
	}

	public async Task ToggleWifiAsync()
	{
		if (ReviewSafety.Enabled) return;
		if (!(_wifi == null))
		{
			try
			{
				await _wifi.SetStateAsync((!WifiOn) ? RadioState.On : RadioState.Off);
			}
			catch (Exception ex)
			{
				Log.Error("wifi", ex);
			}
			Changed?.Invoke();
		}
	}

	public async Task ToggleBluetoothAsync()
	{
		if (ReviewSafety.Enabled) return;
		if (!(_bluetooth == null))
		{
			try
			{
				await _bluetooth.SetStateAsync((!BluetoothOn) ? RadioState.On : RadioState.Off);
			}
			catch (Exception ex)
			{
				Log.Error("bluetooth", ex);
			}
			Changed?.Invoke();
		}
	}

	public void SetDarkTheme(bool dark)
	{
		if (ReviewSafety.Enabled) return;
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
			registryKey.SetValue("AppsUseLightTheme", (!dark) ? 1 : 0, RegistryValueKind.DWord);
			registryKey.SetValue("SystemUsesLightTheme", (!dark) ? 1 : 0, RegistryValueKind.DWord);
			Task.Run(() =>
			{
				Win32.BroadcastSettingChange("ImmersiveColorSet");
			});
		}
		catch (Exception ex)
		{
			Log.Error("theme", ex);
		}
		Changed?.Invoke();
	}

	public async Task RefreshBrightnessAsync()
	{
		Brightness = await Task.Run((Func<int?>)ReadBrightness);
		Changed?.Invoke();
	}

	private static int? ReadBrightness()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectSearcher.Get().GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				return Convert.ToInt32(((ManagementObject)managementObjectEnumerator.Current)["CurrentBrightness"]);
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	public void SetBrightness(int value)
	{
		if (ReviewSafety.Enabled) return;
		Brightness = value;
		Interlocked.Exchange(ref _pendingBrightness, value);
		if (Interlocked.Exchange(ref _writingBrightness, 1) == 1)
		{
			return;
		}
		Task.Run(() =>
		{
			while (true)
			{
				int num;
				if ((num = Interlocked.Exchange(ref _pendingBrightness, -1)) >= 0)
				{
					try
					{
						using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
						foreach (ManagementObject item in managementObjectSearcher.Get())
						{
							item.InvokeMethod("WmiSetBrightness", new object[2]
							{
								1u,
								(byte)num
							});
						}
					}
					catch (Exception ex)
					{
						Log.Error("brightness", ex);
					}
				}
				else
				{
					Interlocked.Exchange(ref _writingBrightness, 0);
					if (Volatile.Read(in _pendingBrightness) < 0 || Interlocked.Exchange(ref _writingBrightness, 1) != 0)
					{
						break;
					}
				}
			}
		});
	}
}
