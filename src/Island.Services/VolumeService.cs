using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Island.Services;

public sealed class VolumeService : IMMNotificationClient, IDisposable
{
	public sealed record AudioOutput(string Id, string Title, string Kind, string Glyph, bool IsDefault);

	[ComImport]
	[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
	private class PolicyConfigClient
	{
	}

	[ComImport]
	[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPolicyConfig
	{
		[PreserveSig]
		int GetMixFormat(nint a, nint b);

		[PreserveSig]
		int GetDeviceFormat(nint a, int b, nint c);

		[PreserveSig]
		int ResetDeviceFormat(nint a);

		[PreserveSig]
		int SetDeviceFormat(nint a, nint b, nint c);

		[PreserveSig]
		int GetProcessingPeriod(nint a, int b, nint c, nint d);

		[PreserveSig]
		int SetProcessingPeriod(nint a, nint b);

		[PreserveSig]
		int GetShareMode(nint a, nint b);

		[PreserveSig]
		int SetShareMode(nint a, nint b);

		[PreserveSig]
		int GetPropertyValue(nint a, int b, nint c, nint d);

		[PreserveSig]
		int SetPropertyValue(nint a, int b, nint c, nint d);

		[PreserveSig]
		int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);

		[PreserveSig]
		int SetEndpointVisibility(nint a, int b);
	}

	private readonly MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();

	private MMDevice? _device;

	private string? _deviceId;

	private SynchronizationContext? _ctx;

	private readonly Dictionary<uint, string> _processNames = new Dictionary<uint, string>();

	private static readonly string[] Browsers = new string[8] { "browser", "chrome", "msedge", "firefox", "opera", "vivaldi", "brave", "yandex" };

	private static readonly PropertyKey FormFactorKey = new PropertyKey(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);

	private static readonly PropertyKey EnumeratorKey = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);

	public float Level { get; private set; }

	public bool Muted { get; private set; }

	public string DeviceName { get; private set; } = "";

	public float Peak
	{
		get
		{
			try
			{
				return _device?.AudioMeterInformation.MasterPeakValue ?? 0f;
			}
			catch
			{
				return 0f;
			}
		}
	}

	public string DefaultGlyph { get; private set; } = "\ue995";

	public event Action<float, bool>? VolumeChanged;

	public event Action<string>? OutputDeviceChanged;

	public event Action? DefaultOutputChanged;

	public void Init()
	{
		if (ReviewSafety.Enabled) return;
		_ctx = SynchronizationContext.Current;
		_enumerator.RegisterEndpointNotificationCallback(this);
		Bind(announce: false);
	}

	public void Step(float delta)
	{
		if (_device == null)
		{
			return;
		}
		try
		{
			AudioEndpointVolume audioEndpointVolume = _device.AudioEndpointVolume;
			audioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(audioEndpointVolume.MasterVolumeLevelScalar + delta, 0f, 1f);
			if (audioEndpointVolume.Mute && delta > 0f)
			{
				audioEndpointVolume.Mute = false;
			}
		}
		catch (Exception)
		{
		}
	}

	public void SetLevel(float level)
	{
		if (ReviewSafety.Enabled) return;
		if (_device == null)
		{
			return;
		}
		try
		{
			AudioEndpointVolume audioEndpointVolume = _device.AudioEndpointVolume;
			audioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
			if (audioEndpointVolume.Mute && level > 0f)
			{
				audioEndpointVolume.Mute = false;
			}
		}
		catch (Exception)
		{
		}
	}

	private List<SimpleAudioVolume> AppSessions(string mediaId)
	{
		List<SimpleAudioVolume> list = new List<SimpleAudioVolume>();
		if (_device == null || string.IsNullOrEmpty(mediaId))
		{
			return list;
		}
		string text = mediaId.ToLowerInvariant();
		int num = text.IndexOf('!');
		string text2 = ((num > 0) ? text.Substring(0, num).Split('_')[0] : text.Split('.', '!', '_', ' ')[0]);
		try
		{
			AudioSessionManager audioSessionManager = _device.AudioSessionManager;
			audioSessionManager.RefreshSessions();
			SessionCollection sessions = audioSessionManager.Sessions;
			for (int i = 0; i < sessions.Count; i++)
			{
				AudioSessionControl audioSessionControl = sessions[i];
				if (audioSessionControl.IsSystemSoundsSession || audioSessionControl.State == AudioSessionState.AudioSessionStateExpired)
				{
					continue;
				}
				uint getProcessID = audioSessionControl.GetProcessID;
				if (getProcessID != 0 && getProcessID != Environment.ProcessId)
				{
					string text3 = ProcessName(getProcessID);
					string text4 = (audioSessionControl.GetSessionIdentifier ?? "").ToLowerInvariant();
					if ((text3.Length >= 3 && text.Contains(text3)) || (text2.Length >= 4 && text4.Contains(text2)))
					{
						list.Add(audioSessionControl.SimpleAudioVolume);
					}
				}
			}
		}
		catch (Exception)
		{
		}
		return list;
	}

	private string ProcessName(uint pid)
	{
		if (_processNames.TryGetValue(pid, out string? value))
		{
			return value;
		}
		try
		{
			value = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
		}
		catch (Exception)
		{
			value = "";
		}
		if (_processNames.Count > 200)
		{
			_processNames.Clear();
		}
		_processNames[pid] = value;
		return value;
	}

