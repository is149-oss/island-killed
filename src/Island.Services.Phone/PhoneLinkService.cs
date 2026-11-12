using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QRCoder;

namespace Island.Services.Phone;

public sealed class PhoneLinkService : IDisposable
{
	private sealed class Connection
	{
		private readonly TcpClient _client;

		private readonly Stream _stream;

		private readonly LinkCipher _cipher;

		private readonly SemaphoreSlim _write = new SemaphoreSlim(1, 1);

		public PhoneDevice Device { get; }

		public Dictionary<string, IncomingFile> Incoming { get; } = new Dictionary<string, IncomingFile>();

		public List<IncomingFile> DropIncoming()
		{
			List<IncomingFile> list = Incoming.Values.ToList();
			foreach (IncomingFile item in list)
			{
				item.Discard();
			}
			Incoming.Clear();
			return list;
		}

		public Connection(TcpClient client, Stream stream, LinkCipher cipher, PhoneDevice device)
		{
			_client = client;
			_stream = stream;
			_cipher = cipher;
			Device = device;
		}

		public async Task<bool> SendAsync(object message)
		{
			await _write.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				byte[] payload = _cipher.Seal(JsonSerializer.SerializeToUtf8Bytes(message));
				using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10L));
				await LinkProtocol.WriteFrameAsync(_stream, payload, timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
				return true;
			}
			catch (Exception)
			{
				Close();
				return false;
			}
			finally
			{
				_write.Release();
			}
		}

		public void Close()
		{
			_client.Dispose();
		}
	}

	public static readonly TimeSpan PairingWindow = TimeSpan.FromMinutes(10L);

	private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15L);

	private static readonly TimeSpan Silence = TimeSpan.FromSeconds(75L);

	private readonly PhoneLinkStore _store;

	private readonly object _gate = new object();

	private readonly object _inline = new object();

	private readonly Dictionary<string, Connection> _connections = new Dictionary<string, Connection>();

	private readonly Dictionary<(string Device, string Key), PhoneNotification> _active = new Dictionary<(string, string), PhoneNotification>();

	private readonly Dictionary<string, (string Name, byte[]? Icon)> _apps = new Dictionary<string, (string, byte[])>();

	private SynchronizationContext? _ctx;

	private CancellationTokenSource? _cts;

	private TcpListener? _listener;

	private UdpClient? _udp;

	private (byte[] Key, DateTime Until)? _pairing;

	public const int MaxClipboard = 100000;

	public const int ChunkSize = 262144;

	public const long MaxFile = 2147483648L;

	private static readonly TimeSpan ReportEvery = TimeSpan.FromMilliseconds(250L);

	private static readonly string TempFolder = Path.Combine(Path.GetTempPath(), "Island", "incoming");

	public int Port { get; private set; }

	public string PcId => _store.PcId;

	public static string PcName => Environment.MachineName;

	public IReadOnlyList<PhoneDevice> Devices => _store.Devices;

	public IReadOnlyList<PhoneNotification> Active => _active.Values.OrderByDescending((PhoneNotification n) => n.Time).ToList();

	public string IncomingFolder { get; set; } = Path.Combine(KnownDownloadsFolder(), "Island");

	public event Action<PhoneNotification>? Posted;

	public event Action<PhoneNotification>? Removed;

	public event Action<PhoneDevice, bool>? ConnectionChanged;

	public event Action<PhoneDevice>? Paired;

	public event Action<PhoneDevice, string>? ClipboardReceived;

	public event Action<PhoneFile>? FileReceived;

	public event Action<PhoneTransfer>? TransferProgress;

	public PhoneLinkService(string? storePath = null)
	{
		_store = PhoneLinkStore.Load(storePath ?? PhoneLinkStore.DefaultPath);
	}

	public bool IsConnected(string deviceId)
	{
		lock (_gate)
		{
			return _connections.ContainsKey(deviceId);
		}
	}

	public void Start()
	{
		if (ReviewSafety.Enabled) return;
		if (_cts == null)
		{
			_ctx = SynchronizationContext.Current;
			_cts = new CancellationTokenSource();
			try
			{
				_listener = Listen(47821);
			}
			catch (SocketException ex)
			{
				Log.Caught($"phone port {47821} is busy, using another one", ex);
				_listener = Listen(0);
			}
			Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
			AcceptLoop(_listener, _cts.Token);
			try
			{
				_udp = new UdpClient();
				_udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, optionValue: true);
				_udp.Client.Bind(new IPEndPoint(IPAddress.Any, 47820));
				DiscoveryLoop(_udp, _cts.Token);
			}
			catch (SocketException ex2)
			{
				Log.Error("phone discovery", ex2);
			}
			Log.Write($"phone link listening on {Port}");
		}
	}

	private static TcpListener Listen(int port)
	{
		TcpListener tcpListener = new TcpListener(IPAddress.Any, port);
		tcpListener.Start();
		return tcpListener;
	}

	public PhonePairing StartPairing()
	{
		byte[] bytes = RandomNumberGenerator.GetBytes(32);
		DateTime dateTime = DateTime.Now + PairingWindow;
		lock (_gate)
		{
			_pairing = (bytes, dateTime);
		}
		Log.Write($"phone pairing started: addresses [{string.Join(", ", LocalAddresses())}], port {Port}, window {PairingWindow.TotalSeconds:0} s");
		return new PhonePairing($"island://pair?v={1}&pc={_store.PcId}&name={Uri.EscapeDataString(PcName)}&hosts={string.Join(",", LocalAddresses())}&port={Port}&key={LinkProtocol.ToBase64Url(bytes)}", dateTime);
	}

	public void CancelPairing()
	{
		lock (_gate)
		{
			_pairing = null;
		}
	}

	public static byte[] QrPng(string text, int pixelsPerModule = 10)
	{
		using QRCodeGenerator qRCodeGenerator = new QRCodeGenerator();
		using QRCodeData data = qRCodeGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
		return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
	}

	public void Unpair(string deviceId)
	{
		Connection value;
		lock (_gate)
		{
			_store.Devices.RemoveAll((PhoneDevice d) => d.Id == deviceId);
			_store.Save();
			_connections.TryGetValue(deviceId, out value);
		}
		value?.Close();
	}

	private static IEnumerable<string> LocalAddresses()
	{
		List<NetworkInterface> list = NetworkInterface.GetAllNetworkInterfaces().Where((NetworkInterface n) =>
		{
			bool flag = n.OperationalStatus == OperationalStatus.Up;
			if (flag)
			{
				NetworkInterfaceType networkInterfaceType = n.NetworkInterfaceType;
				bool flag2 = ((networkInterfaceType == NetworkInterfaceType.Loopback || networkInterfaceType == NetworkInterfaceType.Tunnel) ? true : false);
				flag = !flag2;
			}
			return flag;
		}).ToList();
		List<NetworkInterface> list2 = list.Where((NetworkInterface n) => n.GetIPProperties().GatewayAddresses.Any((GatewayIPAddressInformation g) => !g.Address.Equals(IPAddress.Any))).ToList();
		return (from a in ((list2.Count > 0) ? list2 : list).SelectMany((NetworkInterface n) => n.GetIPProperties().UnicastAddresses)
			where a.Address.AddressFamily == AddressFamily.InterNetwork
			select a.Address.ToString()).Distinct().ToList();
	}

	public void Dismiss(PhoneNotification n)
	{
		Send(n.DeviceId, new
		{
			t = "dismiss",
			key = n.Key
		});
	}

	public void Invoke(PhoneNotification n, PhoneAction action, string? reply = null)
	{
		Send(n.DeviceId, new
		{
			t = "action",
			key = n.Key,
			index = action.Index,
			text = reply
		});
	}

	public void SendClipboard(string text)
	{
		if (text.Length == 0 || text.Length > 100000)
		{
			return;
		}
		List<Connection> list;
		lock (_gate)
		{
			list = _connections.Values.ToList();
		}
		foreach (Connection item in list)
		{
			item.SendAsync(new
			{
				t = "clip",
				text = text
			});
		}
	}

	private void Send(string deviceId, object message)
	{
		Connection value;
		lock (_gate)
		{
			_connections.TryGetValue(deviceId, out value);
		}
		value?.SendAsync(message);
	}

	private async Task AcceptLoop(TcpListener listener, CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception) when (ct.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex2)
			{
				Log.Error("phone accept", ex2);
				await Task.Delay(1000, ct).ConfigureAwait(continueOnCapturedContext: false);
				continue;
			}
			HandleAsync(client, ct);
		}
	}

	private async Task DiscoveryLoop(UdpClient udp, CancellationToken ct)
	{
		byte[] ask = Encoding.UTF8.GetBytes("ISLAND?" + _store.PcId);
		while (!ct.IsCancellationRequested)
		{
			try
			{
				UdpReceiveResult udpReceiveResult = await udp.ReceiveAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				if (!((ReadOnlySpan<byte>)udpReceiveResult.Buffer.AsSpan()).SequenceEqual((ReadOnlySpan<byte>)ask))
				{
					Log.Write($"phone discovery: a request from {udpReceiveResult.RemoteEndPoint.Address} is not for this PC (another PC id or not Island)");
					continue;
				}
				Log.Write($"phone discovery: answered {udpReceiveResult.RemoteEndPoint.Address}");
				byte[] bytes = Encoding.UTF8.GetBytes($"{"ISLAND!"}{_store.PcId} {Port}");
				await udp.SendAsync(bytes, udpReceiveResult.RemoteEndPoint, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception) when (ct.IsCancellationRequested)
			{
				break;
			}
			catch (SocketException)
			{
			}
		}
	}

	private async Task HandleAsync(TcpClient client, CancellationToken ct)
	{
		Connection? connection = null;
		PhoneDevice? device = null;
		LinkCipher? cipher = null;
		try
		{
			client.NoDelay = true;
			Log.Write($"phone: incoming connection from {(client.Client.RemoteEndPoint as IPEndPoint)?.Address}");
			NetworkStream stream = client.GetStream();
			using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			cts.CancelAfter(HandshakeTimeout);
			using JsonDocument hello = JsonDocument.Parse(await LinkProtocol.ReadFrameAsync(stream, cts.Token).ConfigureAwait(continueOnCapturedContext: false));
			JsonElement rootElement = hello.RootElement;
			string deviceId = PhoneNotification.Str(rootElement, "device") ?? "";
			byte[]? phoneNonce = PhoneNotification.Bytes(PhoneNotification.Str(rootElement, "nonce"));
			bool flag = rootElement.TryGetProperty("pair", out var value) && value.ValueKind == JsonValueKind.True;
			if (!rootElement.TryGetProperty("v", out var value2) || !value2.TryGetInt32(out var value3) || value3 != 1)
			{
				Log.Warn($"phone: protocol version mismatch (the phone says {(rootElement.TryGetProperty("v", out var value4) ? value4.ToString() : "none")}, the PC speaks {1}): update Island Link or Island");
				await SendPlain(stream, new
				{
					t = "error",
					code = "version"
				}, cts.Token).ConfigureAwait(continueOnCapturedContext: false);
				return;
			}
			int length = deviceId.Length;
			bool flag2 = ((length > 64 || length == 0) ? true : false);
			if (flag2 || phoneNonce == null || phoneNonce.Length != 16)
			{
				Log.Warn("phone: a hello that is not Island Link was dropped");
				return;
			}
			byte[]? key = null;
			byte[]? pairKey = null;
			lock (_gate)
			{
				if (!flag)
				{
					goto IL_03c4;
				}
				(byte[], DateTime)? pairing = _pairing;
				if (!pairing.HasValue)
				{
					goto IL_03c4;
				}
				(byte[], DateTime) valueOrDefault = pairing.GetValueOrDefault();
				if (!(valueOrDefault.Item2 > DateTime.Now))
				{
					goto IL_03c4;
				}
				key = (pairKey = valueOrDefault.Item1);
				goto end_IL_036c;
				IL_03c4:
				PhoneDevice? phoneDevice = _store.Find(deviceId);
				if (phoneDevice != null)
				{
					key = PhoneLinkStore.UnprotectKey(phoneDevice);
				}
				end_IL_036c:;
			}
			if (key == null)
			{
				Log.Warn($"phone: unknown device refused (pairing {(flag ? "asked but no pairing window is open on the PC" : "not asked: the phone is not paired with this PC")}); device {deviceId.Substring(0, Math.Min(6, deviceId.Length))}…");
				await SendPlain(stream, new
				{
					t = "error",
					code = "unknown_device"
				}, cts.Token).ConfigureAwait(continueOnCapturedContext: false);
				return;
			}
			byte[] pcNonce = RandomNumberGenerator.GetBytes(16);
			await SendPlain(stream, new
			{
				t = "hello",
				v = 1,
				pc = _store.PcId,
				nonce = Convert.ToBase64String(pcNonce)
			}, cts.Token).ConfigureAwait(continueOnCapturedContext: false);
			cipher = LinkCipher.ForPc(key, phoneNonce, pcNonce);
			LinkCipher linkCipher = cipher;
			using JsonDocument auth = JsonDocument.Parse(linkCipher.Open(await LinkProtocol.ReadFrameAsync(stream, cts.Token).ConfigureAwait(continueOnCapturedContext: false)));
			JsonElement rootElement2 = auth.RootElement;
			if (PhoneNotification.Str(rootElement2, "t") != "auth")
			{
				Log.Warn("phone: the first encrypted message was not auth");
				return;
			}
			Connection value5;
			lock (_gate)
			{
				device = _store.Find(deviceId);
				if (pairKey != null)
				{
					if (device == null)
					{
						device = new PhoneDevice
						{
							Id = deviceId,
							PairedAt = DateTime.Now
						};
						_store.Devices.Add(device);
					}
					device.Key = PhoneLinkStore.ProtectKey(pairKey);
					_pairing = null;
				}
				if (device == null)
				{
					return;
				}
				device.Name = Clip(PhoneNotification.Str(rootElement2, "name"), 80) ?? "Телефон";
				device.Model = Clip(PhoneNotification.Str(rootElement2, "model"), 80);
				device.Os = Clip(PhoneNotification.Str(rootElement2, "os"), 40);
				device.LastSeen = DateTime.Now;
				_store.Save();
				connection = new Connection(client, stream, cipher, device);
				_connections.TryGetValue(deviceId, out value5);
				_connections[deviceId] = connection;
			}
			value5?.Close();
			Log.Write("phone connected: " + device.Name + ((pairKey != null) ? " (paired)" : ""));
			PhoneDevice connected = device;
			Post(() =>
			{
				if (pairKey != null)
				{
					Paired?.Invoke(connected);
				}
				ConnectionChanged?.Invoke(connected, arg2: true);
			});
			await connection.SendAsync(new
			{
				t = "ready",
				name = PcName
			}).ConfigureAwait(continueOnCapturedContext: false);
			while (true)
			{
				cts.CancelAfter(Silence);
				linkCipher = cipher;
				byte[] frame = linkCipher.Open(await LinkProtocol.ReadFrameAsync(stream, cts.Token).ConfigureAwait(continueOnCapturedContext: false));
				OnFrame(connection, frame);
			}
		}
		catch (Exception ex) when ((ex is OperationCanceledException || ex is IOException || ex is SocketException || ex is CryptographicException || ex is JsonException || ex is InvalidDataException) ? true : false)
		{
			if (connection == null)
			{
				string name = ex.GetType().Name;
				string text;
				if (ex is CryptographicException)
				{
					text = " (the key does not match: pair the phone again)";
				}
				else
				{
					text = ((ex is OperationCanceledException) ? " (the phone went silent)" : "");
				}
				Log.Warn("phone: handshake ended without a connection: " + name + text);
			}
		}
		catch (Exception ex2)
		{
			Log.Error("phone connection", ex2);
		}
		finally
		{
			client.Dispose();
			cipher?.Dispose();
			if (connection != null)
			{
				foreach (IncomingFile item in connection.DropIncoming())
				{
					Report(item, PhoneTransferState.Failed);
				}
			}
			bool flag3 = false;
			if (connection != null)
			{
				lock (_gate)
				{
					if (_connections.TryGetValue(connection.Device.Id, out Connection? value6) && value6 == connection)
					{
						_connections.Remove(connection.Device.Id);
						flag3 = true;
					}
				}
			}
			if (flag3 && device != null)
			{
				PhoneDevice gone = device;
				Log.Write("phone disconnected: " + gone.Name);
				Post(() =>
				{
					foreach (PhoneNotification item2 in _active.Values.Where((PhoneNotification n) => n.DeviceId == gone.Id).ToList())
					{
						_active.Remove((item2.DeviceId, item2.Key));
						Removed?.Invoke(item2);
					}
					ConnectionChanged?.Invoke(gone, arg2: false);
				});
			}
		}
	}

	private void OnFrame(Connection connection, byte[] frame)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(frame);
		JsonElement rootElement = jsonDocument.RootElement;
		string? text = PhoneNotification.Str(rootElement, "t");
		if (text == null)
		{
			return;
		}
		switch (text.Length)
		{
		case 4:
			switch (text[0])
			{
			default:
				return;
			case 'p':
				if (text == "ping")
				{
					connection.SendAsync(new
					{
						t = "pong"
					});
				}
				return;
			case 'c':
			{
				if (!(text == "clip"))
				{
					return;
				}
				string? text2 = PhoneNotification.Str(rootElement, "text");
				PhoneDevice device = connection.Device;
				Log.Write($"phone clipboard from {device.Name}: {text2?.Length ?? 0} chars");
				if (text2 == null)
				{
					return;
				}
				int length = text2.Length;
				if (length > 0 && length <= 100000)
				{
					Post(() =>
					{
						ClipboardReceived?.Invoke(device, text2);
					});
				}
				return;
			}
			case 'f':
				break;
			}
			if (!(text == "file"))
			{
				break;
			}
			goto IL_02e7;
		case 3:
			switch (text[0])
			{
			default:
				return;
			case 'a':
			{
				if (!(text == "app"))
				{
					return;
				}
				string pkg = PhoneNotification.Str(rootElement, "pkg") ?? "";
				(string, byte[]) app = (PhoneNotification.Str(rootElement, "name") ?? pkg, PhoneNotification.Bytes(PhoneNotification.Str(rootElement, "icon")));
				if (pkg.Length > 0)
				{
					Post(() =>
					{
						_apps[pkg] = app;
					});
				}
				return;
			}
			case 'e':
				break;
			}
			if (!(text == "end"))
			{
				break;
			}
			goto IL_02e7;
		case 6:
		{
			if (!(text == "posted"))
			{
				break;
			}
			JsonElement copy = rootElement.Clone();
			PhoneDevice device2 = connection.Device;
			Post(() =>
			{
				string text3 = PhoneNotification.Str(copy, "pkg") ?? "";
				string appName;
				byte[] appIcon;
				if (_apps.TryGetValue(text3, out (string, byte[]) value))
				{
					(appName, appIcon) = value;
				}
				else
				{
					appIcon = null;
					appName = text3;
				}
				PhoneNotification phoneNotification = PhoneNotification.Parse(copy, device2, appName, appIcon);
				if (phoneNotification.Key.Length != 0)
				{
					_active[(phoneNotification.DeviceId, phoneNotification.Key)] = phoneNotification;
					Posted?.Invoke(phoneNotification);
				}
			});
			break;
		}
		case 5:
			if (!(text == "chunk"))
			{
				break;
			}
			goto IL_02e7;
		case 7:
			{
				if (!(text == "removed"))
				{
					break;
				}
				string key = PhoneNotification.Str(rootElement, "key") ?? "";
				string deviceId = connection.Device.Id;
				Post(() =>
				{
					if (_active.Remove((deviceId, key), out PhoneNotification? value))
					{
						Removed?.Invoke(value);
					}
				});
				break;
			}
			IL_02e7:
			OnFileFrame(connection, rootElement);
			break;
		}
	}

	private static Task SendPlain(Stream stream, object message, CancellationToken ct)
	{
		return LinkProtocol.WriteFrameAsync(stream, JsonSerializer.SerializeToUtf8Bytes(message), ct);
	}

	private static string? Clip(string? s, int max)
	{
		if (s != null)
		{
			if (s.Length > max)
			{
				return s.Substring(0, max);
			}
			return s;
		}
		return null;
	}

	private void Post(Action a)
	{
		if (_ctx != null)
		{
			_ctx.Post((object? _) =>
			{
				a();
			}, null);
			return;
		}
		lock (_inline)
		{
			a();
		}
	}

	public void Dispose()
	{
		_cts?.Cancel();
		_listener?.Stop();
		_udp?.Dispose();
		List<Connection> list;
		lock (_gate)
		{
			list = _connections.Values.ToList();
		}
		foreach (Connection item in list)
		{
			item.Close();
		}
	}

	private void Report(IncomingFile f, PhoneTransferState state)
	{
		f.LastReport = DateTime.UtcNow;
		PhoneTransfer t = new PhoneTransfer(f.Id, f.Name, f.Received, f.Size, Incoming: true, f.Clipboard, state);
		Post(() =>
		{
			TransferProgress?.Invoke(t);
		});
	}

	private void Report(PhoneTransfer t)
	{
		Post(() =>
		{
			TransferProgress?.Invoke(t);
		});
	}

	private void OnFileFrame(Connection connection, JsonElement j)
	{
		string text = PhoneNotification.Str(j, "id") ?? "";
		int length = text.Length;
		if ((length > 64 || length == 0) ? true : false)
		{
			return;
		}
		switch (PhoneNotification.Str(j, "t"))
		{
		case "file":
		{
			long num = ((j.TryGetProperty("size", out var value2) && value2.TryGetInt64(out var value3)) ? value3 : (-1));
			if (num >= 0 && num <= 2147483648u && !connection.Incoming.ContainsKey(text))
			{
				Directory.CreateDirectory(TempFolder);
				IncomingFile incomingFile = new IncomingFile
				{
					Id = text,
					Name = SafeName(PhoneNotification.Str(j, "name")),
					Mime = (PhoneNotification.Str(j, "mime") ?? "application/octet-stream"),
					Size = num,
					Clipboard = (j.TryGetProperty("clip", out var value4) && value4.ValueKind == JsonValueKind.True),
					TempPath = Path.Combine(TempFolder, Guid.NewGuid().ToString("N") + ".part")
				};
				incomingFile.Stream = new FileStream(incomingFile.TempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536);
				connection.Incoming[text] = incomingFile;
				Report(incomingFile, PhoneTransferState.Running);
			}
			break;
		}
		case "chunk":
		{
			if (!connection.Incoming.TryGetValue(text, out IncomingFile? value5))
			{
				break;
			}
			byte[]? array = PhoneNotification.Bytes(PhoneNotification.Str(j, "data"));
			if (array == null || value5.Received + array.Length > value5.Size)
			{
				value5.Discard();
				connection.Incoming.Remove(text);
				Report(value5, PhoneTransferState.Failed);
				break;
			}
			value5.Stream.Write(array);
			value5.Received += array.Length;
			if (DateTime.UtcNow - value5.LastReport >= ReportEvery)
			{
				Report(value5, PhoneTransferState.Running);
			}
			break;
		}
		case "end":
		{
			if (!connection.Incoming.Remove(text, out IncomingFile? value))
			{
				break;
			}
			value.Stream.Dispose();
			value.Stream = null;
			if (value.Received != value.Size)
			{
				value.Discard();
				Report(value, PhoneTransferState.Failed);
				break;
			}
			try
			{
				Directory.CreateDirectory(IncomingFolder);
				string text2 = FreePath(IncomingFolder, value.Name);
				File.Move(value.TempPath, text2);
				PhoneDevice device = connection.Device;
				Log.Write($"phone file from {device.Name}: {value.Name}, {value.Size} bytes{(value.Clipboard ? " (clipboard)" : "")}");
				PhoneFile received = new PhoneFile(device, Path.GetFileName(text2), text2, value.Mime, value.Size, value.Clipboard);
				Report(value, PhoneTransferState.Done);
				Post(() =>
				{
					FileReceived?.Invoke(received);
				});
				break;
			}
			catch (Exception ex)
			{
				Log.Error("phone file", ex);
				value.Discard();
				Report(value, PhoneTransferState.Failed);
				break;
			}
		}
		}
	}

	public async Task<bool> SendFileAsync(string path, bool clipboard = false, string? name = null)
	{
		FileInfo info = new FileInfo(path);
		if (!info.Exists || info.Length > 2147483648u)
		{
			return false;
		}
		List<Connection> list;
		lock (_gate)
		{
			list = _connections.Values.ToList();
		}
		if (list.Count == 0)
		{
			return false;
		}
		if (name == null)
		{
			name = info.Name;
		}
		return (await Task.WhenAll(list.Select((Connection c) => SendFileAsync(c, path, name, MimeOf(name), info.Length, clipboard))).ConfigureAwait(continueOnCapturedContext: false)).Any((bool ok) => ok);
	}

	private async Task<bool> SendFileAsync(Connection c, string path, string name, string mime, long size, bool clipboard)
	{
		string id = Guid.NewGuid().ToString("N");
		long sent = 0L;
		DateTime last = DateTime.MinValue;
		bool ok = false;
		try
		{
			if (!(await c.SendAsync(new
			{
				t = "file",
				id = id,
				name = name,
				size = size,
				mime = mime,
				clip = clipboard
			}).ConfigureAwait(continueOnCapturedContext: false)))
			{
				return false;
			}
			Report(new PhoneTransfer(id, name, 0L, size, Incoming: false, clipboard, PhoneTransferState.Running));
			bool result;
			await using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, useAsync: true))
			{
				byte[] buffer = new byte[262144];
				while (true)
				{
					int num;
					int read = (num = await stream.ReadAsync(buffer).ConfigureAwait(continueOnCapturedContext: false));
					if (num > 0)
					{
						if (!(await c.SendAsync(new
						{
							t = "chunk",
							id = id,
							data = Convert.ToBase64String(buffer, 0, read)
						}).ConfigureAwait(continueOnCapturedContext: false)))
						{
							result = false;
							break;
						}
						sent += read;
						if (!(DateTime.UtcNow - last < ReportEvery))
						{
							last = DateTime.UtcNow;
							Report(new PhoneTransfer(id, name, sent, size, Incoming: false, clipboard, PhoneTransferState.Running));
						}
						continue;
					}
					bool flag;
					ok = (flag = await c.SendAsync(new
					{
						t = "end",
						id = id
					}).ConfigureAwait(continueOnCapturedContext: false));
					result = flag;
					break;
				}
			}
			return result;
		}
		catch (IOException ex)
		{
			Log.Error("send file to phone", ex);
			return false;
		}
		finally
		{
			Report(new PhoneTransfer(id, name, sent, size, Incoming: false, clipboard, ok ? PhoneTransferState.Done : PhoneTransferState.Failed));
		}
	}

	private static string SafeName(string? name)
	{
		name = Path.GetFileName((name ?? "").Replace('\\', '/').Split('/').Last()).Trim().TrimEnd('.');
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			name = name.Replace(oldChar, '_');
		}
		if (name.Length > 120)
		{
			name = name.Substring(0, 80) + Path.GetExtension(name);
		}
		if (name.Length != 0)
		{
			return name;
		}
		return "файл";
	}

	private static string FreePath(string folder, string name)
	{
		string text = Path.Combine(folder, name);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(name);
		string extension = Path.GetExtension(name);
		int num = 2;
		while (File.Exists(text))
		{
			text = Path.Combine(folder, $"{fileNameWithoutExtension} ({num}){extension}");
			num++;
		}
		return text;
	}

	private static string MimeOf(string name)
	{
		string text = Path.GetExtension(name).ToLowerInvariant();
		if (text != null)
		{
			int length = text.Length;
			if (length != 4)
			{
				if (length == 5)
				{
					char c = text[1];
					if ((uint)c <= 104u)
					{
						switch (c)
						{
						case 'h':
							if (!(text == ".heic"))
							{
								break;
							}
							return "image/heic";
						case 'd':
							if (!(text == ".docx"))
							{
								break;
							}
							return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
						}
					}
					else if (c != 'j')
					{
						switch (c)
						{
						case 'w':
							if (!(text == ".webp"))
							{
								break;
							}
							return "image/webp";
						case 'x':
							if (!(text == ".xlsx"))
							{
								break;
							}
							return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
						}
					}
					else if (text == ".jpeg")
					{
						goto IL_0204;
					}
				}
			}
			else
			{
				char c = text[1];
				if ((uint)c <= 106u)
				{
					if ((uint)c <= 98u)
					{
						switch (c)
						{
						case 'b':
							if (!(text == ".bmp"))
							{
								break;
							}
							return "image/bmp";
						case 'a':
							if (!(text == ".apk"))
							{
								break;
							}
							return "application/vnd.android.package-archive";
						}
					}
					else if (c != 'g')
					{
						if (c == 'j' && text == ".jpg")
						{
							goto IL_0204;
						}
					}
					else if (text == ".gif")
					{
						return "image/gif";
					}
				}
				else
				{
					switch (c)
					{
					case 'p':
						if (!(text == ".png"))
						{
							if (!(text == ".pdf"))
							{
								break;
							}
							return "application/pdf";
						}
						return "image/png";
					case 'm':
						switch (text)
						{
						case ".mp4":
							return "video/mp4";
						case ".mov":
							return "video/quicktime";
						case ".mp3":
							return "audio/mpeg";
						}
						break;
					case 't':
						if (!(text == ".txt"))
						{
							break;
						}
						return "text/plain";
					case 'z':
						if (!(text == ".zip"))
						{
							break;
						}
						return "application/zip";
					}
				}
			}
		}
		return "application/octet-stream";
		IL_0204:
		return "image/jpeg";
	}

	[DllImport("shell32.dll")]
	private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, nint token, out nint path);

	private static string KnownDownloadsFolder()
	{
		try
		{
			if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0u, IntPtr.Zero, out var path) == 0)
			{
				string? text = Marshal.PtrToStringUni(path);
				Marshal.FreeCoTaskMem(path);
				if (!string.IsNullOrEmpty(text))
				{
					return text;
				}
			}
		}
		catch (Exception)
		{
		}
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
	}
}
