using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace Island.Services;

public sealed class FpsMeter
{
	private const string SessionName = "IslandFps";

	private static readonly Guid Dxgi = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");

	private const int PresentStart = 42;

	private readonly object _gate = new object();

	private readonly Dictionary<int, Queue<long>> _presents = new Dictionary<int, Queue<long>>();

	private TraceEventSession? _session;

	private int _users;

	public string? Problem { get; private set; }

	public bool Running => _session != null;

	public int? Current
	{
		get
		{
			if (_session == null)
			{
				return null;
			}
			GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
			if (processId == 0 || processId == Environment.ProcessId)
			{
				return null;
			}
			long tickCount = Environment.TickCount64;
			lock (_gate)
			{
				if (!_presents.TryGetValue((int)processId, out Queue<long>? value))
				{
					return null;
				}
				while (value.Count > 0 && tickCount - value.Peek() > 1000)
				{
					value.Dequeue();
				}
				return (value.Count > 0) ? new int?(value.Count) : ((int?)null);
			}
		}
	}

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

	public void Acquire()
	{
		lock (_gate)
		{
			if (_users++ > 0)
			{
				return;
			}
		}
		Start();
	}

	public void Release()
	{
		lock (_gate)
		{
			if (--_users > 0)
			{
				return;
			}
			_users = 0;
		}
		Stop();
	}

	public static bool CanTrace()
	{
		try
		{
			WindowsPrincipal windowsPrincipal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
			if (windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator))
			{
				return true;
			}
			return windowsPrincipal.IsInRole(new SecurityIdentifier("S-1-5-32-559"));
		}
		catch (Exception)
		{
			return false;
		}
	}

	private void Start()
	{
		if (ReviewSafety.Enabled) return;
		if (_session != null)
		{
			return;
		}
		if (!CanTrace())
		{
			Problem = "access";
			Log.Write("fps: no right to open the Windows trace (not an administrator, not in Performance Log Users)");
			return;
		}
		try
		{
			TraceEventSession.GetActiveSession("IslandFps")?.Stop();
			TraceEventSession session = new TraceEventSession("IslandFps")
			{
				StopOnDispose = true
			};
			session.EnableProvider(Dxgi, TraceEventLevel.Informational, ulong.MaxValue);
			session.Source.Dynamic.All += OnEvent;
			_session = session;
			Problem = null;
			Task.Run(() =>
			{
				try
				{
					session.Source.Process();
				}
				catch (Exception ex3)
				{
					Log.Caught("fps trace", ex3);
				}
			});
			Log.Write("fps: trace started");
		}
		catch (UnauthorizedAccessException ex)
		{
			Problem = "access";
			Log.Caught("fps trace: access denied", ex);
		}
		catch (Exception ex2)
		{
			Problem = "error";
			Log.Caught("fps trace", ex2);
		}
	}

	private void Stop()
	{
		TraceEventSession? session = _session;
		_session = null;
		if (session != null)
		{
			try
			{
				session.Dispose();
			}
			catch (Exception ex)
			{
				Log.Caught("fps trace stop", ex);
			}
			lock (_gate)
			{
				_presents.Clear();
			}
			Log.Write("fps: trace stopped");
		}
	}

	private void OnEvent(TraceEvent data)
	{
		if (data.ProviderGuid != Dxgi || data.ID != (TraceEventID)42)
		{
			return;
		}
		long tickCount = Environment.TickCount64;
		lock (_gate)
		{
			if (!_presents.TryGetValue(data.ProcessID, out Queue<long>? value))
			{
				value = (_presents[data.ProcessID] = new Queue<long>());
			}
			value.Enqueue(tickCount);
			while (value.Count > 0 && tickCount - value.Peek() > 2000)
			{
				value.Dequeue();
			}
		}
	}
}
