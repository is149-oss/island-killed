using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using System.Windows.Threading;
using Island.Native;
using Island.Services;
using Microsoft.Win32;

namespace Island;

public partial class App : System.Windows.Application
{
	private Mutex? _mutex;

	private NotifyIcon? _tray;

	private string? _logo;

	private SettingsWindow? _settingsWindow;

	private static string LogoUri => "pack://application:,,,/Assets/" + ((global::Island.MainWindow.HalloweenNow && Settings.Current.HalloweenLogo) ? "island-halloween.ico" : "island.ico");

	public MainWindow? Island { get; private set; }

	protected override void OnStartup(StartupEventArgs e)
	{
		_mutex = new Mutex(initiallyOwned: true, Social.SocialBuild.Enabled ? "Island.SocialHub.SingleInstance" : DeveloperAccess.Enabled ? "Island.DeveloperPro.SingleInstance" : "Island.SingleInstance", out var createdNew);
		bool flag = e.Args.Contains("--updated");
		if (!createdNew & flag)
		{
			try
			{
				createdNew = _mutex.WaitOne(TimeSpan.FromSeconds(20L));
			}
			catch (AbandonedMutexException)
			{
				createdNew = true;
			}
		}
		if (!createdNew)
		{
			Shutdown();
			return;
		}
		Task.Run((Action)UpdateService.CleanUp);
		Log.Session();
		SystemEvents.DisplaySettingsChanged += (object? _, EventArgs _) =>
		{
			Log.Write("screens changed: " + string.Join("; ", Screen.AllScreens.Select((Screen s) => $"{s.Bounds.Width}x{s.Bounds.Height}@{s.Bounds.X},{s.Bounds.Y}")));
		};
		SystemEvents.PowerModeChanged += (object _, PowerModeChangedEventArgs args) =>
		{
			Log.Write($"power: {args.Mode}");
		};
		SystemEvents.SessionSwitch += (object _, SessionSwitchEventArgs args) =>
		{
			Log.Write($"session switch: {args.Reason}");
		};
		DispatcherUnhandledException += (object _, DispatcherUnhandledExceptionEventArgs args) =>
		{
			Log.Error("UI thread", args.Exception);
			args.Handled = true;
		};
		TaskScheduler.UnobservedTaskException += (object? _, UnobservedTaskExceptionEventArgs args) =>
		{
			Log.Error("background task", args.Exception);
			args.SetObserved();
		};
		AppDomain.CurrentDomain.UnhandledException += (object _, UnhandledExceptionEventArgs args) =>
		{
			Log.Write($"FATAL (terminating={args.IsTerminating}): {args.ExceptionObject}");
		};
		SessionEnding += (object _, SessionEndingCancelEventArgs args) =>
		{
			Log.Write($"Windows session ending: {args.ReasonSessionEnding}");
		};
		int num = ((Settings.Current.MaxFps > 0) ? Settings.Current.MaxFps : Win32.GetRefreshRate());
		Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata
		{
			DefaultValue = num
		});
		base.OnStartup(e);
		AppFont.Apply();
		MainWindow island = new MainWindow();
		Island = island;
		island.Show();
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
		ToolStripMenuItem hide = new ToolStripMenuItem("Скрыть остров");
		hide.Click += (object? _, EventArgs _) =>
		{
			island.UserHidden = !island.UserHidden;
			hide.Text = (island.UserHidden ? "Показать остров" : "Скрыть остров");
			hide.Checked = island.UserHidden;
		};
		contextMenuStrip.Items.Add(hide);
		contextMenuStrip.Items.Add("Настройки…", null, (object? _, EventArgs _) =>
		{
			ShowSettings();
		});
		contextMenuStrip.Items.Add("Выход", null, (object? _, EventArgs _) =>
		{
			Log.Write("Exit requested from tray menu");
			Shutdown();
		});
		_tray = new NotifyIcon
		{
			Text = "Island",
			ContextMenuStrip = contextMenuStrip
		};
		ApplySeasonLogo();
		_tray.Visible = true;
		_tray.MouseDoubleClick += (object? _, MouseEventArgs _) =>
		{
			ShowSettings();
		};
		if (ReviewSafety.Enabled && e.Args.Contains("--review-settings"))
		{
			ShowSettings();
		}
	}

	public void ApplySeasonLogo()
	{
		string logoUri = LogoUri;
		if (!(logoUri == _logo) && _tray != null)
		{
			_logo = logoUri;
			Icon? icon = _tray.Icon;
			_tray.Icon = LoadTrayIcon(logoUri);
			if (icon != null && icon != SystemIcons.Application)
			{
				icon.Dispose();
			}
			if (_settingsWindow != null)
			{
				_settingsWindow.Icon = LogoImage();
			}
		}
	}

	public static ImageSource LogoImage()
	{
		return BitmapFrame.Create(new Uri(LogoUri));
	}

	private static Icon LoadTrayIcon(string uri)
	{
		try
		{
			StreamResourceInfo resourceStream = System.Windows.Application.GetResourceStream(new Uri(uri));
			if (resourceStream != null)
			{
				using (Stream stream = resourceStream.Stream)
				{
					return new Icon(stream, SystemInformation.SmallIconSize);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error("tray icon", ex);
		}
		return SystemIcons.Application;
	}

	public void ReleaseInstanceLock()
	{
		try
		{
			_mutex?.ReleaseMutex();
		}
		catch (ApplicationException)
		{
		}
		_mutex?.Dispose();
		_mutex = null;
	}

	public void ShowSettings(string? page = null, string? anchor = null)
	{
		if (_settingsWindow == null)
		{
			_settingsWindow = new SettingsWindow
			{
				Icon = LogoImage()
			};
			_settingsWindow.Closed += (object? _, EventArgs _) =>
			{
				_settingsWindow = null;
			};
			_settingsWindow.Show();
		}
		if (page != null)
		{
			_settingsWindow.OpenPage(page);
		}
		if (anchor != null)
		{
			_settingsWindow.ScrollToSection(anchor);
		}
		if (_settingsWindow.WindowState == WindowState.Minimized)
		{
			_settingsWindow.WindowState = WindowState.Normal;
		}
		_settingsWindow.Activate();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		Log.Write($"Exit (code {e.ApplicationExitCode})");
		ListenStats.Current.Flush();
		if (_tray != null)
		{
			_tray.Visible = false;
			_tray.Dispose();
		}
		_mutex?.Dispose();
		base.OnExit(e);
	}
}