	public string? PlayingBrowser()
	{
		if (ReviewSafety.Enabled) return null;
		if (_device == null)
		{
			return null;
		}
		try
		{
			AudioSessionManager audioSessionManager = _device.AudioSessionManager;
			audioSessionManager.RefreshSessions();
			SessionCollection sessions = audioSessionManager.Sessions;
			for (int i = 0; i < sessions.Count; i++)
			{
				AudioSessionControl audioSessionControl = sessions[i];
				if (audioSessionControl.State == AudioSessionState.AudioSessionStateActive && audioSessionControl.GetProcessID != 0)
				{
					string text = ProcessName(audioSessionControl.GetProcessID);
					if (Array.IndexOf(Browsers, text) >= 0 && audioSessionControl.AudioMeterInformation.MasterPeakValue > 0.0005f)
					{
						return text;
					}
				}
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	public float? GetAppVolume(string mediaId)
	{
		List<SimpleAudioVolume> list = AppSessions(mediaId);
		try
		{
			return (list.Count > 0) ? new float?(list.Max((SimpleAudioVolume s) => (!s.Mute) ? s.Volume : 0f)) : ((float?)null);
		}
		catch (Exception)
		{
			return null;
		}
	}

	public void SetAppVolume(string mediaId, float level)
	{
		foreach (SimpleAudioVolume item in AppSessions(mediaId))
		{
			try
			{
				item.Volume = Math.Clamp(level, 0f, 1f);
				if (item.Mute && level > 0f)
				{
					item.Mute = false;
				}
			}
			catch (Exception)
			{
			}
		}
	}

	public void ToggleMute()
	{
		if (ReviewSafety.Enabled) return;
		if (_device == null)
		{
			return;
		}
		try
		{
			_device.AudioEndpointVolume.Mute = !_device.AudioEndpointVolume.Mute;
		}
		catch (Exception)
		{
		}
	}

	private void Bind(bool announce)
	{
		if (_device != null)
		{
			_device.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
			_device.Dispose();
			_device = null;
		}
		try
		{
			_device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
			_device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
		}
		catch (Exception)
		{
			return;
		}
		try
		{
			DeviceName = _device.FriendlyName;
			DefaultGlyph = GlyphFor(_device);
			Level = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
			Muted = _device.AudioEndpointVolume.Mute;
		}
		catch (Exception)
		{
		}
		if (announce && _device.ID != _deviceId)
		{
			OutputDeviceChanged?.Invoke(DeviceName);
		}
		_deviceId = _device.ID;
		DefaultOutputChanged?.Invoke();
	}

	public List<AudioOutput> Outputs()
	{
		List<AudioOutput> list = new List<AudioOutput>();
		try
		{
			string? deviceId = _deviceId;
			foreach (MMDevice item in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
			{
				using (item)
				{
					string text = item.FriendlyName ?? "";
					string title = text;
					string kind = "";
					int num = text.LastIndexOf(" (", StringComparison.Ordinal);
					if (num > 0 && text.EndsWith(")"))
					{
						kind = text.Substring(0, num);
						string text2 = text;
						int num2 = num + 2;
						title = text2.Substring(num2, text2.Length - 1 - num2);
					}
					list.Add(new AudioOutput(item.ID, title, kind, GlyphFor(item), item.ID == deviceId));
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("audio outputs", ex);
		}
		return list;
	}

	private static string GlyphFor(MMDevice d)
	{
		uint num = 10u;
		bool flag = false;
		try
		{
			if (d.Properties.Contains(FormFactorKey) && d.Properties[FormFactorKey].Value is uint num2)
			{
				num = num2;
			}
			if (d.Properties.Contains(EnumeratorKey) && d.Properties[EnumeratorKey].Value is string text)
			{
				flag = text.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);
			}
		}
		catch (Exception)
		{
		}
		if ((num != 3 && num != 5) || 1 == 0)
		{
			if (!flag)
			{
				if (num != 9)
				{
					return "\ue995";
				}
				return "\ue7f4";
			}
			return "\ue702";
		}
		return "\ue7f6";
	}

	public bool SetDefaultOutput(string id)
	{
		if (ReviewSafety.Enabled) return false;
		try
		{
			IPolicyConfig policyConfig = (IPolicyConfig)new PolicyConfigClient();
			for (int i = 0; i < 3; i++)
			{
				Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(id, i));
			}
			Log.Write("audio output switched to " + id);
			return true;
		}
		catch (Exception ex)
		{
			Log.Error("set default output", ex);
			return false;
		}
	}

	private void OnVolume(AudioVolumeNotificationData data)
	{
		_ctx?.Post((object? _) =>
		{
			Level = data.MasterVolume;
			Muted = data.Muted;
			VolumeChanged?.Invoke(data.MasterVolume, data.Muted);
		}, null);
	}

	public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
	{
		if (flow == DataFlow.Render && role == Role.Multimedia)
		{
			_ctx?.Post((object? _) =>
			{
				Bind(announce: true);
			}, null);
		}
	}

	public void OnDeviceStateChanged(string deviceId, DeviceState newState)
	{
	}

	public void OnDeviceAdded(string pwstrDeviceId)
	{
	}

	public void OnDeviceRemoved(string deviceId)
	{
	}

	public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
	{
	}

	public void Dispose()
	{
		try
		{
			_enumerator.UnregisterEndpointNotificationCallback(this);
		}
		catch
		{
		}
		_device?.Dispose();
		_enumerator.Dispose();
	}
}
