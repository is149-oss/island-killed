using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using Island.Native;
using Island.Pets;
using Island.Services;
using Island.Services.Phone;
using Microsoft.Win32;
using QRCoder;

namespace Island;

public partial class MainWindow : Window, IComponentConnector, IStyleConnector
{
	private sealed class LyricView(TextBlock box, double fallbackWidth, FontWeight widest)
	{
		public readonly TextBlock Box = box;

		public readonly double FallbackWidth = fallbackWidth;

		public readonly FontWeight Widest = widest;

		public Run?[] Runs = Array.Empty<Run>();

		public List<int> Pages = new List<int> { 0 };

		public int Page = -1;

		public double Width = -1.0;
	}

	private sealed class AiTypewriter
	{
		private const int Tail = 14;

		private readonly TextBlock _block;

		private readonly Action _scroll;

		private readonly DispatcherTimer _timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(16L)
		};

		private string _target = "";

		private int _shown;

		private double _fade;

		private bool _complete;

		public AiTypewriter(TextBlock block, Action scroll)
		{
			_block = block;
			_scroll = scroll;
			_timer.Tick += (object? _, EventArgs _) =>
			{
				Step();
			};
		}

		public void Show(string text)
		{
			_target = text;
			if (ReducedMotion)
			{
				Flush();
			}
			else if (!_timer.IsEnabled)
			{
				_timer.Start();
			}
		}

		public void Complete()
		{
			_complete = true;
			if (ReducedMotion || !_timer.IsEnabled)
			{
				Flush();
			}
		}

		public void Flush()
		{
			_timer.Stop();
			_shown = _target.Length;
			RenderMarkdown(_block, _target);
			_scroll();
		}

		public void Stop()
		{
			_timer.Stop();
		}

		private void Step()
		{
			int num = _target.Length - _shown;
			if (num > 0)
			{
				int num2 = Math.Max(1, (num + 11) / 12);
				_shown += num2;
				_fade = Math.Min(14.0, _fade + (double)num2);
			}
			else
			{
				if (_complete && _fade < 0.5)
				{
					Flush();
					return;
				}
				_fade = Math.Max(0.0, _fade - 1.2);
			}
			RenderMarkdown(_block, _target.Substring(0, _shown));
			FadeTail(_block, (int)Math.Round(_fade));
			_scroll();
		}

		private static void FadeTail(TextBlock block, int count)
		{
			if (count <= 0)
			{
				return;
			}
			int num = count;
			List<Run> list = block.Inlines.OfType<Run>().ToList();
			int num2 = list.Count - 1;
			while (num2 >= 0 && num > 0)
			{
				Run run = list[num2];
				string text = run.Text;
				int num3 = Math.Min(num, text.Length);
				if (num3 != 0)
				{
					System.Windows.Media.Color color = (run.Foreground as SolidColorBrush)?.Color ?? Colors.White;
					Inline previousSibling = run;
					for (int i = 0; i < num3; i++)
					{
						double num4 = ((double)(num - num3 + (num3 - 1 - i)) + 1.0) / (double)(count + 1);
						SolidColorBrush solidColorBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)((double)(int)color.A * num4), color.R, color.G, color.B));
						solidColorBrush.Freeze();
						Run run2 = new Run(text[text.Length - num3 + i].ToString())
						{
							Foreground = solidColorBrush,
							FontWeight = run.FontWeight,
							FontFamily = run.FontFamily,
							FontSize = run.FontSize
						};
						block.Inlines.InsertAfter(previousSibling, run2);
						previousSibling = run2;
					}
					if (num3 == text.Length)
					{
						block.Inlines.Remove(run);
					}
					else
					{
						string text2 = text;
						int num5 = num3;
						run.Text = text2.Substring(0, text2.Length - num5);
					}
					num -= num3;
				}
				num2--;
			}
		}
	}

	private sealed record BackdropArt(ImageSource? Blur, System.Windows.Media.Color[] Palette, System.Windows.Media.Brush[] Blobs, System.Windows.Media.Brush[] Rings, System.Windows.Media.Brush[] Aurora, System.Windows.Media.Brush AuroraBase);

	private enum CalView
	{
		Day,
		Week,
		Month
	}

	private struct ScreenPoint
	{
		public int X;

		public int Y;
	}

	private struct KbdHook
	{
		public uint VkCode;

		public uint ScanCode;

		public uint Flags;

		public uint Time;

		public nint ExtraInfo;
	}

	private delegate nint HookProc(int code, nint wParam, nint lParam);

	internal readonly record struct Bottom(double Bulge, double WaveAmp, double WaveTime, double Pull, double PullX, double Ripple = 0.0, double RipplePhase = 0.0);

	private sealed class MonitorTile
	{
		public Border Root;

		public TextBlock Value;

		public ScaleTransform Fill;
	}

	private struct LastInput
	{
		public uint Size;

		public uint Time;
	}

	private sealed record PhoneFileEntry(string Name, string Path, long Size, bool Incoming, DateTime Time, string Device);

	public sealed class DockItem
	{
		public string Title { get; init; } = "";

		public string Tooltip { get; init; } = "";

		public string? Glyph { get; init; }

		public ImageSource? Icon { get; init; }

		public FileEntry? Pin { get; init; }
	}

	private sealed class NotesFile
	{
		public List<string> Notes { get; set; } = new List<string>();

		public List<string?>? Docs { get; set; }

		public int Active { get; set; }
	}

	private sealed class Note
	{
		public string Text = "";

		public string? Xaml;
	}

	private System.Windows.Point? _toastPress;

	private readonly DownloadsService _downloads = new DownloadsService();

	private readonly LyricsService _lyrics = new LyricsService();

	private readonly DispatcherTimer _lyricsTimer = new DispatcherTimer(DispatcherPriority.Render)
	{
		Interval = TimeSpan.FromMilliseconds(120L)
	};

	private string _lang = "";

	private bool? _caps;

	private int _downloadCount;

	private bool _activityAnimating;

	private int _lyricIndex = -2;

	private string? _lyricShown;

	private DateTime _offsetShownUntil;

	private readonly List<TimeSpan> _wordStarts = new List<TimeSpan>();

	private readonly List<string> _wordTexts = new List<string>();

	private int _litWord = -2;

	private LyricView? _compactView;

	private LyricView? _playerView;

	private static readonly System.Windows.Media.Brush SungBrush = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromArgb(245, byte.MaxValue, byte.MaxValue, byte.MaxValue)));

	private static readonly System.Windows.Media.Brush UpcomingBrush = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromArgb(112, byte.MaxValue, byte.MaxValue, byte.MaxValue)));

	private string? _lastVpn;

	private string? _lastSsid;

	private bool _networkKnown;

	private const double BaseWindowWidth = 460.0;

	private const double BaseWindowHeight = 372.0;

	public const double BaseScale = 1.1;

	private readonly List<(string Role, string Text)> _aiHistory = new List<(string, string)>();

	private readonly Dictionary<string, List<string>> _aiModels = new Dictionary<string, List<string>>();

	private CancellationTokenSource? _aiRun;

	private int _aiGeneration;

	private static readonly System.Windows.Media.Brush UserBubble = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromArgb(46, byte.MaxValue, byte.MaxValue, byte.MaxValue)));

	private static readonly Regex MarkdownBits = new Regex("\\*\\*(.+?)\\*\\*|`([^`]+)`", RegexOptions.Compiled);

	private BackdropArt? _bdArt;

	private BackdropArt? _bdOld;

	private object? _bdCover;

	private System.Windows.Media.Color _bdAccent;

	private bool _bdLoop;

	private double _bdFrameT;

	private double _bdLastT;

	private double _bdTime;

	private double _bdFade;

	private double _bdSwap = 1.0;

	private double _bdLevel;

	private double _bdRingCool;

	private readonly List<(double Born, double Strength, int Color)> _bdRings = new List<(double, double, int)>();

	private double _bdPrevNorm;

	private double _bdFlux;

	private const double RingLife = 1.5;

	private readonly CalendarService _calendar = new CalendarService();

	private readonly DispatcherTimer _calendarRefreshTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMinutes(15L)
	};

	private readonly DispatcherTimer _reminderTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(20L)
	};

	private readonly HashSet<string> _reminded = new HashSet<string>();

	private static readonly System.Windows.Media.Brush LocalDot = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(79, 163, byte.MaxValue)));

	private static readonly System.Windows.Media.Brush FeedDot = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(94, 216, 122)));

	private const double CalDayHeight = 236.0;

	private const double CalWeekHeight = 280.0;

	private const double CalMonthHeight = 246.0;

	private static readonly string[] ShortWeekdays = new string[7] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };

	private static readonly System.Windows.Media.Brush CalSelected = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, byte.MaxValue, byte.MaxValue, byte.MaxValue)));

	private static readonly System.Windows.Media.Brush CalHover = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue)));

	private CalView _calView;

	private DateTime _calDate = DateTime.Today;

	private readonly SystemControls _controls = new SystemControls();

	private float? _pendingMusicVolume;

	private double _musicBeforeMute = 0.6;

	private DateTime? _browserQuietSince;

	private bool _browserLookup;

	private string? _browserProcess;

	private (string Tab, string Title, string Artist)? _browserTrack;

	private bool _userHidden;

	private bool _hiding;

	private bool _shownByKey;

	private bool _holdFlip;

	private ScaleTransform? _rootScale;

	private TranslateTransform? _rootShift;

	private readonly ScaleTransform _stateScale = new ScaleTransform();

	private uint _fgPid;

	private string _fgName = "";

	private const bool DebugOpen = false;

	private Action? _deferredToast;

	private DateTime _deferredAt;

	private static readonly TimeSpan DeferredFor = TimeSpan.FromSeconds(10L);

	private static readonly nint HwndTopmost = new IntPtr(-1);

	private bool _floating;

	private bool _movingIsland;

	private bool _floatInit;

	private System.Windows.Point _pressCursor;

	private System.Windows.Point _pressWindow;

	private bool _pressArmed;

	private readonly ScaleTransform _dragScl = new ScaleTransform(1.0, 1.0);

	private readonly RotateTransform _dragRot = new RotateTransform();

	private bool _dragHeld;

	private bool _dragLoop;

	private System.Windows.Point _dragRaw;

	private double _dragT;

	private double _fx;

	private double _fy;

	private double _fvx;

	private double _fvy;

	private double _offX;

	private double _offXv;

	private double _offY;

	private double _offYv;

	private Rect _dragPill;

	private const double RubberRange = 34.0;

	private const double TopMagnet = 16.0;

	private bool _gameActive;

	private bool _gameFps;

	private DateTime _gamePreviewUntil;

	private readonly Dictionary<string, (TextBlock Value, ScaleTransform Fill)> _gameTiles = new Dictionary<string, (TextBlock, ScaleTransform)>();

	private static readonly Dictionary<string, (string Glyph, string Label, System.Windows.Media.Color Color)> GameKinds = new Dictionary<string, (string, string, System.Windows.Media.Color)>
	{
		["fps"] = ("\ue9d9", "FPS", System.Windows.Media.Color.FromRgb(94, 216, 122)),
		["cpu"] = ("\ue950", "CPU", System.Windows.Media.Color.FromRgb(63, 184, 232)),
		["ram"] = ("\ue964", "RAM", System.Windows.Media.Color.FromRgb(240, 180, 41)),
		["gpu"] = ("\ue7f4", "GPU", System.Windows.Media.Color.FromRgb(239, 83, 80)),
		["disk"] = ("\ueda2", "ДИСК", System.Windows.Media.Color.FromRgb(79, 163, byte.MaxValue)),
		["net"] = ("\ue74b", "СЕТЬ", System.Windows.Media.Color.FromRgb(79, 195, 247))
	};

	public static readonly string[] GameItemIds = new string[6] { "fps", "cpu", "ram", "gpu", "disk", "net" };

	private double _gameNetPeak = 20.0;

	private Border? _gameFpsTile;

	private readonly List<FrameworkElement> _pumpkins = new List<FrameworkElement>();

	private readonly DispatcherTimer _seasonTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMinutes(30L)
	};

	private readonly DispatcherTimer _spiderTimer = new DispatcherTimer();

	private readonly DispatcherTimer _visitorTimer = new DispatcherTimer();

	private readonly Random _spookRandom = new Random();

	private readonly List<UIElement> _seasonHiddenIcons = new List<UIElement>();

	private bool _pumpkinsOn;

	private Canvas? _spookLayer;

	private Canvas? _spider;

	private System.Windows.Shapes.Rectangle? _thread;

	private System.Windows.Controls.Image? _spiderBody;

	private RotateTransform? _spiderSway;

	private ScaleTransform? _spiderBob;

	private const double SpiderDepth = 30.0;

	private double _threadLength = 44.0;

	private const bool HalloweenEnabled = false;

	private static DrawingImage? _pumpkinImage;

	private static DrawingImage? _spiderImage;

	private static DrawingImage? _batImage;

	private static DrawingImage? _ghostImage;

	private (bool, bool, bool, bool)? _halloweenShown;

	private System.Windows.Point _spiderAnchor = new System.Windows.Point(double.NaN, double.NaN);

	private double _spiderSpot = 0.7;

	private readonly DispatcherTimer _spiderWatch = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(40L)
	};

	private RotateTransform? _spiderNudge;

	private bool _spiderScared;

	private bool _spiderNudged;

	private DateTime _scaredUntil;

	private const int WH_KEYBOARD_LL = 13;

	private const int WM_KEYDOWN = 256;

	private const int WM_KEYUP = 257;

	private const int WM_SYSKEYDOWN = 260;

	private const int WM_SYSKEYUP = 261;

	private const uint LLKHF_INJECTED = 16u;

	private nint _hook;

	private HookProc? _hookProc;

	private bool _hotkeyDown;

	private bool _holding;

	private static readonly TimeSpan HoldAfter = TimeSpan.FromMilliseconds(300L);

	private readonly DispatcherTimer _holdTimer = new DispatcherTimer
	{
		Interval = HoldAfter
	};

	internal const double NeckIdle = 7.0;

	internal const double NeckExpanded = 13.0;

	internal const double NeckMotionMax = 9.0;

	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private bool _loopOn;

	private double _lastW;

	private double _lastH;

	private double _lastT;

	private double _rippleAmp;

	private double _rippleStart;

	private double _ripple;

	private double _ripplePhase;

	private double _bulge;

	private double _bulgeVelocity;

	private double _neck;

	private double _shoulder;

	private double _shoulderV;

	private double _floatMix;

	private int _stillFrames;

	private double _tabIndicatorUntil;

	private bool _ambientOn;

	private double _ambientFrameT;

	private double _liquidFrameT;

	private readonly DispatcherTimer _proximityTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(100L)
	};

	private static readonly double[] BarSpeed = new double[4] { 7.3, 9.1, 6.2, 8.4 };

	private static readonly double[] BarPhase = new double[4] { 0.0, 1.7, 3.1, 4.6 };

	private double _ambientLastT;

	private bool _wasDeformed;

	private double _waveAmp;

	private double _waveTime;

	private double _waveLevel;

	private double _peakAvg;

	private double _musicNorm;

	private double _sideL;

	private double _sideLv;

	private double _sideR;

	private double _sideRv;

	private double _pull;

	private double _pullV;

	private double _pullX;

	private double _clipW = -1.0;

	private double _clipH = -1.0;

	private double _shapeTop;

	private double _clipMix;

	private const double FloatingTopCorner = 24.0;

	private const double CornerReach = 1.28;

	private const double CornerPull = 0.36;

	private string? _marqueeKey;

	private const double MiniPadding = 30.0;

	private const double MiniMinWidth = 84.0;

	private bool _miniLyrics;

	private double _miniWidth;

	private bool _miniApplied = Settings.Current.MiniPill;

	private readonly Dictionary<string, MonitorTile> _monitorTiles = new Dictionary<string, MonitorTile>();

	private bool _monitorWatching;

	private double _netPeak = 20.0;

	private static readonly System.Windows.Media.Color CpuColor = System.Windows.Media.Color.FromRgb(63, 184, 232);

	private static readonly System.Windows.Media.Color RamColor = System.Windows.Media.Color.FromRgb(240, 180, 41);

	private static readonly System.Windows.Media.Color GpuColor = System.Windows.Media.Color.FromRgb(239, 83, 80);

	private static readonly System.Windows.Media.Color NetColor = System.Windows.Media.Color.FromRgb(79, 195, 247);

	private static readonly System.Windows.Media.Color DiskColor = System.Windows.Media.Color.FromRgb(79, 163, byte.MaxValue);

	private string? _shownTrack;

	private readonly TranslateTransform _trackInfoShift = new TranslateTransform();

	private const int VizBarCount = 64;

	private readonly System.Windows.Shapes.Rectangle[] _vizBars = new System.Windows.Shapes.Rectangle[64];

	private readonly double[] _vizSpeed = new double[64];

	private readonly double[] _vizPhase = new double[64];

	private readonly double[] _vizLevel = new double[64];

	private double _pulse;

	private static readonly System.Windows.Media.Brush OutCard = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 39)));

	private static readonly System.Windows.Media.Brush OutRow = Freeze(new SolidColorBrush(Colors.Transparent));

	private static readonly System.Windows.Media.Brush OutRowHover = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 46, 53)));

	private static readonly System.Windows.Media.Brush OutActive = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 46, 53)));

	private static readonly System.Windows.Media.Brush OutActiveText = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 242, 245)));

	private static readonly System.Windows.Media.Brush OutActiveIcon = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 242, 245)));

	private Popup? _outputPopup;

	private PetView? _slotPet;

	private PetView? _hangPet;

	private PetView? _sidePet;

	private Canvas? _petBack;

	private Canvas? _petFront;

	private Canvas? _hangClip;

	private TranslateTransform? _hangSlide;

	private RotateTransform? _hangSway;

	private FrameworkElement? _hangPaws;

	private Border? _slotHost;

	private bool _petLoop;

	private bool _hangShown;

	private double _petFrameAt;

	private string _petBuiltFor = "";

	private (string State, string Bubble, double Until)? _petReaction;

	private long _lastKeyAt;

	private bool _petOffline;

	private bool _petWasSleeping;

	private static readonly Dictionary<string, (string State, string Bubble, double Seconds)> Reactions = new Dictionary<string, (string, string, double)>
	{
		["notify"] = ("notify", "mail", 3.2),
		["call"] = ("alarm", "phone", 3.6),
		["calendar"] = ("alarm", "clock", 3.2),
		["update"] = ("curious", "gift", 3.2),
		["caps"] = ("startle", "caps", 1.7),
		["vol"] = ("volume", "vol", 1.3),
		["loud"] = ("startle", "vol", 1.8),
		["mute"] = ("mute", "mute", 2.0),
		["copy"] = ("copy", "", 1.8),
		["drop"] = ("pounce", "file", 1.4),
		["done"] = ("happy", "check", 2.2),
		["charge"] = ("happy", "bolt", 2.2),
		["online"] = ("happy", "wifi", 2.0),
		["bt"] = ("bt", "bt", 2.8),
		["aidone"] = ("happy", "", 1.5),
		["pet"] = ("pet", "", 2.6),
		["wake"] = ("wake", "", 1.7)
	};

	private string _petShown = "";

	private string _petShownBubble = "";

	private static readonly HashSet<string> Peek = new HashSet<string>
	{
		"notify", "alarm", "startle", "volume", "mute", "download", "happy", "copy", "pounce", "curious",
		"think", "offline", "bt", "tired"
	};

	private readonly Dictionary<string, string> _phoneShown = new Dictionary<string, string>();

	private readonly Dictionary<string, ImageSource?> _phoneIcons = new Dictionary<string, ImageSource>();

	private PhoneNotification? _phoneToast;

	private readonly List<PhoneNotification> _phoneQueue = new List<PhoneNotification>();

	private bool _phoneCardShown;

	private (string Text, Action Run)? _phoneButton1;

	private (string Text, Action Run)? _phoneButton2;

	private const double PhoneCardWidth = 372.0;

	private bool _phoneExpanded;

	private int _phoneSubTab;

	private bool _phoneOnScreen;

	private bool _cascade;

	private readonly HashSet<string> _centerSeen = new HashSet<string>();

	private string? _centerReplyId;

	private string _centerReplyDraft = "";

	private System.Windows.Controls.TextBox? _centerReplyBox;

	private Border? _centerReplyFrame;

	private bool _centerStale;

	private string? _clipFromPhone;

	private DateTime _clipFromPhoneAt;

	private DateTime _imageFromPhoneAt;

	private const string ReceivingLabel = "Принимаю с телефона";

	private const string SendingLabel = "Отправляю на телефон";

	private const int PhoneFilesSub = 2;

	private const int PhoneFilesKept = 60;

	private static readonly string PhoneFilesPath = System.IO.Path.Combine(Settings.Folder, "phone-files.json");

	private List<PhoneFileEntry>? _phoneFiles;

	private readonly HashSet<string> _filesSeen = new HashSet<string>();

	private readonly FileListStore _shelf = new FileListStore((Settings s) => s.Shelf, 40, 64, iconOnly: false);

	private readonly FileListStore _pins = new FileListStore((Settings s) => s.Pins, 8, 32, iconOnly: true);

	private static readonly System.Windows.Media.Brush TileHover = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromArgb(22, byte.MaxValue, byte.MaxValue, byte.MaxValue)));

	private static readonly System.Windows.Media.Brush DropFrameIdle = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, byte.MaxValue, byte.MaxValue, byte.MaxValue)));

	private bool _dragIn;

	private FileEntry? _pressedTile;

	private System.Windows.Point _pressPoint;

	private readonly DispatcherTimer _springTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(350L)
	};

	private int _springTab = -1;

	private int _tileLoadOrder;

	private static readonly System.Windows.Media.Brush ShelfNoteBrush = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(byte.MaxValue, 184, 107)));

	private DispatcherTimer? _shelfNoteTimer;

	private string? _shelfHintText;

	private System.Windows.Media.Brush? _shelfHintBrush;

	private Border[] _tabBadges = Array.Empty<Border>();

	private readonly int[] _badgeValues = new int[8];

	private bool _aiUnseen;

	private static readonly System.Windows.Media.Brush BadgeBrush = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 72, 77)));

	private readonly DispatcherTimer _tabHold = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(380L)
	};

	private System.Windows.Controls.Button? _tabPressed;

	private System.Windows.Point _tabPressAt;

	private double _tabGrab;

	private bool _tabDragging;

	private static readonly string NotesPath = System.IO.Path.Combine(Settings.Folder, "notes.txt");

	private static readonly System.Windows.Media.Brush SubTabActive = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, byte.MaxValue, byte.MaxValue, byte.MaxValue)));

	private readonly DispatcherTimer _notesSaveTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(800L)
	};

	private readonly DispatcherTimer _notesUndoTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(6L)
	};

	private readonly DispatcherTimer _qrTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(250L)
	};

	private readonly TodoService _todo = new TodoService();

	private bool _typing;

	private System.Windows.Controls.Primitives.TextBoxBase? _typingBox;

	private nint _previousForeground;

	private int _subTab;

	private static readonly string NotesListPath = System.IO.Path.Combine(Settings.Folder, "notes.json");

	private List<Note> _notes = new List<Note>
	{
		new Note()
	};

	private int _note;

	private bool _switchingNote;

	private bool _noteDirty;

	private Action? _notesUndo;

	private const double HeadingSize = 17.0;

	private BitmapSource? _qr;

	private double _tipHeight = 42.0;

	private FrameworkElement? _tipFor;

	private string? _tipId;

	private DateTime _tipShownAt;

	private DispatcherTimer? _tipTimer;

	private Dictionary<FrameworkElement, (string Id, string Text)>? _tips;

	private System.Windows.Point _coverDown;

	private bool _coverPressed;

	private DispatcherTimer? _coverVolumeBack;

	private Popup? _infoPopup;

	private Popup? _tracksPopup;

	private bool _statsTab;

	private const double DropSize = 28.0;

	private const double DropGap = 14.0;

	private const double DropTop = 4.0;

	private const double BridgeReach = 9.0;

	private bool _bubbleShown;

	private string _bubbleKind = "";

	private double _dropP;

	private double _dropV;

	private double _dropTarget;

	private bool _dropLoop;

	private double _dropLastT;

	private bool _downloadAnim;

	private DispatcherTimer? _waveTimerField;

	private static readonly System.Windows.Media.Color StripDownload = System.Windows.Media.Color.FromRgb(94, 216, 122);

	private static readonly System.Windows.Media.Color StripPhone = System.Windows.Media.Color.FromRgb(byte.MaxValue, 90, 79);

	private string _stripKind = "";

	private double _stripLevel;

	private double _stripStart;

	private double _stripFrameT;

	private bool _stripLoop;

	private bool _twoApplied = Settings.Current.TwoIslands;

	private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

	private static readonly TimeSpan RemindEvery = TimeSpan.FromHours(3);

	private readonly DispatcherTimer _updateTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(12L)
	};

	private bool _updating;

	private bool _updateToastShown;

	private bool _readyToastPending;

	private const double ExpandedWidth = 400.0;

	private const double TabBarHeight = 46.0;

	private const double PinsBarHeight = 60.0;

	private const int HomeTab = 0;

	private const int ShelfTab = 1;

	private const int ClipTab = 2;

	private const int TextTab = 3;

	private const int CalendarTab = 4;

	private const int WeatherTab = 5;

	private const int AiTab = 6;

	private const int PhoneTab = 7;

	private const int MonitorTab = 8;

	private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

	private static readonly System.Windows.Media.Brush FillNormal = Freeze(new SolidColorBrush(Colors.White));

	private static readonly System.Windows.Media.Brush FillCharging = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(94, 216, 122)));

	private static readonly System.Windows.Media.Brush FillLow = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(byte.MaxValue, 90, 79)));

	private readonly MediaService _media = new MediaService();

	private readonly VolumeService _volume = new VolumeService();

	private readonly BluetoothService _bluetooth = new BluetoothService();

	private DateTime _bluetoothToastAt;

	private readonly PowerService _power = new PowerService();

	private readonly ClipboardService _clipboard = new ClipboardService();

	private readonly WeatherService _weather = new WeatherService();

	private readonly DispatcherTimer _toastTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(1800L)
	};

	private readonly DispatcherTimer _hoverTimer = new DispatcherTimer();

	private readonly DispatcherTimer _tickTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(500L)
	};

	private readonly DispatcherTimer _fullscreenTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(700L)
	};

	private readonly DispatcherTimer _weatherTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMinutes(30L)
	};

	private readonly FrameworkElement[] _panels;

	private readonly FrameworkElement[] _tabPanels;

	private readonly System.Windows.Controls.Button[] _tabButtons;

	private readonly TextBlock[] _tabLabels;

	private readonly System.Windows.Shapes.Rectangle[] _compactBars;

	private readonly System.Windows.Shapes.Rectangle[] _expandedBars;

	private nint _hwnd;

	private IslandState? _state;

	private int _tab;

	private FrameworkElement? _shownTab;

	private bool _homeHasMedia = true;

	private bool _hover;

	private bool _pendingHover;

	private bool _menuOpen;

	private bool _dragOut;

	private static readonly string[] TabIds = new string[9] { "home", "shelf", "clip", "text", "calendar", "weather", "ai", "phone", "monitor" };

	private static readonly string[] TabNames = new string[9] { "Плеер", "Полка", "Буфер обмена", "Текст", "Календарь", "Погода", "ИИ-чат", "Телефон", "Мониторинг" };

	private int _ticks;

	private string _appliedFeeds = string.Join("\n", Settings.Current.CalendarFeeds);

	private System.Windows.Media.Color _accent;

	private double? _scrub;

	private string? _seekRefused;

	private bool _cityEditing;

	private bool _weatherDays;

	private System.Windows.Point? _hourlyPress;

	private double _hourlyPressOffset;

	private Action? _toastAction;

	private Action? _toastAction2;

	private bool _toastInteractive;

	private bool _toastVolume;

	private string? _toastDragPath;

	private bool DownloadsActive
	{
		get
		{
			if (Settings.Current.Downloads)
			{
				return _downloads.Active.Count > 0;
			}
			return false;
		}
	}

	private bool LyricsActive
	{
		get
		{
			if (_media.Current != null)
			{
				return _lyrics.Lines != null;
			}
			return false;
		}
	}

	private LyricView CompactView => _compactView ?? (_compactView = new LyricView(CompactTitle, 214.0, FontWeights.Bold));

	private LyricView PlayerView => _playerView ?? (_playerView = new LyricView(SourceText, 380.0, FontWeights.SemiBold));

	private static bool LyricsInCompact => Settings.Current.LyricsInCompact;

	private LyricView[] LyricViews
	{
		get
		{
			if (LyricsInCompact)
			{
				return new LyricView[2] { CompactView, PlayerView };
			}
			return new LyricView[1] { PlayerView };
		}
	}

	private static double ExpandedScale => 1.1 * (Pro ? Math.Clamp(Settings.Current.UiScale, 0.75, 1.5) : 1.0);

	private static double CollapsedScale => 1.1 * (Pro ? Math.Clamp(Settings.Current.UiScaleCollapsed ?? Settings.Current.UiScale, 0.75, 1.5) : 1.0);

	private static AiProvider AiProviderNow => AiProviders.Get(Settings.Current.AiProvider);

	private static string BackdropMode
	{
		get
		{
			if (Pro)
			{
				if (!Settings.Current.ExpRipples)
				{
					return Settings.Current.MusicBackdrop;
				}
				return "ripple";
			}
			return "none";
		}
	}

	private bool BackdropWanted
	{
		get
		{
			if (BackdropMode != "none" && _bdArt != null)
			{
				if (!MediaShown)
				{
					if (_state == IslandState.CompactMedia)
					{
						return Settings.Current.BackdropCompact;
					}
					return false;
				}
				return true;
			}
			return false;
		}
	}

	private static bool BackdropHidesTint => BackdropMode == "aurora";

	public bool UserHidden
	{
		get
		{
			return _userHidden;
		}
		set
		{
			_userHidden = value;
			if (value)
			{
				_shownByKey = false;
			}
			UpdateHidden();
			UserHiddenChanged?.Invoke();
		}
	}

	private static bool AnimateHiding
	{
		get
		{
			if (!ReducedMotion)
			{
				return Settings.Current.HideAnimation;
			}
			return false;
		}
	}

	private bool IslandFree
	{
		get
		{
			IslandState? state = _state;
			bool flag;
			if (state.HasValue)
			{
				IslandState valueOrDefault = state.GetValueOrDefault();
				if ((uint)(valueOrDefault - 3) <= 1u)
				{
					flag = true;
					goto IL_0024;
				}
			}
			flag = false;
			goto IL_0024;
			IL_0024:
			if (!flag)
			{
				return !KeepOpen;
			}
			return false;
		}
	}

	internal static bool HalloweenNow => false;

	public static bool HotkeyPaused { get; set; }

	private static bool VolumeKeysOurs
	{
		get
		{
			if (Settings.Current.ReplaceVolumeOsd)
			{
				return Settings.Current.ToastVolume;
			}
			return false;
		}
	}

	private bool MusicActive
	{
		get
		{
			bool flag = _media.Current?.IsPlaying ?? false;
			if (flag)
			{
				bool flag2;
				switch (_state)
				{
				case IslandState.CompactMedia:
				case IslandState.Expanded:
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = flag2;
			}
			if (flag)
			{
				return Root.Visibility == Visibility.Visible;
			}
			return false;
		}
	}

	private static bool MiniPill => Settings.Current.MiniPill;

	private bool MiniLyrics
	{
		get
		{
			if (LyricsActive)
			{
				return LyricsInCompact;
			}
			return false;
		}
	}

	private static string MusicVisual
	{
		get
		{
			if (!Pro)
			{
				return "pulse";
			}
			return Settings.Current.MusicVisual;
		}
	}

	private static System.Windows.Media.FontFamily OutFont => AppFont.Current;

	private static Settings P => Settings.Current;

	private static bool PetOn => P.PetEnabled;

	internal bool MusicNow => _media.Current?.IsPlaying ?? false;

	private static double PetNow => (double)Environment.TickCount64 / 1000.0;

	internal static PhoneLinkService? Phones { get; private set; }

	private static bool PhoneOn
	{
		get
		{
			if (Settings.Current.PhoneLink && Pro)
			{
				return Island.Services.License.Gate();
			}
			return false;
		}
	}

	private bool PhoneCardHeld
	{
		get
		{
			if (_phoneExpanded && _typing)
			{
				return _typingBox == PhoneReplyInput;
			}
			return false;
		}
	}

	private bool CenterReplyTyping
	{
		get
		{
			if (_typing && _centerReplyBox != null)
			{
				return _typingBox == _centerReplyBox;
			}
			return false;
		}
	}

	private bool ImageFromPhone => DateTime.Now - _imageFromPhoneAt < TimeSpan.FromSeconds(3L);

	private bool PhoneOnline
	{
		get
		{
			PhoneLinkService? link = Phones;
			if (link != null)
			{
				return link.Devices.Any((PhoneDevice d) => link.IsConnected(d.Id));
			}
			return false;
		}
	}

	private List<PhoneFileEntry> PhoneFiles
	{
		get
		{
			if (_phoneFiles != null)
			{
				return _phoneFiles;
			}
			try
			{
				_phoneFiles = (File.Exists(PhoneFilesPath) ? (JsonSerializer.Deserialize<List<PhoneFileEntry>>(File.ReadAllText(PhoneFilesPath)) ?? new List<PhoneFileEntry>()) : new List<PhoneFileEntry>());
			}
			catch (Exception ex)
			{
				Log.Error("phone files list", ex);
				_phoneFiles = new List<PhoneFileEntry>();
			}
			return _phoneFiles;
		}
	}

	private bool PhoneFilesShown
	{
		get
		{
			if (_state == IslandState.Expanded && _shownTab == PhonePanel)
			{
				return _phoneSubTab == 2;
			}
			return false;
		}
	}

	private Dictionary<FrameworkElement, (string Id, string Text)> Tips
	{
		get
		{
			Dictionary<FrameworkElement, (string, string)>? dictionary = _tips;
			if (dictionary == null)
			{
				Dictionary<FrameworkElement, (string, string)> dictionary2 = new Dictionary<FrameworkElement, (string, string)>
				{
					[HomePanel] = ("home2", "Линия внизу — громкость только этого плеера. Играет в нескольких приложениях? Значок «1 из 2» рядом с исполнителем переключает между ними"),
					[ShelfPanel] = ("shelf2", "Полка хранит ссылки, а не копии: сами файлы остаются там, где были"),
					[ClipPanel] = ("clip3", "Сохранять ли историю после перезапуска — меняется в настройках, «Вкладки и данные»"),
					[TextPanel] = ("text2", "Заметок может быть сколько угодно: «+» — новая, ПКМ по заметке — удалить. ПКМ по задаче — выполнить или удалить"),
					[CalendarPanel] = ("calendar", "Двойной клик по дню — его расписание. ПКМ по событию — ссылка. Свои календари — в настройках"),
					[AiPanel] = ("ai", "Enter — отправить, Shift+Enter — новая строка. ПКМ по ответу — копировать. Модель меняется кнопкой слева вверху"),
					[WeatherPanel] = ("weather3", "Часы листаются колёсиком или перетаскиванием. Город меняется в настройках, раздел «Вкладки и данные»")
				};
				Dictionary<FrameworkElement, (string, string)> dictionary3 = dictionary2;
				_tips = dictionary2;
				dictionary = dictionary3;
			}
			return dictionary;
		}
	}

	private bool PlayerControllable
	{
		get
		{
			if (_media.Current != null)
			{
				return PlayerButtons.IsEnabled;
			}
			return false;
		}
	}

	private static bool TwoIslands => Settings.Current.TwoIslands;

	private DispatcherTimer _waveTimer
	{
		get
		{
			if (_waveTimerField != null)
			{
				return _waveTimerField;
			}
			_waveTimerField = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(3.2)
			};
			_waveTimerField.Tick += (object? _, EventArgs _) =>
			{
				SendWave();
			};
			return _waveTimerField;
		}
	}

	private static string AnimationStyle
	{
		get
		{
			if (!Pro)
			{
				return "drop";
			}
			return Settings.Current.AnimationStyle;
		}
	}

	private bool MusicOnCollapsed
	{
		get
		{
			Island.Services.MediaState? current = _media.Current;
			if ((object)current != null && current.IsPlaying)
			{
				return Settings.Current.CollapsedMusic;
			}
			return false;
		}
	}

	private bool KeepOpen
	{
		get
		{
			if (DeveloperBranding.WorkspaceReview) return true;
			if (!_typing && !_menuOpen && !_dragOut && !_dragIn)
			{
				return _movingIsland;
			}
			return true;
		}
	}

	private static bool ReducedMotion
	{
		get
		{
			if (SystemParameters.ClientAreaAnimation)
			{
				return Settings.Current.ReduceMotion;
			}
			return true;
		}
	}

	private static bool Pro => Island.Services.License.IsPro;

	private bool HomeShown
	{
		get
		{
			if (_state == IslandState.Expanded)
			{
				return _shownTab == HomePanel;
			}
			return false;
		}
	}

	private bool MediaShown
	{
		get
		{
			if (HomeShown)
			{
				return _homeHasMedia;
			}
			return false;
		}
	}

	private static bool OpenOnClick => Settings.Current.OpenOnClick;

	private bool ToastHeld
	{
		get
		{
			if (_state == IslandState.Toast)
			{
				if (!PhoneCardHeld)
				{
					if (_toastInteractive)
					{
						if (!_hover && !_pendingHover)
						{
							return _toastTimer.IsEnabled;
						}
						return true;
					}
					return false;
				}
				return true;
			}
			return false;
		}
	}

	public event Action? UserHiddenChanged;

	internal static event Action? PhonesChanged;

	private void InitActions()
	{
		_clipboard.Captured += OnCopied;
		_clipboard.TextCopied += OnPcTextCopied;
		_clipboard.ImageCopied += OnPcImageCopied;
	}

	private void PlayToastAnim(ToastAnim anim)
	{
		Ellipse[] array = new Ellipse[2] { ToastRing, ToastRing2 };
		for (int i = 0; i < array.Length; i++)
		{
			array[i].BeginAnimation(UIElement.OpacityProperty, null);
		}
		Ellipse toastRing = ToastRing;
		double opacity = (ToastRing2.Opacity = 0.0);
		toastRing.Opacity = opacity;
		ToastIconGhost.BeginAnimation(UIElement.OpacityProperty, null);
		ToastIconGhost.Opacity = 0.0;
		ToastCheck.Visibility = Visibility.Collapsed;
		ToastIcon.BeginAnimation(UIElement.OpacityProperty, null);
		ToastIcon.Opacity = 1.0;
		if (ToastThumb.Visibility != Visibility.Visible)
		{
			ToastIcon.Visibility = Visibility.Visible;
		}
		if (ReducedMotion)
		{
			if (anim == ToastAnim.Done)
			{
				ShowCheck(instant: true);
			}
			return;
		}
		CubicEase ease = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		switch (anim)
		{
		case ToastAnim.Copy:
			Pop((ToastThumb.Visibility == Visibility.Visible) ? ((FrameworkElement)ToastThumb) : ((FrameworkElement)ToastIcon), 0.5);
			Animate(ToastIconGhost, UIElement.OpacityProperty, 0.0, 620, ease, 120, 0.8);
			Animate(ToastGhostShift, TranslateTransform.XProperty, -6.0, 520, ease, 120, 0.0);
			Animate(ToastGhostShift, TranslateTransform.YProperty, -5.0, 520, ease, 120, 0.0);
			break;
		case ToastAnim.Connect:
			Pop(ToastIcon, 0.4);
			Ripple(ToastRing, ToastRingScale, 0);
			Ripple(ToastRing2, ToastRing2Scale, 280);
			break;
		case ToastAnim.Disconnect:
			Pop(ToastIcon, 1.35);
			Animate(ToastIcon, UIElement.OpacityProperty, 0.45, 500, ease, 150, 1.0);
			break;
		case ToastAnim.Done:
			ShowCheck(instant: false);
			break;
		default:
			Pop((ToastThumb.Visibility == Visibility.Visible) ? ((FrameworkElement)ToastThumb) : ((FrameworkElement)ToastIcon), 0.4);
			break;
		}
	}

	private void Ripple(Shape ring, ScaleTransform scale, int delayMs)
	{
		ring.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(94, 216, 122));
		CubicEase easingFunction = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		RepeatBehavior repeatBehavior = new RepeatBehavior(2.0);
		TimeSpan timeSpan = TimeSpan.FromMilliseconds(950L);
		TimeSpan value = TimeSpan.FromMilliseconds(delayMs);
		ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.9, 0.0, timeSpan)
		{
			BeginTime = value,
			EasingFunction = easingFunction,
			RepeatBehavior = repeatBehavior
		});
		scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1.9, timeSpan)
		{
			BeginTime = value,
			EasingFunction = easingFunction,
			RepeatBehavior = repeatBehavior
		});
		scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, 1.9, timeSpan)
		{
			BeginTime = value,
			EasingFunction = easingFunction,
			RepeatBehavior = repeatBehavior
		});
	}

	private void ShowCheck(bool instant)
	{
		ToastIcon.Visibility = Visibility.Hidden;
		ToastThumb.Visibility = Visibility.Collapsed;
		ToastCheck.Visibility = Visibility.Visible;
		if (instant)
		{
			ToastCheckPath.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
			ToastCheckPath.StrokeDashOffset = 0.0;
			return;
		}
		SpringEase ease = new SpringEase
		{
			Damping = 0.5,
			Stiffness = 12.0
		};
		Animate(ToastCheckScale, ScaleTransform.ScaleXProperty, 1.0, 600, ease, 0, 0.2);
		Animate(ToastCheckScale, ScaleTransform.ScaleYProperty, 1.0, 600, ease, 0, 0.2);
		ToastCheckPath.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, new DoubleAnimation(8.0, 0.0, TimeSpan.FromMilliseconds(320L))
		{
			BeginTime = TimeSpan.FromMilliseconds(180L),
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseInOut
			}
		});
	}

	private void Toast_MouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (_toastVolume)
		{
			_volume.Step((e.Delta > 0) ? 0.04f : (-0.04f));
			e.Handled = true;
		}
	}

	private void ToastBar_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (_toastVolume)
		{
			ToastBar.CaptureMouse();
			SetVolumeFromBar(e);
			e.Handled = true;
		}
	}

	private void ToastBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (ToastBar.IsMouseCaptured)
		{
			SetVolumeFromBar(e);
		}
	}

	private void ToastBar_MouseUp(object sender, MouseButtonEventArgs e)
	{
		ToastBar.ReleaseMouseCapture();
	}

	private void SetVolumeFromBar(System.Windows.Input.MouseEventArgs e)
	{
		_volume.SetLevel((float)Math.Clamp(e.GetPosition(ToastBar).X / ToastBar.ActualWidth, 0.0, 1.0));
	}

	private void ToastIcon_MouseDown(object sender, MouseButtonEventArgs e)
	{
		_toastPress = ((_toastDragPath != null) ? new System.Windows.Point?(e.GetPosition(this)) : ((System.Windows.Point?)null));
	}

	private void ToastIcon_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		System.Windows.Point? toastPress = _toastPress;
		if (!toastPress.HasValue)
		{
			return;
		}
		System.Windows.Point valueOrDefault = toastPress.GetValueOrDefault();
		string? toastDragPath = _toastDragPath;
		if (toastDragPath == null || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		Vector vector = e.GetPosition(this) - valueOrDefault;
		if (Math.Abs(vector.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(vector.Y) < SystemParameters.MinimumVerticalDragDistance)
		{
			return;
		}
		_toastPress = null;
		if (!File.Exists(toastDragPath) && !Directory.Exists(toastDragPath))
		{
			return;
		}
		_toastTimer.Stop();
		_dragOut = true;
		try
		{
			DragDrop.DoDragDrop(ToastIconHost, new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new string[1] { toastDragPath }), System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move | System.Windows.DragDropEffects.Link);
		}
		catch (Exception ex)
		{
			Log.Error("toast drag", ex);
		}
		finally
		{
			_dragOut = false;
			_toastTimer.Start();
		}
	}

	private void OnCopied(ClipItem item)
	{
		PetReact("copy");
		if (!Settings.Current.ToastCopy || _state == IslandState.Expanded)
		{
			return;
		}
		bool flag = !Settings.Current.IsTabHidden("text");
		switch (item.Kind)
		{
		case ClipKind.Text:
		{
			string text = item.Text.Trim();
			bool flag3 = Uri.TryCreate(text, UriKind.Absolute, out Uri? result) && (result.Scheme == "http" || result.Scheme == "https");
			(string, Action)? tuple = (flag ? new (string, Action)?(("QR-код", () =>
			{
				OpenQr(text);
			})) : (((string, Action)?)null));
			(string, Action)? tuple2 = (flag ? new (string, Action)?(("В заметки", () =>
			{
				AppendNote(text);
			})) : (((string, Action)?)null));
			ShowToast("\ue8c8", FromPhone(item.Text) ? "Скопировано с телефона" : "Скопировано", null, "", FillNormal, flag3 ? new (string, Action)?(("Открыть", () =>
			{
				Launch(text);
			})) : tuple, 3500, symbolFont: false, new ToastExtra(item.Preview, flag3 ? tuple : tuple2, ToastAnim.Copy));
			break;
		}
		case ClipKind.Image:
		{
			string label = (ImageFromPhone ? "Картинка с телефона" : "Картинка скопирована");
			System.Windows.Media.Brush fillNormal = FillNormal;
			(string, Action)? action = ("Сохранить", () =>
			{
				SaveImage(item.Image);
			});
			string sub = $"{item.Image.PixelWidth} × {item.Image.PixelHeight}";
			ImageSource? thumb = item.Thumb;
			ShowToast("\ueb9f", label, null, "", fillNormal, action, 3500, symbolFont: false, new ToastExtra(sub, null, ToastAnim.Copy, null, thumb));
			break;
		}
		case ClipKind.Files:
		{
			string[]? files = item.Files;
			bool flag2 = !Settings.Current.IsTabHidden("shelf");
			ShowToast((files.Length == 1 && Directory.Exists(files[0])) ? "\ue8b7" : "\ue8a5", (files.Length == 1) ? "Файл скопирован" : $"Скопировано файлов: {files.Length}", null, "", FillNormal, flag2 ? new (string, Action)?(("На полку", () =>
			{
				_shelf.Add(files);
				ShowToast("\ue8b7", "Лежит на полке", null, "", FillCharging, null, 1600, symbolFont: false, new ToastExtra("Откройте остров → «Полка»", null, ToastAnim.Done));
			})) : (((string, Action)?)null), 3500, symbolFont: false, new ToastExtra(item.Preview, DragPath: (files.Length == 1) ? files[0] : null, Second: PhoneOnline ? new (string, Action)?(("На телефон", () =>
			{
				SendToPhone(files);
			})) : (((string, Action)?)null), Anim: ToastAnim.Copy));
			break;
		}
		}
	}

	private void OpenQr(string text)
	{
		SelectTab(3);
		ShowSubTab(1);
		QrInput.Text = text;
		_hoverTimer.Stop();
		_hover = true;
		ApplyState(IslandState.Expanded);
	}

	private void AppendNote(string text)
	{
		AppendToNote(text);
		ShowToast("\ue70b", "Добавлено в заметки", null, "", FillCharging, null, 1600, symbolFont: false, new ToastExtra("Остров → «Текст» → «Заметки»", null, ToastAnim.Done));
	}

	private void SaveImage(BitmapSource image)
	{
		try
		{
			string text = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Island");
			Directory.CreateDirectory(text);
			string path = System.IO.Path.Combine(text, $"Картинка {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");
			PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
			pngBitmapEncoder.Frames.Add(BitmapFrame.Create(image));
			using (FileStream stream = File.Create(path))
			{
				pngBitmapEncoder.Save(stream);
			}
			System.Windows.Media.Brush fillCharging = FillCharging;
			(string, Action)? action = ("Показать", () =>
			{
				RevealInExplorer(path);
			});
			string? fileName = System.IO.Path.GetFileName(path);
			string dragPath = path;
			ShowToast("\ueb9f", "Сохранено в «Изображения»", null, "", fillCharging, action, 4000, symbolFont: false, new ToastExtra(fileName, null, ToastAnim.Done, dragPath));
		}
		catch (Exception ex)
		{
			Log.Error("save image", ex);
			ShowToast("\ue783", "Не удалось сохранить картинку", null, "", FillLow);
		}
	}

	private static string BluetoothGlyph(string name)
	{
		string text = name.ToLowerInvariant();
		if (IsAudioDevice(text))
		{
			return "\ue7f6";
		}
		if (text.Contains("mouse") || text.Contains("мыш"))
		{
			return "\ue962";
		}
		if (text.Contains("keyboard") || text.Contains("клавиат"))
		{
			return "\ue765";
		}
		if (text.Contains("controller") || text.Contains("gamepad") || text.Contains("xbox") || text.Contains("dualsense") || text.Contains("dualshock") || text.Contains("геймпад"))
		{
			return "\ue7fc";
		}
		return "\ue702";
	}

	private static bool IsAudioDevice(string lowerName)
	{
		if (!lowerName.Contains("наушник") && !lowerName.Contains("headphone") && !lowerName.Contains("headset") && !lowerName.Contains("airpods") && !lowerName.Contains("buds") && !lowerName.Contains("гарнитур") && !lowerName.Contains("speaker") && !lowerName.Contains("колонк") && !lowerName.Contains("jbl") && !lowerName.Contains("sony wh"))
		{
			return lowerName.Contains("sony wf");
		}
		return true;
	}

	private void OnBluetoothChanged(string name, bool on)
	{
		bool flag = IsAudioDevice(name.ToLowerInvariant());
		if (on & flag)
		{
			PetReact("bt");
		}
		if (on & flag)
		{
			ShowToast(BluetoothGlyph(name), name, _volume.Muted ? 0f : _volume.Level, $"{Math.Round(_volume.Level * 100f)}", FillCharging, null, 3500, symbolFont: false, new ToastExtra("Подключено · колёсико — громкость", null, ToastAnim.Connect, null, null, Volume: true));
			return;
		}
		string icon = BluetoothGlyph(name);
		System.Windows.Media.Brush fill = (on ? FillCharging : FillNormal);
		(string, Action)? action = ("Bluetooth", () =>
		{
			Launch("ms-settings:bluetooth");
		});
		string sub = (on ? "Подключено" : "Отключено");
		ToastAnim anim = (on ? ToastAnim.Connect : ToastAnim.Disconnect);
		ShowToast(icon, name, null, "", fill, action, 3000, symbolFont: false, new ToastExtra(sub, null, anim));
	}

	private void InitActivity()
	{
		_downloads.Changed += OnDownloadsChanged;
		_downloads.Completed += (string path) =>
		{
			if (Settings.Current.Downloads)
			{
				long num = 0L;
				try
				{
					num = new FileInfo(path).Length;
				}
				catch (Exception)
				{
				}
				ShowToast("\ue896", System.IO.Path.GetFileName(path), (double?)null, "", FillCharging, ((string Text, Action Run)?)("Открыть", () =>
				{
					Launch(path);
				}), 7000, false, new ToastExtra(((num > 0) ? ("Загружено · " + DownloadsService.FormatSize(num)) : "Загружено") + " · значок можно перетащить", ((string Text, Action Run)?)("Папка", () =>
				{
					RevealInExplorer(path);
				}), ToastAnim.Done, path, (ImageSource?)null, false));
			}
		};
		try
		{
			_downloads.Init();
		}
		catch (Exception ex)
		{
			Log.Error("downloads init", ex);
		}
		_lyrics.Changed += () =>
		{
			_lyricIndex = -2;
			_lyricShown = null;
			UpdateLyrics();
			UpdateLyricsTimer();
		};
		_lyricsTimer.Tick += (object? _, EventArgs _) =>
		{
			UpdateLyrics();
		};
		_controls.Changed += OnNetworkMaybeChanged;
		ApplyAppearance();
	}

	[DllImport("user32.dll")]
	private static extern nint GetKeyboardLayout(uint threadId);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hwnd, nint pid);

	[DllImport("user32.dll")]
	private static extern short GetKeyState(int key);

	private void PollKeyboard()
	{
		bool idleShowKeyboard = Settings.Current.IdleShowKeyboard;
		LangBadge.Visibility = ((!idleShowKeyboard) ? Visibility.Collapsed : Visibility.Visible);
		if (!idleShowKeyboard)
		{
			CapsIcon.Visibility = Visibility.Collapsed;
			MiniCaps.Visibility = Visibility.Collapsed;
			return;
		}
		try
		{
			int num = (int)((long)GetKeyboardLayout(GetWindowThreadProcessId(Win32.GetForegroundWindow(), IntPtr.Zero)) & 0xFFFFL);
			string text = ((num == 0) ? _lang : CultureInfo.GetCultureInfo(num).TwoLetterISOLanguageName.ToUpperInvariant());
			if (text != _lang)
			{
				_lang = text;
				LangText.Text = text;
				if (_state == IslandState.Idle)
				{
					Pop(LangBadge, 0.7);
				}
			}
		}
		catch (Exception)
		{
		}
		bool flag = (GetKeyState(20) & 1) != 0;
		if (flag != _caps)
		{
			bool flag2 = !_caps.HasValue;
			_caps = flag;
			CapsIcon.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
			if (flag)
			{
				Pop(CapsIcon, 0.4);
			}
			if (!flag2)
			{
				PetReact("caps");
			}
			if (MiniPill)
			{
				UpdateMini();
			}
			if (!flag2 && Settings.Current.ToastCapsLock && _state != IslandState.Expanded)
			{
				ShowToast("⇪", flag ? "Caps Lock включён" : "Caps Lock выключен", null, "", FillNormal, null, 1200, symbolFont: true);
			}
		}
	}

	private void OnDownloadsChanged()
	{
		if (DownloadsActive)
		{
			List<DownloadItem> list = _downloads.Active.ToList();
			DownloadItem downloadItem = list[0];
			ActivityTitle.Text = ((list.Count > 1) ? $"{downloadItem.Name} и ещё {list.Count - 1}" : downloadItem.Name);
			double bytes = list.Sum((DownloadItem i) => i.Size);
			double num = list.Sum((DownloadItem i) => i.Speed);
			ActivityDetail.Text = ((num > 1024.0) ? (DownloadsService.FormatSize(bytes) + " · " + DownloadsService.FormatSize(num) + "/с") : DownloadsService.FormatSize(bytes));
		}
		StartActivityAnimation(DownloadsActive && _state == IslandState.Activity);
		if (!_toastTimer.IsEnabled)
		{
			ApplyState(BaseState());
		}
		UpdateBubble();
		int num2 = (DownloadsActive ? _downloads.Active.Count : 0);
		if (num2 == 0 && _downloadCount > 0)
		{
			PetReact("done");
		}
		if (num2 > _downloadCount && _state == IslandState.Activity)
		{
			Pop(ActivityTitle, 0.85);
		}
		_downloadCount = num2;
	}

	private void StartActivityAnimation(bool on)
	{
		if (on != _activityAnimating)
		{
			_activityAnimating = on;
			if (on && !ReducedMotion)
			{
				DoubleAnimation doubleAnimation = new DoubleAnimation(-2.0, 2.0, TimeSpan.FromMilliseconds(600L))
				{
					AutoReverse = true,
					RepeatBehavior = RepeatBehavior.Forever,
					EasingFunction = new SineEase()
				};
				DoubleAnimation doubleAnimation2 = new DoubleAnimation(-14.0, 34.0, TimeSpan.FromMilliseconds(1100L))
				{
					RepeatBehavior = RepeatBehavior.Forever,
					EasingFunction = new SineEase
					{
						EasingMode = EasingMode.EaseInOut
					}
				};
				Timeline.SetDesiredFrameRate(doubleAnimation, 30);
				Timeline.SetDesiredFrameRate(doubleAnimation2, 30);
				ActivityIconShift.BeginAnimation(TranslateTransform.YProperty, doubleAnimation);
				ActivityShimmerShift.BeginAnimation(TranslateTransform.XProperty, doubleAnimation2);
			}
			else
			{
				ActivityIconShift.BeginAnimation(TranslateTransform.YProperty, null);
				ActivityShimmerShift.BeginAnimation(TranslateTransform.XProperty, null);
			}
		}
	}

	private void OnTrackMaybeChanged(Island.Services.MediaState m)
	{
		_lyrics.LoadAsync((_media.IsBrowserFallback && m.Artist == "") ? "" : m.Title, m.Artist, m.Duration);
	}

	private void UpdateLyricsTimer()
	{
		int num;
		if (_lyrics.Lines != null)
		{
			Island.Services.MediaState? current = _media.Current;
			if ((object)current != null && current.IsPlaying)
			{
				num = ((_state == IslandState.CompactMedia || MediaShown) ? 1 : 0);
				goto IL_003e;
			}
		}
		num = 0;
		goto IL_003e;
		IL_003e:
		bool flag = (byte)num != 0;
		_lyricsTimer.IsEnabled = flag;
		if (!flag)
		{
			UpdateLyrics();
		}
	}

	private void UpdateLyrics()
	{
		Island.Services.MediaState? current = _media.Current;
		if (current == null || DateTime.Now < _offsetShownUntil)
		{
			return;
		}
		if (_lyrics.Lines == null)
		{
			if (_lyricIndex != -1)
			{
				_lyricIndex = -1;
				_lyricShown = null;
				CompactTitle.Effect = null;
				SourceText.Effect = null;
				CompactTitle.Text = (string.IsNullOrEmpty(current.Title) ? "Неизвестный трек" : current.Title);
				ShowTitleLarge(current);
			}
			return;
		}
		TimeSpan timeSpan = current.Position + (current.IsPlaying ? (DateTimeOffset.Now - current.PositionUpdatedAt) : TimeSpan.Zero) + TimeSpan.FromMilliseconds(Settings.Current.LyricsOffsetMs);
		int num = _lyrics.IndexAt(timeSpan + TimeSpan.FromMilliseconds(150L));
		if (_lyricIndex >= 0 && num >= 0 && num < _lyricIndex && _lyrics.Lines[_lyricIndex].Time - timeSpan < TimeSpan.FromSeconds(1.5))
		{
			PaintLyricWords(timeSpan);
			return;
		}
		if (num == _lyricIndex && _lyricShown != null)
		{
			PaintLyricWords(timeSpan);
			return;
		}
		_lyricIndex = num;
		string text = ((num >= 0) ? _lyrics.Lines[num].Text : "");
		BuildLyricWords(num, _lyricShown = ((text.Length > 0) ? text : "♪"));
		PaintLyricWords(timeSpan);
		if (!ReducedMotion && LyricsInCompact)
		{
			Animate(CompactTitleShift, TranslateTransform.YProperty, 0.0, 320, new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}, 0, 6.0);
			Animate(CompactTitle, UIElement.OpacityProperty, 1.0, 260, new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}, 0, 0.2);
		}
	}

	private void ShowTitleLarge(Island.Services.MediaState m)
	{
		SourceText.Effect = null;
		SourceText.Text = (string.IsNullOrEmpty(m.Title) ? "Неизвестный трек" : m.Title);
		SourceText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
		TitleClip.Visibility = Visibility.Collapsed;
		TitleSep.Visibility = Visibility.Collapsed;
		if (string.IsNullOrEmpty(m.Artist))
		{
			ArtistText.Text = m.SourceApp;
		}
	}

	private void ShowTitleSmall()
	{
		TitleClip.Visibility = Visibility.Visible;
		TitleSep.Visibility = (string.IsNullOrEmpty(ArtistText.Text) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void SourceText_MouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (LyricsActive)
		{
			e.Handled = true;
			Settings current = Settings.Current;
			current.LyricsOffsetMs = Math.Clamp(current.LyricsOffsetMs + ((e.Delta > 0) ? 100 : (-100)), -1500, 1500);
			current.Save();
			int lyricsOffsetMs = current.LyricsOffsetMs;
			SourceText.Effect = null;
			TextBlock sourceText = SourceText;
			string text;
			if (Math.Abs(lyricsOffsetMs) < 25)
			{
				text = "Текст: точно по треку";
			}
			else
			{
				text = ((lyricsOffsetMs > 0) ? $"Текст раньше на {(double)lyricsOffsetMs / 1000.0:0.0#} с" : $"Текст позже на {(double)(-lyricsOffsetMs) / 1000.0:0.0#} с");
			}
			sourceText.Text = text;
			_offsetShownUntil = DateTime.Now.AddSeconds(1.2);
			_lyricIndex = -2;
			_lyricShown = null;
		}
	}

	private void BuildLyricWords(int index, string shown)
	{
		_wordStarts.Clear();
		_wordTexts.Clear();
		_litWord = -2;
		ShowTitleSmall();
		SourceText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
		IReadOnlyList<LyricLine>? lines = _lyrics.Lines;
		foreach (var (item, item2) in (index >= 0) ? WordTimings(lines, index) : new List<(TimeSpan, string)> { (TimeSpan.Zero, shown) })
		{
			_wordStarts.Add(item);
			_wordTexts.Add(item2);
		}
		if (!LyricsInCompact)
		{
			CompactTitle.Effect = null;
			Island.Services.MediaState? current = _media.Current;
			CompactTitle.Text = ((current == null || string.IsNullOrEmpty(current.Title)) ? "Неизвестный трек" : current.Title);
		}
		LyricView[] lyricViews = LyricViews;
		foreach (LyricView lyricView in lyricViews)
		{
			lyricView.Runs = new Run[_wordTexts.Count];
			lyricView.Width = -1.0;
			ShowLyricPage(lyricView, 0);
		}
		DropShadowEffect dropShadowEffect = new DropShadowEffect
		{
			Color = _accent,
			ShadowDepth = 0.0,
			BlurRadius = 10.0,
			Opacity = 0.55,
			RenderingBias = RenderingBias.Performance
		};
		if (LyricsInCompact)
		{
			CompactTitle.Effect = (ReducedMotion ? null : dropShadowEffect);
		}
		SourceText.Effect = (ReducedMotion ? null : dropShadowEffect);
	}

	private void PaintLyricWords(TimeSpan pos)
	{
		int num = -1;
		for (int i = 0; i < _wordStarts.Count; i++)
		{
			if (_wordStarts[i] <= pos + TimeSpan.FromMilliseconds(60L))
			{
				num = i;
			}
		}
		bool flag = (LyricsInCompact && WidthChanged(CompactView)) || WidthChanged(PlayerView);
		if (num == _litWord && !flag)
		{
			return;
		}
		_litWord = num;
		SolidColorBrush solidColorBrush = new SolidColorBrush(_accent);
		LyricView[] lyricViews = LyricViews;
		foreach (LyricView lyricView in lyricViews)
		{
			ShowLyricPage(lyricView, Math.Max(num, 0));
			for (int k = 0; k < lyricView.Runs.Length; k++)
			{
				Run? run = lyricView.Runs[k];
				if (run != null)
				{
					Run run2 = run;
					System.Windows.Media.Brush foreground;
					if (k < num)
					{
						foreground = SungBrush;
					}
					else
					{
						foreground = ((k == num) ? solidColorBrush : UpcomingBrush);
					}
					run2.Foreground = foreground;
					Run run3 = run;
					FontWeight fontWeight;
					if (lyricView != CompactView)
					{
						fontWeight = ((k == num) ? FontWeights.SemiBold : FontWeights.Normal);
					}
					else
					{
						fontWeight = ((k == num) ? FontWeights.Bold : FontWeights.SemiBold);
					}
					run3.FontWeight = fontWeight;
				}
			}
		}
	}

	private bool WidthChanged(LyricView view)
	{
		if (view.Box.ActualWidth > 20.0)
		{
			return Math.Abs(view.Box.ActualWidth - view.Width) > 1.0;
		}
		return false;
	}

	private void ShowLyricPage(LyricView view, int word)
	{
		if (view.Width < 0.0 || WidthChanged(view))
		{
			view.Width = ((view.Box.ActualWidth > 20.0) ? view.Box.ActualWidth : view.FallbackWidth);
			view.Pages = PaginateLyric(view);
			view.Page = -1;
		}
		int num = view.Pages.Count - 1;
		while (num > 0 && view.Pages[num] > word)
		{
			num--;
		}
		if (num == view.Page)
		{
			return;
		}
		view.Page = num;
		Array.Clear(view.Runs);
		view.Box.Inlines.Clear();
		int num2 = view.Pages[num];
		int num3 = ((num + 1 < view.Pages.Count) ? view.Pages[num + 1] : _wordTexts.Count);
		if (num > 0)
		{
			view.Box.Inlines.Add(new Run("… ")
			{
				Foreground = UpcomingBrush
			});
		}
		for (int i = num2; i < num3; i++)
		{
			if (i > num2)
			{
				view.Box.Inlines.Add(new Run(" "));
			}
			Run run = new Run(_wordTexts[i])
			{
				Foreground = UpcomingBrush
			};
			view.Runs[i] = run;
			view.Box.Inlines.Add(run);
		}
		if (num3 < _wordTexts.Count)
		{
			view.Box.Inlines.Add(new Run(" …")
			{
				Foreground = UpcomingBrush
			});
		}
		if (num > 0)
		{
			Animate(view.Box, UIElement.OpacityProperty, 1.0, 220, new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}, 0, 0.35);
		}
	}

	private List<int> PaginateLyric(LyricView view)
	{
		TextBlock box = view.Box;
		Typeface typeface = new Typeface(box.FontFamily, FontStyles.Normal, view.Widest, FontStretches.Normal);
		double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
		double num = view.Width - Measure("… ") - Measure(" …");
		double num2 = Measure(" ");
		List<int> list = new List<int> { 0 };
		double num3 = 0.0;
		for (int i = 0; i < _wordTexts.Count; i++)
		{
			double num4 = Measure(_wordTexts[i]);
			bool flag = i == list[list.Count - 1];
			if (!flag && num3 + num2 + num4 > num)
			{
				list.Add(i);
				num3 = num4;
			}
			else
			{
				num3 += (flag ? 0.0 : num2) + num4;
			}
		}
		if (list.Count == 2 && Measure(string.Join(" ", _wordTexts)) <= view.Width)
		{
			return new List<int> { 0 };
		}
		return list;
		double Measure(string s)
		{
			return new FormattedText(s, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, typeface, box.FontSize, System.Windows.Media.Brushes.White, dip).WidthIncludingTrailingWhitespace;
		}
	}

	private static List<(TimeSpan Start, string Text)> WordTimings(IReadOnlyList<LyricLine> lines, int index)
	{
		LyricLine lyricLine = lines[index];
		IReadOnlyList<LyricWord>? words = lyricLine.Words;
		if (words != null && words.Count > 0)
		{
			return words.Select((LyricWord w) => (Time: w.Time, Text: w.Text)).ToList();
		}
		string[] array = lyricLine.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 0)
		{
			return new List<(TimeSpan, string)> { (lyricLine.Time, "♪") };
		}
		double num = Math.Min((((index + 1 < lines.Count) ? lines[index + 1].Time : (lyricLine.Time + TimeSpan.FromSeconds(5L))) - lyricLine.Time).TotalSeconds * 0.85, Math.Max(1.5, (double)lyricLine.Text.Length * 0.11));
		double num2 = array.Sum((string w) => (double)w.Length + 1.0);
		double num3 = 0.0;
		List<(TimeSpan, string)> list = new List<(TimeSpan, string)>(array.Length);
		string[] array2 = array;
		foreach (string text in array2)
		{
			list.Add((lyricLine.Time + TimeSpan.FromSeconds(num * num3 / num2), text));
			num3 += (double)text.Length + 1.0;
		}
		return list;
	}

	private void OnNetworkMaybeChanged()
	{
		string? vpnName = _controls.VpnName;
		string? text = ((_controls.Network == NetworkKind.Wifi) ? _controls.NetworkName : null);
		if (_networkKnown && Settings.Current.ToastNetwork)
		{
			if (vpnName != _lastVpn)
			{
				ShowToast("\ue72e", (vpnName != null) ? "VPN подключён" : "VPN отключён", null, vpnName ?? "", (vpnName != null) ? FillCharging : FillNormal, null, 2200);
			}
			else if (text != _lastSsid && text != null)
			{
				ShowToast("\ue701", "Wi‑Fi: " + text, null, "", FillNormal, null, 2200);
			}
			else if (text != _lastSsid && _controls.Network == NetworkKind.None)
			{
				ShowToast("\ue871", "Нет подключения к сети", null, "", FillLow, null, 2200);
			}
		}
		OnPetNetwork(_controls.Network != NetworkKind.None);
		_networkKnown = true;
		_lastVpn = vpnName;
		_lastSsid = text;
	}

	private void ApplyStateScale(bool instant)
	{
		RootTransforms();
		double num = Math.Max(ExpandedScale, CollapsedScale);
		double num2 = ((_state == IslandState.Expanded) ? ExpandedScale : CollapsedScale) / num;
		if (!(Math.Abs(_stateScale.ScaleX - num2) < 0.001) || instant)
		{
			if (instant || ReducedMotion)
			{
				_stateScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
				_stateScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
				ScaleTransform stateScale = _stateScale;
				double scaleX = (_stateScale.ScaleY = num2);
				stateScale.ScaleX = scaleX;
			}
			else
			{
				SpringEase ease = new SpringEase
				{
					Damping = 0.8,
					Stiffness = 10.0
				};
				Animate(_stateScale, ScaleTransform.ScaleXProperty, num2, 520, ease);
				Animate(_stateScale, ScaleTransform.ScaleYProperty, num2, 520, ease);
			}
		}
	}

	private void ApplyAppearance()
	{
		Settings current = Settings.Current;
		double num = Math.Max(ExpandedScale, CollapsedScale);
		if (!(Root.LayoutTransform is ScaleTransform scaleTransform) || Math.Abs(scaleTransform.ScaleX - num) > 0.001)
		{
			Root.LayoutTransform = ((Math.Abs(num - 1.0) < 0.001) ? Transform.Identity : new ScaleTransform(num, num));
			Width = 460.0 * num;
			Height = 372.0 * num;
			PositionWindow();
		}
		ApplyStateScale(instant: true);
		System.Windows.Media.Color fill = (Pro ? ParseColor(current.IslandColor) : ((System.Windows.Media.Color?)null)) ?? Colors.Black;
		bool outline = !Pro || current.Outline;
		Shape.SetLook(fill, outline);
		_clipW = -1.0;
		BuildShape();
		ApplyAccent(_media.Current?.Accent ?? MediaService.DefaultAccent, force: true);
	}

	private void UpdateGlowEffect()
	{
		bool flag = Root.Visibility == Visibility.Visible;
		bool flag2 = (Pro && Settings.Current.Glow && _media.Current != null) & flag;
		bool flag3 = (!flag2 & flag) && Settings.Current.Shadow;
		if (!flag2 && !flag3)
		{
			Shape.Effect = null;
			return;
		}
		DropShadowEffect? dropShadowEffect = Shape.Effect as DropShadowEffect;
		if (dropShadowEffect == null)
		{
			dropShadowEffect = new DropShadowEffect
			{
				RenderingBias = RenderingBias.Performance
			};
			Shape.Effect = dropShadowEffect;
		}
		if (flag2)
		{
			dropShadowEffect.ShadowDepth = 0.0;
			dropShadowEffect.BlurRadius = 26.0;
			dropShadowEffect.Color = _accent;
			dropShadowEffect.Opacity = Settings.Current.GlowStrength;
		}
		else
		{
			dropShadowEffect.ShadowDepth = 3.0;
			dropShadowEffect.Direction = 270.0;
			dropShadowEffect.BlurRadius = 16.0;
			dropShadowEffect.Color = Colors.Black;
			dropShadowEffect.Opacity = 0.4;
		}
	}

	private static System.Windows.Media.Color? ParseColor(string? hex)
	{
		try
		{
			return (hex != null) ? new System.Windows.Media.Color?((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)) : ((System.Windows.Media.Color?)null);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private void InitAi()
	{
		UpdateAiState();
	}

	private void UpdateAiState()
	{
		AiProvider aiProviderNow = AiProviderNow;
		bool flag = AiProviders.Key(aiProviderNow) != null;
		AiInput.IsEnabled = flag;
		AiFrame.Opacity = (flag ? 1.0 : 0.4);
		AiModelButton.Visibility = ((!flag) ? Visibility.Hidden : Visibility.Visible);
		TextBlock aiModelText = AiModelText;
		string? aiModel = Settings.Current.AiModel;
		aiModelText.Text = ((aiModel != null) ? ShortModel(aiModel) : "Модель по умолчанию");
		AiSetupButton.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		TextBlock aiEmptyText = AiEmptyText;
		string text;
		if (flag)
		{
			if (aiProviderNow.Free)
			{
				text = "Спросите что угодно. Отвечает " + aiProviderNow.Name + " — бесплатная модель на вашем ключе";
			}
			else
			{
				text = ((aiProviderNow.Id == AiProviders.Custom.Id) ? "Спросите что угодно. Отвечает ваш сервер" : ("Спросите что угодно. Отвечает " + aiProviderNow.Name + " на вашем ключе"));
			}
		}
		else if (aiProviderNow.Id == AiProviders.Custom.Id)
		{
			text = "Впишите адрес своего сервера в настройках, раздел «ИИ-чат»";
		}
		else
		{
			text = (aiProviderNow.Free ? "Чату нужен бесплатный API-ключ. Получить его — пара минут, в настройках есть пошаговый гайд" : ("Чату нужен API-ключ " + aiProviderNow.Name + ". Вставьте его в настройках, раздел «ИИ-чат»"));
		}
		aiEmptyText.Text = text;
		UpdateAiEmpty();
	}

	private void UpdateAiEmpty()
	{
		bool flag = AiMessages.Children.Count == 0;
		AiEmpty.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		AiNewButton.Visibility = (flag ? Visibility.Hidden : Visibility.Visible);
	}

	internal static string ShortModel(string id)
	{
		string text = (id.Contains('/') ? id.Substring(id.LastIndexOf('/') + 1) : id);
		if (!text.EndsWith(":free"))
		{
			return text;
		}
		string text2 = text;
		return text2.Substring(0, text2.Length - 5);
	}

	private void AiSetup_Click(object sender, RoutedEventArgs e)
	{
		((App)System.Windows.Application.Current).ShowSettings("tabs", "ИИ-чат");
	}

	private void AiInput_TextChanged(object sender, TextChangedEventArgs e)
	{
		AiPlaceholder.Visibility = ((AiInput.Text.Length != 0) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void AiInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.Return && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
		{
			e.Handled = true;
			AiSendAsync();
		}
	}

	private void AiSend_Click(object sender, RoutedEventArgs e)
	{
		if (_aiRun != null)
		{
			_aiRun.Cancel();
		}
		else
		{
			AiSendAsync();
		}
	}

	private void AiNew_Click(object sender, RoutedEventArgs e)
	{
		_aiRun?.Cancel();
		_aiGeneration++;
		_aiHistory.Clear();
		AiMessages.Children.Clear();
		UpdateAiEmpty();
	}

	private async Task<List<string>?> AiModelsAsync(string key)
	{
		AiProvider aiProviderNow = AiProviderNow;
		string cacheKey = aiProviderNow.Id + " " + aiProviderNow.BaseUrl;
		if (_aiModels.TryGetValue(cacheKey, out List<string>? value))
		{
			return value;
		}
		try
		{
			List<string> list = await AiService.ListModelsAsync(aiProviderNow, key);
			_aiModels[cacheKey] = list;
			return list;
		}
		catch (Exception ex)
		{
			AddAiError((ex is AiException) ? ex.Message : "Не удалось получить список моделей — проверьте интернет");
			Log.Error("ai models", ex);
			return null;
		}
	}

	private static string? PickDefaultModel(AiProvider provider, List<string> models)
	{
		string[] array = provider.Preferred ?? Array.Empty<string>();
		foreach (string want in array)
		{
			string? text = models.FirstOrDefault((string m) => m.Equals(want, StringComparison.OrdinalIgnoreCase)) ?? models.FirstOrDefault((string m) => m.Contains(want, StringComparison.OrdinalIgnoreCase));
			if (text != null)
			{
				return text;
			}
		}
		string[] array2 = new string[8] { "gemini", "llama", "deepseek", "gemma", "qwen", "mistral", "gpt", "nemotron" };
		string[] avoid = new string[12]
		{
			"nano", "mini", "small", "lite", "xs", "tiny", "code", "coder", "reason", "vl",
			"vision", "preview"
		};
		array = array2;
		foreach (string family in array)
		{
			string? text2 = models.Where((string m) => m.Contains(family, StringComparison.OrdinalIgnoreCase) && General(m)).OrderByDescending(Billions).FirstOrDefault();
			if (text2 != null)
			{
				return text2;
			}
		}
		return models.FirstOrDefault(General) ?? models.FirstOrDefault();
		bool General(string m)
		{
			return !avoid.Any((string a) => m.Contains(a, StringComparison.OrdinalIgnoreCase));
		}
	}

	private static int Billions(string model)
	{
		return (from m in Regex.Matches(model, "(\\d+)b\\b", RegexOptions.IgnoreCase)
			select int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();
	}

	private async void AiModel_Click(object sender, RoutedEventArgs e)
	{
		string? text = AiProviders.Key(AiProviderNow);
		if (text == null)
		{
			return;
		}
		List<string>? list = await AiModelsAsync(text);
		if (list == null || list.Count == 0)
		{
			return;
		}
		string? current = Settings.Current.AiModel;
		ShowMenu(AiModelButton, (from m in list.Take(40)
			select ((string, bool, Action))(((m == current) ? "✓  " : "     ") + ShortModel(m), true, () =>
			{
				Settings.Current.AiModel = m;
				Settings.Current.Save();
				AiModelText.Text = ShortModel(m);
				Pop(AiModelButton, 0.85);
			})).ToArray());
	}

	private async Task AiSendAsync()
	{
		string text = AiInput.Text.Trim();
		if (text.Length == 0 || _aiRun != null || !Island.Services.License.Gate())
		{
			return;
		}
		AiProvider provider = AiProviderNow;
		string? key = AiProviders.Key(provider);
		if (key == null)
		{
			UpdateAiState();
			return;
		}
		AiInput.Text = "";
		AddAiBubble(text, user: true);
		_aiHistory.Add(("user", text));
		int generation = _aiGeneration;
		TextBlock answer = AddAiBubble("", user: false);
		StartThinking(answer);
		_aiRun = new CancellationTokenSource();
		AiSendIcon.Text = "\ue71a";
		AiSendButton.ToolTip = "Остановить";
		StringBuilder reply = new StringBuilder();
		AiTypewriter typing = new AiTypewriter(answer, () =>
		{
			AiScroll.ScrollToEnd();
		});
		try
		{
			string? model = Settings.Current.AiModel;
			if (model == null)
			{
				List<string>? list = await AiModelsAsync(key);
				model = ((list != null) ? PickDefaultModel(provider, list) : null);
				if (model == null)
				{
					throw new AiException(provider.Free ? "Не нашлось бесплатных моделей у этого сервиса" : "Сервис не назвал своих моделей — впишите модель в настройках, раздел «ИИ-чат»");
				}
				Settings.Current.AiModel = model;
				Settings.Current.Save();
				AiModelText.Text = ShortModel(model);
			}
			List<(string, string)> messages = new List<(string, string)> { ("system", SystemPrompt()) };
			messages.AddRange(_aiHistory.TakeLast(20));
			int attempt = 1;
			while (true)
			{
				try
				{
					await foreach (string item in AiService.StreamAsync(provider, key, model, messages, _aiRun.Token))
					{
						if (reply.Length == 0)
						{
							StopThinking(answer);
						}
						reply.Append(item);
						typing.Show(reply.ToString());
					}
					typing.Complete();
				}
				catch (HttpRequestException ex) when (reply.Length == 0 && attempt < 2)
				{
					Log.Error("ai chat, retrying", ex);
					await Task.Delay(700, _aiRun.Token);
					goto IL_04f3;
				}
				break;
				IL_04f3:
				attempt++;
			}
			if (reply.Length == 0)
			{
				throw new AiException("Модель ничего не ответила — попробуйте ещё раз или другую модель");
			}
			_aiHistory.Add(("assistant", reply.ToString()));
			MarkAiAnswered();
			PetReact("aidone");
		}
		catch (OperationCanceledException)
		{
			if (reply.Length > 0)
			{
				typing.Flush();
			}
			else
			{
				StopThinking(answer);
			}
			if (generation == _aiGeneration)
			{
				if (reply.Length > 0)
				{
					_aiHistory.Add(("assistant", reply.ToString()));
				}
				else
				{
					AiMessages.Children.Remove((UIElement)answer.Parent);
				}
			}
		}
		catch (Exception ex3)
		{
			typing.Stop();
			StopThinking(answer);
			AiMessages.Children.Remove((UIElement)answer.Parent);
			if (generation == _aiGeneration && _aiHistory.Count > 0)
			{
				_aiHistory.RemoveAt(_aiHistory.Count - 1);
			}
			AddAiError((ex3 is AiException) ? ex3.Message : ("Связь с " + provider.Name + " оборвалась — попробуйте ещё раз. Если повторяется, проверьте интернет: в некоторых странах сервис работает только через VPN"));
			Log.Error($"ai chat ({provider.Id}, proxy: {((NetProxy.Current() == null) ? "none" : "custom")})", ex3);
		}
		finally
		{
			_aiRun.Dispose();
			_aiRun = null;
			AiSendIcon.Text = "\ue724";
			AiSendButton.ToolTip = "Отправить";
			UpdateAiEmpty();
		}
	}

	private static string SystemPrompt()
	{
		return "Ты — ассистент внутри Island, небольшого виджета вверху экрана Windows. Отвечай на языке пользователя." + (Settings.Current.AiShortAnswers ? " Отвечай кратко: обычно 1–4 предложения, без длинных списков и таблиц." : "");
	}

	private TextBlock AddAiBubble(string text, bool user)
	{
		TextBlock block = new TextBlock
		{
			Text = text,
			FontSize = 13.0,
			TextWrapping = TextWrapping.Wrap,
			Foreground = (user ? FillNormal : ((System.Windows.Media.Brush)FindResource("TextPrimary"))),
			LineHeight = 18.0
		};
		FrameworkElement item = (user ? new Border
		{
			Child = block,
			Background = UserBubble,
			CornerRadius = new CornerRadius(14.0, 14.0, 4.0, 14.0),
			Padding = new Thickness(10.0, 6.0, 10.0, 7.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
			MaxWidth = 300.0,
			Margin = new Thickness(40.0, 6.0, 0.0, 2.0)
		} : new Border
		{
			Child = block,
			Margin = new Thickness(2.0, 6.0, 24.0, 2.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
			Background = System.Windows.Media.Brushes.Transparent
		});
		item.MouseRightButtonUp += (object _, MouseButtonEventArgs e) =>
		{
			ShowMenu(item, ("Копировать", true, () =>
			{
				try
				{
					ReviewClipboard.SetText(PlainText(block));
				}
				catch (Exception)
				{
				}
			}));
			e.Handled = true;
		};
		item.Loaded += PopIn_Loaded;
		AiMessages.Children.Add(item);
		UpdateAiEmpty();
		AiScroll.ScrollToEnd();
		return block;
	}

	private static string PlainText(TextBlock block)
	{
		if (block.Inlines.Count <= 0)
		{
			return block.Text;
		}
		return new TextRange(block.ContentStart, block.ContentEnd).Text;
	}

	private void AddAiError(string message)
	{
		TextBlock textBlock = AddAiBubble(message, user: false);
		textBlock.Foreground = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromRgb(byte.MaxValue, 138, 128)));
		textBlock.FontSize = 12.0;
	}

	private void StartThinking(TextBlock block)
	{
		block.Text = "•  •  •";
		block.Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary");
		if (!ReducedMotion)
		{
			block.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(600L))
			{
				AutoReverse = true,
				RepeatBehavior = RepeatBehavior.Forever,
				EasingFunction = new SineEase()
			});
		}
	}

	private void StopThinking(TextBlock block)
	{
		block.BeginAnimation(UIElement.OpacityProperty, null);
		block.Opacity = 1.0;
		block.Text = "";
		block.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
	}

	private static void RenderMarkdown(TextBlock block, string text)
	{
		block.Inlines.Clear();
		string[] array = text.Replace("\r", "").Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i];
			bool flag = false;
			if (text2.StartsWith("#"))
			{
				text2 = text2.TrimStart('#').TrimStart();
				flag = true;
			}
			if (text2.StartsWith("- ") || text2.StartsWith("* "))
			{
				text2 = "•  " + text2.Substring(2);
			}
			int num = 0;
			foreach (Match item in MarkdownBits.Matches(text2))
			{
				if (item.Index > num)
				{
					InlineCollection inlines = block.Inlines;
					string text3 = text2;
					int num2 = num;
					inlines.Add(new Run(text3.Substring(num2, item.Index - num2))
					{
						FontWeight = (flag ? FontWeights.SemiBold : FontWeights.Normal)
					});
				}
				if (item.Groups[1].Success)
				{
					block.Inlines.Add(new Run(item.Groups[1].Value)
					{
						FontWeight = FontWeights.SemiBold
					});
				}
				else
				{
					block.Inlines.Add(new Run(item.Groups[2].Value)
					{
						FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"),
						FontSize = 12.0
					});
				}
				num = item.Index + item.Length;
			}
			if (num < text2.Length)
			{
				block.Inlines.Add(new Run(text2.Substring(num))
				{
					FontWeight = (flag ? FontWeights.SemiBold : FontWeights.Normal)
				});
			}
			if (i < array.Length - 1)
			{
				block.Inlines.Add(new LineBreak());
			}
		}
	}

	private void SetBackdropCover(BitmapSource? cover, System.Windows.Media.Color accent)
	{
		if (cover != _bdCover || !(accent == _bdAccent) || !(_bdArt != null))
		{
			_bdCover = cover;
			_bdAccent = accent;
			_bdOld = _bdArt;
			_bdArt = MakeBackdropArt(cover, accent);
			_bdSwap = ((_bdOld == null) ? 1 : 0);
			SyncBackdrop();
		}
	}

	private void SyncBackdrop()
	{
		if (!_bdLoop && (BackdropWanted || _bdFade > 0.001))
		{
			_bdLoop = true;
			_bdLastT = _clock.Elapsed.TotalSeconds;
			Shape.Backdrop = DrawBackdrop;
			CompositionTarget.Rendering += OnBackdropFrame;
		}
	}

	private void OnBackdropFrame(object? sender, EventArgs e)
	{
		if (!FrameDue(ref _bdFrameT))
		{
			return;
		}
		double totalSeconds = _clock.Elapsed.TotalSeconds;
		if (!MusicActive && BackdropWanted && _bdFade > 0.99 && _bdOld == null && totalSeconds - _bdLastT < 0.05)
		{
			return;
		}
		double num = Math.Clamp(totalSeconds - _bdLastT, 0.005, 0.1);
		_bdLastT = totalSeconds;
		_bdFade += ((double)(BackdropWanted ? 1 : 0) - _bdFade) * Smooth(0.16, num);
		if (!BackdropWanted && _bdFade < 0.004)
		{
			_bdFade = 0.0;
			_bdLoop = false;
			_bdRings.Clear();
			CompositionTarget.Rendering -= OnBackdropFrame;
			Shape.Backdrop = null;
			Shape.InvalidateVisual();
			return;
		}
		_bdSwap = Math.Min(1.0, _bdSwap + num / 0.9);
		if (_bdSwap >= 1.0)
		{
			_bdOld = null;
		}
		double num2 = ((MusicActive && !ReducedMotion) ? _waveLevel : 0.0);
		_bdLevel += (num2 - _bdLevel) * Smooth((num2 > _bdLevel) ? 0.5 : 0.12, num);
		if (!ReducedMotion)
		{
			_bdTime += num * (MusicActive ? (1.0 + _bdLevel * 0.6) : 0.4);
		}
		if (BackdropMode == "ripple" && !ReducedMotion)
		{
			SpawnRipples(totalSeconds, num);
		}
		Shape.InvalidateVisual();
	}

	private void SpawnRipples(double now, double dt)
	{
		double num = (MusicActive ? _musicNorm : 0.0);
		double num2 = Math.Max(0.0, num - _bdPrevNorm);
		_bdPrevNorm = num;
		_bdRingCool -= dt;
		bool num3 = num2 > _bdFlux * 2.6 + 0.07 && num > 0.35 && _bdRingCool <= 0.0;
		_bdFlux += (num2 - _bdFlux) * Smooth(0.08, dt);
		if (num3)
		{
			_bdRings.Add((now, Math.Clamp(0.45 + num2 * 1.4, 0.55, 1.0), 0));
			_bdRingCool = 0.24;
			if (_bdRings.Count > 6)
			{
				_bdRings.RemoveAt(0);
			}
		}
		_bdRings.RemoveAll(((double Born, double Strength, int Color) r) => now - r.Born > 1.5);
	}

	private void DrawBackdrop(DrawingContext dc, Rect area)
	{
		if (!(_bdArt == null) && !(_bdFade < 0.004))
		{
			dc.PushOpacity(_bdFade * Math.Clamp(Settings.Current.BackdropStrength, 0.3, 1.0));
			if (_bdOld != null)
			{
				DrawBackdropArt(dc, _bdOld, area, 1.0);
			}
			DrawBackdropArt(dc, _bdArt, area, (_bdOld != null) ? SwapEase(_bdSwap) : 1.0);
			dc.Pop();
		}
	}

	private void DrawBackdropArt(DrawingContext dc, BackdropArt art, Rect area, double opacity)
	{
		double bdTime = _bdTime;
		double bdLevel = _bdLevel;
		dc.PushOpacity(opacity);
		string backdropMode = BackdropMode;
		if (!(backdropMode == "blur"))
		{
			if (!(backdropMode == "ripple"))
			{
				goto IL_01af;
			}
			DrawRipples(dc, art, area, bdLevel);
		}
		else
		{
			if (art.Blur == null)
			{
				goto IL_01af;
			}
			double num = area.Width * (1.3 + 0.07 * Math.Sin(bdTime * 0.11) + 0.05 * bdLevel);
			System.Windows.Point point = new System.Windows.Point(area.X + area.Width / 2.0 + Math.Sin(bdTime * 0.07) * area.Width * 0.07, area.Y + area.Height / 2.0 + Math.Cos(bdTime * 0.09) * area.Height * 0.25);
			dc.PushTransform(new RotateTransform(7.0 * Math.Sin(bdTime * 0.05), point.X, point.Y));
			dc.DrawImage(art.Blur, new Rect(point.X - num / 2.0, point.Y - num / 2.0, num, num));
			dc.Pop();
			dc.PushOpacity(0.5 - 0.14 * bdLevel);
			dc.DrawRectangle(System.Windows.Media.Brushes.Black, null, area);
			dc.Pop();
		}
		goto IL_01b9;
		IL_01af:
		DrawAurora(dc, art, area, bdTime, bdLevel);
		goto IL_01b9;
		IL_01b9:
		dc.Pop();
	}

	private static void DrawAurora(DrawingContext dc, BackdropArt art, Rect a, double t, double p)
	{
		double num = (0.5 + 0.5 * Math.Sin(t * 0.08)) * a.Width;
		dc.DrawRectangle(art.AuroraBase, null, new Rect(a.X - num, a.Y, a.Width * 2.0, a.Height));
		for (int i = 0; i < 3; i++)
		{
			double num2 = (double)i * 2.1;
			System.Windows.Point center = new System.Windows.Point(a.X + a.Width * (0.5 + 0.42 * Math.Sin(t * (0.19 + 0.05 * (double)i) + num2)), a.Y + a.Height * (0.5 + 0.45 * Math.Sin(t * (0.15 + 0.04 * (double)i) + num2 * 1.7)));
			double num3 = a.Width * (0.36 + 0.06 * Math.Sin(t * 0.3 + num2)) * (1.0 + 0.18 * p);
			double radiusY = Math.Max(a.Height * (0.8 + 0.1 * Math.Cos(t * 0.26 + num2)), num3 * 0.6) * (1.0 + 0.18 * p);
			dc.PushOpacity(Math.Min(1.0, 0.8 + 0.25 * p));
			dc.DrawEllipse(art.Aurora[i], null, center, num3, radiusY);
			dc.Pop();
		}
	}

	private void DrawRipples(DrawingContext dc, BackdropArt art, Rect a, double p)
	{
		Grid grid = ((_state == IslandState.Expanded) ? CoverArt : CompactCover);
		System.Windows.Point center = ((grid.ActualWidth > 0.0) ? grid.TranslatePoint(new System.Windows.Point(grid.ActualWidth / 2.0, grid.ActualHeight / 2.0), Shape) : new System.Windows.Point(a.X + 44.0, a.Y + a.Height / 2.0));
		double totalSeconds = _clock.Elapsed.TotalSeconds;
		double num = 0.0;
		foreach (var bdRing in _bdRings)
		{
			num = Math.Max(num, bdRing.Strength * Math.Pow(1.0 - Math.Clamp((totalSeconds - bdRing.Born) / 0.5, 0.0, 1.0), 2.0));
		}
		double num2 = Math.Min(1.0, 0.25 * p + 0.85 * num);
		if (num2 > 0.01)
		{
			double num3 = Math.Min(80.0, a.Height * 1.1) * (1.0 + 0.35 * num);
			dc.PushOpacity(num2);
			dc.DrawEllipse(art.Blobs[0], null, center, num3, num3 * 0.9);
			dc.Pop();
		}
		double num4 = Math.Max(center.X - a.Left, a.Right - center.X) * 1.15;
		double num5 = Math.Clamp(a.Height / a.Width * 1.8, 0.3, 1.0);
		double num6 = Math.Min(28.0, a.Height * 0.45);
		foreach (var bdRing2 in _bdRings)
		{
			double num7 = Math.Clamp((totalSeconds - bdRing2.Born) / 1.5, 0.0, 1.0);
			double num8 = num6 + (num4 - num6) * (1.0 - Math.Pow(1.0 - num7, 3.0));
			dc.PushOpacity(bdRing2.Strength * Math.Pow(1.0 - num7, 1.4));
			dc.DrawEllipse(art.Rings[bdRing2.Color], null, center, num8, Math.Max(num8 * num5, num6));
			dc.Pop();
		}
	}

	private static double SwapEase(double x)
	{
		if (!(x < 0.5))
		{
			return 1.0 - Math.Pow(-2.0 * x + 2.0, 2.0) / 2.0;
		}
		return 2.0 * x * x;
	}

	private static BackdropArt MakeBackdropArt(BitmapSource? cover, System.Windows.Media.Color accent)
	{
		ImageSource? blur = null;
		System.Windows.Media.Color[]? array = null;
		if (cover != null)
		{
			try
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(new TransformedBitmap(cover, new ScaleTransform(40.0 / (double)cover.PixelWidth, 40.0 / (double)cover.PixelHeight)), PixelFormats.Bgra32, null, 0.0);
				int pixelWidth = formatConvertedBitmap.PixelWidth;
				int pixelHeight = formatConvertedBitmap.PixelHeight;
				byte[] array2 = new byte[pixelWidth * pixelHeight * 4];
				formatConvertedBitmap.CopyPixels(array2, pixelWidth * 4, 0);
				array = CoverPalette(array2);
				blur = BlurPixels(array2, pixelWidth, pixelHeight);
			}
			catch (Exception ex)
			{
				Log.Error("cover backdrop", ex);
			}
		}
		if (array == null)
		{
			(double h, double s, double l) tuple = MediaService.ToHsl((double)(int)accent.R / 255.0, (double)(int)accent.G / 255.0, (double)(int)accent.B / 255.0);
			double item = tuple.h;
			double item2 = tuple.s;
			item2 = Math.Max(item2, 0.5);
			array = new System.Windows.Media.Color[3]
			{
				FromHue(item, item2),
				FromHue(item + 35.0, item2),
				FromHue(item - 35.0, item2)
			};
		}
		System.Windows.Media.Brush[] array3 = new System.Windows.Media.Brush[3];
		System.Windows.Media.Brush[] array4 = new System.Windows.Media.Brush[3];
		System.Windows.Media.Brush[] array5 = new System.Windows.Media.Brush[3];
		for (int i = 0; i < 3; i++)
		{
			System.Windows.Media.Color c = array[i];
			array3[i] = FrozenBrush(new RadialGradientBrush(new GradientStopCollection
			{
				new GradientStop(System.Windows.Media.Color.FromArgb(192, c.R, c.G, c.B), 0.0),
				new GradientStop(System.Windows.Media.Color.FromArgb(80, c.R, c.G, c.B), 0.45),
				new GradientStop(System.Windows.Media.Color.FromArgb(0, c.R, c.G, c.B), 0.9)
			}));
			array4[i] = FrozenBrush(new RadialGradientBrush(new GradientStopCollection
			{
				new GradientStop(System.Windows.Media.Color.FromArgb(0, c.R, c.G, c.B), 0.78),
				new GradientStop(System.Windows.Media.Color.FromArgb(40, c.R, c.G, c.B), 0.9),
				new GradientStop(System.Windows.Media.Color.FromArgb(224, c.R, c.G, c.B), 0.97),
				new GradientStop(System.Windows.Media.Color.FromArgb(0, c.R, c.G, c.B), 1.0)
			}));
			System.Windows.Media.Color color = Muted(c, 0.42);
			array5[i] = FrozenBrush(new RadialGradientBrush(new GradientStopCollection
			{
				new GradientStop(System.Windows.Media.Color.FromArgb(176, color.R, color.G, color.B), 0.0),
				new GradientStop(System.Windows.Media.Color.FromArgb(96, color.R, color.G, color.B), 0.5),
				new GradientStop(System.Windows.Media.Color.FromArgb(0, color.R, color.G, color.B), 1.0)
			}));
		}
		System.Windows.Media.Color color2 = Muted(array[0], 0.2);
		System.Windows.Media.Brush auroraBase = FrozenBrush(new LinearGradientBrush(new GradientStopCollection
		{
			new GradientStop(color2, 0.0),
			new GradientStop(Muted(array[1], 0.2), 0.33),
			new GradientStop(Muted(array[2], 0.2), 0.66),
			new GradientStop(color2, 1.0)
		}, new System.Windows.Point(0.0, 0.3), new System.Windows.Point(1.0, 0.7)));
		return new BackdropArt(blur, array, array3, array4, array5, auroraBase);
	}

	private static System.Windows.Media.Color Muted(System.Windows.Media.Color c, double light)
	{
		var (h, val, _) = MediaService.ToHsl((double)(int)c.R / 255.0, (double)(int)c.G / 255.0, (double)(int)c.B / 255.0);
		return MediaService.FromHsl(h, Math.Min(val, 0.6) * 0.75, light);
	}

	private static System.Windows.Media.Color FromHue(double hue, double sat)
	{
		return MediaService.FromHsl((hue % 360.0 + 360.0) % 360.0, Math.Min(1.0, sat), 0.6);
	}

	private static System.Windows.Media.Color[]? CoverPalette(byte[] px)
	{
		double[] array = new double[12];
		double[] array2 = new double[12];
		double[] array3 = new double[12];
		for (int i = 0; i < px.Length; i += 4)
		{
			var (num, num2, num3) = MediaService.ToHsl((double)(int)px[i + 2] / 255.0, (double)(int)px[i + 1] / 255.0, (double)(int)px[i] / 255.0);
			if (!(num2 < 0.15) && !(num3 < 0.12) && !(num3 > 0.92))
			{
				double num4 = num2 * (1.0 - Math.Abs(2.0 * num3 - 1.0));
				int num5 = (int)(num / 30.0) % 12;
				array[num5] += num4;
				array2[num5] += num * num4;
				array3[num5] += num2 * num4;
			}
		}
		List<int> list = new List<int>();
		double num6 = 0.0;
		double[] array4 = array;
		foreach (double num7 in array4)
		{
			num6 += num7;
		}
		if (num6 < 1.0)
		{
			return null;
		}
		for (int k = 0; k < 3; k++)
		{
			int num8 = -1;
			int b;
			for (b = 0; b < 12; b++)
			{
				bool flag = list.Exists((int q) => Math.Min((b - q + 12) % 12, (q - b + 12) % 12) < 2);
				if (array[b] > num6 * 0.03 && !flag && (num8 < 0 || array[b] > array[num8]))
				{
					num8 = b;
				}
			}
			if (num8 < 0)
			{
				break;
			}
			list.Add(num8);
		}
		System.Windows.Media.Color[] array5 = new System.Windows.Media.Color[3];
		for (int num9 = 0; num9 < 3; num9++)
		{
			if (num9 < list.Count)
			{
				int num10 = list[num9];
				array5[num9] = FromHue(array2[num10] / array[num10], Math.Max(0.55, array3[num10] / array[num10]));
			}
			else
			{
				var (num11, sat, _) = MediaService.ToHsl((double)(int)array5[0].R / 255.0, (double)(int)array5[0].G / 255.0, (double)(int)array5[0].B / 255.0);
				array5[num9] = FromHue(num11 + (double)((num9 == 1) ? 30 : (-30)), sat);
			}
		}
		return array5;
	}

	private static BitmapSource BlurPixels(byte[] px, int w, int h)
	{
		float[][] array = new float[3][];
		for (int i = 0; i < 3; i++)
		{
			array[i] = new float[w * h];
			for (int j = 0; j < w * h; j++)
			{
				array[i][j] = (int)px[j * 4 + i];
			}
		}
		float[] array2 = new float[w * h];
		for (int k = 0; k < 3; k++)
		{
			float[][] array3 = array;
			foreach (float[] array4 in array3)
			{
				BoxBlur(array4, array2, w, h, 3, horizontal: true);
				BoxBlur(array2, array4, w, h, 3, horizontal: false);
			}
		}
		byte[] array5 = new byte[w * h * 4];
		for (int m = 0; m < w * h; m++)
		{
			float num = array[0][m];
			float num2 = array[1][m];
			float num3 = array[2][m];
			float num4 = 0.299f * num3 + 0.587f * num2 + 0.114f * num;
			array5[m * 4] = Clamp255(num4 + (num - num4) * 1.35f);
			array5[m * 4 + 1] = Clamp255(num4 + (num2 - num4) * 1.35f);
			array5[m * 4 + 2] = Clamp255(num4 + (num3 - num4) * 1.35f);
			array5[m * 4 + 3] = byte.MaxValue;
		}
		BitmapSource bitmapSource = BitmapSource.Create(w, h, 96.0, 96.0, PixelFormats.Bgra32, null, array5, w * 4);
		bitmapSource.Freeze();
		return bitmapSource;
	}

	private static void BoxBlur(float[] src, float[] dst, int w, int h, int r, bool horizontal)
	{
		int num = (horizontal ? h : w);
		int len = (horizontal ? w : h);
		int line;
		for (line = 0; line < num; line++)
		{
			float num2 = 0f;
			for (int i = -r; i <= r; i++)
			{
				num2 += src[At(i)];
			}
			for (int j = 0; j < len; j++)
			{
				dst[At(j)] = num2 / (float)(2 * r + 1);
				num2 += src[At(j + r + 1)] - src[At(j - r)];
			}
		}
		int At(int k)
		{
			if (!horizontal)
			{
				return Math.Clamp(k, 0, len - 1) * w + line;
			}
			return line * w + Math.Clamp(k, 0, len - 1);
		}
	}

	private static byte Clamp255(float v)
	{
		return (byte)Math.Clamp(v, 0f, 255f);
	}

	private static System.Windows.Media.Brush FrozenBrush(System.Windows.Media.Brush b)
	{
		b.Freeze();
		return b;
	}

	private void InitCalendar()
	{
		_calendar.Changed += () =>
		{
			if (_shownTab == CalendarPanel)
			{
				RenderCalendar();
			}
		};
		_calendarRefreshTimer.Tick += async (object? _, EventArgs _) =>
		{
			await _calendar.RefreshAsync();
		};
		_reminderTimer.Tick += (object? _, EventArgs _) =>
		{
			CheckReminders();
		};
		_calendarRefreshTimer.Start();
		_reminderTimer.Start();
	}

	private static DateTime WeekStart(DateTime d)
	{
		return d.Date.AddDays(-((int)(d.DayOfWeek + 6) % 7));
	}

	private void CalView_Click(object sender, RoutedEventArgs e)
	{
		_calView = (CalView)int.Parse((string)((System.Windows.Controls.Button)sender).Tag);
		RenderCalendar();
	}

	private void CalPrev_Click(object sender, RoutedEventArgs e)
	{
		StepCalendar(-1);
	}

	private void CalNext_Click(object sender, RoutedEventArgs e)
	{
		StepCalendar(1);
	}

	private void CalToday_Click(object sender, RoutedEventArgs e)
	{
		_calDate = DateTime.Today;
		RenderCalendar();
	}

	private void StepCalendar(int direction)
	{
		_calDate = _calView switch
		{
			CalView.Day => _calDate.AddDays(direction), 
			CalView.Week => _calDate.AddDays(7 * direction), 
			_ => _calDate.AddMonths(direction), 
		};
		RenderCalendar();
	}

	private void CalAdd_Click(object sender, RoutedEventArgs e)
	{
		_menuOpen = true;
		try
		{
			EventDialog eventDialog = new EventDialog((_calDate < DateTime.Today) ? DateTime.Today : _calDate);
			if (eventDialog.ShowDialog() == true && eventDialog.Result != null)
			{
				_calendar.AddLocal(eventDialog.Result);
			}
		}
		finally
		{
			_menuOpen = false;
			ReleaseKeepOpen();
		}
	}

	private void RenderCalendar()
	{
		System.Windows.Controls.Button[] array = new System.Windows.Controls.Button[3] { CalDayBtn, CalWeekBtn, CalMonthBtn };
		for (int i = 0; i < array.Length; i++)
		{
			bool flag = i == (int)_calView;
			array[i].Background = (flag ? SubTabActive : System.Windows.Media.Brushes.Transparent);
			array[i].Foreground = (flag ? FillNormal : ((System.Windows.Media.Brush)FindResource("TextSecondary")));
		}
		TextBlock calTodayText = CalTodayText;
		calTodayText.Text = _calView switch
		{
			CalView.Day => Capitalize(_calDate.ToString("ddd, d MMM", Ru)).Replace(".", ""), 
			CalView.Week => WeekTitle(WeekStart(_calDate)), 
			_ => MonthYear(_calDate), 
		};
		CalStatus.Text = _calendar.Problem ?? "";
		CalWeekStrip.Visibility = ((_calView != CalView.Week) ? Visibility.Collapsed : Visibility.Visible);
		CalMonthView.Visibility = ((_calView != CalView.Month) ? Visibility.Collapsed : Visibility.Visible);
		CalMonthDivider.Visibility = CalMonthView.Visibility;
		DateTime date = _calDate.Date;
		(DateTime, DateTime) tuple = _calView switch
		{
			CalView.Day => (date, date.AddDays(1.0)), 
			CalView.Week => (WeekStart(date), WeekStart(date).AddDays(7.0)), 
			_ => (WeekStart(new DateTime(date.Year, date.Month, 1)), WeekStart(new DateTime(date.Year, date.Month, 1)).AddDays(42.0)), 
		};
		DateTime item = tuple.Item1;
		DateTime item2 = tuple.Item2;
		IReadOnlyList<CalendarEvent> readOnlyList = _calendar.Between(item, item2);
		if (_calView == CalView.Week)
		{
			RenderWeekStrip(item, readOnlyList);
		}
		if (_calView == CalView.Month)
		{
			RenderMonthGrid(item, readOnlyList);
		}
		RenderDayList(date, readOnlyList);
		double num = _calView switch
		{
			CalView.Day => 236.0, 
			CalView.Week => 280.0, 
			_ => 246.0, 
		};
		if (Math.Abs(CalendarPanel.Height - num) > 0.5)
		{
			CalendarPanel.Height = num;
			if (_state == IslandState.Expanded && _shownTab == CalendarPanel)
			{
				Resize(400.0, 46.0 + num);
			}
		}
	}

	private static string WeekTitle(DateTime monday)
	{
		DateTime dateTime = monday.AddDays(6.0);
		if (monday.Month == dateTime.Month)
		{
			return $"{monday.Day}–{dateTime.Day} {dateTime.ToString("MMM", Ru).TrimEnd('.')}";
		}
		return $"{monday.Day} {monday.ToString("MMM", Ru).TrimEnd('.')} – {dateTime.Day} {dateTime.ToString("MMM", Ru).TrimEnd('.')}";
	}

	private IReadOnlyList<CalendarEvent> EventsOn(DateTime day, IReadOnlyList<CalendarEvent> source)
	{
		return source.Where((CalendarEvent e) => e.Start < day.AddDays(1.0) && e.End > day).ToList();
	}

	private void RenderDayList(DateTime day, IReadOnlyList<CalendarEvent> visible)
	{
		CalendarList.Children.Clear();
		IReadOnlyList<CalendarEvent> readOnlyList = EventsOn(day, visible);
		bool flag = readOnlyList.Count == 0;
		CalEmpty.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		TextBlock calEmptyText = CalEmptyText;
		string text;
		if (_calView == CalView.Month)
		{
			text = day.ToString("d MMMM", Ru) + "\nсобытий нет";
		}
		else
		{
			text = ((day == DateTime.Today) ? "Сегодня событий нет" : (Capitalize(day.ToString("d MMMM", Ru)) + " — событий нет"));
		}
		calEmptyText.Text = text;
		CalEmptyHint.Visibility = ((_calView != CalView.Day || Settings.Current.CalendarFeeds.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		CalConnectLink.Visibility = ((_calView == CalView.Month || Settings.Current.CalendarFeeds.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		if (_calView != CalView.Day && !flag)
		{
			CalendarList.Children.Add(new TextBlock
			{
				Text = DayTitle(day),
				FontSize = 10.5,
				FontWeight = FontWeights.SemiBold,
				Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary"),
				Margin = new Thickness(2.0, 0.0, 0.0, 3.0)
			});
		}
		bool compact = _calView == CalView.Month;
		for (int i = 0; i < readOnlyList.Count; i++)
		{
			FrameworkElement element = EventRow(readOnlyList[i], compact);
			CalendarList.Children.Add(element);
			Stagger(element, i);
		}
	}

	private static void Stagger(FrameworkElement element, int index)
	{
		if (!ReducedMotion)
		{
			TranslateTransform target = (TranslateTransform)(element.RenderTransform = new TranslateTransform(0.0, 6.0));
			element.Opacity = 0.0;
			int delayMs = Math.Min(index, 8) * 35;
			CubicEase ease = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			Animate(element, UIElement.OpacityProperty, 1.0, 240, ease, delayMs);
			Animate(target, TranslateTransform.YProperty, 0.0, 320, ease, delayMs);
		}
	}

	private void RenderWeekStrip(DateTime monday, IReadOnlyList<CalendarEvent> events)
	{
		CalWeekStrip.Children.Clear();
		for (int i = 0; i < 7; i++)
		{
			DateTime day = monday.AddDays(i);
			IReadOnlyList<CalendarEvent> dayEvents = EventsOn(day, events);
			Border border = DayCell(day, new StackPanel
			{
				VerticalAlignment = VerticalAlignment.Center,
				Children = 
				{
					(UIElement)new TextBlock
					{
						Text = ShortWeekdays[i],
						FontSize = 10.0,
						Foreground = (System.Windows.Media.Brush)FindResource((i >= 5) ? "TextTertiary" : "TextSecondary"),
						HorizontalAlignment = System.Windows.HorizontalAlignment.Center
					},
					(UIElement)DayNumber(day, 14.0),
					(UIElement)Dots(dayEvents)
				}
			}, 12.0);
			border.Margin = new Thickness(1.5, 0.0, 1.5, 0.0);
			CalWeekStrip.Children.Add(border);
		}
	}

	private void RenderMonthGrid(DateTime start, IReadOnlyList<CalendarEvent> events)
	{
		if (CalMonthHeader.Children.Count == 0)
		{
			string[] shortWeekdays = ShortWeekdays;
			foreach (string text in shortWeekdays)
			{
				CalMonthHeader.Children.Add(new TextBlock
				{
					Text = text,
					FontSize = 10.0,
					Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary"),
					HorizontalAlignment = System.Windows.HorizontalAlignment.Center
				});
			}
		}
		CalMonthGrid.Children.Clear();
		DateTime dateTime = new DateTime(_calDate.Year, _calDate.Month, 1);
		for (int j = 0; j < 42; j++)
		{
			DateTime day = start.AddDays(j);
			StackPanel stackPanel = new StackPanel
			{
				VerticalAlignment = VerticalAlignment.Center
			};
			TextBlock textBlock = DayNumber(day, 11.5);
			if (day.Month != dateTime.Month)
			{
				textBlock.Opacity = 0.35;
			}
			stackPanel.Children.Add(textBlock);
			stackPanel.Children.Add(Dots(EventsOn(day, events)));
			Border border = DayCell(day, stackPanel, 8.0);
			border.Height = 28.0;
			border.Margin = new Thickness(0.5, 0.5, 0.5, 0.5);
			CalMonthGrid.Children.Add(border);
		}
	}

	private Border DayCell(DateTime day, UIElement content, double radius)
	{
		bool flag = day == _calDate.Date;
		Border cell = new Border
		{
			CornerRadius = new CornerRadius(radius),
			Background = (flag ? CalSelected : System.Windows.Media.Brushes.Transparent),
			Cursor = System.Windows.Input.Cursors.Hand,
			Child = content,
			ToolTip = Capitalize(day.ToString("dddd, d MMMM", Ru))
		};
		cell.MouseEnter += (object _, System.Windows.Input.MouseEventArgs _) =>
		{
			if (day != _calDate.Date)
			{
				cell.Background = CalHover;
			}
		};
		cell.MouseLeave += (object _, System.Windows.Input.MouseEventArgs _) =>
		{
			if (day != _calDate.Date)
			{
				cell.Background = System.Windows.Media.Brushes.Transparent;
			}
		};
		cell.MouseLeftButtonUp += (object _, MouseButtonEventArgs e) =>
		{
			_calDate = day;
			RenderCalendar();
		};
		cell.MouseLeftButtonDown += (object _, MouseButtonEventArgs e) =>
		{
			if (e.ClickCount == 2)
			{
				_calDate = day;
				_calView = CalView.Day;
				RenderCalendar();
				e.Handled = true;
			}
		};
		return cell;
	}

	private TextBlock DayNumber(DateTime day, double size)
	{
		return new TextBlock
		{
			Text = day.Day.ToString(CultureInfo.InvariantCulture),
			FontSize = size,
			FontWeight = ((day == DateTime.Today || day == _calDate.Date) ? FontWeights.Bold : FontWeights.SemiBold),
			Foreground = ((day == DateTime.Today) ? new SolidColorBrush(_accent) : ((System.Windows.Media.Brush)FindResource("TextPrimary"))),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center
		};
	}

	private FrameworkElement Dots(IReadOnlyList<CalendarEvent> dayEvents)
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			Height = 5.0,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
		};
		for (int i = 0; i < Math.Min(3, dayEvents.Count); i++)
		{
			stackPanel.Children.Add(new Border
			{
				Width = 4.0,
				Height = 4.0,
				CornerRadius = new CornerRadius(2.0),
				Margin = new Thickness(1.0, 0.0, 1.0, 0.0),
				Background = new SolidColorBrush(_accent)
			});
		}
		return stackPanel;
	}

	private FrameworkElement EventRow(CalendarEvent ev, bool compact = false)
	{
		if (compact)
		{
			return CompactEventRow(ev);
		}
		Grid grid = new Grid
		{
			Height = 34.0,
			Background = System.Windows.Media.Brushes.Transparent,
			Margin = new Thickness(0.0, 0.0, 0.0, 2.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(12.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(46.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		Border element = new Border
		{
			Width = 6.0,
			Height = 6.0,
			CornerRadius = new CornerRadius(3.0),
			Background = ((ev.LocalId != null) ? LocalDot : FeedDot),
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.Children.Add(element);
		bool flag = ev.Start <= DateTime.Now && ev.End > DateTime.Now;
		TextBlock element2 = new TextBlock
		{
			Text = (ev.AllDay ? "весь день" : ev.Start.ToString("HH:mm")),
			FontSize = (ev.AllDay ? 10.5 : 12.0),
			FontWeight = FontWeights.SemiBold,
			Foreground = (flag ? new SolidColorBrush(_accent) : ((System.Windows.Media.Brush)FindResource("TextSecondary"))),
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(element2, 1);
		grid.Children.Add(element2);
		TextBlock element3 = new TextBlock
		{
			Text = ev.Title,
			FontSize = 13.0,
			Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
			TextTrimming = TextTrimming.CharacterEllipsis,
			VerticalAlignment = VerticalAlignment.Center,
			ToolTip = ev.Title + (ev.AllDay ? "" : $"\n{ev.Start:HH:mm}–{ev.End:HH:mm}") + ((ev.Link != null) ? ("\n" + ev.Link) : "")
		};
		Grid.SetColumn(element3, 2);
		grid.Children.Add(element3);
		if (ev.Link != null)
		{
			System.Windows.Controls.Button button = new System.Windows.Controls.Button
			{
				Content = (IsCallLink(ev.Link) ? "Подключиться" : "Открыть"),
				Style = (Style)FindResource("ToastActionButton"),
				Margin = new Thickness(6.0, 0.0, 0.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center
			};
			button.Click += (object _, RoutedEventArgs _) =>
			{
				Launch(ev.Link);
			};
			Grid.SetColumn(button, 3);
			grid.Children.Add(button);
		}
		AttachEventMenu(grid, ev);
		return grid;
	}

	private FrameworkElement CompactEventRow(CalendarEvent ev)
	{
		bool flag = ev.Start <= DateTime.Now && ev.End > DateTime.Now;
		Border border = new Border
		{
			Background = (System.Windows.Media.Brush)FindResource("Surface"),
			CornerRadius = new CornerRadius(9.0),
			Padding = new Thickness(8.0, 5.0, 4.0, 5.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
		};
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = (ev.AllDay ? "весь день" : $"{ev.Start:HH:mm}–{ev.End:HH:mm}"),
			FontSize = 10.5,
			FontWeight = FontWeights.SemiBold,
			Foreground = (flag ? new SolidColorBrush(_accent) : ((System.Windows.Media.Brush)FindResource("TextSecondary")))
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = ev.Title,
			FontSize = 12.5,
			Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
			TextTrimming = TextTrimming.CharacterEllipsis,
			ToolTip = ev.Title + ((ev.Link != null) ? ("\n" + ev.Link) : "")
		});
		grid.Children.Add(stackPanel);
		if (ev.Link != null)
		{
			System.Windows.Controls.Button button = new System.Windows.Controls.Button
			{
				Style = (Style)FindResource("PinButton"),
				Width = 28.0,
				Height = 28.0,
				FontSize = 12.0,
				Content = (IsCallLink(ev.Link) ? "\ue714" : "\ue8a7"),
				ToolTip = (IsCallLink(ev.Link) ? "Подключиться" : "Открыть ссылку"),
				VerticalAlignment = VerticalAlignment.Center
			};
			button.Click += (object _, RoutedEventArgs _) =>
			{
				Launch(ev.Link);
			};
			Grid.SetColumn(button, 1);
			grid.Children.Add(button);
		}
		border.Child = grid;
		AttachEventMenu(border, ev);
		return border;
	}

	private void AttachEventMenu(FrameworkElement target, CalendarEvent ev)
	{
		target.MouseRightButtonUp += (object _, MouseButtonEventArgs e) =>
		{
			List<(string, bool, Action)> list = new List<(string, bool, Action)>();
			if (ev.Link != null)
			{
				list.Add(("Открыть ссылку", true, () =>
				{
					Launch(ev.Link);
				}));
			}
			list.Add(("Копировать", true, () =>
			{
				try
				{
					ReviewClipboard.SetText($"{ev.Title} — {ev.Start:d MMMM HH:mm}" + ((ev.Link != null) ? (" " + ev.Link) : ""));
				}
				catch
				{
				}
			}));
			if (ev.LocalId != null)
			{
				list.Add(("Удалить событие", true, () =>
				{
					_calendar.RemoveLocal(ev.LocalId);
				}));
			}
			ShowMenu(target, list.ToArray());
			e.Handled = true;
		};
	}

	private void CalConnect_Click(object sender, RoutedEventArgs e)
	{
		((App)System.Windows.Application.Current).ShowSettings("tabs", "Календарь");
	}

	private static string DayTitle(DateTime day)
	{
		DateTime today = DateTime.Today;
		if (day == today)
		{
			return "СЕГОДНЯ";
		}
		if (day == today.AddDays(1.0))
		{
			return "ЗАВТРА";
		}
		return day.ToString("dddd, d MMMM", Ru).ToUpper(Ru);
	}

	private static string Capitalize(string s)
	{
		if (s.Length != 0)
		{
			return char.ToUpper(s[0], Ru) + s.Substring(1);
		}
		return s;
	}

	private static string MonthYear(DateTime d)
	{
		return Capitalize(Ru.DateTimeFormat.GetMonthName(d.Month)) + " " + d.Year;
	}

	private static bool IsCallLink(string url)
	{
		string text = url.ToLowerInvariant();
		if (!text.Contains("meet.google") && !text.Contains("zoom.us") && !text.Contains("teams.") && !text.Contains("telemost") && !text.Contains("webex") && !text.Contains("whereby") && !text.Contains("discord"))
		{
			return text.Contains("jit.si");
		}
		return true;
	}

	private void CheckReminders()
	{
		if (!Settings.Current.CalendarReminders)
		{
			return;
		}
		DateTime now = DateTime.Now;
		foreach (CalendarEvent ev in _calendar.Events)
		{
			if (ev.AllDay)
			{
				continue;
			}
			double totalMinutes = (ev.Start - now).TotalMinutes;
			string text;
			if (totalMinutes > 0.0 && totalMinutes <= 10.0)
			{
				text = "soon";
			}
			else
			{
				text = ((totalMinutes > -1.0 && totalMinutes <= 0.0) ? "now" : null);
			}
			if (text == null)
			{
				continue;
			}
			string item = $"{text}|{ev.Title}|{ev.Start:O}";
			if (_reminded.Add(item))
			{
				PetReact("calendar");
				string label = ((text == "now") ? ev.Title : $"Через {Math.Max(1, (int)Math.Ceiling(totalMinutes))} мин: {ev.Title}");
				(string, Action)? action = ((ev.Link != null) ? new (string, Action)?((IsCallLink(ev.Link) ? "Подключиться" : "Открыть", () =>
				{
					Launch(ev.Link);
				})) : (((string, Action)?)null));
				ShowToast("\ue787", label, null, (text == "now") ? "сейчас" : "", FillNormal, action, 12000);
			}
		}
	}

	private void InitControls()
	{
		MusicVolume.ValueChanged += SetMusicVolume;
		MusicVolume.HoverChanged += (bool on) =>
		{
			Animate(MusicVolumeText, UIElement.OpacityProperty, on ? 1 : 0, 180, new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			});
		};
	}

	private void ApplyPlayerLayout()
	{
		bool playerVolume = Settings.Current.PlayerVolume;
		MusicVolumeRow.Visibility = ((!playerVolume) ? Visibility.Collapsed : Visibility.Visible);
		double num = (playerVolume ? 130 : 106);
		if (MediaPanel.Height != num)
		{
			MediaPanel.Height = num;
			if (HomeShown)
			{
				Resize(400.0, 46.0 + TabHeight(HomePanel));
			}
		}
	}

	private void SetMusicVolume(double v)
	{
		ShowMusicVolume(v);
		bool hasValue = _pendingMusicVolume.HasValue;
		_pendingMusicVolume = (float)v;
		if (hasValue)
		{
			return;
		}
		Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() =>
		{
			float? pendingMusicVolume = _pendingMusicVolume;
			if (pendingMusicVolume.HasValue)
			{
				float valueOrDefault = pendingMusicVolume.GetValueOrDefault();
				Island.Services.MediaState? current = _media.Current;
				if ((object)current != null)
				{
					_volume.SetAppVolume(current.SourceId, valueOrDefault);
				}
			}
			_pendingMusicVolume = null;
		}));
	}

	private void MusicMute_Click(object sender, RoutedEventArgs e)
	{
		double value = MusicVolume.Value;
		if (value > 0.001)
		{
			_musicBeforeMute = value;
		}
		double num = ((value > 0.001) ? 0.0 : Math.Max(_musicBeforeMute, 0.1));
		MusicVolume.Value = num;
		SetMusicVolume(num);
		Pop(MusicVolumeIcon);
	}

	private void ShowMusicVolume(double v)
	{
		SetText(MusicVolumeText, $"{Math.Round(v * 100.0)}%");
		string text;
		if (v < 0.001)
		{
			text = "\ue74f";
		}
		else if (v < 0.34)
		{
			text = "\ue993";
		}
		else
		{
			text = ((v < 0.67) ? "\ue994" : "\ue995");
		}
		if (MusicVolumeIcon.Text != text)
		{
			MusicVolumeIcon.Text = text;
		}
		MusicMute.ToolTip = ((v < 0.001) ? "Вернуть звук плеера" : "Выключить звук плеера");
	}

	private void UpdateMusicVolume(Island.Services.MediaState m)
	{
		if (Settings.Current.PlayerVolume && !MusicVolume.IsDragging && !_pendingMusicVolume.HasValue)
		{
			float? appVolume = _volume.GetAppVolume(m.SourceId);
			MusicVolumeRow.IsEnabled = appVolume.HasValue;
			MusicVolumeRow.Opacity = (appVolume.HasValue ? 1.0 : 0.35);
			MusicVolumeRow.ToolTip = (appVolume.HasValue ? "Громкость этого плеера — остальные звуки не меняются" : "Плеер ещё не выводит звук на это устройство");
			if (appVolume.HasValue)
			{
				MusicVolume.Value = appVolume.Value;
				ShowMusicVolume(appVolume.Value);
			}
		}
	}

	private void PollBrowserAudio()
	{
		if (!Settings.Current.BrowserAudio)
		{
			if (_media.HasBrowserAudio)
			{
				_media.SetBrowserAudio(null, null);
			}
			return;
		}
		string? text = _volume.PlayingBrowser();
		if (text != null)
		{
			_browserQuietSince = null;
			_browserProcess = text;
			LookUpBrowserTrack(text, quiet: false);
		}
		else
		{
			if (!_media.HasBrowserAudio)
			{
				return;
			}
			if (_media.BrowserControllable && _browserProcess != null)
			{
				LookUpBrowserTrack(_browserProcess, quiet: true);
				return;
			}
			DateTime valueOrDefault = _browserQuietSince.GetValueOrDefault();
			if (!_browserQuietSince.HasValue)
			{
				valueOrDefault = DateTime.Now;
				_browserQuietSince = valueOrDefault;
			}
			valueOrDefault = DateTime.Now;
			DateTime? browserQuietSince = _browserQuietSince;
			if (valueOrDefault - browserQuietSince > TimeSpan.FromSeconds(4L))
			{
				_media.SetBrowserAudio(null, null);
			}
		}
	}

	private async void LookUpBrowserTrack(string process, bool quiet)
	{
		if (_browserLookup)
		{
			return;
		}
		_browserLookup = true;
		try
		{
			BrowserTabs.Found? found = await Task.Run(() => BrowserTabs.Find(process, quiet));
			if (!Settings.Current.BrowserAudio)
			{
				return;
			}
			if (quiet)
			{
				if ((object)found == null || !found.HasControls)
				{
					_media.SetBrowserAudio(null, null);
					return;
				}
			}
			else if (!_media.HasBrowserAudio && _volume.PlayingBrowser() == null)
			{
				return;
			}
			string? title = found?.TabTitle;
			string text = "";
			string? text2 = found?.Title;
			if (text2 != null)
			{
				text = found.Artist ?? "";
				title = text2;
				_browserTrack = (found.TabTitle ?? "", text2, text);
			}
			else
			{
				(string, string, string)? browserTrack = _browserTrack;
				if (browserTrack.HasValue)
				{
					(string, string, string) valueOrDefault = browserTrack.GetValueOrDefault();
					if (valueOrDefault.Item1 == found?.TabTitle)
					{
						string item = valueOrDefault.Item2;
						text = valueOrDefault.Item3;
						title = item;
					}
				}
			}
			_media.SetBrowserAudio(process, title, text, found?.Playing ?? (!quiet), found?.HasControls ?? false);
		}
		finally
		{
			_browserLookup = false;
		}
	}

	private void IslandSettings_Click(object sender, RoutedEventArgs e)
	{
		((App)System.Windows.Application.Current).ShowSettings();
	}

	private void UpdateHidden()
	{
		bool flag = (Settings.Current.HideInFullscreen && !_gameActive && Win32.IsFullscreenAppRunning()) || (Settings.Current.HideInApps.Count > 0 && Settings.Current.HideInApps.Contains(ForegroundApp()));
		if (!flag)
		{
			_shownByKey = false;
		}
		bool flag2 = (_userHidden || (flag && !_shownByKey)) != _holdFlip;
		if (flag2 != _hiding)
		{
			_hiding = flag2;
			if (flag2)
			{
				LeaveScreen();
			}
			else
			{
				EnterScreen();
			}
		}
	}

	private (ScaleTransform, TranslateTransform) RootTransforms()
	{
		if (_rootScale == null || _rootShift == null)
		{
			_rootScale = new ScaleTransform();
			_rootShift = new TranslateTransform();
			Root.RenderTransformOrigin = new System.Windows.Point(0.5, 0.0);
			Root.RenderTransform = new TransformGroup
			{
				Children = 
				{
					(Transform)_stateScale,
					(Transform)_rootScale,
					(Transform)_rootShift,
					(Transform)_dragScl,
					(Transform)_dragRot
				}
			};
		}
		return (_rootScale, _rootShift);
	}

	private void LeaveScreen()
	{
		var (scale, shift) = RootTransforms();
		if (!AnimateHiding)
		{
			Root.Visibility = Visibility.Hidden;
			AfterVisibilityChange();
			return;
		}
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = AnimateLeave(Root, PillContent, scale, shift, Settings.Current.HideStyle);
		doubleAnimationUsingKeyFrames.Completed += (object? _, EventArgs _) =>
		{
			if (_hiding)
			{
				Root.Visibility = Visibility.Hidden;
				AfterVisibilityChange();
			}
		};
		Root.BeginAnimation(UIElement.OpacityProperty, doubleAnimationUsingKeyFrames);
	}

	internal static DoubleAnimationUsingKeyFrames AnimateLeave(UIElement root, UIElement content, ScaleTransform scale, TranslateTransform shift, string style)
	{
		CubicEase ease = new CubicEase
		{
			EasingMode = EasingMode.EaseIn
		};
		CubicEase ease2 = new CubicEase
		{
			EasingMode = EasingMode.EaseInOut
		};
		Animate(content, UIElement.OpacityProperty, 0.0, 140, ease);
		if (!(style == "fold"))
		{
			if (style == "fade")
			{
				scale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(ease2, (300, 0.92)));
				scale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(ease2, (300, 0.92)));
				shift.BeginAnimation(TranslateTransform.YProperty, null);
				return Keys(ease2, (300, 0.0));
			}
			scale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(ease, (240, 0.1)));
			scale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(ease2, (200, 0.75), (470, 0.18)));
			shift.BeginAnimation(TranslateTransform.YProperty, Keys(ease, (200, 0.0), (470, -40.0)));
			return Keys(ease, (320, 1.0), (470, 0.0));
		}
		scale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(ease, (320, 0.02)));
		scale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(ease2, (320, 0.85)));
		shift.BeginAnimation(TranslateTransform.YProperty, null);
		return Keys(ease, (250, 1.0), (340, 0.0));
	}

	private void EnterScreen()
	{
		var (scaleTransform, translateTransform) = RootTransforms();
		Root.Visibility = Visibility.Visible;
		AfterVisibilityChange();
		if (!AnimateHiding)
		{
			(IAnimatable, DependencyProperty)[] array = new (IAnimatable, DependencyProperty)[5]
			{
				(scaleTransform, ScaleTransform.ScaleXProperty),
				(scaleTransform, ScaleTransform.ScaleYProperty),
				(translateTransform, TranslateTransform.YProperty),
				(Root, UIElement.OpacityProperty),
				(PillContent, UIElement.OpacityProperty)
			};
			for (int i = 0; i < array.Length; i++)
			{
				var (animatable, dp) = array[i];
				animatable.BeginAnimation(dp, null);
			}
		}
		else
		{
			AnimateEnter(Root, PillContent, scaleTransform, translateTransform, Settings.Current.HideStyle);
		}
	}

	internal static void AnimateEnter(UIElement root, UIElement content, ScaleTransform scale, TranslateTransform shift, string style)
	{
		CubicEase ease = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		if (!(style == "fold"))
		{
			if (style == "fade")
			{
				shift.BeginAnimation(TranslateTransform.YProperty, null);
				shift.Y = 0.0;
				Animate(root, UIElement.OpacityProperty, 1.0, 300, ease, 0, 0.0);
				Animate(scale, ScaleTransform.ScaleXProperty, 1.0, 480, new SpringEase
				{
					Damping = 0.75,
					Stiffness = 11.0
				}, 0, 0.92);
				Animate(scale, ScaleTransform.ScaleYProperty, 1.0, 480, new SpringEase
				{
					Damping = 0.75,
					Stiffness = 11.0
				}, 0, 0.92);
				Animate(content, UIElement.OpacityProperty, 1.0, 260, ease, 120, 0.0);
			}
			else
			{
				Animate(root, UIElement.OpacityProperty, 1.0, 160, ease, 0, 0.0);
				Animate(shift, TranslateTransform.YProperty, 0.0, 340, ease, 0, -40.0);
				Animate(scale, ScaleTransform.ScaleYProperty, 1.0, 620, new SpringEase
				{
					Damping = 0.55,
					Stiffness = 11.0
				}, 60, 0.18);
				Animate(scale, ScaleTransform.ScaleXProperty, 1.0, 680, new SpringEase
				{
					Damping = 0.6,
					Stiffness = 10.0
				}, 200, 0.1);
				Animate(content, UIElement.OpacityProperty, 1.0, 260, ease, 380, 0.0);
			}
		}
		else
		{
			Animate(root, UIElement.OpacityProperty, 1.0, 120, ease, 0, 0.0);
			shift.BeginAnimation(TranslateTransform.YProperty, null);
			shift.Y = 0.0;
			Animate(scale, ScaleTransform.ScaleYProperty, 1.0, 560, new SpringEase
			{
				Damping = 0.62,
				Stiffness = 11.0
			}, 0, 0.02);
			Animate(scale, ScaleTransform.ScaleXProperty, 1.0, 520, new SpringEase
			{
				Damping = 0.7,
				Stiffness = 11.0
			}, 60, 0.85);
			Animate(content, UIElement.OpacityProperty, 1.0, 240, ease, 260, 0.0);
		}
	}

	private void PlayEntrance()
	{
		_hiding = false;
		if (AnimateHiding)
		{
			EnterScreen();
		}
	}

	private void AfterVisibilityChange()
	{
		UpdateMusicMotion();
		UpdateGlowEffect();
		UpdateMarquee();
	}

	private static DoubleAnimationUsingKeyFrames Keys(IEasingFunction ease, params (int Ms, double Value)[] keys)
	{
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames();
		for (int i = 0; i < keys.Length; i++)
		{
			var (num, value) = keys[i];
			doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(num)), ease));
		}
		return doubleAnimationUsingKeyFrames;
	}

	private string ForegroundApp()
	{
		GetWindowThreadProcessId(Win32.GetForegroundWindow(), out var pid);
		if (pid == _fgPid)
		{
			return _fgName;
		}
		_fgPid = pid;
		try
		{
			_fgName = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
		}
		catch (Exception)
		{
			_fgName = "";
		}
		return _fgName;
	}

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

	public static List<(string Name, string Title)> OpenApps()
	{
		List<(string, string)> list = new List<(string, string)>();
		HashSet<string> hashSet = new HashSet<string>();
		Process[] processes = Process.GetProcesses();
		foreach (Process process in processes)
		{
			try
			{
				if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(process.MainWindowTitle))
				{
					string text = process.ProcessName.ToLowerInvariant();
					bool flag;
					switch (text)
					{
					case "island":
					case "explorer":
					case "applicationframehost":
					case "textinputhost":
						flag = true;
						break;
					default:
						flag = false;
						break;
					}
					if (!flag && hashSet.Add(text))
					{
						list.Add((text, process.MainWindowTitle));
					}
				}
			}
			catch (Exception)
			{
			}
			finally
			{
				process.Dispose();
			}
		}
		list.Sort(((string, string) a, (string, string) b) => string.Compare(a.Item1, b.Item1, StringComparison.Ordinal));
		return list;
	}

	private void OpenDebugTab()
	{
	}

	private void PetDemo()
	{
	}

	private bool DeferToast(Action show, bool momentary)
	{
		if (_state != IslandState.Expanded)
		{
			return false;
		}
		if (!momentary)
		{
			_deferredToast = show;
			_deferredAt = DateTime.Now;
		}
		return true;
	}

	private void ShowDeferredSoon()
	{
		Dispatcher.BeginInvoke((Action)(() =>
		{
			IslandState? state = _state;
			bool flag;
			if (state.HasValue)
			{
				IslandState valueOrDefault = state.GetValueOrDefault();
				if ((uint)(valueOrDefault - 3) <= 1u)
				{
					flag = true;
					goto IL_0024;
				}
			}
			flag = false;
			goto IL_0024;
			IL_0024:
			if (!flag && !KeepOpen && !ShowNextPhone())
			{
				Action? deferredToast = _deferredToast;
				_deferredToast = null;
				if (deferredToast != null && DateTime.Now - _deferredAt < DeferredFor)
				{
					deferredToast();
				}
			}
		}), DispatcherPriority.Background);
	}

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);

	[DllImport("user32.dll")]
	private static extern nint WindowFromPoint(ScreenPoint p);

	[DllImport("user32.dll")]
	private static extern nint GetAncestor(nint hwnd, uint flags);

	private void KeepOnTop()
	{
		if (!IsVisible || Root.Visibility != Visibility.Visible || _menuOpen || PresentationSource.FromVisual(Pill) == null)
		{
			return;
		}
		nint handle = new WindowInteropHelper(this).Handle;
		if (handle == IntPtr.Zero)
		{
			return;
		}
		System.Windows.Point point = Pill.PointToScreen(new System.Windows.Point(Pill.ActualWidth / 2.0, Math.Min(12.0, Pill.ActualHeight / 2.0)));
		nint num = WindowFromPoint(new ScreenPoint
		{
			X = (int)point.X,
			Y = (int)point.Y
		});
		if (num == IntPtr.Zero)
		{
			return;
		}
		nint ancestor = GetAncestor(num, 2u);
		if (ancestor != handle)
		{
			GetWindowThreadProcessId(ancestor, out var pid);
			if (pid != Environment.ProcessId)
			{
				SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, 19u);
			}
		}
	}

	private void InitDetach()
	{
		LocationChanged += (object? _, EventArgs _) =>
		{
			UpdateFloating();
		};
		Root.PreviewMouseLeftButtonDown += IslandPress;
		Root.PreviewMouseMove += IslandMove;
		Root.PreviewMouseLeftButtonUp += IslandRelease;
		ApplyDetachButton();
	}

	private Rect PillRectDip()
	{
		PresentationSource presentationSource = PresentationSource.FromVisual(Pill);
		if (presentationSource?.CompositionTarget == null || Pill.ActualWidth <= 0.0)
		{
			return Rect.Empty;
		}
		Matrix transformFromDevice = presentationSource.CompositionTarget.TransformFromDevice;
		System.Windows.Point point = transformFromDevice.Transform(Pill.PointToScreen(new System.Windows.Point(0.0, 0.0)));
		System.Windows.Point point2 = transformFromDevice.Transform(Pill.PointToScreen(new System.Windows.Point(Pill.ActualWidth, Pill.ActualHeight)));
		return new Rect(point, point2);
	}

	private Rect ScreenOfDip(System.Windows.Point p)
	{
		PresentationSource presentationSource = PresentationSource.FromVisual(this);
		if (presentationSource?.CompositionTarget == null)
		{
			return new Rect(0.0, 0.0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
		}
		Matrix transformToDevice = presentationSource.CompositionTarget.TransformToDevice;
		Matrix transformFromDevice = presentationSource.CompositionTarget.TransformFromDevice;
		System.Windows.Point point = transformToDevice.Transform(p);
		System.Drawing.Rectangle bounds = Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y)).Bounds;
		return new Rect(transformFromDevice.Transform(new System.Windows.Point(bounds.Left, bounds.Top)), transformFromDevice.Transform(new System.Windows.Point(bounds.Right, bounds.Bottom)));
	}

	private System.Windows.Point CursorDip()
	{
		if (!Win32.GetCursorPos(out var pt))
		{
			return default;
		}
		PresentationSource presentationSource = PresentationSource.FromVisual(this);
		if (presentationSource?.CompositionTarget != null)
		{
			return presentationSource.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(pt.X, pt.Y));
		}
		return new System.Windows.Point(pt.X, pt.Y);
	}

	private void PositionWindow()
	{
		Settings current = Settings.Current;
		Pill.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
		Pill.VerticalAlignment = VerticalAlignment.Top;
		if (current.Detached)
		{
			double num = Math.Clamp(current.DetachedX, SystemParameters.VirtualScreenLeft + 150.0, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 150.0);
			double top = Math.Clamp(current.DetachedY, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40.0);
			Left = num - Width / 2.0;
			Top = top;
		}
		else
		{
			Left = (SystemParameters.PrimaryScreenWidth - Width) / 2.0;
			Top = 0.0;
		}
		UpdateFloating();
		if (current.Detached)
		{
			Dispatcher.BeginInvoke(new Action(ApplyEdgeShift), DispatcherPriority.Loaded);
		}
	}

	private void UpdateFloating()
	{
		bool flag = false;
		if (Settings.Current.Detached)
		{
			Rect rect = PillRectDip();
			if (!rect.IsEmpty)
			{
				Rect rect2 = ScreenOfDip(new System.Windows.Point(rect.X + rect.Width / 2.0, rect.Y + 1.0));
				flag = rect.Y > rect2.Top + 1.0;
			}
		}
		if (flag != _floating || !_floatInit)
		{
			_floating = flag;
			if (!_floatInit)
			{
				_floatInit = true;
				_floatMix = (flag ? 1 : 0);
				_shoulder = ((!flag) ? 1 : 0);
			}
			_clipW = -1.0;
			StartLiquidLoop();
			BuildShape();
		}
	}

	private void ApplyEdgeShift()
	{
		Settings current = Settings.Current;
		if (!current.Detached || _movingIsland || _dragLoop)
		{
			return;
		}
		Rect rect = PillRectDip();
		if (!rect.IsEmpty)
		{
			Rect rect2 = ScreenOfDip(new System.Windows.Point(current.DetachedX, current.DetachedY + rect.Height / 2.0));
			double num = current.DetachedX - rect.Width / 2.0;
			double num2 = rect2.Left + 4.0;
			double max = Math.Max(num2, rect2.Right - rect.Width - 4.0);
			double num3 = Math.Clamp(num, num2, max) - num;
			double detachedY = current.DetachedY;
			double max2 = Math.Max(rect2.Top, rect2.Bottom - rect.Height - 4.0);
			double num4 = Math.Clamp(detachedY, rect2.Top, max2) - detachedY;
			double num5 = current.DetachedX - Width / 2.0 + num3;
			double num6 = current.DetachedY + num4;
			if (Math.Abs(Left - num5) > 0.05)
			{
				Left = num5;
			}
			if (Math.Abs(Top - num6) > 0.05)
			{
				Top = num6;
			}
		}
	}

	private void SaveDetachedPosition()
	{
		Settings current = Settings.Current;
		if (current.Detached)
		{
			Rect rect = PillRectDip();
			if (!rect.IsEmpty)
			{
				current.DetachedX = rect.X + rect.Width / 2.0;
				current.DetachedY = rect.Y;
				current.Save();
			}
		}
	}

	private void ApplyDetachButton()
	{
		bool detached = Settings.Current.Detached;
		TabDetachIcon.Text = (detached ? "\ue77a" : "\ue718");
		TabDetach.ToolTip = (detached ? "Закрепить остров у верхнего края" : "Открепить остров: его можно перетащить в любое место экрана");
		AutomationProperties.SetName(TabDetach, detached ? "Закрепить остров" : "Открепить остров");
	}

	private void Detach_Click(object sender, RoutedEventArgs e)
	{
		Settings current = Settings.Current;
		if (!current.Detached)
		{
			current.Detached = true;
			current.DetachedX = Left + Width / 2.0;
			current.DetachedY = Top;
			current.Save();
			ApplyDetachButton();
			AnimateWindow(Left, Top + 44.0, () =>
			{
				SaveDetachedPosition();
				Log.Write("island detached");
			});
		}
		else
		{
			current.Detached = false;
			current.Save();
			ApplyDetachButton();
			AnimateWindow((SystemParameters.PrimaryScreenWidth - Width) / 2.0, 0.0, () =>
			{
				PositionWindow();
				Log.Write("island docked");
			});
		}
	}

	private void AnimateWindow(double left, double top, Action done)
	{
		if (ReducedMotion)
		{
			Left = left;
			Top = top;
			done();
			return;
		}
		CubicEase easingFunction = new CubicEase
		{
			EasingMode = EasingMode.EaseInOut
		};
		DoubleAnimation animation = new DoubleAnimation(left, TimeSpan.FromMilliseconds(380L))
		{
			EasingFunction = easingFunction
		};
		DoubleAnimation doubleAnimation = new DoubleAnimation(top, TimeSpan.FromMilliseconds(380L))
		{
			EasingFunction = easingFunction
		};
		doubleAnimation.Completed += (object? _, EventArgs _) =>
		{
			BeginAnimation(Window.LeftProperty, null);
			BeginAnimation(Window.TopProperty, null);
			Left = left;
			Top = top;
			done();
		};
		BeginAnimation(Window.LeftProperty, animation);
		BeginAnimation(Window.TopProperty, doubleAnimation);
	}

	private static bool IsInteractive(DependencyObject? d)
	{
		while (d != null)
		{
			if ((d is System.Windows.Controls.Primitives.ButtonBase || d is System.Windows.Controls.Primitives.TextBoxBase || d is System.Windows.Controls.Primitives.ScrollBar || d is Slider || d is System.Windows.Controls.ComboBox || d is ListBoxItem || d is Thumb || d is PasswordBox || d is LineSlider) ? true : false)
			{
				return true;
			}
			if (d is FrameworkElement frameworkElement && (frameworkElement.Cursor == System.Windows.Input.Cursors.Hand || frameworkElement.Cursor == System.Windows.Input.Cursors.IBeam))
			{
				return true;
			}
			bool flag = ((d is Visual || d is Visual3D) ? true : false);
			d = (flag ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d));
		}
		return false;
	}

	private void IslandPress(object sender, MouseButtonEventArgs e)
	{
		if (Settings.Current.Detached && e.ChangedButton == MouseButton.Left && !IsInteractive(e.OriginalSource as DependencyObject))
		{
			_pressArmed = true;
			_pressCursor = CursorDip();
			_pressWindow = new System.Windows.Point(Left, Top);
		}
	}

	private void IslandMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (!_pressArmed || _dragHeld)
		{
			return;
		}
		if (e.LeftButton != MouseButtonState.Pressed)
		{
			EndIslandMove();
			return;
		}
		System.Windows.Point point = CursorDip();
		if (!(Math.Abs(point.X - _pressCursor.X) < 5.0) || !(Math.Abs(point.Y - _pressCursor.Y) < 5.0))
		{
			_movingIsland = true;
			Root.CaptureMouse();
			StartDragMotion();
			e.Handled = true;
		}
	}

	private void IslandRelease(object sender, MouseButtonEventArgs e)
	{
		if (_dragHeld)
		{
			e.Handled = true;
		}
		EndIslandMove();
	}

	private void EndIslandMove()
	{
		_pressArmed = false;
		if (Root.IsMouseCaptured)
		{
			Root.ReleaseMouseCapture();
		}
		_dragHeld = false;
	}

	private static double Rubber(double over)
	{
		return 34.0 * (1.0 - Math.Exp((0.0 - over) / 34.0));
	}

	private void StartDragMotion()
	{
		_dragHeld = true;
		_dragRaw = new System.Windows.Point(Left, Top);
		_dragT = _clock.Elapsed.TotalSeconds;
		_fx = Left;
		_fy = Top;
		_fvx = (_fvy = 0.0);
		_offX = (_offXv = (_offY = (_offYv = 0.0)));
		RootTransforms();
		Rect rect = PillRectDip();
		_dragPill = (rect.IsEmpty ? new Rect(0.0, 0.0, 100.0, 34.0) : new Rect(rect.X - Left, rect.Y - Top, rect.Width, rect.Height));
		if (!_dragLoop)
		{
			_dragLoop = true;
			CompositionTarget.Rendering += OnDragFrame;
		}
	}

	private void OnDragFrame(object? sender, EventArgs e)
	{
		if (_dragHeld && (GetAsyncKeyState(1) & 0x8000) == 0)
		{
			_dragHeld = false;
			_pressArmed = false;
			if (Root.IsMouseCaptured)
			{
				Root.ReleaseMouseCapture();
			}
		}
		double totalSeconds = _clock.Elapsed.TotalSeconds;
		double num = Math.Clamp(totalSeconds - _dragT, 1.0 / 240.0, 0.05);
		_dragT = totalSeconds;
		System.Windows.Point point = CursorDip();
		if (_dragHeld)
		{
			System.Windows.Point dragRaw = new System.Windows.Point(_pressWindow.X + point.X - _pressCursor.X, _pressWindow.Y + point.Y - _pressCursor.Y);
			_dragRaw = dragRaw;
		}
		Rect rect = new Rect(_dragRaw.X + _dragPill.X, _dragRaw.Y + _dragPill.Y, _dragPill.Width, _dragPill.Height);
		Rect rect2 = ScreenOfDip(_dragHeld ? point : new System.Windows.Point(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0));
		double num2 = rect2.Left - rect.X;
		double num3 = rect.Right - rect2.Right;
		double num4 = rect.Bottom - rect2.Bottom;
		double num5 = rect.Y - rect2.Top;
		double num6 = 0.0;
		double num7 = 0.0;
		if (num2 > 0.0)
		{
			num6 = (_dragHeld ? (num2 - Rubber(num2)) : num2);
		}
		else if (num3 > 0.0)
		{
			num6 = 0.0 - (_dragHeld ? (num3 - Rubber(num3)) : num3);
		}
		if (num4 > 0.0)
		{
			num7 = 0.0 - (_dragHeld ? (num4 - Rubber(num4)) : num4);
		}
		else if (num5 < 16.0)
		{
			num7 = 0.0 - num5;
		}
		if (ReducedMotion)
		{
			_offX = num6;
			_offY = num7;
			_offXv = (_offYv = 0.0);
			_fx = _dragRaw.X;
			_fy = _dragRaw.Y;
			_fvx = (_fvy = 0.0);
		}
		else
		{
			_offXv += (230.0 * (num6 - _offX) - 16.0 * _offXv) * num;
			_offX += _offXv * num;
			_offYv += (230.0 * (num7 - _offY) - 16.0 * _offYv) * num;
			_offY += _offYv * num;
			_fvx += (600.0 * (_dragRaw.X - _fx) - 30.0 * _fvx) * num;
			_fx += _fvx * num;
			_fvy += (600.0 * (_dragRaw.Y - _fy) - 30.0 * _fvy) * num;
			_fy += _fvy * num;
		}
		Left = _fx + _offX;
		Top = _fy + _offY;
		double num8;
		if (num2 > 0.0)
		{
			num8 = num2 - _offX;
		}
		else
		{
			num8 = ((num3 > 0.0) ? (num3 + _offX) : 0.0);
		}
		double num9 = ((num4 > 0.0) ? (num4 + _offY) : 0.0);
		double num10 = Math.Clamp(num8 * 0.0016, 0.0, 0.07);
		double num11 = Math.Clamp(num9 * 0.0016, 0.0, 0.07);
		_dragScl.ScaleX = 1.0 - num10 + num11 * 0.6;
		_dragScl.ScaleY = 1.0 - num11 + num10 * 0.6;
		if (!_dragHeld && Math.Abs(_offX - num6) < 0.2 && Math.Abs(_offY - num7) < 0.2 && Math.Abs(_offXv) + Math.Abs(_offYv) < 3.0 && Math.Abs(_fx - _dragRaw.X) < 0.2 && Math.Abs(_fy - _dragRaw.Y) < 0.2 && Math.Abs(_fvx) + Math.Abs(_fvy) < 3.0)
		{
			CompositionTarget.Rendering -= OnDragFrame;
			_dragLoop = false;
			ScaleTransform dragScl = _dragScl;
			double scaleX = (_dragScl.ScaleY = 1.0);
			dragScl.ScaleX = scaleX;
			Left = _dragRaw.X + num6;
			Top = _dragRaw.Y + num7;
			_offX = (_offY = (_offXv = (_offYv = 0.0)));
			_movingIsland = false;
			UpdateFloating();
			SaveDetachedPosition();
			Dispatcher.BeginInvoke(new Action(ReleaseKeepOpen), DispatcherPriority.Background);
		}
	}

	private void OfferFirewall()
	{
		if (!PhoneOn || TipSeen("firewall"))
		{
			return;
		}
		DispatcherTimer wait = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(15L)
		};
		wait.Tick += async (object? _, EventArgs _) =>
		{
			if (!IslandFree)
			{
				wait.Interval = TimeSpan.FromSeconds(5L);
			}
			else
			{
				wait.Stop();
				bool flag = !PhoneOn;
				if (!flag)
				{
					flag = await LinkFirewall.IsAllowedAsync() != false;
				}
				if (!flag)
				{
					MarkTipSeen("firewall");
					ShowToast("\ue83d", "Разрешить Island в брандмауэре?", (double?)null, "", FillNormal, ((string Text, Action Run)?)("Разрешить", AllowFirewall), 15000, false, new ToastExtra("Иначе Windows может не пускать телефон", ((string Text, Action Run)?)("Позже", () =>
					{
					}), ToastAnim.None, (string?)null, (ImageSource?)null, false));
				}
			}
		};
		wait.Start();
	}

	private async void AllowFirewall()
	{
		bool flag = await LinkFirewall.AllowAsync();
		ShowToast("\ue83d", flag ? "Брандмауэр пропускает телефон" : "Брандмауэр не изменён", null, "", flag ? FillCharging : FillNormal, null, 3000, symbolFont: false, new ToastExtra(flag ? "Только устройства из вашей сети" : "Можно разрешить позже: настройки → «Телефон»"));
	}

	private void InitGameMode()
	{
		GameModeCheck.IsChecked = Settings.Current.GameMode;
		BuildGameTiles();
		Settings.Changed += OnGameSettings;
	}

	private void GameModeCheck_Click(object sender, RoutedEventArgs e)
	{
		Settings.Current.GameMode = GameModeCheck.IsChecked == true;
		Settings.Current.Save();
		Settings.NotifyChanged();
	}

	private void OnGameSettings()
	{
		GameModeCheck.IsChecked = Settings.Current.GameMode;
		bool gameActive = _gameActive;
		if (gameActive)
		{
			SystemMonitor.Release(_gameFps);
			_gameActive = false;
		}
		BuildGameTiles();
		UpdateGameMode();
		if (_gameActive && _state == IslandState.Game)
		{
			Resize(GameWidth(), 44.0);
		}
		else if (gameActive && !_gameActive && !_toastTimer.IsEnabled)
		{
			ApplyState(BaseState());
		}
	}

	private void BuildGameTiles()
	{
		GameRow.Children.Clear();
		_gameTiles.Clear();
		_gameFpsTile = null;
		foreach (string item in GameItemIds.Where(Settings.Current.GameBarItems.Contains))
		{
			AddGameTile(item);
		}
	}

	private double GameWidth()
	{
		GameRow.Measure(new System.Windows.Size(double.PositiveInfinity, 40.0));
		return Math.Max(90.0, Math.Ceiling(GameRow.DesiredSize.Width) + 16.0);
	}

	private void AddGameTile(string id)
	{
		(string Glyph, string Label, System.Windows.Media.Color Color) tuple = GameKinds[id];
		string item = tuple.Glyph;
		string item2 = tuple.Label;
		SolidColorBrush solidColorBrush = new SolidColorBrush(tuple.Color);
		TextBlock element = new TextBlock
		{
			Text = item,
			FontFamily = (System.Windows.Media.FontFamily)FindResource("Icons"),
			FontSize = 12.0,
			Foreground = solidColorBrush,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 5.0, 0.0),
			ToolTip = item2
		};
		TextBlock textBlock = new TextBlock
		{
			Text = "—",
			FontSize = 13.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
			VerticalAlignment = VerticalAlignment.Center,
			MinWidth = ((id == "net") ? 30 : 26)
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center
		};
		stackPanel.Children.Add(element);
		stackPanel.Children.Add(textBlock);
		ScaleTransform scaleTransform = new ScaleTransform(0.0, 1.0);
		StackPanel stackPanel2 = new StackPanel();
		stackPanel2.Children.Add(stackPanel);
		if (id != "fps")
		{
			Grid grid = new Grid
			{
				Height = 2.5,
				Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
			};
			grid.Children.Add(new Border
			{
				CornerRadius = new CornerRadius(1.25),
				Background = (System.Windows.Media.Brush)FindResource("Track")
			});
			grid.Children.Add(new Border
			{
				CornerRadius = new CornerRadius(1.25),
				Background = solidColorBrush,
				RenderTransformOrigin = new System.Windows.Point(0.0, 0.5),
				RenderTransform = scaleTransform
			});
			stackPanel2.Children.Add(grid);
		}
		GameRow.Children.Add(new Border
		{
			Margin = new Thickness(2.0, 0.0, 2.0, 0.0),
			Padding = new Thickness(9.0, 5.0, 9.0, (id == "fps") ? 8 : 6),
			CornerRadius = new CornerRadius(11.0),
			Background = (System.Windows.Media.Brush)FindResource("Surface"),
			Child = stackPanel2
		});
		_gameTiles[id] = (textBlock, scaleTransform);
		if (id == "fps")
		{
			UIElementCollection children = GameRow.Children;
			_gameFpsTile = (Border)children[children.Count - 1];
		}
		if (id == "fps")
		{
			_gameFpsTile.Visibility = Visibility.Collapsed;
		}
	}

	private void SetGame(string id, string text, double fraction)
	{
		if (_gameTiles.TryGetValue(id, out (TextBlock, ScaleTransform) value))
		{
			if (value.Item1.Text != text)
			{
				value.Item1.Text = text;
			}
			value.Item2.ScaleX = Math.Clamp(fraction, 0.0, 1.0);
		}
	}

	private void ApplyGame(MonitorSnapshot s)
	{
		if (_gameFpsTile != null)
		{
			bool hasValue = s.Fps.HasValue;
			if (hasValue)
			{
				SetGame("fps", s.Fps.Value.ToString(), 0.0);
			}
			if (_gameFpsTile.Visibility == Visibility.Visible != hasValue)
			{
				_gameFpsTile.Visibility = ((!hasValue) ? Visibility.Collapsed : Visibility.Visible);
				if (_state == IslandState.Game)
				{
					Resize(GameWidth(), 44.0);
				}
			}
		}
		SetGame("cpu", $"{s.Cpu:0}%", s.Cpu / 100.0);
		SetGame("ram", $"{s.RamPercent:0}%", s.RamPercent / 100.0);
		SetGame("gpu", (s.Gpu < 0.0) ? "—" : $"{s.Gpu:0}%", Math.Max(s.Gpu, 0.0) / 100.0);
		SetGame("disk", $"{s.Disk:0}%", s.Disk / 100.0);
		double num = Math.Max(s.NetUpMbps, s.NetDownMbps);
		_gameNetPeak = Math.Max(20.0, (num > _gameNetPeak) ? num : (_gameNetPeak * 0.97));
		SetGame("net", $"{num:0.#}", num / _gameNetPeak);
	}

	private void UpdateGameMode()
	{
		bool flag = DateTime.Now < _gamePreviewUntil || (Settings.Current.GameMode && GameDetector.IsGameInFront(out string _));
		if (flag == _gameActive)
		{
			return;
		}
		_gameActive = flag;
		if (flag)
		{
			_gameFps = Settings.Current.GameBarItems.Contains("fps");
			SystemMonitor.Acquire(_gameFps);
			MonitorSnapshot? latest = SystemMonitor.Latest;
			if ((object)latest != null)
			{
				ApplyGame(latest);
			}
		}
		else
		{
			SystemMonitor.Release(_gameFps);
		}
		Log.Write("game mode: " + (flag ? "the island shows the load" : "the island is as usual"));
		bool flag2;
		switch (_state)
		{
		case IslandState.Idle:
		case IslandState.CompactMedia:
		case IslandState.Activity:
		case IslandState.Game:
			flag2 = true;
			break;
		default:
			flag2 = false;
			break;
		}
		if (flag2 && !_toastTimer.IsEnabled)
		{
			ApplyState(BaseState());
		}
	}

	public void PreviewGameBar()
	{
		_gamePreviewUntil = DateTime.Now.AddSeconds(8.0);
		UpdateGameMode();
	}

	private void UpdateSeasonIcons()
	{
		foreach (UIElement seasonHiddenIcon in _seasonHiddenIcons)
		{
			seasonHiddenIcon.Visibility = (_pumpkinsOn ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private static DrawingImage FrozenImage(DrawingGroup group)
	{
		group.Freeze();
		DrawingImage drawingImage = new DrawingImage(group);
		drawingImage.Freeze();
		return drawingImage;
	}

	internal static DrawingImage PumpkinImage()
	{
		if (_pumpkinImage != null)
		{
			return _pumpkinImage;
		}
		RadialGradientBrush brush = new RadialGradientBrush(System.Windows.Media.Color.FromRgb(byte.MaxValue, 168, 62), System.Windows.Media.Color.FromRgb(224, 92, 8))
		{
			GradientOrigin = new System.Windows.Point(0.4, 0.35)
		};
		System.Windows.Media.Pen pen = new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(168, 64, 0)), 0.7);
		SolidColorBrush brush2 = new SolidColorBrush(System.Windows.Media.Color.FromRgb(byte.MaxValue, 224, 106));
		SolidColorBrush brush3 = new SolidColorBrush(System.Windows.Media.Color.FromRgb(110, 143, 42));
		return _pumpkinImage = FrozenImage(new DrawingGroup
		{
			Children = 
			{
				(Drawing)new GeometryDrawing(System.Windows.Media.Brushes.Transparent, null, new RectangleGeometry(new Rect(0.0, 0.0, 24.0, 24.0))),
				(Drawing)new GeometryDrawing(brush, pen, new EllipseGeometry(new System.Windows.Point(7.6, 14.4), 5.6, 7.0)),
				(Drawing)new GeometryDrawing(brush, pen, new EllipseGeometry(new System.Windows.Point(16.4, 14.4), 5.6, 7.0)),
				(Drawing)new GeometryDrawing(brush, pen, new EllipseGeometry(new System.Windows.Point(12.0, 14.4), 5.4, 7.5)),
				(Drawing)new GeometryDrawing(brush3, null, Geometry.Parse("M11,7.4 C11,5.2 11.6,3.6 13.6,2.6 L14.5,3.7 C13.1,4.5 12.9,5.7 13.1,7.4 Z")),
				(Drawing)new GeometryDrawing(brush3, null, Geometry.Parse("M13.4,4.6 C15.4,3.4 17.6,3.8 18.6,5.2 C16.8,5.9 15,5.8 13.4,4.6 Z")),
				(Drawing)new GeometryDrawing(brush2, null, Geometry.Parse("M6.9,12.6 L10,12.6 L8.45,10.4 Z M14,12.6 L17.1,12.6 L15.55,10.4 Z M11.1,14.6 L12.9,14.6 L12,13.3 Z M6.4,16 Q12,21.2 17.6,16 L16.2,16.5 L15.3,17.5 L14.2,16.8 L13.1,17.9 L12,17.1 L10.9,17.9 L9.8,16.8 L8.7,17.5 L7.8,16.5 Z"))
			}
		});
	}

	internal static DrawingImage SpiderImage()
	{
		if (_spiderImage != null)
		{
			return _spiderImage;
		}
		SolidColorBrush brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 22, 24));
		System.Windows.Media.Pen pen = new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(192, 154, 154, 168)), 0.6);
		System.Windows.Media.Pen pen2 = new System.Windows.Media.Pen(brush, 1.1)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round,
			LineJoin = PenLineJoin.Round
		};
		System.Windows.Media.Pen pen3 = new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(144, 154, 154, 168)), 2.2)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round,
			LineJoin = PenLineJoin.Round
		};
		Geometry geometry = Geometry.Parse("M10,10 L5,6 L2.5,9 M10,11.5 L4.5,10 L1.5,13.5 M10,13 L5,14.5 L3,18.5 M10.5,14 L6.5,18 L6,22 M14,10 L19,6 L21.5,9 M14,11.5 L19.5,10 L22.5,13.5 M14,13 L19,14.5 L21,18.5 M13.5,14 L17.5,18 L18,22");
		DrawingGroup drawingGroup = new DrawingGroup
		{
			Children = 
			{
				(Drawing)new GeometryDrawing(System.Windows.Media.Brushes.Transparent, null, new RectangleGeometry(new Rect(0.0, 0.0, 24.0, 24.0))),
				(Drawing)new GeometryDrawing(null, pen3, geometry),
				(Drawing)new GeometryDrawing(null, pen2, geometry),
				(Drawing)new GeometryDrawing(brush, pen, new EllipseGeometry(new System.Windows.Point(12.0, 14.0), 4.4, 5.0)),
				(Drawing)new GeometryDrawing(brush, pen, new EllipseGeometry(new System.Windows.Point(12.0, 8.2), 2.8, 2.5))
			}
		};
		SolidColorBrush brush2 = new SolidColorBrush(System.Windows.Media.Color.FromRgb(byte.MaxValue, 138, 31));
		drawingGroup.Children.Add(new GeometryDrawing(brush2, null, new EllipseGeometry(new System.Windows.Point(11.0, 8.6), 0.75, 0.75)));
		drawingGroup.Children.Add(new GeometryDrawing(brush2, null, new EllipseGeometry(new System.Windows.Point(13.0, 8.6), 0.75, 0.75)));
		return _spiderImage = FrozenImage(drawingGroup);
	}

	internal static DrawingImage BatImage()
	{
		if (_batImage != null)
		{
			return _batImage;
		}
		SolidColorBrush brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 20, 34));
		System.Windows.Media.Pen pen = new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 160, 128, 200)), 0.5);
		DrawingGroup drawingGroup = new DrawingGroup
		{
			Children = 
			{
				(Drawing)new GeometryDrawing(System.Windows.Media.Brushes.Transparent, null, new RectangleGeometry(new Rect(0.0, 0.0, 24.0, 12.0))),
				(Drawing)new GeometryDrawing(brush, pen, Geometry.Parse("M12,5 C10,3 7,1 2.5,2 C4,3.5 4,5 3,6.8 C5.2,5.6 6.8,6 7.8,7.6 C8.8,6.6 10.4,6.6 12,8.4 C13.6,6.6 15.2,6.6 16.2,7.6 C17.2,6 18.8,5.6 21,6.8 C20,5 20,3.5 21.5,2 C17,1 14,3 12,5 Z")),
				(Drawing)new GeometryDrawing(brush, null, Geometry.Parse("M10.6,5.6 L10.8,3.2 L11.6,4.6 L12.4,4.6 L13.2,3.2 L13.4,5.6 C13.4,7 10.6,7 10.6,5.6 Z"))
			}
		};
		SolidColorBrush brush2 = new SolidColorBrush(System.Windows.Media.Color.FromRgb(byte.MaxValue, 176, 64));
		drawingGroup.Children.Add(new GeometryDrawing(brush2, null, new EllipseGeometry(new System.Windows.Point(11.4, 5.5), 0.45, 0.45)));
		drawingGroup.Children.Add(new GeometryDrawing(brush2, null, new EllipseGeometry(new System.Windows.Point(12.6, 5.5), 0.45, 0.45)));
		return _batImage = FrozenImage(drawingGroup);
	}

	internal static DrawingImage GhostImage()
	{
		if (_ghostImage != null)
		{
			return _ghostImage;
		}
		LinearGradientBrush brush = new LinearGradientBrush(System.Windows.Media.Color.FromRgb(250, 250, byte.MaxValue), System.Windows.Media.Color.FromRgb(200, 204, 224), 90.0);
		SolidColorBrush brush2 = new SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 36));
		return _ghostImage = FrozenImage(new DrawingGroup
		{
			Children = 
			{
				(Drawing)new GeometryDrawing(System.Windows.Media.Brushes.Transparent, null, new RectangleGeometry(new Rect(0.0, 0.0, 20.0, 24.0))),
				(Drawing)new GeometryDrawing(brush, null, Geometry.Parse("M3,22 L3,10 C3,4.5 7,1.5 10,1.5 C13,1.5 17,4.5 17,10 L17,22 L14.5,19.5 L12,22 L10,19.5 L8,22 L5.5,19.5 Z")),
				(Drawing)new GeometryDrawing(brush2, null, new EllipseGeometry(new System.Windows.Point(7.6, 10.0), 1.3, 1.8)),
				(Drawing)new GeometryDrawing(brush2, null, new EllipseGeometry(new System.Windows.Point(12.4, 10.0), 1.3, 1.8)),
				(Drawing)new GeometryDrawing(brush2, null, new EllipseGeometry(new System.Windows.Point(10.0, 14.6), 1.2, 1.6))
			}
		});
	}

	private FrameworkElement MakePumpkin(double size, Thickness margin)
	{
		System.Windows.Controls.Image image = new System.Windows.Controls.Image
		{
			Source = PumpkinImage(),
			Width = size,
			Height = size,
			Margin = margin,
			VerticalAlignment = VerticalAlignment.Center,
			IsHitTestVisible = false,
			RenderTransformOrigin = new System.Windows.Point(0.5, 0.9),
			RenderTransform = new RotateTransform(),
			Effect = new DropShadowEffect
			{
				Color = System.Windows.Media.Color.FromRgb(byte.MaxValue, 138, 31),
				BlurRadius = size / 2.0,
				ShadowDepth = 0.0,
				Opacity = 0.55
			},
			Visibility = Visibility.Collapsed
		};
		RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
		_pumpkins.Add(image);
		return image;
	}

	private void InitHalloween()
	{
		if (CalTodayText.Parent is System.Windows.Controls.Button { Parent: StackPanel parent } button)
		{
			parent.Children.Insert(parent.Children.IndexOf(button) + 1, MakePumpkin(18.0, new Thickness(0.0, 0.0, 2.0, 1.0)));
		}
		(System.Windows.Controls.Button, int)[] array = new (System.Windows.Controls.Button, int)[1] { (WeatherHoursBtn, 1) };
		for (int i = 0; i < array.Length; i++)
		{
			var (button2, value) = array[i];
			if (button2.Parent is StackPanel { Parent: Border { Parent: Grid parent3 } })
			{
				FrameworkElement frameworkElement = MakePumpkin(20.0, new Thickness(0.0, 0.0, 2.0, 0.0));
				frameworkElement.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
				Grid.SetRow(frameworkElement, value);
				parent3.Children.Add(frameworkElement);
			}
		}
		StackPanel[] array2 = new StackPanel[2] { ClipEmpty, AiEmpty };
		foreach (StackPanel empty in array2)
		{
			SwapEmptyIcon(empty);
		}
		if (ClockText.Parent is StackPanel stackPanel2)
		{
			int index = stackPanel2.Children.IndexOf(ClockText);
			stackPanel2.Children.RemoveAt(index);
			StackPanel stackPanel3 = new StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal
			};
			stackPanel3.Children.Add(ClockText);
			stackPanel3.Children.Add(MakePumpkin(24.0, new Thickness(8.0, 4.0, 0.0, 0.0)));
			stackPanel2.Children.Insert(index, stackPanel3);
		}
		if (ShelfEmpty.Children.Count > 1 && ShelfEmpty.Children[1] is StackPanel empty2)
		{
			SwapEmptyIcon(empty2);
		}
		_spookLayer = new Canvas
		{
			IsHitTestVisible = false,
			Visibility = Visibility.Collapsed
		};
		Root.Children.Insert(0, _spookLayer);
		BuildSpider();
		Pill.SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			PlaceSpider();
		};
		Pill.LayoutUpdated += (object? _, EventArgs _) =>
		{
			if (_spookLayer.Visibility == Visibility.Visible)
			{
				PlaceSpider();
			}
		};
		_spiderTimer.Tick += (object? _, EventArgs _) =>
		{
			SpiderMove();
		};
		_spiderWatch.Tick += (object? _, EventArgs _) =>
		{
			WatchCursorForSpider();
		};
		_visitorTimer.Tick += (object? _, EventArgs _) =>
		{
			SendVisitor();
		};
		_seasonTimer.Tick += (object? _, EventArgs _) =>
		{
			ApplyHalloween();
		};
		_seasonTimer.Start();
		ApplyHalloween();
	}

	private void SwapEmptyIcon(StackPanel empty)
	{
		if (empty.Children.Count != 0)
		{
			_seasonHiddenIcons.Add(empty.Children[0]);
			FrameworkElement frameworkElement = MakePumpkin(30.0, new Thickness(0.0, -4.0, 0.0, -2.0));
			frameworkElement.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
			empty.Children.Insert(1, frameworkElement);
		}
	}

	private void ApplyHalloween()
	{
		Settings current = Settings.Current;
		bool halloweenNow = HalloweenNow;
		bool flag = halloweenNow && current.HalloweenPumpkins;
		bool flag2 = halloweenNow && current.HalloweenSpider;
		bool flag3 = halloweenNow && current.HalloweenVisitors;
		_pumpkinsOn = flag;
		UpdateSeasonIcons();
		(bool, bool, bool, bool) tuple = (flag, flag2, flag3, halloweenNow && current.HalloweenLogo);
		(bool, bool, bool, bool) tuple2 = tuple;
		(bool, bool, bool, bool)? halloweenShown = _halloweenShown;
		if (halloweenShown.HasValue)
		{
			(bool, bool, bool, bool) valueOrDefault = halloweenShown.GetValueOrDefault();
			if (tuple2.Item1 == valueOrDefault.Item1 && tuple2.Item2 == valueOrDefault.Item2 && tuple2.Item3 == valueOrDefault.Item3 && tuple2.Item4 == valueOrDefault.Item4)
			{
				return;
			}
		}
		halloweenShown = _halloweenShown;
		int num;
		if (halloweenShown.HasValue)
		{
			(bool, bool, bool, bool) valueOrDefault2 = halloweenShown.GetValueOrDefault();
			num = ((valueOrDefault2.Item1 || valueOrDefault2.Item2 || valueOrDefault2.Item3 || valueOrDefault2.Item4) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		bool flag4 = (byte)num != 0;
		_halloweenShown = tuple;
		for (int i = 0; i < _pumpkins.Count; i++)
		{
			FrameworkElement frameworkElement = _pumpkins[i];
			frameworkElement.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
			RotateTransform rotateTransform = (RotateTransform)frameworkElement.RenderTransform;
			DropShadowEffect dropShadowEffect = (DropShadowEffect)frameworkElement.Effect;
			rotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
			dropShadowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, null);
			if (flag && !ReducedMotion)
			{
				rotateTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-6.0, 6.0, TimeSpan.FromSeconds(1.6))
				{
					AutoReverse = true,
					RepeatBehavior = RepeatBehavior.Forever,
					BeginTime = TimeSpan.FromMilliseconds(i * 450),
					EasingFunction = new SineEase
					{
						EasingMode = EasingMode.EaseInOut
					}
				});
				DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
				{
					RepeatBehavior = RepeatBehavior.Forever,
					BeginTime = TimeSpan.FromMilliseconds(i * 300)
				};
				(int, double)[] array = new (int, double)[7]
				{
					(0, 0.55),
					(180, 0.8),
					(260, 0.45),
					(520, 0.7),
					(640, 0.35),
					(900, 0.75),
					(1300, 0.55)
				};
				for (int j = 0; j < array.Length; j++)
				{
					var (num2, value) = array[j];
					doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(num2))));
				}
				dropShadowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, doubleAnimationUsingKeyFrames);
			}
		}
		if (_spookLayer != null)
		{
			_spookLayer.Visibility = ((!(flag2 | flag3)) ? Visibility.Collapsed : Visibility.Visible);
		}
		if (_spider != null)
		{
			_spider.Visibility = ((!flag2) ? Visibility.Collapsed : Visibility.Visible);
		}
		if (flag2 && !ReducedMotion)
		{
			_spiderTimer.Interval = TimeSpan.FromSeconds(2L);
			_spiderTimer.Start();
			_spiderWatch.Start();
			StartSpiderSway();
		}
		else
		{
			_spiderTimer.Stop();
			_spiderWatch.Stop();
		}
		if (flag3 && !ReducedMotion)
		{
			_visitorTimer.Interval = TimeSpan.FromSeconds(20L);
			_visitorTimer.Start();
		}
		else
		{
			_visitorTimer.Stop();
		}
		((App)System.Windows.Application.Current).ApplySeasonLogo();
		if (flag2)
		{
			PlaceSpider();
		}
		if (halloweenNow && !flag4)
		{
			GreetHalloween();
		}
	}

	private void BuildSpider()
	{
		_thread = new System.Windows.Shapes.Rectangle
		{
			Width = 0.8,
			Height = _threadLength,
			Fill = new LinearGradientBrush(System.Windows.Media.Color.FromArgb(48, byte.MaxValue, byte.MaxValue, byte.MaxValue), System.Windows.Media.Color.FromArgb(176, byte.MaxValue, byte.MaxValue, byte.MaxValue), 90.0)
		};
		Canvas.SetLeft(_thread, 10.6);
		_spiderBody = new System.Windows.Controls.Image
		{
			Source = SpiderImage(),
			Width = 22.0,
			Height = 22.0,
			RenderTransformOrigin = new System.Windows.Point(0.5, 0.3),
			RenderTransform = new ScaleTransform(1.0, 1.0),
			Effect = new DropShadowEffect
			{
				Color = System.Windows.Media.Color.FromRgb(byte.MaxValue, 176, 96),
				BlurRadius = 5.0,
				ShadowDepth = 0.0,
				Opacity = 0.35
			}
		};
		RenderOptions.SetBitmapScalingMode(_spiderBody, BitmapScalingMode.HighQuality);
		Canvas.SetTop(_spiderBody, _threadLength - 5.0);
		_spiderSway = new RotateTransform();
		_spiderNudge = new RotateTransform
		{
			CenterX = 11.0
		};
		_spiderBob = new ScaleTransform();
		_spider = new Canvas
		{
			Width = 22.0,
			Height = 1.0,
			RenderTransform = new TransformGroup
			{
				Children = 
				{
					(Transform)_spiderBob,
					(Transform)_spiderSway,
					(Transform)_spiderNudge
				}
			}
		};
		_spider.Children.Add(_thread);
		_spider.Children.Add(_spiderBody);
		_spookLayer.Children.Add(_spider);
	}

	private void PlaceSpider()
	{
		if (_spider != null && _spookLayer != null && Pill.IsLoaded)
		{
			System.Windows.Point spiderAnchor = Pill.TranslatePoint(new System.Windows.Point(Pill.ActualWidth * _spiderSpot, Math.Max(4.0, Pill.ActualHeight - 30.0)), _spookLayer);
			if (!(Math.Abs(spiderAnchor.X - _spiderAnchor.X) < 0.3) || !(Math.Abs(spiderAnchor.Y - _spiderAnchor.Y) < 0.3))
			{
				_spiderAnchor = spiderAnchor;
				Canvas.SetLeft(_spider, spiderAnchor.X - 11.0);
				Canvas.SetTop(_spider, spiderAnchor.Y);
			}
		}
	}

	private void StartSpiderSway()
	{
		_spiderSway.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-10.0, 10.0, TimeSpan.FromSeconds(2.8))
		{
			AutoReverse = true,
			RepeatBehavior = RepeatBehavior.Forever,
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseInOut
			}
		});
		_spiderSway.CenterX = 11.0;
		_spiderBob.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1.04, TimeSpan.FromSeconds(1.9))
		{
			AutoReverse = true,
			RepeatBehavior = RepeatBehavior.Forever,
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseInOut
			}
		});
	}

	private void SpiderMove()
	{
		_spiderTimer.Interval = TimeSpan.FromSeconds(4.0 + _spookRandom.NextDouble() * 6.0);
		if (!_spiderScared)
		{
			double num = _spookRandom.Next(4) switch
			{
				0 => 0.0, 
				1 => 64.0 + _spookRandom.NextDouble() * 10.0, 
				_ => 42.0 + _spookRandom.NextDouble() * 16.0, 
			};
			SpiderTo(num, (num > _threadLength) ? 45 : 70);
		}
	}

	private void WatchCursorForSpider()
	{
		if (_spiderBody == null || Root.Visibility != Visibility.Visible || !_spiderBody.IsVisible || !Win32.GetCursorPos(out var pt) || PresentationSource.FromVisual(_spiderBody) == null)
		{
			return;
		}
		System.Windows.Point point = _spiderBody.PointToScreen(new System.Windows.Point(11.0, 11.0));
		double num = ((Root.LayoutTransform is ScaleTransform scaleTransform) ? scaleTransform.ScaleX : 1.0);
		double num2 = 46.0 * num;
		double num3 = 150.0 * num;
		double num4 = (double)pt.X - point.X;
		double num5 = (double)pt.Y - point.Y;
		double num6 = Math.Sqrt(num4 * num4 + num5 * num5);
		bool flag = num6 < num2;
		if (num6 < num3 && !_spiderScared)
		{
			double num7 = 1.0 - num6 / num3;
			double toValue = (double)(-Math.Sign((num4 == 0.0) ? 1.0 : num4)) * num7 * 26.0;
			_spiderNudge.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(toValue, TimeSpan.FromMilliseconds(260L))
			{
				EasingFunction = new SineEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
			_spiderNudged = true;
		}
		else if (_spiderNudged)
		{
			_spiderNudged = false;
			_spiderNudge.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(1400L))
			{
				EasingFunction = new ElasticEase
				{
					Oscillations = 1,
					Springiness = 6.0,
					EasingMode = EasingMode.EaseOut
				}
			});
		}
		if (flag)
		{
			_scaredUntil = DateTime.Now.AddSeconds(2.5);
			if (!_spiderScared)
			{
				_spiderScared = true;
				SpiderTo(0.0, 7.0, fast: true);
			}
		}
		else if (_spiderScared && DateTime.Now > _scaredUntil)
		{
			_spiderScared = false;
			SpiderTo(40.0 + _spookRandom.NextDouble() * 8.0, 90.0);
			_spiderTimer.Stop();
			_spiderTimer.Interval = TimeSpan.FromSeconds(3.0 + _spookRandom.NextDouble() * 3.0);
			_spiderTimer.Start();
		}
	}

	private void SpiderTo(double target, double msPerPixel, bool fast = false)
	{
		if (_thread == null || _spiderBody == null)
		{
			return;
		}
		bool flag = target > _threadLength;
		TimeSpan timeSpan = TimeSpan.FromMilliseconds(Math.Abs(target - _threadLength) * msPerPixel + (double)(fast ? 60 : 200));
		IEasingFunction easingFunction2;
		if (!flag)
		{
			if (!fast)
			{
				IEasingFunction easingFunction = new SineEase
				{
					EasingMode = EasingMode.EaseInOut
				};
				easingFunction2 = easingFunction;
			}
			else
			{
				IEasingFunction easingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				};
				easingFunction2 = easingFunction;
			}
		}
		else
		{
			IEasingFunction easingFunction = new BounceEase
			{
				Bounces = 1,
				Bounciness = 4.0,
				EasingMode = EasingMode.EaseOut
			};
			easingFunction2 = easingFunction;
		}
		IEasingFunction easingFunction3 = easingFunction2;
		DoubleAnimation doubleAnimation = new DoubleAnimation(target, timeSpan)
		{
			EasingFunction = easingFunction3
		};
		if (target <= 0.0)
		{
			doubleAnimation.Completed += (object? _, EventArgs _) =>
			{
				if (!(_threadLength > 0.0))
				{
					double num;
					do
					{
						num = 0.2 + _spookRandom.NextDouble() * 0.6;
					}
					while (Math.Abs(num - _spiderSpot) < 0.2);
					_spiderSpot = num;
					PlaceSpider();
				}
			};
		}
		_thread.BeginAnimation(FrameworkElement.HeightProperty, doubleAnimation);
		DoubleAnimation animation = new DoubleAnimation(target - 5.0, timeSpan)
		{
			EasingFunction = easingFunction3
		};
		_spiderBody.BeginAnimation(Canvas.TopProperty, animation);
		_threadLength = target;
		((ScaleTransform)_spiderBody.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, 0.82, TimeSpan.FromMilliseconds(110L))
		{
			AutoReverse = true,
			RepeatBehavior = new RepeatBehavior(Math.Max(1.0, timeSpan.TotalMilliseconds / 220.0))
		});
	}

	private void SendVisitor()
	{
		_visitorTimer.Interval = TimeSpan.FromSeconds(25.0 + _spookRandom.NextDouble() * 35.0);
		if (_spookLayer != null && Root.Visibility == Visibility.Visible)
		{
			if (_spookRandom.Next(3) == 0)
			{
				PeekGhost();
			}
			else
			{
				FlyBats(_spookRandom.Next(1, 4));
			}
		}
	}

	private void FlyBats(int count)
	{
		double actualWidth = _spookLayer.ActualWidth;
		double y = Pill.TranslatePoint(new System.Windows.Point(0.0, Pill.ActualHeight), _spookLayer).Y;
		bool flag = _spookRandom.Next(2) == 0;
		for (int i = 0; i < count; i++)
		{
			double num = 18.0 + _spookRandom.NextDouble() * 8.0;
			ScaleTransform scaleTransform = new ScaleTransform(flag ? 1 : (-1), 1.0);
			TranslateTransform translateTransform = new TranslateTransform();
			System.Windows.Controls.Image bat = new System.Windows.Controls.Image
			{
				Source = BatImage(),
				Width = num,
				Height = num / 2.0,
				RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
				RenderTransform = new TransformGroup
				{
					Children = 
					{
						(Transform)scaleTransform,
						(Transform)translateTransform
					}
				},
				Effect = new DropShadowEffect
				{
					Color = System.Windows.Media.Color.FromRgb(176, 128, byte.MaxValue),
					BlurRadius = 6.0,
					ShadowDepth = 0.0,
					Opacity = 0.4
				}
			};
			_spookLayer.Children.Add(bat);
			double length = y + 14.0 + _spookRandom.NextDouble() * 40.0;
			Canvas.SetTop(bat, length);
			Canvas.SetLeft(bat, 0.0);
			TimeSpan value = TimeSpan.FromMilliseconds(i * (260 + _spookRandom.Next(200)));
			TimeSpan timeSpan = TimeSpan.FromSeconds(3.2 + _spookRandom.NextDouble() * 1.4);
			DoubleAnimation doubleAnimation = new DoubleAnimation(flag ? (-30.0) : (actualWidth + 10.0), flag ? (actualWidth + 10.0) : (-30.0), timeSpan)
			{
				BeginTime = value
			};
			doubleAnimation.Completed += (object? _, EventArgs _) =>
			{
				_spookLayer.Children.Remove(bat);
			};
			translateTransform.BeginAnimation(TranslateTransform.XProperty, doubleAnimation);
			DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
			{
				BeginTime = value
			};
			for (int num2 = 0; num2 <= 6; num2++)
			{
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new SplineDoubleKeyFrame((num2 % 2 != 0) ? ((num2 % 4 == 1) ? 9 : (-7)) : 0, KeyTime.FromTimeSpan(TimeSpan.FromTicks(timeSpan.Ticks * num2 / 6)), new KeySpline(0.4, 0.0, 0.6, 1.0)));
			}
			translateTransform.BeginAnimation(TranslateTransform.YProperty, doubleAnimationUsingKeyFrames);
			scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(120L))
			{
				AutoReverse = true,
				RepeatBehavior = RepeatBehavior.Forever,
				BeginTime = value
			});
		}
	}

	private void PeekGhost()
	{
		bool flag = _spookRandom.Next(2) == 0;
		System.Windows.Point point = Pill.TranslatePoint(new System.Windows.Point(flag ? (Pill.ActualWidth * 0.18) : (Pill.ActualWidth * 0.82), Pill.ActualHeight), _spookLayer);
		TranslateTransform translateTransform = new TranslateTransform();
		RotateTransform rotateTransform = new RotateTransform();
		System.Windows.Controls.Image ghost = new System.Windows.Controls.Image
		{
			Source = GhostImage(),
			Width = 20.0,
			Height = 24.0,
			Opacity = 0.92,
			RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
			RenderTransform = new TransformGroup
			{
				Children = 
				{
					(Transform)rotateTransform,
					(Transform)translateTransform
				}
			},
			Effect = new DropShadowEffect
			{
				Color = Colors.White,
				BlurRadius = 10.0,
				ShadowDepth = 0.0,
				Opacity = 0.5
			}
		};
		Canvas.SetLeft(ghost, point.X - 10.0);
		Canvas.SetTop(ghost, point.Y - 28.0);
		_spookLayer.Children.Add(ghost);
		SineEase easingFunction = new SineEase
		{
			EasingMode = EasingMode.EaseInOut
		};
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames();
		(int, double)[] array = new (int, double)[6]
		{
			(0, 0.0),
			(700, 30.0),
			(1300, 26.0),
			(1900, 31.0),
			(2500, 27.0),
			(3200, 0.0)
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (num, value) = array[i];
			doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(num)), easingFunction));
		}
		doubleAnimationUsingKeyFrames.Completed += (object? _, EventArgs _) =>
		{
			_spookLayer.Children.Remove(ghost);
		};
		translateTransform.BeginAnimation(TranslateTransform.YProperty, doubleAnimationUsingKeyFrames);
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames2 = new DoubleAnimationUsingKeyFrames();
		array = new (int, double)[4]
		{
			(0, 0.0),
			(900, flag ? (-5.0) : 5.0),
			(1800, flag ? 4.0 : (-4.0)),
			(3200, 0.0)
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (num2, value2) = array[i];
			doubleAnimationUsingKeyFrames2.KeyFrames.Add(new EasingDoubleKeyFrame(value2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(num2)), easingFunction));
		}
		translateTransform.BeginAnimation(TranslateTransform.XProperty, doubleAnimationUsingKeyFrames2);
		rotateTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-10.0, 10.0, TimeSpan.FromMilliseconds(500L))
		{
			AutoReverse = true,
			RepeatBehavior = new RepeatBehavior(3.0),
			EasingFunction = easingFunction
		});
	}

	private void GreetHalloween()
	{
		string key = $"halloween-{DateTime.Today.Year}";
		if (Settings.Current.SeenTips.Contains(key))
		{
			return;
		}
		DispatcherTimer wait = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(8L)
		};
		wait.Tick += (object? _, EventArgs _) =>
		{
			if (!HalloweenNow)
			{
				wait.Stop();
				return;
			}
			IslandState? state = _state;
			bool flag;
			if (state.HasValue)
			{
				IslandState valueOrDefault = state.GetValueOrDefault();
				if ((uint)(valueOrDefault - 3) <= 1u)
				{
					flag = true;
					goto IL_003c;
				}
			}
			flag = false;
			goto IL_003c;
			IL_003c:
			if (!flag && !KeepOpen && !_hiding)
			{
				wait.Stop();
				Settings.Current.SeenTips.Add(key);
				Settings.Current.Save();
				MainWindow mainWindow = this;
				System.Windows.Media.Brush fillNormal = FillNormal;
				ImageSource thumb = PumpkinImage();
				mainWindow.ShowToast("", "Счастливого Хеллоуина!", null, "", fillNormal, null, 6000, symbolFont: false, new ToastExtra("Тыквы, паук и летучие мыши — до 5 ноября", null, ToastAnim.None, null, thumb));
				if (!ReducedMotion)
				{
					FlyBats(3);
				}
			}
		};
		wait.Start();
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint SetWindowsHookEx(int idHook, HookProc proc, nint hMod, uint threadId);

	[DllImport("user32.dll")]
	private static extern bool UnhookWindowsHookEx(nint hook);

	[DllImport("user32.dll")]
	private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vk);

	[DllImport("user32.dll")]
	private static extern void keybd_event(byte vk, byte scan, uint flags, nuint extra);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern nint GetModuleHandle(string? name);

	private void InitHotkey()
	{
		_holdTimer.Tick += (object? _, EventArgs _) =>
		{
			_holdTimer.Stop();
			if (_hotkeyDown)
			{
				_holding = true;
				_holdFlip = true;
				UpdateHidden();
			}
		};
		ApplyHotkey();
	}

	private void ApplyHotkey()
	{
		if (ReviewSafety.Enabled) return;
		bool flag = Settings.Current.HotkeyVk != 0 || VolumeKeysOurs || (PetOn && Settings.Current.PetTyping);
		if (flag && _hook == IntPtr.Zero)
		{
			_hookProc = OnKeyboard;
			_hook = SetWindowsHookEx(13, _hookProc, GetModuleHandle(null), 0u);
			if (_hook == IntPtr.Zero)
			{
				Log.Write($"keyboard hook failed: {Marshal.GetLastWin32Error()}");
			}
		}
		else if (!flag)
		{
			RemoveHotkeyHook();
		}
		if (!flag)
		{
			EndHold();
		}
	}

	private void RemoveHotkeyHook()
	{
		if (_hook != IntPtr.Zero)
		{
			UnhookWindowsHookEx(_hook);
		}
		_hook = IntPtr.Zero;
		_hotkeyDown = false;
	}

	private static bool Down(int vk)
	{
		return (GetAsyncKeyState(vk) & 0x8000) != 0;
	}

	private static int ModsNow()
	{
		return (int)((Down(17) ? 1u : 0u) | (uint)(Down(18) ? 2 : 0) | (uint)(Down(16) ? 4 : 0)) | ((Down(91) || Down(92)) ? 8 : 0);
	}

	private nint OnKeyboard(int code, nint wParam, nint lParam)
	{
		if (code >= 0 && !HotkeyPaused)
		{
			KbdHook kbdHook = Marshal.PtrToStructure<KbdHook>(lParam);
			int num = (int)wParam;
			Settings current = Settings.Current;
			bool flag = ((num == 256 || num == 260) ? true : false);
			if (flag && (kbdHook.Flags & 0x10) == 0)
			{
				PetKeyPressed();
			}
			uint vkCode = kbdHook.VkCode;
			flag = vkCode - 173 <= 2;
			if (flag && VolumeKeysOurs && (kbdHook.Flags & 0x10) == 0 && !_hiding)
			{
				if ((num == 256 || num == 260) ? true : false)
				{
					uint vk = kbdHook.VkCode;
					Dispatcher.BeginInvoke((Action)(() =>
					{
						if (vk == 173)
						{
							_volume.ToggleMute();
						}
						else
						{
							_volume.Step((vk == 175) ? 0.02f : (-0.02f));
						}
					}));
				}
				return 1;
			}
			if ((kbdHook.Flags & 0x10) == 0 && kbdHook.VkCode == current.HotkeyVk)
			{
				flag = ((num == 256 || num == 260) ? true : false);
				bool flag2 = flag;
				flag = ((num == 257 || num == 261) ? true : false);
				bool flag3 = flag;
				if (flag2 && (_hotkeyDown || ModsNow() == current.HotkeyMods))
				{
					if (!_hotkeyDown)
					{
						_hotkeyDown = true;
						if ((current.HotkeyMods & 0xA) != 0)
						{
							TapDummyKey();
						}
						Dispatcher.BeginInvoke(new Action(OnHotkeyPressed));
					}
					return 1;
				}
				if (flag3 && _hotkeyDown)
				{
					_hotkeyDown = false;
					Dispatcher.BeginInvoke(new Action(OnHotkeyReleased));
					return 1;
				}
			}
		}
		return CallNextHookEx(_hook, code, wParam, lParam);
	}

	private static void TapDummyKey()
	{
		keybd_event(232, 0, 0u, UIntPtr.Zero);
		keybd_event(232, 0, 2u, UIntPtr.Zero);
	}

	private void OnHotkeyPressed()
	{
		_holding = false;
		_holdTimer.Stop();
		_holdTimer.Start();
	}

	private void OnHotkeyReleased()
	{
		_holdTimer.Stop();
		if (_holding)
		{
			_holding = false;
			EndHold();
		}
		else if (_hiding)
		{
			_userHidden = false;
			_shownByKey = true;
			UpdateHidden();
			UserHiddenChanged?.Invoke();
		}
		else
		{
			UserHidden = true;
		}
	}

	private void EndHold()
	{
		if (_holdFlip)
		{
			_holdFlip = false;
			UpdateHidden();
		}
	}

	public static string HotkeyText(int vk, int mods)
	{
		if (vk == 0)
		{
			return "Не задана";
		}
		List<string> list = new List<string>();
		if ((mods & 1) != 0)
		{
			list.Add("Ctrl");
		}
		if ((mods & 2) != 0)
		{
			list.Add("Alt");
		}
		if ((mods & 4) != 0)
		{
			list.Add("Shift");
		}
		if ((mods & 8) != 0)
		{
			list.Add("Win");
		}
		Key key = KeyInterop.KeyFromVirtualKey(vk);
		List<string> list2 = list;
		string item;
		switch (key)
		{
		case Key.D0:
		case Key.D1:
		case Key.D2:
		case Key.D3:
		case Key.D4:
		case Key.D5:
		case Key.D6:
		case Key.D7:
		case Key.D8:
		case Key.D9:
			item = ((int)(key - 34)).ToString();
			break;
		case Key.Oem3:
			item = "Ё";
			break;
		case Key.Space:
			item = "Пробел";
			break;
		case Key.Pause:
			item = "Pause";
			break;
		case Key.Scroll:
			item = "Scroll Lock";
			break;
		case Key.Insert:
			item = "Insert";
			break;
		default:
			item = key.ToString();
			break;
		}
		list2.Add(item);
		return string.Join(" + ", list);
	}

	private void StartRipple(double amplitude)
	{
		_rippleAmp = amplitude;
		_rippleStart = _clock.Elapsed.TotalSeconds;
		StartLiquidLoop();
	}

	private void InitLiquid()
	{
		_proximityTimer.Tick += (object? _, EventArgs _) =>
		{
			PollKeyboard();
			if (!_ambientOn && _state != IslandState.Expanded && Pro && Settings.Current.WaterEffects && CursorNear(out var _))
			{
				StartAmbient();
			}
		};
		_proximityTimer.Start();
	}

	private void OnPillSizeChanged()
	{
		ApplyEdgeShift();
		BuildShape();
		StartLiquidLoop();
	}

	private void StartLiquidLoop()
	{
		if (!_loopOn)
		{
			_loopOn = true;
			_lastT = _clock.Elapsed.TotalSeconds;
			_lastW = Pill.ActualWidth;
			_lastH = Pill.ActualHeight;
			_stillFrames = 0;
			CompositionTarget.Rendering += OnLiquidFrame;
		}
	}

	private void OnLiquidFrame(object? sender, EventArgs e)
	{
		if (FrameDue(ref _liquidFrameT))
		{
			double totalSeconds = _clock.Elapsed.TotalSeconds;
			double num = Math.Clamp(totalSeconds - _lastT, 1.0 / 240.0, 0.05);
			_lastT = totalSeconds;
			double actualWidth = Pill.ActualWidth;
			double actualHeight = Pill.ActualHeight;
			double value = (actualWidth - _lastW) / num;
			double num2 = (actualHeight - _lastH) / num;
			_lastW = actualWidth;
			_lastH = actualHeight;
			if (ReducedMotion)
			{
				_bulge = (_bulgeVelocity = (_neck = 0.0));
			}
			else
			{
				double num3 = AnimationStyle switch
				{
					"smooth" => 0.3, 
					"snap" => 0.25, 
					"jelly" => 1.8, 
					"bubble" => 1.4, 
					"bounce" => 1.3, 
					"elastic" => 1.2, 
					"stretch" => 0.7, 
					"wave" => 0.8, 
					_ => 1.0, 
				};
				double num4 = Math.Clamp(num2 * 0.016 * num3, -7.0 * num3, 12.0 * num3);
				_bulgeVelocity += (260.0 * (num4 - _bulge) - 16.0 * _bulgeVelocity) * num;
				_bulge += _bulgeVelocity * num;
				double num5 = Math.Min(9.0, (Math.Abs(num2) + Math.Abs(value) * 0.6) * 0.012);
				_neck += (num5 - _neck) * Math.Min(1.0, num * 12.0);
			}
			_ripple = (ReducedMotion ? 0.0 : (_rippleAmp * Math.Exp((0.0 - (totalSeconds - _rippleStart)) * 2.8)));
			_ripplePhase = totalSeconds - _rippleStart;
			double num6 = ((!_floating && _floatMix <= 0.003) ? 1 : 0);
			if (ReducedMotion)
			{
				_shoulder = num6;
				_shoulderV = 0.0;
			}
			else
			{
				_shoulderV += (150.0 * (num6 - _shoulder) - 13.0 * _shoulderV) * num;
				_shoulder = Math.Max(0.0, _shoulder + _shoulderV * num);
			}
			double num7 = ((_floating && _shoulder < 0.03) ? 1 : 0);
			double floatMix = _floatMix;
			_floatMix = ((ReducedMotion || Math.Abs(num7 - _floatMix) < 0.004) ? num7 : (_floatMix + (num7 - _floatMix) * Math.Min(1.0, num * 13.0)));
			if (floatMix > 0.003 && _floatMix <= 0.003 && !ReducedMotion)
			{
				_bulgeVelocity -= 35.0;
			}
			BuildShape();
			if (totalSeconds < _tabIndicatorUntil)
			{
				SyncTabIndicator();
			}
			if (!(Math.Abs(value) < 0.5) || !(Math.Abs(num2) < 0.5) || !(Math.Abs(_bulge) < 0.05) || !(Math.Abs(_bulgeVelocity) < 0.5) || !(_neck < 0.05) || !(totalSeconds >= _tabIndicatorUntil) || !(_ripple < 0.05) || !(Math.Abs(_floatMix - num7) < 0.004) || !(Math.Abs(_shoulder - num6) < 0.004) || !(Math.Abs(_shoulderV) < 0.05))
			{
				_stillFrames = 0;
			}
			else if (++_stillFrames >= 4)
			{
				CompositionTarget.Rendering -= OnLiquidFrame;
				_loopOn = false;
				_bulge = (_bulgeVelocity = (_neck = 0.0));
				BuildShape();
			}
		}
	}

	private void UpdateMusicMotion()
	{
		if (ReducedMotion)
		{
			SetBarLevels(_compactBars, (MusicActive && _state == IslandState.CompactMedia) ? 0.7 : 0.3);
			SetBarLevels(_expandedBars, (MusicActive && MediaShown) ? 0.7 : 0.3);
		}
		else if (MusicActive)
		{
			StartAmbient();
		}
	}

	private void StartAmbient()
	{
		if (!_ambientOn && !ReducedMotion)
		{
			_ambientOn = true;
			_ambientLastT = _clock.Elapsed.TotalSeconds;
			CompositionTarget.Rendering += OnAmbientFrame;
		}
	}

	private void StopAmbient()
	{
		CompositionTarget.Rendering -= OnAmbientFrame;
		_ambientOn = false;
	}

	private void OnAmbientFrame(object? sender, EventArgs e)
	{
		if (FrameDue(ref _ambientFrameT))
		{
			OnAmbientTick();
		}
	}

	private bool FrameDue(ref double lastFrame)
	{
		int maxFps = Settings.Current.MaxFps;
		double totalSeconds = _clock.Elapsed.TotalSeconds;
		if (maxFps > 0 && totalSeconds - lastFrame < 0.9 / (double)maxFps)
		{
			return false;
		}
		lastFrame = totalSeconds;
		return true;
	}

	private void OnAmbientTick()
	{
		double totalSeconds = _clock.Elapsed.TotalSeconds;
		double num = Math.Clamp(totalSeconds - _ambientLastT, 0.005, 0.1);
		_ambientLastT = totalSeconds;
		bool flag = MusicActive && !ReducedMotion;
		bool flag2 = _state == IslandState.Expanded;
		bool flag3 = Pro && Settings.Current.WaterEffects && !ReducedMotion && (!flag2 || Settings.Current.RimAnimation);
		double num2 = ((flag && !_volume.Muted) ? _volume.Peak : 0f);
		_peakAvg += (num2 - _peakAvg) * Math.Min(1.0, num / 1.5);
		double num3 = (_musicNorm = ((num2 > 0.0) ? Math.Min(1.0, num2 / Math.Max(_peakAvg * 1.7, 0.0015)) : 0.0));
		_waveLevel += (num3 - _waveLevel) * Smooth((num3 > _waveLevel) ? 0.55 : 0.12, num);
		double num4;
		if (flag3)
		{
			if (flag)
			{
				num4 = (0.6 + _waveLevel * 4.5) * Settings.Current.WaterStrength * (flag2 ? 0.5 : 1.0);
			}
			else
			{
				num4 = (flag2 ? (1.1 * Settings.Current.WaterStrength) : 0.0);
			}
		}
		else
		{
			num4 = 0.0;
		}
		_waveAmp += (num4 - _waveAmp) * Smooth(0.3, num);
		_waveTime += num * (flag ? (1.0 + _waveLevel * 1.2) : 0.55);
		SetBars(_compactBars, flag && _state == IslandState.CompactMedia);
		SetBars(_expandedBars, flag && MediaShown);
		UpdatePlayerPulse(flag && MediaShown, _waveLevel, _waveTime, num);
		double tL = 0.0;
		double tR = 0.0;
		double tPull = 0.0;
		if (flag & flag3)
		{
			double num5 = _waveLevel * 2.4 * Settings.Current.WaterStrength * (flag2 ? 0.4 : 1.0);
			tL = num5 * (0.65 + 0.35 * Math.Sin(_waveTime * 2.1));
			tR = num5 * (0.65 + 0.35 * Math.Sin(_waveTime * 2.1 + Math.PI));
		}
		System.Windows.Point cursor = default;
		bool flag4 = flag3 && !flag2 && CursorNear(out cursor);
		if (flag4)
		{
			AddAttraction(cursor, ref tL, ref tR, ref tPull);
		}
		_sideLv += (170.0 * (tL - _sideL) - 13.0 * _sideLv) * num;
		_sideL = Math.Max(-3.0, _sideL + _sideLv * num);
		_sideRv += (170.0 * (tR - _sideR) - 13.0 * _sideRv) * num;
		_sideR = Math.Max(-3.0, _sideR + _sideRv * num);
		_pullV += (170.0 * (tPull - _pull) - 13.0 * _pullV) * num;
		_pull += _pullV * num;
		bool flag5 = _waveAmp >= 0.05 || Math.Abs(_sideL) >= 0.05 || Math.Abs(_sideR) >= 0.05 || Math.Abs(_pull) >= 0.05;
		if (!_loopOn && (flag5 || _wasDeformed))
		{
			BuildShape();
		}
		_wasDeformed = flag5;
		bool flag6 = _waveAmp < 0.05 && Math.Abs(_sideL) < 0.05 && Math.Abs(_sideR) < 0.05 && Math.Abs(_pull) < 0.05 && Math.Abs(_sideLv) + Math.Abs(_sideRv) + Math.Abs(_pullV) < 1.0;
		if (((!flag && !flag4) & flag6) && _pulse < 0.01)
		{
			StopAmbient();
			ResetPlayerPulse();
			_waveAmp = (_waveLevel = 0.0);
			_sideL = (_sideLv = (_sideR = (_sideRv = (_pull = (_pullV = 0.0)))));
			BuildShape();
		}
	}

	private bool CursorNear(out System.Windows.Point cursor)
	{
		cursor = default;
		if (Root.Visibility != Visibility.Visible || PresentationSource.FromVisual(Root) == null)
		{
			return false;
		}
		if (!Win32.GetCursorPos(out var pt))
		{
			return false;
		}
		cursor = Root.PointFromScreen(new System.Windows.Point(pt.X, pt.Y));
		double x = Pill.TranslatePoint(new System.Windows.Point(0.0, 0.0), Root).X;
		double num = x + Pill.ActualWidth;
		double actualHeight = Pill.ActualHeight;
		if ((!(cursor.X >= x) || !(cursor.X <= num) || !(cursor.Y <= actualHeight)) && cursor.Y < actualHeight + 80.0 && cursor.X > x - 90.0)
		{
			return cursor.X < num + 90.0;
		}
		return false;
	}

	private void AddAttraction(System.Windows.Point cursor, ref double tL, ref double tR, ref double tPull)
	{
		double x = Pill.TranslatePoint(new System.Windows.Point(0.0, 0.0), Root).X;
		double num = x + Pill.ActualWidth;
		double actualHeight = Pill.ActualHeight;
		double num2 = RadiusFor(actualHeight, Pill.ActualWidth);
		if (cursor.Y < actualHeight + 30.0)
		{
			double attractStrength = Settings.Current.AttractStrength;
			if (cursor.X < x)
			{
				tL += Falloff(x - cursor.X, 85.0) * 3.0 * attractStrength;
			}
			else if (cursor.X > num)
			{
				tR += Falloff(cursor.X - num, 85.0) * (double)(_bubbleShown ? 1 : 3) * attractStrength;
			}
		}
		if (cursor.Y > actualHeight && cursor.X > x - 20.0 && cursor.X < num + 20.0)
		{
			tPull = Falloff(cursor.Y - actualHeight, 75.0) * 9.0 * Settings.Current.AttractStrength;
			_pullX = Math.Clamp(cursor.X, x + num2, num - num2);
		}
	}

	private static double Smooth(double perThirtieth, double dt)
	{
		return 1.0 - Math.Pow(1.0 - perThirtieth, dt * 30.0);
	}

	private static double Falloff(double distance, double range)
	{
		if (distance >= range)
		{
			return 0.0;
		}
		double num = 1.0 - Math.Max(0.0, distance) / range;
		return num * num * (3.0 - 2.0 * num);
	}

	private void SetBars(System.Windows.Shapes.Rectangle[] bars, bool on)
	{
		for (int i = 0; i < bars.Length; i++)
		{
			double scaleY = (on ? (0.3 + 0.7 * Math.Clamp(_waveLevel * (0.6 + 0.4 * Math.Sin(_waveTime * BarSpeed[i] + BarPhase[i])), 0.0, 1.0)) : 0.3);
			((ScaleTransform)bars[i].RenderTransform).ScaleY = scaleY;
		}
	}

	private static void SetBarLevels(System.Windows.Shapes.Rectangle[] bars, double level)
	{
		for (int i = 0; i < bars.Length; i++)
		{
			((ScaleTransform)bars[i].RenderTransform).ScaleY = level;
		}
	}

	private void BuildShape()
	{
		double actualWidth = Pill.ActualWidth;
		double actualHeight = Pill.ActualHeight;
		if (actualWidth <= 0.0 || actualHeight <= 0.0 || PresentationSource.FromVisual(Pill) == null)
		{
			return;
		}
		double num = Pill.TranslatePoint(new System.Windows.Point(0.0, 0.0), Root).X - _sideL;
		double num2 = Pill.TranslatePoint(new System.Windows.Point(0.0, 0.0), Root).X + actualWidth + _sideR;
		double num3 = RadiusFor(actualHeight, num2 - num);
		double num4 = (7.0 + 6.0 * Math.Clamp((actualHeight - 34.0) / 150.0, 0.0, 1.0)) * (Pro ? Settings.Current.NeckScale : 1.0) + _neck;
		num4 *= Math.Clamp(_shoulder, 0.0, 1.4);
		num4 = Math.Min(num4, Math.Max(0.0, actualHeight - num3));
		double y = Pill.TranslatePoint(new System.Windows.Point(0.0, 0.0), Root).Y;
		if (Math.Abs(y - _shapeTop) > 0.01)
		{
			_shapeTop = y;
			Shape.RenderTransform = ((y == 0.0) ? Transform.Identity : new TranslateTransform(0.0, y));
		}
		Bottom b = new Bottom(_bulge, _waveAmp, _waveTime, _pull, _pullX, _ripple, _ripplePhase);
		bool flag = _floatMix > 0.003;
		Geometry geometry = (flag ? FloatingOutline(num, num2, actualHeight, num3, b, filled: true, _floatMix) : Outline(num, num2, actualHeight, num3, num4, b, closed: true));
		Geometry outline = (flag ? FloatingOutline(num, num2, actualHeight, num3, b, filled: false, _floatMix) : Outline(num, num2, actualHeight, num3, num4, b, closed: false));
		(Geometry, Geometry, bool)? tuple = BubbleDrop(num2 - _sideR, num2);
		if (tuple.HasValue)
		{
			(Geometry, Geometry, bool) valueOrDefault = tuple.GetValueOrDefault();
			var (geometry2, extraOutline, _) = valueOrDefault;
			if (valueOrDefault.Item3)
			{
				CombinedGeometry combinedGeometry = new CombinedGeometry(GeometryCombineMode.Union, geometry, geometry2);
				combinedGeometry.Freeze();
				Shape.Update(combinedGeometry, combinedGeometry);
			}
			else
			{
				Shape.Update(geometry, outline, geometry2, extraOutline);
			}
		}
		else
		{
			Shape.Update(geometry, outline);
		}
		double num5 = Math.Round(_floatMix * 40.0) / 40.0;
		if (actualWidth != _clipW || actualHeight != _clipH || num5 != _clipMix)
		{
			_clipW = actualWidth;
			_clipH = actualHeight;
			_clipMix = num5;
			double num6 = CornerSpan(0.0, actualWidth, actualHeight, RadiusFor(actualHeight, actualWidth), 0.0);
			PathFigure pathFigure = new PathFigure
			{
				StartPoint = new System.Windows.Point(0.0, 0.0),
				IsClosed = true
			};
			double num7 = 0.0;
			if (num5 > 0.003)
			{
				num6 = Math.Min(num6, actualHeight / 2.0);
				num7 = Math.Min(num6, 24.0) * num5;
				pathFigure.StartPoint = new System.Windows.Point(num7, 0.0);
				pathFigure.Segments.Add(new LineSegment(new System.Windows.Point(actualWidth - num7, 0.0), isStroked: false));
				pathFigure.Segments.Add(SmoothCorner(new System.Windows.Point(actualWidth - num7, 0.0), new System.Windows.Point(actualWidth, num7), new System.Windows.Point(actualWidth, 0.0)));
			}
			else
			{
				pathFigure.Segments.Add(new LineSegment(new System.Windows.Point(actualWidth, 0.0), isStroked: false));
			}
			pathFigure.Segments.Add(new LineSegment(new System.Windows.Point(actualWidth, actualHeight - num6), isStroked: false));
			pathFigure.Segments.Add(SmoothCorner(new System.Windows.Point(actualWidth, actualHeight - num6), new System.Windows.Point(actualWidth - num6, actualHeight), new System.Windows.Point(actualWidth, actualHeight)));
			pathFigure.Segments.Add(new LineSegment(new System.Windows.Point(num6, actualHeight), isStroked: false));
			pathFigure.Segments.Add(SmoothCorner(new System.Windows.Point(num6, actualHeight), new System.Windows.Point(0.0, actualHeight - num6), new System.Windows.Point(0.0, actualHeight)));
			if (num5 > 0.003)
			{
				pathFigure.Segments.Add(new LineSegment(new System.Windows.Point(0.0, num7), isStroked: false));
				pathFigure.Segments.Add(SmoothCorner(new System.Windows.Point(0.0, num7), new System.Windows.Point(num7, 0.0), new System.Windows.Point(0.0, 0.0)));
			}
			PathGeometry pathGeometry = new PathGeometry
			{
				Figures = { pathFigure }
			};
			pathGeometry.Freeze();
			PillContent.Clip = pathGeometry;
		}
	}

	internal static Geometry Outline(double left, double right, double h, double r, double neck, Bottom b, bool closed)
	{
		PathFigure pathFigure = new PathFigure
		{
			StartPoint = new System.Windows.Point(left - neck, 0.0),
			IsClosed = closed,
			IsFilled = closed
		};
		if (neck > 0.5)
		{
			pathFigure.Segments.Add(new ArcSegment(new System.Windows.Point(left, neck), new System.Windows.Size(neck, neck), 0.0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true));
		}
		double num = CornerSpan(left, right, h, r, neck);
		pathFigure.Segments.Add(new LineSegment(new System.Windows.Point(left, h - num), isStroked: true));
		pathFigure.Segments.Add(SmoothCorner(new System.Windows.Point(left, h - num), new System.Windows.Point(left + num, h), new System.Windows.Point(left, h)));
		double num2 = left + num;
		double num3 = right - num;
		if ((b.WaveAmp < 0.05 && Math.Abs(b.Pull) < 0.05 && b.Ripple < 0.05) || num3 - num2 < 4.0)
		{
			pathFigure.Segments.Add(new QuadraticBezierSegment(new System.Windows.Point((left + right) / 2.0, h + 2.0 * b.Bulge), new System.Windows.Point(num3, h), isStroked: true));
		}
		else
		{
			int num4 = Math.Max(10, (int)((num3 - num2) / 7.0));
			PointCollection pointCollection = new PointCollection(num4);
			for (int i = 1; i <= num4; i++)
			{
				double num5 = (double)i / (double)num4;
				double num6 = num2 + (num3 - num2) * num5;
				double num7 = Math.Sin(Math.PI * num5);
				double num8 = (num6 - b.PullX) / 42.0;
				double y = h + 4.0 * b.Bulge * num5 * (1.0 - num5) + b.WaveAmp * num7 * (0.35 + 0.65 * Wave(num6, b.WaveTime)) + b.Pull * num7 * Math.Exp((0.0 - num8) * num8) + b.Ripple * num7 * Math.Sin(Math.Abs(num5 - 0.5) * 26.0 - b.RipplePhase * 16.0);
				pointCollection.Add(new System.Windows.Point(num6, y));
			}
			pathFigure.Segments.Add(new PolyLineSegment(pointCollection, isStroked: true));
		}
		pathFigure.Segments.Add(SmoothCorner(new System.Windows.Point(right - num, h), new System.Windows.Point(right, h - num), new System.Windows.Point(right, h)));
		pathFigure.Segments.Add(new LineSegment(new System.Windows.Point(right, neck), isStroked: true));
		if (neck > 0.5)
		{
			pathFigure.Segments.Add(new ArcSegment(new System.Windows.Point(right + neck, 0.0), new System.Windows.Size(neck, neck), 0.0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true));
		}
		PathGeometry pathGeometry = new PathGeometry();
		pathGeometry.Figures.Add(pathFigure);
		pathGeometry.Freeze();
		return pathGeometry;
	}

	internal static Geometry FloatingOutline(double left, double right, double h, double r, Bottom b, bool filled, double topMix = 1.0)
	{
		double num = Math.Min(CornerSpan(left, right, h, r, 0.0), h / 2.0);
		double num2 = Math.Min(num, 24.0) * topMix;
		PathFigure pathFigure = new PathFigure
		{
			StartPoint = new System.Windows.Point(left + num2, 0.0),
			IsClosed = true,
			IsFilled = filled
		};
		pathFigure.Segments.Add(SmoothCorner(new System.Windows.Point(left + num2, 0.0), new System.Windows.Point(left, num2), new System.Windows.Point(left, 0.0)));
		pathFigure.Segments.Add(new LineSegment(new System.Windows.Point(left, h - num), isStroked: true));
		pathFigure.Segments.Add(SmoothCorner(new System.Windows.Point(left, h - num), new System.Windows.Point(left + num, h), new System.Windows.Point(left, h)));
		double num3 = left + num;
		double num4 = right - num;
		if ((b.WaveAmp < 0.05 && Math.Abs(b.Pull) < 0.05 && b.Ripple < 0.05) || num4 - num3 < 4.0)
		{
			pathFigure.Segments.Add(new QuadraticBezierSegment(new System.Windows.Point((left + right) / 2.0, h + 2.0 * b.Bulge), new System.Windows.Point(num4, h), isStroked: true));
		}
		else
		{
			int num5 = Math.Max(10, (int)((num4 - num3) / 7.0));
			PointCollection pointCollection = new PointCollection(num5);
			for (int i = 1; i <= num5; i++)
			{
				double num6 = (double)i / (double)num5;
				double num7 = num3 + (num4 - num3) * num6;
				double num8 = Math.Sin(Math.PI * num6);
				double num9 = (num7 - b.PullX) / 42.0;
				double y = h + 4.0 * b.Bulge * num6 * (1.0 - num6) + b.WaveAmp * num8 * (0.35 + 0.65 * Wave(num7, b.WaveTime)) + b.Pull * num8 * Math.Exp((0.0 - num9) * num9) + b.Ripple * num8 * Math.Sin(Math.Abs(num6 - 0.5) * 26.0 - b.RipplePhase * 16.0);
				pointCollection.Add(new System.Windows.Point(num7, y));
			}
			pathFigure.Segments.Add(new PolyLineSegment(pointCollection, isStroked: true));
		}
		pathFigure.Segments.Add(SmoothCorner(new System.Windows.Point(right - num, h), new System.Windows.Point(right, h - num), new System.Windows.Point(right, h)));
		pathFigure.Segments.Add(new LineSegment(new System.Windows.Point(right, num2), isStroked: true));
		pathFigure.Segments.Add(SmoothCorner(new System.Windows.Point(right, num2), new System.Windows.Point(right - num2, 0.0), new System.Windows.Point(right, 0.0)));
		PathGeometry pathGeometry = new PathGeometry();
		pathGeometry.Figures.Add(pathFigure);
		pathGeometry.Freeze();
		return pathGeometry;
	}

	internal static double CornerSpan(double left, double right, double h, double r, double neck)
	{
		return Math.Max(0.0, Math.Min(r * 1.28, Math.Min(h - neck, (right - left) / 2.0)));
	}

	internal static BezierSegment SmoothCorner(System.Windows.Point from, System.Windows.Point to, System.Windows.Point corner)
	{
		return new BezierSegment(from + (corner - from) * 0.64, to + (corner - to) * 0.64, to, isStroked: true);
	}

	private static double Wave(double x, double t)
	{
		return 0.55 * Math.Sin(x * 0.05 + t * 3.2) + 0.3 * Math.Sin(x * 0.11 - t * 4.6) + 0.15 * Math.Sin(x * 0.19 + t * 7.1);
	}

	private void AnimateTabs(bool instant)
	{
		for (int i = 0; i < _tabLabels.Length; i++)
		{
			bool flag = i == _tab;
			double num = (flag ? 90 : 0);
			if (instant)
			{
				_tabLabels[i].BeginAnimation(FrameworkElement.MaxWidthProperty, null);
				_tabLabels[i].BeginAnimation(UIElement.OpacityProperty, null);
				_tabLabels[i].MaxWidth = num;
				_tabLabels[i].Opacity = (flag ? 1 : 0);
			}
			else
			{
				Animate(_tabLabels[i], FrameworkElement.MaxWidthProperty, num, 460, new SpringEase
				{
					Damping = 0.9,
					Stiffness = 9.0
				});
				Animate(_tabLabels[i], UIElement.OpacityProperty, flag ? 1 : 0, flag ? 260 : 120, new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}, flag ? 80 : 0);
			}
		}
		if (instant)
		{
			Dispatcher.BeginInvoke(new Action(SyncTabIndicator), DispatcherPriority.Loaded);
			return;
		}
		_tabIndicatorUntil = _clock.Elapsed.TotalSeconds + 0.6;
		StartLiquidLoop();
	}

	private void SyncTabIndicator()
	{
		System.Windows.Controls.Button button = _tabButtons[_tab];
		if (button.IsVisible)
		{
			System.Windows.Point point = button.TranslatePoint(new System.Windows.Point(0.0, 0.0), TabStrip);
			TabIndicatorShift.X = point.X;
			TabIndicator.Width = button.ActualWidth;
		}
	}

	private static void Pop(FrameworkElement element, double from = 0.6)
	{
		if (!ReducedMotion)
		{
			ScaleTransform? scaleTransform = element.RenderTransform as ScaleTransform;
			if (scaleTransform == null || scaleTransform.IsFrozen)
			{
				scaleTransform = (ScaleTransform)(element.RenderTransform = new ScaleTransform());
				element.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
			}
			SpringEase ease = new SpringEase
			{
				Damping = 0.55,
				Stiffness = 12.0
			};
			Animate(scaleTransform, ScaleTransform.ScaleXProperty, 1.0, 520, ease, 0, from);
			Animate(scaleTransform, ScaleTransform.ScaleYProperty, 1.0, 520, ease, 0, from);
		}
	}

	private void PopIn_Loaded(object sender, RoutedEventArgs e)
	{
		FrameworkElement frameworkElement = (FrameworkElement)sender;
		Pop(frameworkElement, 0.7);
		Animate(frameworkElement, UIElement.OpacityProperty, 1.0, 220, new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		}, 0, 0.0);
	}

	private void InitMarquee()
	{
		TrackBlock.SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			UpdateMarquee();
		};
		PlayerSwitch.SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			UpdateMarquee();
		};
	}

	private void UpdateMarquee()
	{
		double actualWidth = TrackBlock.ActualWidth;
		double actualWidth2 = TrackBlock.ActualWidth;
		if (!(actualWidth <= 0.0) && !(actualWidth2 <= 0.0))
		{
			double num = ((PlayerSwitch.Visibility == Visibility.Visible) ? (PlayerSwitch.ActualWidth + 6.0) : 0.0);
			TitleClip.MaxWidth = Math.Max(40.0, actualWidth * 0.55);
			ArtistClip.MaxWidth = Math.Max(40.0, actualWidth2 * 0.45 - EqBars.DesiredSize.Width - 18.0 - num);
			bool flag = MediaShown && !ReducedMotion && Root.Visibility == Visibility.Visible;
			string text = $"{flag}|{TitleText.Text}|{ArtistText.Text}|{TitleClip.MaxWidth:0}|{ArtistClip.MaxWidth:0}";
			if (!(text == _marqueeKey))
			{
				_marqueeKey = text;
				Scroll(TitleText, TitleClip.MaxWidth, flag);
				Scroll(ArtistText, ArtistClip.MaxWidth, flag);
			}
		}
	}

	private void Scroll(TextBlock text, double room, bool run)
	{
		TranslateTransform? translateTransform = text.RenderTransform as TranslateTransform;
		if (translateTransform == null)
		{
			translateTransform = (TranslateTransform)(text.RenderTransform = new TranslateTransform());
		}
		translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
		translateTransform.X = 0.0;
		text.ToolTip = (string.IsNullOrEmpty(text.Text) ? null : text.Text);
		double widthIncludingTrailingWhitespace = new FormattedText(text.Text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, System.Windows.Media.Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
		if (!run || widthIncludingTrailingWhitespace <= room + 1.0)
		{
			text.Width = double.NaN;
			text.MaxWidth = room;
			text.TextTrimming = TextTrimming.CharacterEllipsis;
			return;
		}
		text.TextTrimming = TextTrimming.None;
		text.MaxWidth = double.PositiveInfinity;
		text.Width = Math.Ceiling(widthIncludingTrailingWhitespace);
		double num = widthIncludingTrailingWhitespace - room + 2.0;
		TimeSpan timeSpan = TimeSpan.FromSeconds(num / 28.0);
		TimeSpan timeSpan2 = TimeSpan.FromSeconds(2.2);
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
		{
			RepeatBehavior = RepeatBehavior.Forever
		};
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(timeSpan2)));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(0.0 - num, KeyTime.FromTimeSpan(timeSpan2 + timeSpan), new SineEase
		{
			EasingMode = EasingMode.EaseInOut
		}));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0 - num, KeyTime.FromTimeSpan(timeSpan2 + timeSpan + TimeSpan.FromSeconds(1.6))));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(timeSpan2 + timeSpan + TimeSpan.FromSeconds(1.7))));
		Timeline.SetDesiredFrameRate(doubleAnimationUsingKeyFrames, 30);
		translateTransform.BeginAnimation(TranslateTransform.XProperty, doubleAnimationUsingKeyFrames);
	}

	private void UpdateMarqueeSoon()
	{
		Dispatcher.BeginInvoke(new Action(UpdateMarquee), DispatcherPriority.Loaded);
	}

	private double MiniWidth()
	{
		MiniRow.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
		return _miniWidth = Math.Max(84.0, Math.Ceiling(MiniRow.DesiredSize.Width + 30.0));
	}

	private void UpdateMini()
	{
		if (_state == IslandState.CompactMedia)
		{
			if (MiniLyrics != _miniLyrics)
			{
				Relayout();
				return;
			}
			if (_miniLyrics)
			{
				return;
			}
		}
		SetText(MiniClock, DateTime.Now.ToString("HH:mm"));
		bool musicOnCollapsed = MusicOnCollapsed;
		Show(MiniCover, musicOnCollapsed);
		PhoneNotification[] array = ((PhoneOn && Settings.Current.PhoneOnCollapsed) ? CenterItems() : Array.Empty<PhoneNotification>());
		PhoneNotification? phoneNotification = array.OrderByDescending((PhoneNotification n) => n.Time).FirstOrDefault();
		ImageSource? imageSource = ((phoneNotification != null) ? Picture(phoneNotification.Avatar) : null);
		ImageSource? imageSource2 = ((phoneNotification != null && imageSource == null) ? AppIcon(phoneNotification) : null);
		bool flag = !musicOnCollapsed && (imageSource != null || imageSource2 != null);
		bool flag2 = MiniAvatar.Visibility == Visibility.Visible;
		Show(MiniAvatar, flag);
		if (flag)
		{
			MiniAvatarBrush.ImageSource = imageSource;
			MiniAppBrush.ImageSource = imageSource2;
			Show(MiniAvatarRound, imageSource != null);
			Show(MiniAvatarSquare, imageSource == null);
		}
		bool flag3 = MiniCount.Visibility == Visibility.Visible;
		string text = ((array.Length > 9) ? "9+" : array.Length.ToString());
		bool flag4 = MiniCountText.Text != text;
		Show(MiniCount, array.Length != 0);
		SetText(MiniCountText, text);
		Show(MiniCaps, Settings.Current.IdleShowKeyboard && _caps == true);
		WeatherNow? weatherNow = ((Pro && Settings.Current.IdleShowWeather) ? _weather.Current : null);
		Show(MiniWeather, weatherNow != null);
		if (weatherNow != null)
		{
			var (text2, _, color) = WeatherService.Describe(weatherNow.Code, weatherNow.IsDay);
			SetText(MiniWeatherIcon, text2);
			MiniWeatherIcon.Foreground = Freeze(new SolidColorBrush(color));
			SetText(MiniWeatherText, WeatherService.FormatTemp(weatherNow.Temp));
		}
		bool flag5 = Settings.Current.IdleShowBattery && _power.HasBattery && !_power.IsPluggedIn && _power.Percent <= 20;
		Show(MiniBattery, flag5);
		if (flag5)
		{
			SetText(MiniBattery, BatteryGlyph(_power.Percent, charging: false));
			MiniBattery.Foreground = FillLow;
		}
		if (flag && !flag2)
		{
			Pop(MiniAvatar, 0.4);
		}
		if (array.Length != 0 && (!flag3 | flag4))
		{
			Pop(MiniCount, 0.5);
		}
		IslandState? state = _state;
		bool flag6;
		if (state.HasValue)
		{
			IslandState valueOrDefault = state.GetValueOrDefault();
			if ((uint)valueOrDefault <= 1u)
			{
				flag6 = true;
				goto IL_0335;
			}
		}
		flag6 = false;
		goto IL_0335;
		IL_0335:
		if (flag6)
		{
			double miniWidth = _miniWidth;
			if (Math.Abs(MiniWidth() - miniWidth) > 0.5)
			{
				Resize(_miniWidth, 34.0);
			}
		}
	}

	private void ApplyMiniPill()
	{
		if (_miniApplied == MiniPill)
		{
			return;
		}
		_miniApplied = MiniPill;
		if (MiniPill)
		{
			UpdateMini();
		}
		IslandState? state = _state;
		bool flag;
		if (state.HasValue)
		{
			IslandState valueOrDefault = state.GetValueOrDefault();
			if ((uint)valueOrDefault <= 1u)
			{
				flag = true;
				goto IL_0048;
			}
		}
		flag = false;
		goto IL_0048;
		IL_0048:
		if (flag)
		{
			Relayout();
		}
	}

	private void Relayout()
	{
		if (_state is not { } value) return;
		_state = null;
		ApplyState(value);
	}

	private void InitMonitor()
	{
		AddMonitorTile("cpu", "\ue950", "CPU", CpuColor);
		AddMonitorTile("up", "\ue74a", "ОТПРАВКА", NetColor);
		AddMonitorTile("ram", "\ue964", "RAM", RamColor);
		AddMonitorTile("down", "\ue74b", "ПРИЁМ", NetColor);
		AddMonitorTile("gpu", "\ue7f4", "GPU", GpuColor);
		AddMonitorTile("disk", "\ueda2", "ДИСК", DiskColor);
		SystemMonitor.Updated += (MonitorSnapshot snap) =>
		{
			if (_monitorWatching)
			{
				ApplyMonitor(snap);
			}
			if (_gameActive)
			{
				ApplyGame(snap);
			}
		};
	}

	private void AddMonitorTile(string key, string glyph, string label, System.Windows.Media.Color color)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(color);
		TextBlock element = new TextBlock
		{
			Text = glyph,
			FontFamily = (System.Windows.Media.FontFamily)FindResource("Icons"),
			FontSize = 16.0,
			Foreground = solidColorBrush,
			Width = 30.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		TextBlock element2 = new TextBlock
		{
			Text = label,
			FontSize = 9.5,
			FontWeight = FontWeights.SemiBold,
			Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary")
		};
		TextBlock textBlock = new TextBlock
		{
			Text = "…",
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
			Margin = new Thickness(0.0, -1.0, 0.0, 0.0)
		};
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel.Children.Add(element2);
		stackPanel.Children.Add(textBlock);
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.Children.Add(element);
		Grid.SetColumn(stackPanel, 1);
		grid.Children.Add(stackPanel);
		ScaleTransform scaleTransform = new ScaleTransform(0.0, 1.0);
		Grid grid2 = new Grid
		{
			Height = 3.0,
			Margin = new Thickness(0.0, 7.0, 0.0, 0.0)
		};
		grid2.Children.Add(new Border
		{
			CornerRadius = new CornerRadius(1.5),
			Background = (System.Windows.Media.Brush)FindResource("Track")
		});
		grid2.Children.Add(new Border
		{
			CornerRadius = new CornerRadius(1.5),
			Background = solidColorBrush,
			RenderTransformOrigin = new System.Windows.Point(0.0, 0.5),
			RenderTransform = scaleTransform
		});
		StackPanel stackPanel2 = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel2.Children.Add(grid);
		stackPanel2.Children.Add(grid2);
		Border border = new Border
		{
			Margin = new Thickness(3.0),
			Padding = new Thickness(11.0, 6.0, 11.0, 8.0),
			CornerRadius = new CornerRadius(12.0),
			Background = (System.Windows.Media.Brush)FindResource("Surface"),
			Child = stackPanel2
		};
		MonitorGrid.Children.Add(border);
		_monitorTiles[key] = new MonitorTile
		{
			Root = border,
			Value = textBlock,
			Fill = scaleTransform
		};
	}

	private void UpdateMonitorWatch()
	{
		bool flag = _state == IslandState.Expanded && _shownTab == MonitorPanel && Root.Visibility == Visibility.Visible;
		if (flag == _monitorWatching)
		{
			return;
		}
		_monitorWatching = flag;
		if (flag)
		{
			SystemMonitor.Acquire();
			MonitorSnapshot? latest = SystemMonitor.Latest;
			if ((object)latest != null)
			{
				ApplyMonitor(latest);
			}
		}
		else
		{
			SystemMonitor.Release();
		}
	}

	private void SetTile(string key, string text, double fraction)
	{
		if (_monitorTiles.TryGetValue(key, out MonitorTile? value))
		{
			if (value.Value.Text != text)
			{
				value.Value.Text = text;
			}
			double num = Math.Clamp(fraction, 0.0, 1.0);
			if (ReducedMotion)
			{
				value.Fill.ScaleX = num;
				return;
			}
			Animate(value.Fill, ScaleTransform.ScaleXProperty, num, 420, new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			});
		}
	}

	private void ApplyMonitor(MonitorSnapshot s)
	{
		SetTile("cpu", $"{s.Cpu:0}%", s.Cpu / 100.0);
		SetTile("ram", $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} ГБ", s.RamPercent / 100.0);
		if (s.Gpu < 0.0)
		{
			SetTile("gpu", "нет данных", 0.0);
		}
		else
		{
			SetTile("gpu", $"{s.Gpu:0}%", s.Gpu / 100.0);
		}
		_netPeak = Math.Max(20.0, (Math.Max(s.NetUpMbps, s.NetDownMbps) > _netPeak) ? Math.Max(s.NetUpMbps, s.NetDownMbps) : (_netPeak * 0.97));
		SetTile("up", SystemMonitor.Speed(s.NetUpMbps), s.NetUpMbps / _netPeak);
		SetTile("down", SystemMonitor.Speed(s.NetDownMbps), s.NetDownMbps / _netPeak);
		SetTile("disk", $"{s.Disk:0}%", s.Disk / 100.0);
	}

	private void AnimateTrackChange(Island.Services.MediaState m)
	{
		string text = $"{m.SourceId}|{m.Title}|{m.Artist}";
		bool flag = _shownTrack == null;
		if (!(text == _shownTrack))
		{
			_shownTrack = text;
			if (!flag && !ReducedMotion)
			{
				SpringEase ease = new SpringEase
				{
					Damping = 0.6,
					Stiffness = 11.0
				};
				CubicEase ease2 = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				};
				Animate(CoverScale, ScaleTransform.ScaleXProperty, 1.0, 620, ease, 0, 0.82);
				Animate(CoverScale, ScaleTransform.ScaleYProperty, 1.0, 620, ease, 0, 0.82);
				Animate(CoverImage, UIElement.OpacityProperty, 1.0, 380, ease2, 60, 0.0);
				TrackBlock.RenderTransform = _trackInfoShift;
				Animate(_trackInfoShift, TranslateTransform.YProperty, 0.0, 460, ease, 40, 9.0);
				Animate(TrackBlock, UIElement.OpacityProperty, 1.0, 300, ease2, 40, 0.0);
			}
		}
	}

	private void NudgeCover(int direction)
	{
		if (!ReducedMotion)
		{
			Animate(CoverShift, TranslateTransform.XProperty, 0.0, 520, new SpringEase
			{
				Damping = 0.55,
				Stiffness = 12.0
			}, 0, 10 * direction);
		}
	}

	private void ApplyCoverGlow(System.Windows.Media.Color accent)
	{
		CoverGlow.Color = accent;
		SolidColorBrush solidColorBrush = new SolidColorBrush(accent);
		solidColorBrush.Freeze();
		System.Windows.Shapes.Rectangle[] vizBars = _vizBars;
		foreach (System.Windows.Shapes.Rectangle rectangle in vizBars)
		{
			if (rectangle != null)
			{
				rectangle.Fill = solidColorBrush;
			}
		}
	}

	private void InitVisualizer()
	{
		Random random = new Random(7);
		for (int i = 0; i < 64; i++)
		{
			System.Windows.Shapes.Rectangle rectangle = new System.Windows.Shapes.Rectangle
			{
				Width = 3.0,
				Height = 9.0,
				RadiusX = 1.5,
				RadiusY = 1.5,
				Margin = new Thickness(1.2, 0.0, 1.2, 0.0),
				Fill = new SolidColorBrush(_accent),
				RenderTransformOrigin = new System.Windows.Point(0.5, 1.0),
				RenderTransform = new ScaleTransform(1.0, 0.15)
			};
			_vizBars[i] = rectangle;
			_vizSpeed[i] = 4.0 + random.NextDouble() * 7.0;
			_vizPhase[i] = random.NextDouble() * Math.PI * 2.0;
			Visualizer.Children.Add(rectangle);
		}
	}

	private void UpdatePlayerPulse(bool on, double level, double time, double dt)
	{
		string musicVisual = MusicVisual;
		bool flag = on && musicVisual != "off";
		_pulse += ((flag ? level : 0.0) - _pulse) * Smooth((level > _pulse) ? 0.5 : 0.15, dt);
		double pulse = _pulse;
		ScaleTransform coverBeat = CoverBeat;
		double scaleX = (CoverBeat.ScaleY = 1.0 + 0.045 * pulse);
		coverBeat.ScaleX = scaleX;
		CoverGlow.Opacity = 0.3 + 0.45 * pulse;
		CoverGlow.BlurRadius = 14.0 + 14.0 * pulse;
		Shape.TintBoost = 0.22 * pulse;
		bool flag2 = on && musicVisual == "full";
		double num2 = (flag2 ? 0.55 : 0.0);
		Visualizer.Opacity += (num2 - Visualizer.Opacity) * Smooth(0.15, dt);
		if (!(Visualizer.Opacity < 0.01))
		{
			for (int i = 0; i < 64; i++)
			{
				double num3 = (double)i / 63.0;
				double num4 = 0.35 + 0.65 * Math.Sin(Math.PI * num3);
				double num5 = 0.5 + 0.5 * Math.Sin(time * _vizSpeed[i] + _vizPhase[i]);
				double num6 = (flag2 ? (0.12 + 0.88 * Math.Clamp(pulse * num4 * (0.35 + 0.65 * num5), 0.0, 1.0)) : 0.12);
				_vizLevel[i] += (num6 - _vizLevel[i]) * Smooth((num6 > _vizLevel[i]) ? 0.6 : 0.2, dt);
				((ScaleTransform)_vizBars[i].RenderTransform).ScaleY = _vizLevel[i];
			}
		}
	}

	private void ResetPlayerPulse()
	{
		_pulse = 0.0;
		ScaleTransform coverBeat = CoverBeat;
		double scaleX = (CoverBeat.ScaleY = 1.0);
		coverBeat.ScaleX = scaleX;
		CoverGlow.Opacity = 0.3;
		CoverGlow.BlurRadius = 14.0;
		Shape.TintBoost = 0.0;
		Visualizer.Opacity = 0.0;
	}

	private void Progress_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
	{
		ThickenProgress(on: true);
	}

	private void Progress_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
	{
		ThickenProgress(on: false);
	}

	private void ThickenProgress(bool on)
	{
		if (on)
		{
			Island.Services.MediaState? current = _media.Current;
			if ((object)current == null || current.Duration.Ticks <= 0)
			{
				return;
			}
		}
		CubicEase ease = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		Animate(ProgressTrack, FrameworkElement.HeightProperty, on ? 6 : 4, 160, ease);
		Animate(ProgressFill, FrameworkElement.HeightProperty, on ? 6 : 4, 160, ease);
	}

	private void InitOutputs()
	{
		InitListening();
	}

	private void ShowOutputs()
	{
		Popup? outputPopup = _outputPopup;
		if (outputPopup != null && outputPopup.IsOpen)
		{
			outputPopup.IsOpen = false;
			return;
		}
		List<VolumeService.AudioOutput> list = _volume.Outputs();
		Log.Write($"audio outputs: {list.Count}");
		if (list.Count == 0)
		{
			return;
		}
		StackPanel stackPanel = new StackPanel
		{
			Width = 264.0
		};
		Popup popup = new Popup
		{
			PlacementTarget = MoreButton,
			Placement = PlacementMode.Bottom,
			StaysOpen = false,
			AllowsTransparency = true,
			PopupAnimation = PopupAnimation.Fade,
			HorizontalOffset = MoreButton.ActualWidth - 264.0 + 6.0,
			VerticalOffset = 2.0,
			Child = new Border
			{
				Margin = new Thickness(14.0),
				Padding = new Thickness(4.0),
				Background = OutCard,
				CornerRadius = new CornerRadius(10.0),
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 52, 59)),
				BorderThickness = new Thickness(1.0),
				Effect = new DropShadowEffect
				{
					BlurRadius = 16.0,
					ShadowDepth = 3.0,
					Direction = 270.0,
					Opacity = 0.45,
					Color = Colors.Black
				},
				Child = stackPanel
			}
		};
		foreach (VolumeService.AudioOutput output in list.OrderByDescending((VolumeService.AudioOutput o) => o.IsDefault))
		{
			TextBlock element = new TextBlock
			{
				Text = output.Glyph,
				FontFamily = (System.Windows.Media.FontFamily)FindResource("Icons"),
				FontSize = 18.0,
				Width = 40.0,
				TextAlignment = TextAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Foreground = (output.IsDefault ? OutActiveIcon : ((System.Windows.Media.Brush)FindResource("TextPrimary")))
			};
			StackPanel stackPanel2 = new StackPanel
			{
				VerticalAlignment = VerticalAlignment.Center
			};
			stackPanel2.Children.Add(new TextBlock
			{
				Text = output.Title,
				FontFamily = OutFont,
				FontSize = 13.0,
				FontWeight = FontWeights.SemiBold,
				TextTrimming = TextTrimming.CharacterEllipsis,
				Foreground = (output.IsDefault ? OutActiveText : ((System.Windows.Media.Brush)FindResource("TextPrimary")))
			});
			if (output.Kind.Length > 0)
			{
				stackPanel2.Children.Add(new TextBlock
				{
					Text = output.Kind,
					FontFamily = OutFont,
					FontSize = 10.5,
					TextTrimming = TextTrimming.CharacterEllipsis,
					Foreground = (output.IsDefault ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(161, 161, 170)) : ((System.Windows.Media.Brush)FindResource("TextTertiary")))
				});
			}
			Grid grid = new Grid
			{
				VerticalAlignment = VerticalAlignment.Center
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = GridLength.Auto
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
			grid.Children.Add(element);
			Grid.SetColumn(stackPanel2, 1);
			grid.Children.Add(stackPanel2);
			Border row = new Border
			{
				Height = 40.0,
				Margin = new Thickness(0.0, 2.0, 0.0, 2.0),
				Padding = new Thickness(6.0, 0.0, 12.0, 0.0),
				CornerRadius = new CornerRadius(6.0),
				Background = (output.IsDefault ? OutActive : OutRow),
				Cursor = System.Windows.Input.Cursors.Hand,
				Child = grid,
				ToolTip = ((output.Kind.Length > 0) ? (output.Kind + " (" + output.Title + ")") : output.Title)
			};
			if (!output.IsDefault)
			{
				row.MouseEnter += (object _, System.Windows.Input.MouseEventArgs _) =>
				{
					row.Background = OutRowHover;
				};
				row.MouseLeave += (object _, System.Windows.Input.MouseEventArgs _) =>
				{
					row.Background = OutRow;
				};
			}
			string id = output.Id;
			row.MouseLeftButtonUp += (object _, MouseButtonEventArgs _) =>
			{
				popup.IsOpen = false;
				if (!output.IsDefault && !_volume.SetDefaultOutput(id))
				{
					ShowToast("\ue7ba", "Не удалось переключить звук", null, "", FillNormal);
				}
			};
			stackPanel.Children.Add(row);
		}
		_outputPopup = popup;
		_menuOpen = true;
		popup.Closed += (object? _, EventArgs _) =>
		{
			_menuOpen = false;
			Dispatcher.BeginInvoke(new Action(ReleaseKeepOpen));
		};
		popup.IsOpen = true;
	}

	private void InitPet()
	{
		_petBack = new Canvas
		{
			IsHitTestVisible = true
		};
		_petFront = new Canvas
		{
			IsHitTestVisible = false
		};
		Root.Children.Insert(0, _petBack);
		Root.Children.Insert(Root.Children.IndexOf(Shape) + 1, _petFront);
		_slotHost = new Border
		{
			Width = 0.0,
			Height = 30.0,
			VerticalAlignment = VerticalAlignment.Center,
			ClipToBounds = true
		};
		NetworkChange.NetworkAvailabilityChanged += (object? _, NetworkAvailabilityEventArgs e) =>
		{
			Dispatcher.BeginInvoke((Action)(() =>
			{
				OnPetNetwork(e.IsAvailable);
			}));
		};
		ApplyPet();
	}

	private void ApplyPet()
	{
		if (_petBack == null)
		{
			return;
		}
		PlaceSlot();
		string text = (PetOn ? $"{P.PetKind}|{P.PetPlace}|{P.PetSize:0.00}" : "");
		if (text != _petBuiltFor)
		{
			_petBuiltFor = text;
			_petBack.Children.Clear();
			_petFront.Children.Clear();
			_slotHost.Child = null;
			_slotHost.BeginAnimation(FrameworkElement.WidthProperty, null);
			_slotHost.Width = 0.0;
			if (MiniPill)
			{
				UpdateMini();
			}
			_slotPet = (_hangPet = (_sidePet = null));
			if (PetOn)
			{
				BuildPet();
			}
		}
		ApplyHotkey();
		StartPetLoop();
	}

	private void BuildPet()
	{
		string petKind = P.PetKind;
		double num = Math.Clamp(P.PetSize, 0.7, 1.5);
		if (P.PetPlace == "side")
		{
			_sidePet = new PetView(petKind, "full", 64.0 * num)
			{
				Cursor = System.Windows.Input.Cursors.Hand
			};
			HookPetting(_sidePet);
			_petBack.Children.Add(_sidePet);
			return;
		}
		if (P.PetPlace == "auto")
		{
			_slotPet = new PetView(petKind, "body", 26.0 * num)
			{
				Cursor = System.Windows.Input.Cursors.Hand,
				VerticalAlignment = VerticalAlignment.Center
			};
			HookPetting(_slotPet);
			_slotHost.Child = _slotPet;
			_slotHost.Width = _slotPet.Width;
			_slotHost.Height = _slotPet.Height;
		}
		double width = 50.0 * num;
		_hangPet = new PetView(petKind, "hang", PetView.HeightFor(petKind, "hang", width))
		{
			IsHitTestVisible = false
		};
		_hangSway = new RotateTransform(0.0, _hangPet.Width / 2.0, 0.0);
		_hangSlide = new TranslateTransform(0.0, (0.0 - _hangPet.Height) * 1.05);
		_hangPet.RenderTransform = new TransformGroup
		{
			Children = 
			{
				(Transform)_hangSway,
				(Transform)_hangSlide
			}
		};
		_hangClip = new Canvas
		{
			Width = _hangPet.Width,
			Height = 66.0 * num,
			ClipToBounds = true,
			IsHitTestVisible = false
		};
		_hangClip.Children.Add(_hangPet);
		_petBack.Children.Add(_hangClip);
		if (petKind != "block")
		{
			SolidColorBrush fill = new SolidColorBrush(PetArt.FurColor(petKind));
			SolidColorBrush stroke = new SolidColorBrush(PetArt.OutlineColor(petKind));
			SolidColorBrush stroke2 = new SolidColorBrush(PetArt.LineColor(petKind));
			Canvas canvas = new Canvas
			{
				Width = width,
				Height = 14.0 * num,
				Opacity = 0.0
			};
			double[] array = new double[2] { 5.0, 45.0 };
			foreach (double num2 in array)
			{
				Ellipse element = new Ellipse
				{
					Width = 13.0 * num,
					Height = 10.0 * num,
					Fill = fill,
					Stroke = stroke,
					StrokeThickness = 1.6 * num
				};
				Canvas.SetLeft(element, (num2 - 6.5) * num);
				Canvas.SetTop(element, 2.0 * num);
				canvas.Children.Add(element);
				double[] array2 = new double[2] { -2.4, 2.4 };
				foreach (double num3 in array2)
				{
					Line element2 = new Line
					{
						X1 = (num2 + num3) * num,
						X2 = (num2 + num3) * num,
						Y1 = 8.5 * num,
						Y2 = 11.0 * num,
						Stroke = stroke2,
						StrokeThickness = 1.1 * num,
						StrokeStartLineCap = PenLineCap.Round,
						StrokeEndLineCap = PenLineCap.Round
					};
					canvas.Children.Add(element2);
				}
			}
			_hangPaws = canvas;
			_petFront.Children.Add(canvas);
		}
		else
		{
			_hangPaws = null;
		}
	}

	private void PlaceSlot()
	{
		if (_slotHost == null)
		{
			return;
		}
		bool num = CompactPanel.Visibility == Visibility.Visible && CompactPanel.IsHitTestVisible;
		System.Windows.Controls.Panel panel = (System.Windows.Controls.Panel)CBar1.Parent;
		System.Windows.Controls.Panel panel2;
		if (num)
		{
			panel2 = panel;
		}
		else
		{
			panel2 = (MiniPill ? MiniRow : ((System.Windows.Controls.Panel)IdleRightText.Parent));
		}
		if (_slotHost.Parent != panel2)
		{
			(_slotHost.Parent as System.Windows.Controls.Panel)?.Children.Remove(_slotHost);
			if (panel2 == panel)
			{
				panel2.Children.Insert(0, _slotHost);
			}
			else
			{
				panel2.Children.Add(_slotHost);
			}
			_slotHost.Margin = ((panel2 == panel) ? new Thickness(0.0, 0.0, 7.0, 0.0) : new Thickness(6.0, 0.0, 0.0, 0.0));
		}
		double num2 = ((panel2 == panel && _slotPet != null) ? (_slotHost.ActualWidth + 7.0) : 0.0);
		Thickness margin = CompactTitle.Margin;
		if (Math.Abs(margin.Right - (48.0 + num2)) > 0.5)
		{
			CompactTitle.Margin = new Thickness(margin.Left, margin.Top, 48.0 + num2, margin.Bottom);
		}
	}

	private void HookPetting(PetView pet)
	{
		DispatcherTimer? hold = null;
		pet.MouseEnter += (object _, System.Windows.Input.MouseEventArgs _) =>
		{
			hold = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(1.1)
			};
			hold.Tick += (object? obj, EventArgs e) =>
			{
				hold.Stop();
				PetReact("pet");
			};
			hold.Start();
		};
		pet.MouseLeave += (object _, System.Windows.Input.MouseEventArgs _) =>
		{
			hold?.Stop();
		};
		pet.MouseLeftButtonUp += (object _, MouseButtonEventArgs e) =>
		{
			hold?.Stop();
			PetReact("pet");
			e.Handled = true;
		};
	}

	private void PetReact(string name)
	{
		if (PetOn && Reactions.TryGetValue(name, out (string, string, double) value))
		{
			bool flag = ((name == "pet" || name == "wake") ? true : false);
			if (flag || P.PetEvents)
			{
				_petReaction = (value.Item1, value.Item2, PetNow + value.Item3);
				RenderPet(restart: true);
			}
		}
	}

	private void OnPetVolume(float level, bool muted)
	{
		MainWindow mainWindow = this;
		string name;
		if (muted || level == 0f)
		{
			name = "mute";
		}
		else
		{
			name = (((double)level >= 0.99) ? "loud" : "vol");
		}
		mainWindow.PetReact(name);
	}

	private void OnPetNetwork(bool available)
	{
		bool flag = !available;
		if (_petOffline && !flag)
		{
			PetReact("online");
		}
		_petOffline = flag;
	}

	private void PetKeyPressed()
	{
		_lastKeyAt = Environment.TickCount64;
	}

	[DllImport("user32.dll")]
	private static extern bool GetLastInputInfo(ref LastInput info);

	private static double IdleSeconds()
	{
		LastInput info = new LastInput
		{
			Size = (uint)Marshal.SizeOf<LastInput>()
		};
		if (!GetLastInputInfo(ref info))
		{
			return 0.0;
		}
		return (double)(Environment.TickCount - (int)info.Time) / 1000.0;
	}

	private static bool PetAsleep()
	{
		if (!P.PetSleep)
		{
			return false;
		}
		int hour = DateTime.Now.Hour;
		double num = ((hour >= 23 || hour < 7) ? 1 : Math.Max(1, P.PetSleepMinutes)) * 60;
		return IdleSeconds() > num;
	}

	private (string State, string Bubble) ComputePet()
	{
		double petNow = PetNow;
		(string, string, double)? petReaction = _petReaction;
		if (petReaction.HasValue)
		{
			(string, string, double) valueOrDefault = petReaction.GetValueOrDefault();
			if (petNow < valueOrDefault.Item3)
			{
				return (State: valueOrDefault.Item1, Bubble: valueOrDefault.Item2);
			}
		}
		_petReaction = null;
		if (P.PetEvents)
		{
			if (_petOffline || _controls.Network == NetworkKind.None)
			{
				return (State: "offline", Bubble: "wifioff");
			}
			if (DownloadsActive)
			{
				return (State: "download", Bubble: "down");
			}
			if (_aiRun != null)
			{
				return (State: "think", Bubble: "dots");
			}
		}
		if (PetAsleep())
		{
			return (State: "sleep", Bubble: "");
		}
		if (P.PetEvents && _power.HasBattery && !_power.IsPluggedIn && _power.Percent <= 20)
		{
			return (State: "tired", Bubble: "battery");
		}
		if (P.PetTyping && Environment.TickCount64 - _lastKeyAt < 1100)
		{
			return (State: "typing", Bubble: "");
		}
		if (P.PetMusic)
		{
			Island.Services.MediaState? current = _media.Current;
			if ((object)current != null && current.IsPlaying)
			{
				return (State: "music", Bubble: "");
			}
		}
		return (State: "idle", Bubble: "");
	}

	private void RenderPet(bool restart = false)
	{
		var (text, text2) = ComputePet();
		if (_petWasSleeping && text != "sleep" && !_petReaction.HasValue)
		{
			_petWasSleeping = false;
			PetReact("wake");
			return;
		}
		_petWasSleeping = text == "sleep";
		if (!(text == _petShown) || !(text2 == _petShownBubble) || restart)
		{
			_petShown = text;
			_petShownBubble = text2;
			PetView[] array = new PetView[3] { _slotPet, _hangPet, _sidePet };
			for (int i = 0; i < array.Length; i++)
			{
				array[i]?.SetState(text, text2, restart);
			}
		}
	}

	private void StartPetLoop()
	{
		if (!_petLoop && PetOn)
		{
			_petLoop = true;
			CompositionTarget.Rendering += OnPetFrame;
		}
	}

	private void StopPetLoop()
	{
		if (_petLoop)
		{
			_petLoop = false;
			CompositionTarget.Rendering -= OnPetFrame;
		}
	}

	private void OnPetFrame(object? sender, EventArgs e)
	{
		if (!PetOn || _petBack == null)
		{
			StopPetLoop();
			return;
		}
		double petNow = PetNow;
		int num;
		if (_petShown == "sleep")
		{
			num = 10;
		}
		else
		{
			bool flag;
			switch (_petShown)
			{
			case "idle":
			case "tired":
			case "offline":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			num = (flag ? 20 : 30);
		}
		double num2 = num;
		if (Root.Visibility == Visibility.Visible && !(petNow - _petFrameAt < 1.0 / num2))
		{
			_petFrameAt = petNow;
			RenderPet();
			System.Windows.Point? lookAt = ((P.PetLook && Win32.GetCursorPos(out var pt)) ? new System.Windows.Point?(new System.Windows.Point(pt.X, pt.Y)) : ((System.Windows.Point?)null));
			PlacePet(petNow);
			_slotPet?.Tick(petNow, lookAt);
			if (_hangPet != null && (_hangShown || (_hangSlide != null && _hangSlide.Y > 0.0 - _hangPet.Height)))
			{
				_hangPet.Tick(petNow, lookAt);
			}
			_sidePet?.Tick(petNow, lookAt);
		}
	}

	private void PlacePet(double now)
	{
		if (PresentationSource.FromVisual(Pill) == null)
		{
			return;
		}
		double num = Pill.TranslatePoint(new System.Windows.Point(0.0, 0.0), Root).X + Pill.ActualWidth;
		double actualHeight = Pill.ActualHeight;
		bool flag = _state == IslandState.Expanded;
		double num2 = Math.Clamp(P.PetSize, 0.7, 1.5);
		PlaceSlot();
		if (_sidePet != null)
		{
			Canvas.SetLeft(_sidePet, num - 8.0 * num2);
			Canvas.SetTop(_sidePet, -7.0 * num2);
			_sidePet.Visibility = (flag ? Visibility.Hidden : Visibility.Visible);
		}
		bool flag2 = Peek.Contains(_petShown);
		if (_slotPet != null && _slotHost != null)
		{
			double want = (flag2 ? 0.0 : _slotPet.Width);
			if (Math.Abs(_slotHost.Width - want) > 0.5 && !_slotHost.HasAnimatedProperties)
			{
				DoubleAnimation doubleAnimation = new DoubleAnimation(want, TimeSpan.FromMilliseconds((!ReducedMotion) ? 450 : 0))
				{
					EasingFunction = new CubicEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				doubleAnimation.Completed += (object? _, EventArgs _) =>
				{
					_slotHost.BeginAnimation(FrameworkElement.WidthProperty, null);
					_slotHost.Width = want;
					if (MiniPill)
					{
						UpdateMini();
					}
				};
				_slotHost.BeginAnimation(FrameworkElement.WidthProperty, doubleAnimation);
				if (MiniPill && !flag2)
				{
					UpdateMini();
				}
			}
			_slotPet.Opacity = ((!flag2) ? 1 : 0);
		}
		if (_hangPet == null || _hangClip == null || _hangSlide == null)
		{
			return;
		}
		bool flag3 = !flag && (P.PetPlace == "hang" || ((P.PetPlace == "auto") & flag2));
		if (flag3 && P.PetPlace == "hang" && CursorNearHang(num, actualHeight, num2))
		{
			flag3 = false;
		}
		Canvas.SetLeft(_hangClip, num - 20.0 * num2 - _hangClip.Width);
		Canvas.SetTop(_hangClip, actualHeight - 11.0 * num2);
		if (flag3 != _hangShown)
		{
			_hangShown = flag3;
			BackEase backEase = new BackEase
			{
				Amplitude = 0.45,
				EasingMode = EasingMode.EaseOut
			};
			TranslateTransform? hangSlide = _hangSlide;
			DependencyProperty yProperty = TranslateTransform.YProperty;
			double to = (flag3 ? 0.0 : ((0.0 - _hangPet.Height) * 1.05));
			int ms = (flag3 ? 700 : 350);
			IEasingFunction ease;
			if (!flag3)
			{
				IEasingFunction easingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseIn
				};
				ease = easingFunction;
			}
			else
			{
				IEasingFunction easingFunction = backEase;
				ease = easingFunction;
			}
			Animate(hangSlide, yProperty, to, ms, ease);
			if (_hangPaws != null)
			{
				Animate(_hangPaws, UIElement.OpacityProperty, flag3 ? 1 : 0, flag3 ? 250 : 150, null);
			}
		}
		if (_hangPaws != null)
		{
			Canvas.SetLeft(_hangPaws, num - 20.0 * num2 - _hangClip.Width);
			Canvas.SetTop(_hangPaws, actualHeight - 7.0 * num2);
		}
		if (_hangSway != null)
		{
			_hangSway.Angle = (ReducedMotion ? 0.0 : (3.0 * Math.Sin(now * Math.PI * 2.0 / ((_petShown == "music") ? 1.04 : 3.6))));
		}
	}

	private bool CursorNearHang(double right, double bottom, double size)
	{
		if (!Win32.GetCursorPos(out var pt) || PresentationSource.FromVisual(Root) == null)
		{
			return false;
		}
		System.Windows.Point point = Root.PointFromScreen(new System.Windows.Point(pt.X, pt.Y));
		if (point.X > right - 95.0 * size && point.X < right + 10.0 && point.Y > bottom - 6.0)
		{
			return point.Y < bottom + 58.0 * size;
		}
		return false;
	}

	private void InitPhone()
	{
		ShowPhoneSubTab(0);
		Closed += (object? _, EventArgs _) =>
		{
			Phones?.Dispose();
			Phones = null;
		};
		ApplyPhone();
	}

	private static byte[] DemoPng(System.Windows.Media.Color color, string letter, bool round)
	{
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			SolidColorBrush brush = new SolidColorBrush(color);
			if (round)
			{
				drawingContext.DrawEllipse(brush, null, new System.Windows.Point(48.0, 48.0), 48.0, 48.0);
			}
			else
			{
				drawingContext.DrawRoundedRectangle(brush, null, new Rect(0.0, 0.0, 96.0, 96.0), 22.0, 22.0);
			}
			FormattedText formattedText = new FormattedText(letter, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 48.0, System.Windows.Media.Brushes.White, 1.0);
			drawingContext.DrawText(formattedText, new System.Windows.Point(48.0 - formattedText.Width / 2.0, 48.0 - formattedText.Height / 2.0));
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(96, 96, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
		using MemoryStream memoryStream = new MemoryStream();
		pngBitmapEncoder.Save(memoryStream);
		return memoryStream.ToArray();
	}

	private void ApplyPhone()
	{
		bool phoneOn = PhoneOn;
		if (phoneOn == (Phones != null))
		{
			return;
		}
		if (phoneOn)
		{
			PhoneLinkService phoneLinkService = new PhoneLinkService();
			try
			{
				phoneLinkService.Posted += OnPhonePosted;
				phoneLinkService.Removed += OnPhoneRemoved;
				phoneLinkService.ClipboardReceived += OnPhoneClipboard;
				phoneLinkService.FileReceived += OnPhoneFile;
				phoneLinkService.TransferProgress += OnPhoneTransfer;
				phoneLinkService.Posted += (PhoneNotification _) =>
				{
					RenderPhoneCenter();
					if (MiniPill)
					{
						UpdateMini();
					}
					UpdateBubble();
					UpdateTabBadges();
				};
				phoneLinkService.Removed += (PhoneNotification _) =>
				{
					RenderPhoneCenter();
					if (MiniPill)
					{
						UpdateMini();
					}
					UpdateBubble();
					UpdateTabBadges();
				};
				phoneLinkService.ConnectionChanged += (PhoneDevice _, bool _) =>
				{
					RenderPhoneCenter();
				};
				phoneLinkService.Start();
				Phones = phoneLinkService;
			}
			catch (Exception ex)
			{
				Log.Error("phone link", ex);
				phoneLinkService.Dispose();
			}
		}
		else
		{
			Phones.Dispose();
			Phones = null;
			_phoneShown.Clear();
		}
		ApplyTabVisibility();
		PhonesChanged?.Invoke();
	}

	private static string PhoneId(PhoneNotification n)
	{
		return n.DeviceId + "|" + n.Key;
	}

	private void OnPhonePosted(PhoneNotification n)
	{
		if (n.IsSync || n.Silent || (n.Ongoing && n.Category != "call") || (!Settings.Current.PhoneCards && n.Category != "call"))
		{
			return;
		}
		var (text, text2) = PhoneLines(n);
		if (text.Length == 0 && text2.Length == 0)
		{
			return;
		}
		string id = PhoneId(n);
		string text3 = text + "\n" + text2;
		if (_phoneShown.TryGetValue(id, out string? value) && value == text3)
		{
			return;
		}
		_phoneShown[id] = text3;
		if (Settings.Current.PhoneDnd)
		{
			return;
		}
		PetReact((n.Category == "call") ? "call" : "notify");
		if (_state == IslandState.Expanded && n.Category != "call")
		{
			_phoneQueue.RemoveAll((PhoneNotification q) => PhoneId(q) == id);
			_phoneQueue.Add(n);
			if (_phoneQueue.Count > 6)
			{
				_phoneQueue.RemoveAt(0);
			}
			return;
		}
		bool flag = _phoneCardShown && _state == IslandState.Toast;
		if (flag && _phoneToast != null && PhoneId(_phoneToast) == id)
		{
			ShowPhoneCard(n);
			return;
		}
		if (!flag || n.Category == "call")
		{
			ShowPhoneCard(n);
			return;
		}
		_phoneQueue.RemoveAll((PhoneNotification q) => PhoneId(q) == id);
		_phoneQueue.Add(n);
		if (_phoneQueue.Count > 6)
		{
			_phoneQueue.RemoveAt(0);
		}
	}

	private void OnPhoneRemoved(PhoneNotification n)
	{
		string id = PhoneId(n);
		_phoneShown.Remove(id);
		_phoneQueue.RemoveAll((PhoneNotification q) => PhoneId(q) == id);
		PhoneNotification? phoneToast = _phoneToast;
		if (phoneToast != null && !(PhoneId(phoneToast) != id) && _phoneCardShown && _state == IslandState.Toast)
		{
			PhoneCardDone();
		}
	}

	private bool ShowNextPhone()
	{
		if (_phoneQueue.Count == 0)
		{
			return false;
		}
		PhoneNotification n = _phoneQueue[0];
		_phoneQueue.RemoveAt(0);
		ShowPhoneCard(n);
		return true;
	}

	private void PhoneCardDone()
	{
		CollapsePhoneCard();
		_phoneToast = null;
		_toastTimer.Stop();
		if (!ShowNextPhone())
		{
			_toastInteractive = false;
			_phoneCardShown = false;
			ApplyState(BaseState());
		}
	}

	private void PhoneButton_Click(object sender, RoutedEventArgs e)
	{
		Action? obj = ((sender != PhoneButton1) ? _phoneButton2?.Run : _phoneButton1?.Run);
		_hover = false;
		obj?.Invoke();
		PhoneCardDone();
	}

	private void HidePhoneCard()
	{
		CollapsePhoneCard();
		_phoneCardShown = false;
		if (PhoneCard.Visibility == Visibility.Visible)
		{
			PhoneCard.Visibility = Visibility.Collapsed;
			ToastRow.Visibility = Visibility.Visible;
		}
	}

	private void PhoneCardHover(bool hover)
	{
		if (!_phoneCardShown || (!_phoneButton1.HasValue && !_phoneButton2.HasValue))
		{
			return;
		}
		bool flag = hover || _phoneExpanded;
		if (flag != (PhoneButtons.Visibility == Visibility.Visible))
		{
			PhoneButtons.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
			FitPhoneCard();
			if (flag && !ReducedMotion)
			{
				Animate(PhoneButtons, UIElement.OpacityProperty, 1.0, 220, new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}, 60, 0.0);
			}
		}
	}

	private void FitPhoneCard()
	{
		ToastPanel.Width = 372.0;
		PhoneCard.Measure(new System.Windows.Size(372.0, double.PositiveInfinity));
		double num = Math.Ceiling(PhoneCard.DesiredSize.Height);
		if (!(Math.Abs(ToastPanel.Height - num) < 0.5))
		{
			ToastPanel.Height = num;
			if (_state == IslandState.Toast)
			{
				Resize(374.0, num + 2.0);
			}
		}
	}

	private void FitPhoneCardSoon()
	{
		Dispatcher.BeginInvoke((Action)(() =>
		{
			if (_phoneCardShown)
			{
				FitPhoneCard();
			}
		}), DispatcherPriority.Loaded);
	}

	private static (string Who, string What) PhoneLines(PhoneNotification n)
	{
		string text = n.Title.Trim();
		string text2;
		if (n.Messages.Count > 0)
		{
			IReadOnlyList<PhoneMessage> messages = n.Messages;
			PhoneMessage phoneMessage = messages[messages.Count - 1];
			text2 = phoneMessage.Text;
			if (text.Length == 0)
			{
				text = phoneMessage.From;
			}
			else if (phoneMessage.From.Length > 0 && phoneMessage.From != text)
			{
				text2 = phoneMessage.From + ": " + text2;
			}
		}
		else
		{
			text2 = ((n.Text.Length > 0) ? n.Text : (n.BigText ?? ""));
		}
		if (text.Length == 0)
		{
			text = n.AppName;
		}
		text2 = string.Join(" ", text2.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
		return (Who: text, What: text2);
	}

	private void ShowPhoneCard(PhoneNotification n)
	{
		(string Who, string What) tuple = PhoneLines(n);
		string item = tuple.Who;
		string item2 = tuple.What;
		ImageSource? imageSource = Picture(n.Avatar);
		ImageSource? imageSource2 = AppIcon(n);
		bool flag = n.Category == "call";
		bool flag2 = _phoneCardShown && _state == IslandState.Toast && _phoneToast != null && PhoneId(_phoneToast) == PhoneId(n);
		PhoneAvatarBrush.ImageSource = imageSource;
		PhoneAvatarRound.Visibility = ((imageSource == null) ? Visibility.Collapsed : Visibility.Visible);
		PhoneAppBrush.ImageSource = ((imageSource == null) ? imageSource2 : null);
		PhoneAvatarSquare.Visibility = ((imageSource != null || imageSource2 == null) ? Visibility.Collapsed : Visibility.Visible);
		PhoneAvatarGlyph.Visibility = ((imageSource != null || imageSource2 != null) ? Visibility.Collapsed : Visibility.Visible);
		PhoneGlyph.Text = (flag ? "\ue717" : "\ue8bd");
		PhoneBadgeBrush.ImageSource = ((imageSource != null) ? imageSource2 : null);
		PhoneBadge.Visibility = ((imageSource == null || imageSource2 == null) ? Visibility.Collapsed : Visibility.Visible);
		PhoneWho.Text = item;
		PhoneApp.Text = ((n.AppName.Length > 0 && n.AppName != item) ? (" · " + n.AppName) : "");
		PhoneTime.Text = (flag ? "звонок" : When(n.Time));
		PhoneText.Text = ((flag && item2.Length == 0) ? "Входящий звонок" : item2);
		PhoneText.Visibility = ((PhoneText.Text.Length <= 0) ? Visibility.Collapsed : Visibility.Visible);
		List<PhoneAction> simple = n.Actions.Where((PhoneAction a) => !a.Reply && a.Title.Length > 0).ToList();
		if (flag)
		{
			ref(string Text, Action Run)? phoneButton = ref _phoneButton1;
			(string, Action)? tuple2;
			if (simple.Count > 1)
			{
				tuple2 = (simple[1].Title, () =>
				{
					Phones?.Invoke(n, simple[1]);
				});
			}
			else
			{
				tuple2 = ((simple.Count > 0) ? new (string, Action)?((simple[0].Title, () =>
				{
					Phones?.Invoke(n, simple[0]);
				})) : (((string, Action)?)null));
			}
			phoneButton = tuple2;
			_phoneButton2 = ((simple.Count > 1) ? new (string, Action)?((simple[0].Title, () =>
			{
				Phones?.Invoke(n, simple[0]);
			})) : (((string, Action)?)null));
		}
		else
		{
			Action item3 = () =>
			{
				Phones?.Dismiss(n);
			};
			_phoneButton1 = ((simple.Count > 0) ? (simple[0].Title, () =>
			{
				Phones?.Invoke(n, simple[0]);
			}) : ("Скрыть", item3));
			_phoneButton2 = ((simple.Count > 0) ? new (string, Action)?(("Скрыть", item3)) : (((string, Action)?)null));
		}
		PhoneButton1.Content = _phoneButton1?.Text;
		PhoneButton1.Visibility = ((!_phoneButton1.HasValue) ? Visibility.Collapsed : Visibility.Visible);
		PhoneButton2.Content = _phoneButton2?.Text;
		PhoneButton2.Visibility = ((!_phoneButton2.HasValue) ? Visibility.Collapsed : Visibility.Visible);
		PhoneButtons.Visibility = ((!flag && !(_hover & flag2)) ? Visibility.Collapsed : Visibility.Visible);
		PhoneButtons.Opacity = 1.0;
		if (!flag2)
		{
			CollapsePhoneCard();
		}
		else if (_phoneExpanded)
		{
			PhoneText.Visibility = Visibility.Collapsed;
			if (_phoneButton1.HasValue)
			{
				PhoneButtons.Visibility = Visibility.Visible;
			}
			FillPhoneMore(n);
		}
		_phoneToast = n;
		_toastAction = (_toastAction2 = null);
		_toastInteractive = true;
		ToastRow.Visibility = Visibility.Collapsed;
		PhoneCard.Visibility = Visibility.Visible;
		bool flag3 = _state != IslandState.Toast || !_phoneCardShown;
		_phoneCardShown = true;
		FitPhoneCard();
		ApplyState(IslandState.Toast);
		if (!ReducedMotion)
		{
			CubicEase ease = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			if (!flag2)
			{
				Pop(PhoneAvatar, flag3 ? 0.4 : 0.7);
				Animate(PhoneLinesShift, TranslateTransform.XProperty, 0.0, 420, ease, 60, 14.0);
				Animate(PhoneWords, UIElement.OpacityProperty, 1.0, 300, ease, 60, 0.0);
			}
			else
			{
				Animate(PhoneText, UIElement.OpacityProperty, 1.0, 260, ease, 0, 0.2);
			}
		}
		_toastTimer.Stop();
		DispatcherTimer toastTimer = _toastTimer;
		double value;
		if (flag)
		{
			value = 25.0;
		}
		else
		{
			value = (_phoneExpanded ? 15.0 : Math.Max(6.0, Settings.Current.ToastSeconds + 3.0));
		}
		toastTimer.Interval = TimeSpan.FromSeconds(value);
		if (!_hover)
		{
			_toastTimer.Start();
		}
	}

	private void PhoneCard_Click(object sender, MouseButtonEventArgs e)
	{
		if (!_phoneCardShown)
		{
			return;
		}
		PhoneNotification? phoneToast = _phoneToast;
		if (phoneToast != null && !_phoneExpanded)
		{
			e.Handled = true;
			_phoneExpanded = true;
			FillPhoneMore(phoneToast);
			PhoneText.Visibility = Visibility.Collapsed;
			PhoneMore.Visibility = Visibility.Visible;
			PhoneCard.Cursor = null;
			if (_phoneButton1.HasValue)
			{
				PhoneButtons.Visibility = Visibility.Visible;
				PhoneButtons.Opacity = 1.0;
			}
			FitPhoneCard();
			if (!ReducedMotion)
			{
				Animate(PhoneMore, UIElement.OpacityProperty, 1.0, 260, new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}, 0, 0.0);
			}
			_toastTimer.Stop();
			_toastTimer.Interval = TimeSpan.FromSeconds(15L);
			if (!_hover)
			{
				_toastTimer.Start();
			}
			if (PhoneReplyFrame.Visibility == Visibility.Visible)
			{
				BeginTyping(PhoneReplyInput);
			}
		}
	}

	private void CollapsePhoneCard()
	{
		if (_phoneExpanded)
		{
			_phoneExpanded = false;
			if (_typing && _typingBox == PhoneReplyInput)
			{
				EndTyping(restoreFocus: true);
			}
			PhoneMore.Visibility = Visibility.Collapsed;
			PhoneHistory.Children.Clear();
			PhoneReplyInput.Clear();
			PhoneCard.Cursor = System.Windows.Input.Cursors.Hand;
			PhoneText.Visibility = ((PhoneText.Text.Length <= 0) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private void FillPhoneMore(PhoneNotification n)
	{
		PhoneHistory.Children.Clear();
		System.Windows.Media.Brush foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
		System.Windows.Media.Brush brush = (System.Windows.Media.Brush)FindResource("TextSecondary");
		if (n.Messages.Count > 0)
		{
			string title = n.Title.Trim();
			bool num = (from m in n.Messages
				select m.From into f
				where f.Length > 0
				select f).Distinct().Count() > 1 || n.Messages.Any((PhoneMessage m) => m.From.Length > 0 && m.From != title);
			IReadOnlyList<PhoneMessage> messages = n.Messages;
			PhoneMessage phoneMessage = messages[messages.Count - 1];
			TextBlock textBlock = Line();
			bool flag = phoneMessage.From.Length == 0;
			if (num | flag)
			{
				textBlock.Inlines.Add(new Run((flag ? "Вы" : phoneMessage.From) + "  ")
				{
					FontWeight = FontWeights.SemiBold,
					Foreground = foreground
				});
			}
			textBlock.Inlines.Add(new Run(phoneMessage.Text)
			{
				Foreground = (flag ? ((System.Windows.Media.Brush)FindResource("TextTertiary")) : brush)
			});
			PhoneHistory.Children.Add(textBlock);
		}
		else
		{
			TextBlock textBlock2 = Line();
			textBlock2.Inlines.Add(new Run((n.BigText ?? n.Text).Trim())
			{
				Foreground = brush
			});
			if (!string.IsNullOrWhiteSpace(n.SubText))
			{
				textBlock2.Inlines.Add(new Run("\n" + n.SubText.Trim())
				{
					Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary")
				});
			}
			PhoneHistory.Children.Add(textBlock2);
		}
		PhoneReplyFrame.Visibility = ((!n.Actions.Any((PhoneAction a) => a.Reply) || Phones == null) ? Visibility.Collapsed : Visibility.Visible);
		PhoneReplyPlaceholder.Visibility = ((PhoneReplyInput.Text.Length != 0) ? Visibility.Collapsed : Visibility.Visible);
		Dispatcher.BeginInvoke((Action)(() =>
		{
			PhoneHistoryScroll.ScrollToEnd();
		}), DispatcherPriority.Loaded);
		FitPhoneCardSoon();
		static TextBlock Line()
		{
			return new TextBlock
			{
				FontSize = 12.5,
				TextWrapping = TextWrapping.Wrap,
				LineHeight = 17.0,
				LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
				Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
			};
		}
	}

	private void PhoneReplyInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.Return && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
		{
			e.Handled = true;
			SendPhoneReply();
		}
	}

	private void PhoneReplyInput_TextChanged(object sender, TextChangedEventArgs e)
	{
		PhoneReplyPlaceholder.Visibility = ((PhoneReplyInput.Text.Length != 0) ? Visibility.Collapsed : Visibility.Visible);
		if (_phoneExpanded)
		{
			FitPhoneCardSoon();
		}
	}

	private void PhoneReplySend_Click(object sender, RoutedEventArgs e)
	{
		SendPhoneReply();
	}

	private void SendPhoneReply()
	{
		string text = PhoneReplyInput.Text.Trim();
		PhoneNotification? phoneToast = _phoneToast;
		if (phoneToast == null)
		{
			return;
		}
		PhoneLinkService? phones = Phones;
		if (phones == null || text.Length == 0)
		{
			return;
		}
		PhoneAction? phoneAction = phoneToast.Actions.FirstOrDefault((PhoneAction a) => a.Reply);
		if (!(phoneAction == null))
		{
			phones.Invoke(phoneToast, phoneAction, text);
			_phoneShown.Remove(PhoneId(phoneToast));
			PhoneReplyInput.Clear();
			EndTyping(restoreFocus: true);
			_hover = false;
			PhoneCardDone();
			if (!_phoneCardShown)
			{
				ShowToast("\ue73e", "Ответ отправлен", null, "", FillNormal);
			}
		}
	}

	private static string When(DateTime time)
	{
		TimeSpan timeSpan = DateTime.Now - time;
		if (!(timeSpan < TimeSpan.FromMinutes(1L)))
		{
			if (!(timeSpan < TimeSpan.FromHours(1)))
			{
				return time.ToString("HH:mm");
			}
			return $"{(int)timeSpan.TotalMinutes} мин";
		}
		return "сейчас";
	}

	private ImageSource? AppIcon(PhoneNotification n)
	{
		if (_phoneIcons.TryGetValue(n.Package, out ImageSource? value) && value != null)
		{
			return value;
		}
		ImageSource? imageSource = Picture(n.AppIcon);
		_phoneIcons[n.Package] = imageSource;
		return imageSource;
	}

	private static ImageSource? Picture(byte[]? png)
	{
		if (png == null || png.Length == 0)
		{
			return null;
		}
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.StreamSource = new MemoryStream(png);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			return bitmapImage;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private void PhoneSubTab_Click(object sender, RoutedEventArgs e)
	{
		ShowPhoneSubTab(int.Parse((string)((System.Windows.Controls.Button)sender).Tag));
	}

	private static int SubTabPlace(int index)
	{
		return index switch
		{
			0 => 0, 
			2 => 1, 
			_ => 2, 
		};
	}

	private void ShowPhoneSubTab(int index)
	{
		int phoneSubTab = _phoneSubTab;
		_phoneSubTab = index;
		FrameworkElement[] array = new FrameworkElement[3] { PhoneNotesView, PhoneDeviceView, PhoneFilesView };
		System.Windows.Controls.Button[] array2 = new System.Windows.Controls.Button[3] { PhoneSubNotes, PhoneSubDevice, PhoneSubFiles };
		double value = Math.Sign(SubTabPlace(index) - SubTabPlace(phoneSubTab)) * 18;
		for (int i = 0; i < array.Length; i++)
		{
			bool flag = i == index;
			array2[i].Background = (flag ? SubTabActive : System.Windows.Media.Brushes.Transparent);
			array2[i].Foreground = (flag ? FillNormal : ((System.Windows.Media.Brush)FindResource("TextSecondary")));
			if (flag && array[i].Visibility != Visibility.Visible)
			{
				array[i].Visibility = Visibility.Visible;
				if (!ReducedMotion)
				{
					CubicEase ease = new CubicEase
					{
						EasingMode = EasingMode.EaseOut
					};
					TranslateTransform? translateTransform = array[i].RenderTransform as TranslateTransform;
					if (translateTransform == null)
					{
						translateTransform = (TranslateTransform)(array[i].RenderTransform = new TranslateTransform());
					}
					Animate(translateTransform, TranslateTransform.XProperty, 0.0, 320, ease, 0, value);
					Animate(array[i], UIElement.OpacityProperty, 1.0, 220, ease, 0, 0.0);
				}
				_cascade = true;
			}
			else if (!flag)
			{
				array[i].Visibility = Visibility.Collapsed;
			}
		}
		RenderPhoneCenter();
	}

	private static void RowIn(FrameworkElement row, int order)
	{
		if (!ReducedMotion)
		{
			CubicEase ease = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			int delayMs = Math.Min(order, 8) * 35;
			TranslateTransform target = (TranslateTransform)(row.RenderTransform = new TranslateTransform());
			row.Opacity = 0.0;
			Animate(target, TranslateTransform.YProperty, 0.0, 360, ease, delayMs, -8.0);
			Animate(row, UIElement.OpacityProperty, 1.0, 260, ease, delayMs, 0.0);
		}
	}

	private static void RowOut(FrameworkElement row, Action removed)
	{
		if (ReducedMotion)
		{
			removed();
			return;
		}
		ScaleTransform fold = new ScaleTransform(1.0, 1.0);
		row.LayoutTransform = fold;
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(140L));
		doubleAnimation.Completed += (object? _, EventArgs _) =>
		{
			DoubleAnimation doubleAnimation2 = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180L))
			{
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseIn
				}
			};
			doubleAnimation2.Completed += (object? obj, EventArgs e) =>
			{
				removed();
			};
			fold.BeginAnimation(ScaleTransform.ScaleYProperty, doubleAnimation2);
		};
		row.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
	}

	private void PhoneDnd_Click(object sender, RoutedEventArgs e)
	{
		Settings.Current.PhoneDnd = !Settings.Current.PhoneDnd;
		Settings.Current.Save();
		Settings.NotifyChanged();
		UpdateDndButton();
		Pop(PhoneDndIcon, 0.5);
		TellDnd();
	}

	private void TellDnd()
	{
		ShowToast("\ue708", Settings.Current.PhoneDnd ? "Не беспокоить" : "Уведомления с телефона снова приходят", null, "", FillNormal, null, 2500, symbolFont: false, Settings.Current.PhoneDnd ? new ToastExtra("Уведомления с телефона собираются во вкладке") : null);
	}

	private void UpdateDndButton()
	{
		bool phoneDnd = Settings.Current.PhoneDnd;
		PhoneDndIcon.Foreground = (phoneDnd ? new SolidColorBrush(_accent) : ((System.Windows.Media.Brush)FindResource("TextSecondary")));
		PhoneDndButton.Background = (phoneDnd ? SubTabActive : System.Windows.Media.Brushes.Transparent);
		PhoneDndButton.ToolTip = (phoneDnd ? "Не беспокоить включено — нажмите, чтобы уведомления с телефона снова всплывали" : "Не беспокоить: уведомления с телефона не всплывают, а тихо собираются здесь");
	}

	private void PhoneSetup_Click(object sender, RoutedEventArgs e)
	{
		((App)System.Windows.Application.Current).ShowSettings("phone");
	}

	private void PhoneClearAll_Click(object sender, RoutedEventArgs e)
	{
		PhoneLinkService? phones = Phones;
		if (phones != null)
		{
			PhoneNotification[] array = CenterItems();
			foreach (PhoneNotification n in array)
			{
				phones.Dismiss(n);
			}
			PhoneCenterList.Children.Clear();
			RenderPhoneCenter(keepList: true);
		}
	}

	private static PhoneNotification[] CenterItems()
	{
		return Phones?.Active.Where((PhoneNotification n) =>
		{
			if (!n.Ongoing)
			{
				var (text, text2) = PhoneLines(n);
				if (text.Length <= 0)
				{
					return text2.Length > 0;
				}
				return true;
			}
			return false;
		}).Take(40).ToArray() ?? Array.Empty<PhoneNotification>();
	}

	private void RenderPhoneCenter()
	{
		RenderPhoneCenter(keepList: false);
	}

	private void RenderPhoneCenter(bool keepList)
	{
		if (_state != IslandState.Expanded || _shownTab != PhonePanel)
		{
			_phoneOnScreen = false;
			return;
		}
		if (!_phoneOnScreen)
		{
			_cascade = true;
		}
		_phoneOnScreen = true;
		UpdateDndButton();
		if (_phoneSubTab == 1)
		{
			RenderPhoneDevice();
			return;
		}
		if (_phoneSubTab == 2)
		{
			RenderPhoneFiles();
			return;
		}
		if (!keepList && CenterReplyTyping)
		{
			_centerStale = true;
			return;
		}
		_centerStale = false;
		PhoneNotification[] array = (keepList ? Array.Empty<PhoneNotification>() : CenterItems());
		if (!keepList)
		{
			PhoneCenterList.Children.Clear();
			HashSet<string> hashSet = new HashSet<string>();
			int num = 0;
			PhoneNotification[] array2 = array;
			foreach (PhoneNotification n in array2)
			{
				FrameworkElement frameworkElement = CenterItem(n);
				string item = PhoneId(n);
				if (_cascade || !_centerSeen.Contains(item))
				{
					RowIn(frameworkElement, _cascade ? num++ : 0);
				}
				hashSet.Add(item);
				PhoneCenterList.Children.Add(frameworkElement);
			}
			_centerSeen.Clear();
			_centerSeen.UnionWith(hashSet);
			_cascade = false;
		}
		int count = PhoneCenterList.Children.Count;
		PhoneCenterHeader.Visibility = ((count <= 0) ? Visibility.Collapsed : Visibility.Visible);
		PhoneCenterScroll.Visibility = ((count <= 0) ? Visibility.Collapsed : Visibility.Visible);
		PhoneCenterEmpty.Visibility = ((count > 0) ? Visibility.Collapsed : Visibility.Visible);
		PhoneCenterCount.Text = $"{count} {Plural(count, "уведомление", "уведомления", "уведомлений")}";
		PhoneLinkService? link = Phones;
		bool flag = link != null && link.Devices.Any((PhoneDevice d) => link.IsConnected(d.Id));
		TextBlock phoneCenterEmptyText = PhoneCenterEmptyText;
		string text;
		if (link == null)
		{
			text = "Уведомления с телефона выключены";
		}
		else if (link.Devices.Count != 0)
		{
			text = ((!flag) ? "Телефон сейчас не на связи. Он появится сам, когда будет в той же сети" : "Новых уведомлений нет");
		}
		else
		{
			text = "Телефон ещё не подключён: отсканируйте QR-код в настройках";
		}
		phoneCenterEmptyText.Text = text;
		PhoneCenterSetup.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		PhoneCenterSetup.Content = ((link == null || link.Devices.Count == 0) ? "Подключить телефон" : "Настройки телефона");
	}

	private FrameworkElement CenterItem(PhoneNotification n)
	{
		(string Who, string What) tuple = PhoneLines(n);
		string item = tuple.Who;
		string item2 = tuple.What;
		ImageSource? imageSource = Picture(n.Avatar);
		ImageSource? imageSource2 = AppIcon(n);
		Grid grid = new Grid
		{
			Width = 32.0,
			Height = 32.0,
			VerticalAlignment = VerticalAlignment.Top
		};
		if (imageSource != null)
		{
			grid.Children.Add(new Ellipse
			{
				Fill = new ImageBrush(imageSource)
				{
					Stretch = Stretch.UniformToFill
				}
			});
			if (imageSource2 != null)
			{
				grid.Children.Add(new Border
				{
					Width = 14.0,
					Height = 14.0,
					CornerRadius = new CornerRadius(4.5),
					Margin = new Thickness(0.0, 0.0, -3.0, -3.0),
					HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
					VerticalAlignment = VerticalAlignment.Bottom,
					BorderBrush = (System.Windows.Media.Brush)FindResource("Surface"),
					BorderThickness = new Thickness(1.5),
					Background = new ImageBrush(imageSource2)
					{
						Stretch = Stretch.UniformToFill
					}
				});
			}
		}
		else if (imageSource2 != null)
		{
			grid.Children.Add(new Border
			{
				CornerRadius = new CornerRadius(8.0),
				Background = new ImageBrush(imageSource2)
				{
					Stretch = Stretch.UniformToFill
				}
			});
		}
		else
		{
			grid.Children.Add(new Ellipse
			{
				Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 44, 46))
			});
			grid.Children.Add(new TextBlock
			{
				Text = "\ue8bd",
				FontFamily = (System.Windows.Media.FontFamily)FindResource("Icons"),
				FontSize = 13.0,
				Foreground = FillNormal,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		RenderOptions.SetBitmapScalingMode(grid, BitmapScalingMode.HighQuality);
		TextBlock textBlock = new TextBlock
		{
			FontSize = 12.5,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		textBlock.Inlines.Add(new Run(item)
		{
			FontWeight = FontWeights.SemiBold,
			Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary")
		});
		if (n.AppName.Length > 0 && n.AppName != item)
		{
			textBlock.Inlines.Add(new Run(" · " + n.AppName)
			{
				Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary")
			});
		}
		TextBlock element = new TextBlock
		{
			Text = When(n.Time),
			FontSize = 11.0,
			Margin = new Thickness(8.0, 1.0, 0.0, 0.0),
			Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary")
		};
		Grid grid2 = new Grid();
		grid2.ColumnDefinitions.Add(new ColumnDefinition());
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid2.Children.Add(textBlock);
		Grid.SetColumn(element, 1);
		grid2.Children.Add(element);
		StackPanel words = new StackPanel
		{
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0)
		};
		words.Children.Add(grid2);
		TextBlock? body = null;
		if (item2.Length > 0)
		{
			UIElementCollection children = words.Children;
			TextBlock textBlock2 = new TextBlock
			{
				Text = item2,
				FontSize = 12.0,
				Margin = new Thickness(0.0, 2.0, 0.0, 0.0),
				TextWrapping = TextWrapping.Wrap,
				TextTrimming = TextTrimming.CharacterEllipsis,
				LineHeight = 16.0,
				LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
				MaxHeight = 33.5,
				Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary")
			};
			TextBlock element2 = textBlock2;
			body = textBlock2;
			children.Add(element2);
		}
		PhoneAction? reply = n.Actions.FirstOrDefault((PhoneAction a) => a.Reply);
		System.Windows.Controls.Button close = new System.Windows.Controls.Button
		{
			Style = (Style)FindResource("PinButton"),
			Width = 22.0,
			Height = 22.0,
			Margin = new Thickness(6.0, -2.0, -4.0, 0.0),
			VerticalAlignment = VerticalAlignment.Top,
			Opacity = 0.0,
			ToolTip = "Скрыть",
			Content = new TextBlock
			{
				Text = "\ue711",
				FontFamily = (System.Windows.Media.FontFamily)FindResource("Icons"),
				FontSize = 9.0
			}
		};
		Grid grid3 = new Grid();
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid3.ColumnDefinitions.Add(new ColumnDefinition());
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid3.Children.Add(grid);
		Grid.SetColumn(words, 1);
		grid3.Children.Add(words);
		Grid.SetColumn(close, 2);
		grid3.Children.Add(close);
		Border item3 = new Border
		{
			Child = grid3,
			Background = (System.Windows.Media.Brush)FindResource("Surface"),
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(10.0, 9.0, 10.0, 9.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
		};
		if (reply != null)
		{
			System.Windows.Controls.Button answer = new System.Windows.Controls.Button
			{
				Style = (Style)FindResource("LinkButton"),
				Content = "Ответить",
				HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
				Margin = new Thickness(-6.0, 2.0, 0.0, -4.0)
			};
			words.Children.Add(answer);
			answer.Click += (object _, RoutedEventArgs e) =>
			{
				e.Handled = true;
				OpenCenterReply(n, reply, words, body, answer);
			};
			item3.Cursor = System.Windows.Input.Cursors.Hand;
			item3.MouseLeftButtonUp += (object _, MouseButtonEventArgs e) =>
			{
				if (!(e.OriginalSource is DependencyObject d) || FindParent<System.Windows.Controls.TextBox>(d) == null)
				{
					OpenCenterReply(n, reply, words, body, answer);
				}
			};
			if (_centerReplyId == PhoneId(n))
			{
				Dispatcher.BeginInvoke((Action)(() =>
				{
					OpenCenterReply(n, reply, words, body, answer);
				}), DispatcherPriority.Loaded);
			}
		}
		item3.MouseEnter += (object _, System.Windows.Input.MouseEventArgs _) =>
		{
			Animate(close, UIElement.OpacityProperty, 1.0, 120, null);
		};
		item3.MouseLeave += (object _, System.Windows.Input.MouseEventArgs _) =>
		{
			Animate(close, UIElement.OpacityProperty, 0.0, 160, null);
		};
		close.Click += (object _, RoutedEventArgs _) =>
		{
			Phones?.Dismiss(n);
			RowOut(item3, () =>
			{
				PhoneCenterList.Children.Remove(item3);
				RenderPhoneCenter(keepList: true);
			});
		};
		item3.MouseRightButtonUp += (object _, MouseButtonEventArgs e) =>
		{
			(string, bool, Action)[] items = (from a in n.Actions
				where !a.Reply && a.Title.Length > 0
				select ((string Title, bool, Action))(Title: a.Title, true, () =>
				{
					Phones?.Invoke(n, a);
				})).Append(("Скрыть", true, () =>
			{
				Phones?.Dismiss(n);
			})).ToArray();
			ShowMenu(item3, items);
			e.Handled = true;
		};
		return item3;
	}

	private void OpenCenterReply(PhoneNotification n, PhoneAction reply, StackPanel words, TextBlock? body, System.Windows.Controls.Button answer)
	{
		if (_centerReplyBox != null && _centerReplyBox.Tag as string == PhoneId(n) && words.Children.Contains(_centerReplyFrame))
		{
			BeginTyping(_centerReplyBox);
			return;
		}
		CloseCenterReply(keepDraft: false);
		_centerReplyId = PhoneId(n);
		if (body != null)
		{
			body.MaxHeight = double.PositiveInfinity;
		}
		answer.Visibility = Visibility.Collapsed;
		System.Windows.Controls.TextBox box = new System.Windows.Controls.TextBox
		{
			Style = (Style)FindResource("IslandTextBox"),
			Padding = new Thickness(8.0, 6.0, 8.0, 6.0),
			FontSize = 12.5,
			MaxHeight = 70.0,
			AcceptsReturn = true,
			TextWrapping = TextWrapping.Wrap,
			VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
			Text = _centerReplyDraft,
			Tag = PhoneId(n)
		};
		TextBlock hint = new TextBlock
		{
			Text = "Ответить…",
			Margin = new Thickness(11.0, 6.0, 0.0, 0.0),
			FontSize = 12.5,
			IsHitTestVisible = false,
			Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary"),
			Visibility = ((box.Text.Length != 0) ? Visibility.Collapsed : Visibility.Visible)
		};
		System.Windows.Controls.Button button = new System.Windows.Controls.Button
		{
			Style = (Style)FindResource("PinButton"),
			Width = 26.0,
			Height = 26.0,
			Margin = new Thickness(0.0, 0.0, 2.0, 2.0),
			VerticalAlignment = VerticalAlignment.Bottom,
			ToolTip = "Отправить",
			Content = new TextBlock
			{
				Text = "\ue724",
				FontFamily = (System.Windows.Media.FontFamily)FindResource("Icons"),
				FontSize = 11.0
			}
		};
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.Children.Add(box);
		grid.Children.Add(hint);
		Grid.SetColumn(button, 1);
		grid.Children.Add(button);
		Border frame = new Border
		{
			Child = grid,
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			CornerRadius = new CornerRadius(11.0),
			BorderThickness = new Thickness(1.0),
			BorderBrush = System.Windows.Media.Brushes.Transparent,
			Margin = new Thickness(0.0, 7.0, 0.0, 1.0)
		};
		words.Children.Add(frame);
		_centerReplyBox = box;
		_centerReplyFrame = frame;
		box.TextChanged += (object _, TextChangedEventArgs _) =>
		{
			_centerReplyDraft = box.Text;
			hint.Visibility = ((box.Text.Length != 0) ? Visibility.Collapsed : Visibility.Visible);
		};
		box.PreviewKeyDown += (object _, System.Windows.Input.KeyEventArgs e) =>
		{
			if (e.Key == Key.Return && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
			{
				e.Handled = true;
				Send();
			}
			else if (e.Key == Key.Escape)
			{
				CloseCenterReply(keepDraft: true);
				if (body != null)
				{
					body.MaxHeight = 33.5;
				}
				answer.Visibility = Visibility.Visible;
				words.Children.Remove(frame);
			}
		};
		button.Click += (object _, RoutedEventArgs _) =>
		{
			Send();
		};
		if (!ReducedMotion)
		{
			Animate(frame, UIElement.OpacityProperty, 1.0, 220, new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}, 0, 0.0);
		}
		Dispatcher.BeginInvoke((Action)(() =>
		{
			BeginTyping(box);
			box.CaretIndex = box.Text.Length;
			frame.BringIntoView();
		}), DispatcherPriority.Loaded);
		void Send()
		{
			string text = box.Text.Trim();
			if (text.Length != 0)
			{
				PhoneLinkService? phones = Phones;
				if (phones != null)
				{
					phones.Invoke(n, reply, text);
					_phoneShown.Remove(PhoneId(n));
					_centerReplyDraft = "";
					CloseCenterReply(keepDraft: false);
					EndTyping(restoreFocus: false);
					words.Children.Add(new TextBlock
					{
						Text = "Ответ отправлен",
						FontSize = 11.5,
						Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
						Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary")
					});
					if (_centerStale)
					{
						RenderPhoneCenter();
					}
				}
			}
		}
	}

	private void CloseCenterReply(bool keepDraft)
	{
		if (!keepDraft)
		{
			_centerReplyId = null;
			_centerReplyDraft = "";
		}
		_centerReplyBox = null;
		_centerReplyFrame = null;
	}

	private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
	{
		while (d != null && !(d is T))
		{
			bool flag = ((d is Visual || d is Visual3D) ? true : false);
			d = (flag ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d));
		}
		return d as T;
	}

	private void RenderPhoneDevice()
	{
		PhoneDeviceView.Children.Clear();
		PhoneLinkService? phones = Phones;
		if (phones == null || phones.Devices.Count == 0)
		{
			PhoneDeviceView.Children.Add(new TextBlock
			{
				Text = ((phones == null) ? "Уведомления с телефона выключены" : "Телефон ещё не подключён"),
				FontSize = 12.0,
				Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 40.0, 0.0, 0.0)
			});
		}
		IEnumerable<PhoneDevice>? enumerable = phones?.Devices;
		foreach (PhoneDevice device in enumerable ?? Array.Empty<PhoneDevice>())
		{
			bool flag = phones.IsConnected(device.Id);
			Ellipse element = new Ellipse
			{
				Width = 8.0,
				Height = 8.0,
				Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center,
				Fill = new SolidColorBrush(flag ? System.Windows.Media.Color.FromRgb(94, 216, 122) : System.Windows.Media.Color.FromRgb(110, 110, 115))
			};
			TextBlock element2 = new TextBlock
			{
				Text = ((device.Name.Length > 0) ? device.Name : (device.Model ?? "Телефон")),
				FontSize = 13.0,
				FontWeight = FontWeights.SemiBold,
				Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary")
			};
			StackPanel stackPanel = new StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal
			};
			stackPanel.Children.Add(element);
			stackPanel.Children.Add(element2);
			string text;
			if (!flag)
			{
				DateTime? lastSeen = device.LastSeen;
				if (lastSeen.HasValue)
				{
					DateTime valueOrDefault = lastSeen.GetValueOrDefault();
					text = $"Не на связи · был {valueOrDefault.ToLocalTime():d MMMM, HH:mm}";
				}
				else
				{
					text = "Ещё не подключался";
				}
			}
			else
			{
				text = "На связи";
			}
			string text2 = text;
			string? os = device.Os;
			if (os != null)
			{
				text2 = text2 + " · " + os;
			}
			StackPanel stackPanel2 = new StackPanel();
			stackPanel2.Children.Add(stackPanel);
			stackPanel2.Children.Add(new TextBlock
			{
				Text = text2,
				FontSize = 12.0,
				Margin = new Thickness(16.0, 3.0, 0.0, 0.0),
				Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary")
			});
			if (flag)
			{
				int num = CenterItems().Count((PhoneNotification x) => x.DeviceId == device.Id);
				stackPanel2.Children.Add(new TextBlock
				{
					Text = ((num > 0) ? $"{num} {Plural(num, "уведомление", "уведомления", "уведомлений")} в центре" : "Уведомлений нет"),
					FontSize = 12.0,
					Margin = new Thickness(16.0, 2.0, 0.0, 0.0),
					Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary")
				});
			}
			PhoneDeviceView.Children.Add(new Border
			{
				Child = stackPanel2,
				Background = (System.Windows.Media.Brush)FindResource("Surface"),
				CornerRadius = new CornerRadius(12.0),
				Padding = new Thickness(12.0, 10.0, 12.0, 10.0),
				Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
			});
		}
		System.Windows.Controls.Button button = new System.Windows.Controls.Button
		{
			Style = (Style)FindResource("ToastActionButton"),
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			Content = ((phones == null || phones.Devices.Count == 0) ? "Подключить телефон" : "Настройки телефона")
		};
		button.Click += PhoneSetup_Click;
		if (_cascade)
		{
			int num2 = 0;
			foreach (FrameworkElement child in PhoneDeviceView.Children)
			{
				RowIn(child, num2++);
			}
			_cascade = false;
		}
		System.Windows.Controls.Button button2 = new System.Windows.Controls.Button
		{
			Style = (Style)FindResource("LinkButton"),
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			Content = "Скачать Island Link на телефон"
		};
		button2.Click += (object _, RoutedEventArgs _) =>
		{
			((App)System.Windows.Application.Current).ShowSettings("phone-app");
		};
		StackPanel stackPanel3 = new StackPanel
		{
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center
		};
		stackPanel3.Children.Add(button);
		stackPanel3.Children.Add(button2);
		PhoneDeviceView.Children.Add(stackPanel3);
	}

	private static string Plural(int n, string one, string few, string many)
	{
		int num = n % 10;
		int num2 = n % 100;
		if (num == 1 && num2 != 11)
		{
			return one;
		}
		bool flag = num >= 2 && num <= 4;
		if (flag)
		{
			bool flag2 = ((num2 < 12 || num2 > 14) ? true : false);
			flag = flag2;
		}
		return flag ? few : many;
	}

	private bool FromPhone(string? text)
	{
		if (text != null && text == _clipFromPhone)
		{
			return DateTime.Now - _clipFromPhoneAt < TimeSpan.FromSeconds(3L);
		}
		return false;
	}

	private void OnPcTextCopied(string text)
	{
		PhoneLinkService? phones = Phones;
		if (phones != null && Settings.Current.PhoneClipboard && !(text == _clipFromPhone))
		{
			_clipFromPhone = null;
			phones.SendClipboard(text);
		}
	}

	private async void OnPcImageCopied(BitmapSource image)
	{
		if (!PhoneOnline || !Settings.Current.PhoneClipboard || ImageFromPhone)
		{
			return;
		}
		string text = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Island", "outgoing");
		string path = System.IO.Path.Combine(text, Guid.NewGuid().ToString("N") + ".png");
		try
		{
			Directory.CreateDirectory(text);
			using (FileStream stream = File.Create(path))
			{
				PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
				pngBitmapEncoder.Frames.Add(BitmapFrame.Create(image));
				pngBitmapEncoder.Save(stream);
			}
			if (new FileInfo(path).Length <= 20000000)
			{
				await Phones.SendFileAsync(path, clipboard: true, "Изображение.png");
			}
		}
		catch (Exception ex)
		{
			Log.Error("picture to phone", ex);
		}
		finally
		{
			try
			{
				File.Delete(path);
			}
			catch (Exception)
			{
			}
		}
	}

	private async void OnPhoneClipboard(PhoneDevice device, string text)
	{
		if (!Settings.Current.PhoneClipboard)
		{
			Log.Write("phone clipboard ignored: the shared clipboard is off");
			return;
		}
		_clipFromPhone = text;
		_clipFromPhoneAt = DateTime.Now;
		await _clipboard.SetTextAsync(text);
		if (!Settings.Current.ToastCopy || Settings.Current.IsTabHidden("clip"))
		{
			ShowToast("\ue8c8", "Скопировано с телефона", null, "", FillNormal, null, 2500, symbolFont: false, new ToastExtra(ClipItem.FromText(text).Preview, null, ToastAnim.Copy));
		}
	}

	private async void OnPhoneFile(PhoneFile file)
	{
		BitmapSource? thumb = (file.IsImage ? Thumbnail(file.Path) : null);
		if (file.Clipboard && file.IsImage && Settings.Current.PhoneClipboard)
		{
			BitmapSource? bitmapSource = LoadImage(file.Path);
			if (bitmapSource != null)
			{
				_imageFromPhoneAt = DateTime.Now;
				await _clipboard.SetImageAsync(bitmapSource);
				if (!Settings.Current.ToastCopy || Settings.Current.IsTabHidden("clip"))
				{
					MainWindow mainWindow = this;
					System.Windows.Media.Brush fillNormal = FillNormal;
					(string, Action)? action = ("Открыть", () =>
					{
						Open(file.Path);
					});
					ImageSource? thumb2 = thumb;
					string path = file.Path;
					mainWindow.ShowToast("\ueb9f", "Картинка с телефона", null, "", fillNormal, action, 3500, symbolFont: false, new ToastExtra("Скопирована в буфер обмена", null, ToastAnim.Copy, path, thumb2));
				}
				return;
			}
		}
		RememberPhoneFile(file.Path, file.Name, file.Size, incoming: true, (file.Device.Name.Length > 0) ? file.Device.Name : (file.Device.Model ?? "Телефон"));
		ShowToast(file.IsImage ? "\ueb9f" : "\ue8a5", file.IsImage ? "Фото с телефона" : "Файл с телефона", (double?)null, "", FillNormal, ((string Text, Action Run)?)("Открыть", () =>
		{
			Open(file.Path);
		}), 5000, false, new ToastExtra(file.Name, ((string Text, Action Run)?)("Папка", () =>
		{
			ShowInFolder(file.Path);
		}), ToastAnim.Done, file.Path, (ImageSource?)thumb, false));
	}

	private void OnPhoneTransfer(PhoneTransfer t)
	{
		if (t.Clipboard)
		{
			return;
		}
		if (t.State == PhoneTransferState.Failed)
		{
			if (t.Incoming)
			{
				ShowToast("\ue783", "Файл с телефона не дошёл", null, "", FillLow, null, 3500, symbolFont: false, new ToastExtra(t.Name + ": связь прервалась"));
			}
		}
		else if (t.State != PhoneTransferState.Done)
		{
			string text = (t.Incoming ? "Принимаю с телефона" : "Отправляю на телефон");
			if (_state != IslandState.Expanded && (_state != IslandState.Toast || (!_phoneCardShown && !(ToastLabel.Text != text))))
			{
				double fraction = t.Fraction;
				ShowToast(t.Incoming ? "\ue896" : "\ue898", text, fraction, $"{(int)(fraction * 100.0)}%", FillNormal, null, 8000, symbolFont: false, new ToastExtra($"{t.Name} · {FileSize(t.Done)} из {FileSize(t.Size)}"));
			}
		}
	}

	private static string FileSize(long bytes)
	{
		if (bytes >= 1048576)
		{
			if (bytes < 1073741824)
			{
				return $"{(double)bytes / 1048576.0:0.#} МБ";
			}
			return $"{(double)bytes / 1073741824.0:0.##} ГБ";
		}
		return $"{Math.Max(1L, bytes / 1024)} КБ";
	}

	private async void SendToPhone(string[] paths)
	{
		PhoneLinkService? link = Phones;
		if (link == null)
		{
			return;
		}
		string[] files = paths.Where(File.Exists).ToArray();
		if (files.Length == 0)
		{
			ShowToast("\ue8b7", "Папки на телефон не отправляются", null, "", FillNormal, null, 2500, symbolFont: false, new ToastExtra("Выберите сами файлы"));
			return;
		}
		int sent = 0;
		string[] array = files;
		foreach (string path in array)
		{
			if (await link.SendFileAsync(path))
			{
				sent++;
				RememberPhoneFile(path, System.IO.Path.GetFileName(path), new FileInfo(path).Length, incoming: false, OnlinePhoneName());
			}
		}
		if (sent == files.Length)
		{
			ShowToast("\ue73e", (files.Length == 1) ? "Отправлено на телефон" : $"Отправлено на телефон: {sent}", null, "", FillCharging, null, 2500, symbolFont: false, new ToastExtra("Загрузки → Island", null, ToastAnim.Done));
			return;
		}
		ShowToast("\ue783", "Не всё отправилось", null, "", FillLow, null, 3500, symbolFont: false, new ToastExtra($"Дошло {sent} из {files.Length}: телефон отключился?"));
	}

	private static BitmapSource? LoadImage(string path, int width = 0)
	{
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			if (width > 0)
			{
				bitmapImage.DecodePixelWidth = width;
			}
			bitmapImage.UriSource = new Uri(path);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			return bitmapImage;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static BitmapSource? Thumbnail(string path)
	{
		return LoadImage(path, 112);
	}

	private static void Open(string path)
	{
		try
		{
			ReviewSafety.Start(new ProcessStartInfo(path)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			Log.Error("open phone file", ex);
		}
	}

	private static void ShowInFolder(string path)
	{
		try
		{
			ReviewSafety.Start("explorer.exe", "/select,\"" + path + "\"");
		}
		catch (Exception ex)
		{
			Log.Error("show phone file", ex);
		}
	}

	private void SavePhoneFiles()
	{
		try
		{
			File.WriteAllText(PhoneFilesPath, JsonSerializer.Serialize(PhoneFiles));
		}
		catch (Exception ex)
		{
			Log.Error("phone files list", ex);
		}
	}

	private void RememberPhoneFile(string path, string name, long size, bool incoming, string device)
	{
		PhoneFiles.Insert(0, new PhoneFileEntry(name, path, size, incoming, DateTime.Now, device));
		if (PhoneFiles.Count > 60)
		{
			PhoneFiles.RemoveRange(60, PhoneFiles.Count - 60);
		}
		SavePhoneFiles();
		if (PhoneFilesShown)
		{
			RenderPhoneFiles();
		}
	}

	private string OnlinePhoneName()
	{
		PhoneLinkService? link = Phones;
		PhoneDevice? phoneDevice = link?.Devices.FirstOrDefault((PhoneDevice d) => link.IsConnected(d.Id));
		string text;
		if (phoneDevice != null)
		{
			if (phoneDevice.Name.Length > 0)
			{
				return phoneDevice.Name;
			}
			text = phoneDevice.Model;
			if (text == null)
			{
				return "Телефон";
			}
		}
		else
		{
			text = "Телефон";
		}
		return text;
	}

	private void RenderPhoneFiles()
	{
		int count = PhoneFiles.Count;
		PhoneFiles.RemoveAll((PhoneFileEntry f) => !File.Exists(f.Path));
		if (PhoneFiles.Count != count)
		{
			SavePhoneFiles();
		}
		PhoneFilesList.Children.Clear();
		int num = 0;
		foreach (PhoneFileEntry phoneFile in PhoneFiles)
		{
			FrameworkElement frameworkElement = PhoneFileItem(phoneFile);
			string item = phoneFile.Path + "|" + phoneFile.Time.Ticks;
			if (_cascade || (_filesSeen.Count > 0 && !_filesSeen.Contains(item)))
			{
				RowIn(frameworkElement, _cascade ? num++ : 0);
			}
			PhoneFilesList.Children.Add(frameworkElement);
		}
		_filesSeen.Clear();
		foreach (PhoneFileEntry phoneFile2 in PhoneFiles)
		{
			_filesSeen.Add(phoneFile2.Path + "|" + phoneFile2.Time.Ticks);
		}
		_cascade = false;
		bool flag = PhoneFiles.Count > 0;
		PhoneFilesScroll.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		PhoneFilesEmpty.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		ShowPhoneDropZone();
	}

	private void ShowPhoneDropZone()
	{
		bool phoneOnline = PhoneOnline;
		bool flag = _dragIn && PhoneFilesShown;
		PhoneDropFrame.Stroke = (flag ? new SolidColorBrush(_accent) : Freeze(new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, byte.MaxValue, byte.MaxValue, byte.MaxValue))));
		PhoneDropFrame.Fill = (flag ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(28, _accent.R, _accent.G, _accent.B)) : Freeze(new SolidColorBrush(System.Windows.Media.Color.FromArgb(10, byte.MaxValue, byte.MaxValue, byte.MaxValue))));
		TextBlock phoneDropText = PhoneDropText;
		string text;
		if (phoneOnline)
		{
			text = (flag ? "Отпустите — файлы уйдут на телефон" : "Перетащите файлы сюда — они уйдут на телефон");
		}
		else
		{
			text = "Телефон не на связи — отправить файлы сейчас не получится";
		}
		phoneDropText.Text = text;
		PhoneDropIcon.Foreground = (flag ? new SolidColorBrush(_accent) : ((System.Windows.Media.Brush)FindResource("TextSecondary")));
		if (flag)
		{
			Pop(PhoneDropIcon);
		}
	}

	private FrameworkElement PhoneFileItem(PhoneFileEntry f)
	{
		BitmapSource? bitmapSource = (IsPicture(f.Name) ? Thumbnail(f.Path) : null);
		Border border = new Border
		{
			Width = 32.0,
			Height = 32.0,
			CornerRadius = new CornerRadius(8.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		if (bitmapSource != null)
		{
			border.Background = new ImageBrush(bitmapSource)
			{
				Stretch = Stretch.UniformToFill
			};
		}
		else
		{
			border.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 44, 46));
			border.Child = new TextBlock
			{
				Text = "\ue8a5",
				FontFamily = (System.Windows.Media.FontFamily)FindResource("Icons"),
				FontSize = 13.0,
				Foreground = FillNormal,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
		}
		RenderOptions.SetBitmapScalingMode(border, BitmapScalingMode.HighQuality);
		TextBlock element = new TextBlock
		{
			Text = (f.Incoming ? "\ue896" : "\ue898"),
			FontFamily = (System.Windows.Media.FontFamily)FindResource("Icons"),
			FontSize = 10.0,
			Foreground = new SolidColorBrush(f.Incoming ? System.Windows.Media.Color.FromRgb(94, 216, 122) : System.Windows.Media.Color.FromRgb(79, 163, byte.MaxValue)),
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 4.0, 0.0)
		};
		TextBlock element2 = new TextBlock
		{
			Text = f.Name,
			FontSize = 12.5,
			FontWeight = FontWeights.SemiBold,
			TextTrimming = TextTrimming.CharacterEllipsis,
			Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary")
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(element);
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"{(f.Incoming ? "С телефона" : "На телефон")} · {FileSize(f.Size)} · {When(f.Time)}",
			FontSize = 11.0,
			Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary")
		});
		StackPanel stackPanel2 = new StackPanel
		{
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel2.Children.Add(element2);
		stackPanel2.Children.Add(stackPanel);
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.Children.Add(border);
		Grid.SetColumn(stackPanel2, 1);
		grid.Children.Add(stackPanel2);
		Border item = new Border
		{
			Child = grid,
			Background = (System.Windows.Media.Brush)FindResource("Surface"),
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(8.0, 7.0, 10.0, 7.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
			Cursor = System.Windows.Input.Cursors.Hand,
			ToolTip = "Открыть. Можно вытащить мышью в любую папку или окно"
		};
		SolidColorBrush hover = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromArgb(24, byte.MaxValue, byte.MaxValue, byte.MaxValue)));
		item.MouseEnter += (object _, System.Windows.Input.MouseEventArgs _) =>
		{
			item.Background = hover;
		};
		item.MouseLeave += (object _, System.Windows.Input.MouseEventArgs _) =>
		{
			item.Background = (System.Windows.Media.Brush)FindResource("Surface");
		};
		System.Windows.Point? down = null;
		item.MouseLeftButtonDown += (object _, MouseButtonEventArgs e) =>
		{
			down = e.GetPosition(item);
		};
		item.MouseLeftButtonUp += (object _, MouseButtonEventArgs _) =>
		{
			if (down.HasValue)
			{
				Open(f.Path);
			}
			down = null;
		};
		item.MouseMove += (object _, System.Windows.Input.MouseEventArgs e) =>
		{
			if (down.HasValue)
			{
				System.Windows.Point valueOrDefault = down.GetValueOrDefault();
				if (e.LeftButton == MouseButtonState.Pressed)
				{
					System.Windows.Point position = e.GetPosition(item);
					if (!(Math.Abs(position.X - valueOrDefault.X) < 5.0) || !(Math.Abs(position.Y - valueOrDefault.Y) < 5.0))
					{
						down = null;
						DragPhoneFileOut(item, f.Path);
					}
				}
			}
		};
		item.MouseRightButtonUp += (object _, MouseButtonEventArgs e) =>
		{
			List<(string, bool, Action)> list = new List<(string, bool, Action)>
			{
				("Открыть", true, () =>
				{
					Open(f.Path);
				}),
				("Показать в папке", true, () =>
				{
					ShowInFolder(f.Path);
				}),
				("Отправить на телефон", PhoneOnline, () =>
				{
					SendToPhone(new string[1] { f.Path });
				}),
				("Убрать из списка", true, () =>
				{
					PhoneFiles.Remove(f);
					SavePhoneFiles();
					RowOut(item, RenderPhoneFiles);
				})
			};
			ShowMenu(item, list.ToArray());
			e.Handled = true;
		};
		return item;
	}

	private static bool IsPicture(string name)
	{
		string text = System.IO.Path.GetExtension(name).ToLowerInvariant();
		if (text != null)
		{
			int length = text.Length;
			if (length != 4)
			{
				if (length == 5)
				{
					char c = text[1];
					if (c != 'h')
					{
						if (c != 'j')
						{
							if (c == 'w' && text == ".webp")
							{
								goto IL_00d1;
							}
						}
						else if (text == ".jpeg")
						{
							goto IL_00d1;
						}
					}
					else if (text == ".heic")
					{
						goto IL_00d1;
					}
				}
			}
			else
			{
				char c = text[1];
				if ((uint)c <= 103u)
				{
					if (c != 'b')
					{
						if (c == 'g' && text == ".gif")
						{
							goto IL_00d1;
						}
					}
					else if (text == ".bmp")
					{
						goto IL_00d1;
					}
				}
				else if (c != 'j')
				{
					if (c == 'p' && text == ".png")
					{
						goto IL_00d1;
					}
				}
				else if (text == ".jpg")
				{
					goto IL_00d1;
				}
			}
		}
		return false;
		IL_00d1:
		return true;
	}

	private void DragPhoneFileOut(FrameworkElement item, string path)
	{
		if (!File.Exists(path))
		{
			return;
		}
		_dragOut = true;
		item.Opacity = 0.45;
		try
		{
			DragDrop.DoDragDrop(item, new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new string[1] { path }), System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Link);
		}
		catch (Exception ex)
		{
			Log.Error("phone file drag", ex);
		}
		finally
		{
			_dragOut = false;
			Animate(item, UIElement.OpacityProperty, 1.0, 200, new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			});
			ReleaseKeepOpen();
		}
	}

	private void DropOnPhone(string[] paths)
	{
		if (!PhoneOnline)
		{
			ShowToast("", "Телефон не на связи", null, "", FillLow, null, 3000, symbolFont: false, new ToastExtra("Проверьте, что он в той же сети, и попробуйте ещё раз"));
		}
		else
		{
			SendToPhone(paths);
		}
	}

	private void More_Click(object sender, RoutedEventArgs e)
	{
		Island.Services.MediaState? m = _media.Current;
		bool item = m != null;
		ShowMenu(MoreButton, ("Куда играет звук…", true, () =>
		{
			Later(ShowOutputs);
		}), ("История и итоги…", true, () =>
		{
			Later(ShowTracks);
		}), ("О треке…", item, () =>
		{
			Later(ShowTrackInfo);
		}), ("Поделиться карточкой", item, () =>
		{
			Later(ShareCurrentTrack);
		}), ((m != null && m.SourceApp.Length > 0) ? ("Открыть " + m.SourceApp) : "Открыть плеер", item, () =>
		{
			if (m != null)
			{
				OpenPlayerApp(m);
			}
		}));
		void Later(Action a)
		{
			Dispatcher.BeginInvoke(a, DispatcherPriority.ApplicationIdle);
		}
	}

	private void InitShelfAndPins()
	{
		ShelfList.ItemsSource = _shelf.Items;
		_shelf.Items.CollectionChanged += (object? _, NotifyCollectionChangedEventArgs _) =>
		{
			UpdateShelfEmpty();
		};
		_pins.Items.CollectionChanged += (object? _, NotifyCollectionChangedEventArgs e) =>
		{
			if (e.NewItems != null)
			{
				foreach (FileEntry newItem in e.NewItems)
				{
					if (newItem.IsLink)
					{
						newItem.PropertyChanged += (object? obj, PropertyChangedEventArgs e2) =>
						{
							RebuildDock();
						};
					}
				}
			}
			RebuildDock();
		};
		foreach (FileEntry item in _pins.Items)
		{
			if (item.IsLink)
			{
				item.PropertyChanged += (object? _, PropertyChangedEventArgs _) =>
				{
					RebuildDock();
				};
			}
		}
		_springTimer.Tick += SpringTimer_Tick;
		UpdateShelfEmpty();
		RebuildDock();
	}

	private void RebuildDock()
	{
		List<DockItem> list = new List<DockItem>();
		foreach (FileEntry item in _pins.Items)
		{
			list.Add(new DockItem
			{
				Title = item.Name,
				Tooltip = item.Path,
				Icon = item.Icon,
				Pin = item
			});
		}
		if (!_pins.IsFull)
		{
			list.Add(new DockItem
			{
				Title = "Добавить",
				Tooltip = "Закрепить программу или файл (можно перетащить сюда)",
				Glyph = "\ue710"
			});
		}
		DockList.ItemsSource = list;
	}

	private void DockScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (!(DockScroll.ScrollableWidth <= 0.0))
		{
			DockScroll.ScrollToHorizontalOffset(DockScroll.HorizontalOffset - (double)e.Delta / 2.0);
			e.Handled = true;
		}
	}

	private void Dock_Click(object sender, RoutedEventArgs e)
	{
		if (((FrameworkElement)sender).DataContext is DockItem dockItem)
		{
			if (dockItem.Pin != null)
			{
				Launch(dockItem.Pin.Path);
			}
			else
			{
				AddPin();
			}
		}
	}

	private void Dock_RightClick(object sender, MouseButtonEventArgs e)
	{
		if (!(((FrameworkElement)sender).DataContext is DockItem dockItem))
		{
			return;
		}
		FileEntry? pin = dockItem.Pin;
		if (pin != null)
		{
			ShowMenu((FrameworkElement)sender, ("Открыть", true, () =>
			{
				Launch(pin.Path);
			}), ("Показать в папке", true, () =>
			{
				RevealInExplorer(pin.Path);
			}), ("Открепить", true, () =>
			{
				_pins.Remove(pin);
			}));
		}
		e.Handled = true;
	}

	private void UpdateShelfEmpty()
	{
		bool flag = _shelf.Items.Count == 0;
		ShelfEmpty.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		ShelfClear.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		ShelfHint.Visibility = (flag ? Visibility.Hidden : Visibility.Visible);
	}

	private static string[]? DroppedPaths(System.Windows.DragEventArgs e)
	{
		if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
		{
			return null;
		}
		return e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
	}

	private static string? DroppedLink(System.Windows.DragEventArgs e)
	{
		if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
		{
			return null;
		}
		string? text = null;
		try
		{
			if (e.Data.GetDataPresent(System.Windows.DataFormats.UnicodeText))
			{
				text = e.Data.GetData(System.Windows.DataFormats.UnicodeText) as string;
			}
			else if (e.Data.GetDataPresent(System.Windows.DataFormats.Text))
			{
				text = e.Data.GetData(System.Windows.DataFormats.Text) as string;
			}
		}
		catch (Exception)
		{
		}
		if (!LinkInfo.TryNormalize(text, allowBare: false, out string url))
		{
			return null;
		}
		return url;
	}

	private void Pill_DragEnter(object sender, System.Windows.DragEventArgs e)
	{
		if ((DroppedPaths(e) != null || DroppedLink(e) != null) && !_dragOut)
		{
			if (!_dragIn && (!HomeShown || _homeHasMedia) && !PhoneFilesShown && !Settings.Current.IsTabHidden("shelf"))
			{
				SelectTab(1);
			}
			_dragIn = true;
			if (PhoneFilesShown)
			{
				ShowPhoneDropZone();
			}
			ShelfDropFrame.Stroke = new SolidColorBrush(_accent);
			MarchDropFrame(on: true);
			_hoverTimer.Stop();
			_hover = true;
			ApplyState(IslandState.Expanded);
		}
	}

	private void Pill_DragOver(object sender, System.Windows.DragEventArgs e)
	{
		e.Effects = (((DroppedPaths(e) != null || DroppedLink(e) != null) && !_dragOut) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None);
		e.Handled = true;
		if (e.Effects != System.Windows.DragDropEffects.None)
		{
			SpringLoadTab(TabUnder(e));
		}
	}

	private int TabUnder(System.Windows.DragEventArgs e)
	{
		for (int i = 0; i < _tabButtons.Length; i++)
		{
			System.Windows.Controls.Button button = _tabButtons[i];
			if (button.IsVisible && !TabLocked(i))
			{
				System.Windows.Point position = e.GetPosition(button);
				if (position.X >= 0.0 && position.Y >= 0.0 && position.X <= button.ActualWidth && position.Y <= button.ActualHeight)
				{
					return i;
				}
			}
		}
		return -1;
	}

	private void SpringLoadTab(int index)
	{
		if (index != _springTab)
		{
			_springTab = index;
			_springTimer.Stop();
			if (index >= 0 && index != _tab)
			{
				_springTimer.Start();
			}
		}
	}

	private void SpringTimer_Tick(object? sender, EventArgs e)
	{
		_springTimer.Stop();
		if (_dragIn && _springTab >= 0)
		{
			SelectTab(_springTab);
			if (_springTab == 7)
			{
				ShowPhoneSubTab(2);
				ShowPhoneDropZone();
			}
		}
	}

	private void Pill_DragLeave(object sender, System.Windows.DragEventArgs e)
	{
		System.Windows.Point position = e.GetPosition(Pill);
		if (!(position.X >= 0.0) || !(position.Y >= 0.0) || !(position.X <= Pill.ActualWidth) || !(position.Y <= Pill.ActualHeight))
		{
			EndDragIn();
			SetHover(value: false, 300);
		}
	}

	private void Pill_Drop(object sender, System.Windows.DragEventArgs e)
	{
		string[]? array = DroppedPaths(e);
		string? text = ((array == null) ? DroppedLink(e) : null);
		bool phoneFilesShown = PhoneFilesShown;
		EndDragIn();
		if (_dragOut)
		{
			return;
		}
		if (text != null)
		{
			AddLink(text);
			e.Handled = true;
		}
		else if (array != null)
		{
			if (phoneFilesShown)
			{
				DropOnPhone(array);
				e.Handled = true;
				return;
			}
			_shelf.Add(array);
			PetReact("drop");
			SelectTab(1);
			e.Handled = true;
		}
	}

	private void EndDragIn()
	{
		_dragIn = false;
		SpringLoadTab(-1);
		ShelfDropFrame.Stroke = DropFrameIdle;
		MarchDropFrame(on: false);
		if (PhoneFilesShown)
		{
			ShowPhoneDropZone();
		}
	}

	private static (ScaleTransform Scale, TranslateTransform Shift) TileTransforms(Border tile)
	{
		if (tile.RenderTransform is TransformGroup { Children: { Count: 2 } children } && children[0] is ScaleTransform item && children[1] is TranslateTransform item2)
		{
			return (Scale: item, Shift: item2);
		}
		ScaleTransform scaleTransform = new ScaleTransform();
		TranslateTransform translateTransform = new TranslateTransform();
		tile.RenderTransformOrigin = new System.Windows.Point(0.5, 0.6);
		tile.RenderTransform = new TransformGroup
		{
			Children = 
			{
				(Transform)scaleTransform,
				(Transform)translateTransform
			}
		};
		return (Scale: scaleTransform, Shift: translateTransform);
	}

	private static FrameworkElement? TileIcon(Border tile)
	{
		return (tile.Child as System.Windows.Controls.Panel)?.Children[0] as FrameworkElement;
	}

	private void ShelfTile_Loaded(object sender, RoutedEventArgs e)
	{
		Border border = (Border)sender;
		var (target, target2) = TileTransforms(border);
		if (!ReducedMotion)
		{
			int delayMs = Math.Min(_tileLoadOrder++, 8) * 40;
			Dispatcher.BeginInvoke((Action)(() =>
			{
				_tileLoadOrder = 0;
			}), DispatcherPriority.ApplicationIdle);
			SpringEase ease = new SpringEase
			{
				Damping = 0.5,
				Stiffness = 11.0
			};
			Animate(target2, TranslateTransform.YProperty, 0.0, 620, ease, delayMs, -18.0);
			Animate(target, ScaleTransform.ScaleXProperty, 1.0, 620, ease, delayMs, 0.6);
			Animate(target, ScaleTransform.ScaleYProperty, 1.0, 620, ease, delayMs, 0.6);
			Animate(border, UIElement.OpacityProperty, 1.0, 200, new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}, delayMs, 0.0);
		}
	}

	private void ShelfTile_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
	{
		Border border = (Border)sender;
		border.Background = TileHover;
		ScaleTransform item = TileTransforms(border).Scale;
		SpringEase ease = new SpringEase
		{
			Damping = 0.6,
			Stiffness = 12.0
		};
		Animate(item, ScaleTransform.ScaleXProperty, 1.06, 380, ease);
		Animate(item, ScaleTransform.ScaleYProperty, 1.06, 380, ease);
		FrameworkElement? frameworkElement = TileIcon(border);
		if (frameworkElement != null && !ReducedMotion)
		{
			TranslateTransform? translateTransform = frameworkElement.RenderTransform as TranslateTransform;
			if (translateTransform == null)
			{
				translateTransform = (TranslateTransform)(frameworkElement.RenderTransform = new TranslateTransform());
			}
			translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimationUsingKeyFrames
			{
				KeyFrames = 
				{
					(DoubleKeyFrame)new EasingDoubleKeyFrame(-5.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140L)), new CubicEase
					{
						EasingMode = EasingMode.EaseOut
					}),
					(DoubleKeyFrame)new EasingDoubleKeyFrame(-2.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420L)), new BounceEase
					{
						Bounces = 1,
						Bounciness = 3.0
					})
				}
			});
		}
	}

	private void ShelfTile_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
	{
		Border border = (Border)sender;
		border.Background = System.Windows.Media.Brushes.Transparent;
		ScaleTransform item = TileTransforms(border).Scale;
		SpringEase ease = new SpringEase
		{
			Damping = 0.7,
			Stiffness = 12.0
		};
		Animate(item, ScaleTransform.ScaleXProperty, 1.0, 380, ease);
		Animate(item, ScaleTransform.ScaleYProperty, 1.0, 380, ease);
		if (TileIcon(border)?.RenderTransform is TranslateTransform target)
		{
			Animate(target, TranslateTransform.YProperty, 0.0, 300, new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			});
		}
	}

	private void RemoveTile(Border? tile, FileEntry item, int delayMs = 0)
	{
		if (tile == null || ReducedMotion)
		{
			_shelf.Remove(item);
			return;
		}
		ScaleTransform item2 = TileTransforms(tile).Scale;
		tile.IsHitTestVisible = false;
		CubicEase cubicEase = new CubicEase
		{
			EasingMode = EasingMode.EaseIn
		};
		Animate(item2, ScaleTransform.ScaleXProperty, 0.4, 200, cubicEase, delayMs);
		Animate(item2, ScaleTransform.ScaleYProperty, 0.4, 200, cubicEase, delayMs);
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200L))
		{
			BeginTime = TimeSpan.FromMilliseconds(delayMs),
			EasingFunction = cubicEase
		};
		doubleAnimation.Completed += (object? _, EventArgs _) =>
		{
			_shelf.Remove(item);
		};
		tile.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
	}

	private Border? TileOf(FileEntry item)
	{
		DependencyObject dependencyObject = ShelfList.ItemContainerGenerator.ContainerFromItem(item);
		if (dependencyObject == null || VisualTreeHelper.GetChildrenCount(dependencyObject) <= 0)
		{
			return null;
		}
		return VisualTreeHelper.GetChild(dependencyObject, 0) as Border;
	}

	private void MarchDropFrame(bool on)
	{
		if (!on || ReducedMotion)
		{
			ShelfDropFrame.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
			return;
		}
		ShelfDropFrame.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, new DoubleAnimation(0.0, -18.0, TimeSpan.FromMilliseconds(900L))
		{
			RepeatBehavior = RepeatBehavior.Forever
		});
	}

	private void ShelfTile_MouseDown(object sender, MouseButtonEventArgs e)
	{
		_pressedTile = ((FrameworkElement)sender).DataContext as FileEntry;
		_pressPoint = e.GetPosition(this);
	}

	private void ShelfTile_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (_pressedTile == null || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		Vector vector = e.GetPosition(this) - _pressPoint;
		if (Math.Abs(vector.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(vector.Y) < SystemParameters.MinimumVerticalDragDistance)
		{
			return;
		}
		FileEntry pressedTile = _pressedTile;
		_pressedTile = null;
		_dragOut = true;
		Border border = (Border)sender;
		border.BeginAnimation(UIElement.OpacityProperty, null);
		border.Opacity = 0.45;
		try
		{
			System.Windows.DataObject data = (pressedTile.IsLink ? new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, pressedTile.Path) : new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new string[1] { pressedTile.Path }));
			System.Windows.DragDropEffects dragDropEffects = DragDrop.DoDragDrop((DependencyObject)sender, data, pressedTile.IsLink ? System.Windows.DragDropEffects.Copy : (System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move | System.Windows.DragDropEffects.Link));
			if (!pressedTile.IsLink && dragDropEffects.HasFlag(System.Windows.DragDropEffects.Move) && !pressedTile.Exists)
			{
				RemoveTile(border, pressedTile);
			}
		}
		catch (Exception ex)
		{
			Log.Error("shelf drag", ex);
		}
		finally
		{
			_dragOut = false;
			if (border.IsHitTestVisible)
			{
				Animate(border, UIElement.OpacityProperty, 1.0, 200, new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				});
			}
			ReleaseKeepOpen();
		}
	}

	private void ShelfTile_MouseUp(object sender, MouseButtonEventArgs e)
	{
		FileEntry? pressedTile = _pressedTile;
		if (pressedTile != null && ((FrameworkElement)sender).DataContext == pressedTile)
		{
			Launch(pressedTile.Path);
		}
		_pressedTile = null;
	}

	private void ShelfTile_RightClick(object sender, MouseButtonEventArgs e)
	{
		object dataContext = ((FrameworkElement)sender).DataContext;
		FileEntry? item = dataContext as FileEntry;
		if (item == null)
		{
			return;
		}
		ShowMenu((FrameworkElement)sender, ("Открыть", true, () =>
		{
			Launch(item.Path);
		}), ((string, bool, Action))(item.IsLink ? ("Скопировать ссылку", true, () =>
		{
			try
			{
				ReviewClipboard.SetText(item.Path);
			}
			catch (Exception)
			{
			}
		}) : ("Показать в папке", true, () =>
		{
			RevealInExplorer(item.Path);
		})), ("Закрепить под часами", !_pins.IsFull, () =>
		{
			_pins.Add(new string[1] { item.Path });
		}), ("Убрать с полки", true, () =>
		{
			RemoveTile((Border)sender, item);
		}));
		e.Handled = true;
	}

	private void ShelfClear_Click(object sender, RoutedEventArgs e)
	{
		List<FileEntry> list = _shelf.Items.ToList();
		for (int i = 0; i < list.Count; i++)
		{
			RemoveTile(TileOf(list[i]), list[i], Math.Min(i, 10) * 30);
		}
	}

	private void AddLink(string url)
	{
		if (_shelf.Items.Any((FileEntry i) => string.Equals(i.Path, url, StringComparison.OrdinalIgnoreCase)))
		{
			SelectTab(1);
			return;
		}
		if (_shelf.IsFull)
		{
			ShelfNote("Полка заполнена");
			return;
		}
		_shelf.Add(new string[1] { url });
		PetReact("drop");
		SelectTab(1);
	}

	private void ShelfAddLink_Click(object sender, RoutedEventArgs e)
	{
		string? text = null;
		try
		{
			if (ReviewClipboard.ContainsText())
			{
				text = ReviewClipboard.GetText();
			}
		}
		catch (Exception)
		{
		}
		if (LinkInfo.TryNormalize(text, allowBare: true, out string url))
		{
			AddLink(url);
		}
		else
		{
			ShelfNote("Сначала скопируйте ссылку");
		}
	}

	private void ShelfNote(string text)
	{
		if (_shelfHintText == null)
		{
			_shelfHintText = ShelfHint.Text;
		}
		if (_shelfHintBrush == null)
		{
			_shelfHintBrush = ShelfHint.Foreground;
		}
		ShelfHint.Text = text;
		ShelfHint.Foreground = ShelfNoteBrush;
		ShelfHint.Visibility = Visibility.Visible;
		_shelfNoteTimer?.Stop();
		_shelfNoteTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(4L)
		};
		_shelfNoteTimer.Tick += (object? _, EventArgs _) =>
		{
			_shelfNoteTimer.Stop();
			ShelfHint.Text = _shelfHintText;
			ShelfHint.Foreground = _shelfHintBrush;
			_shelfHintText = null;
			_shelfHintBrush = null;
			UpdateShelfEmpty();
		};
		_shelfNoteTimer.Start();
	}

	private void RevealInExplorer(string path)
	{
		try
		{
			ReviewSafety.Start("explorer.exe", "/select,\"" + path + "\"");
		}
		catch (Exception ex)
		{
			Log.Error("reveal", ex);
		}
	}

	private void AddPin()
	{
		Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
		{
			Title = "Закрепить программу или файл",
			Filter = "Программы и ярлыки|*.exe;*.lnk;*.url;*.bat;*.cmd|Все файлы|*.*",
			DereferenceLinks = false,
			Multiselect = true,
			InitialDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
		};
		_menuOpen = true;
		try
		{
			if (openFileDialog.ShowDialog() == true)
			{
				_pins.Add(openFileDialog.FileNames);
			}
		}
		finally
		{
			_menuOpen = false;
			ReleaseKeepOpen();
		}
	}

	private void PinsBar_DragOver(object sender, System.Windows.DragEventArgs e)
	{
		e.Effects = ((DroppedPaths(e) != null && !_pins.IsFull) ? System.Windows.DragDropEffects.Link : System.Windows.DragDropEffects.None);
		e.Handled = true;
	}

	private void PinsBar_Drop(object sender, System.Windows.DragEventArgs e)
	{
		string[]? array = DroppedPaths(e);
		EndDragIn();
		if (array != null)
		{
			_pins.Add(array);
		}
		e.Handled = true;
	}

	private void ShowMenu(FrameworkElement target, params (string Text, bool Enabled, Action Run)[] items)
	{
		System.Windows.Controls.ContextMenu contextMenu = new System.Windows.Controls.ContextMenu
		{
			PlacementTarget = target
		};
		for (int i = 0; i < items.Length; i++)
		{
			(string, bool, Action) tuple = items[i];
			string item = tuple.Item1;
			bool item2 = tuple.Item2;
			Action run = tuple.Item3;
			System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
			{
				Header = item,
				IsEnabled = item2
			};
			menuItem.Click += (object _, RoutedEventArgs _) =>
			{
				run();
			};
			contextMenu.Items.Add(menuItem);
		}
		_menuOpen = true;
		contextMenu.Closed += (object _, RoutedEventArgs _) =>
		{
			_menuOpen = false;
			Dispatcher.BeginInvoke(new Action(ReleaseKeepOpen));
		};
		contextMenu.IsOpen = true;
	}

	private void InitTabBadges()
	{
		_tabBadges = new Border[_tabButtons.Length];
		for (int i = 0; i < _tabButtons.Length; i++)
		{
			if (_tabButtons[i].Content is StackPanel stackPanel && stackPanel.Children.Count != 0)
			{
				UIElement element = stackPanel.Children[0];
				stackPanel.Children.RemoveAt(0);
				Grid grid = new Grid();
				grid.Children.Add(element);
				Border border = new Border
				{
					Background = BadgeBrush,
					HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
					VerticalAlignment = VerticalAlignment.Top,
					IsHitTestVisible = false,
					Visibility = Visibility.Collapsed,
					RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
					Child = new TextBlock
					{
						FontSize = 8.5,
						FontWeight = FontWeights.Bold,
						Foreground = System.Windows.Media.Brushes.White,
						HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
						VerticalAlignment = VerticalAlignment.Center
					}
				};
				grid.Children.Add(border);
				stackPanel.Children.Insert(0, grid);
				_tabBadges[i] = border;
			}
		}
	}

	private void SetTabBadge(int tab, int count)
	{
		if (tab >= _tabBadges.Length)
		{
			return;
		}
		Border border = _tabBadges[tab];
		if (border == null)
		{
			return;
		}
		int num = _badgeValues[tab];
		if (count == num)
		{
			return;
		}
		_badgeValues[tab] = count;
		if (count == 0)
		{
			border.Visibility = Visibility.Collapsed;
			return;
		}
		TextBlock textBlock = (TextBlock)border.Child;
		if (count > 0)
		{
			textBlock.Text = ((count > 9) ? "9+" : count.ToString());
			border.Height = 12.0;
			border.MinWidth = 12.0;
			border.Width = double.NaN;
			border.CornerRadius = new CornerRadius(6.0);
			border.Padding = new Thickness(3.0, 0.0, 3.0, 0.0);
			border.Margin = new Thickness(0.0, -6.0, -9.0, 0.0);
		}
		else
		{
			textBlock.Text = "";
			double num2 = (border.MinWidth = 6.0);
			double width = (border.Height = num2);
			border.Width = width;
			border.CornerRadius = new CornerRadius(3.0);
			border.Padding = new Thickness(0.0);
			border.Margin = new Thickness(0.0, -2.0, -4.0, 0.0);
		}
		border.Visibility = Visibility.Visible;
		if (num == 0 || count > num)
		{
			Pop(border, 0.4);
		}
	}

	private void UpdateTabBadges()
	{
		if (!Settings.Current.TabBadges)
		{
			for (int i = 0; i < _badgeValues.Length; i++)
			{
				SetTabBadge(i, 0);
			}
			return;
		}
		SetTabBadge(7, PhoneOn ? CenterItems().Length : 0);
		if (_aiUnseen && _state == IslandState.Expanded && _shownTab == AiPanel)
		{
			_aiUnseen = false;
		}
		SetTabBadge(6, _aiUnseen ? (-1) : 0);
		DateTime now = DateTime.Now;
		bool flag = _calendar.Between(now.AddHours(-12.0), now.AddMinutes(15.0)).Any((CalendarEvent e) => !e.AllDay && e.Start <= now.AddMinutes(15.0) && e.End > now);
		SetTabBadge(4, flag ? (-1) : 0);
	}

	private void MarkAiAnswered()
	{
		if (_state != IslandState.Expanded || _shownTab != AiPanel)
		{
			_aiUnseen = true;
			UpdateTabBadges();
		}
	}

	private void InitTabOrder()
	{
		_tabHold.Tick += (object? _, EventArgs _) =>
		{
			_tabHold.Stop();
			BeginTabDrag();
		};
		TabStrip.PreviewMouseLeftButtonDown += TabStrip_Down;
		TabStrip.PreviewMouseMove += TabStrip_Move;
		TabStrip.PreviewMouseLeftButtonUp += TabStrip_Up;
		TabStrip.LostMouseCapture += (object _, System.Windows.Input.MouseEventArgs _) =>
		{
			if (_tabDragging && Mouse.LeftButton != MouseButtonState.Pressed)
			{
				EndTabDrag();
			}
		};
		ApplyTabOrder();
	}

	private void ApplyTabOrder()
	{
		List<string> order = Settings.Current.TabOrder;
		List<System.Windows.Controls.Button> list = ((order.Count == 0) ? _tabButtons.ToList() : _tabButtons.OrderBy((System.Windows.Controls.Button b) =>
		{
			int num2 = order.IndexOf(TabIds[int.Parse((string)b.Tag)]);
			return (num2 >= 0) ? num2 : int.MaxValue;
		}).ToList());
		if (list.Select((System.Windows.Controls.Button b, int i) => TabStrip.Children[i] == b).All((bool same) => same))
		{
			return;
		}
		foreach (System.Windows.Controls.Button item in list)
		{
			TabStrip.Children.Remove(item);
		}
		for (int num = 0; num < list.Count; num++)
		{
			TabStrip.Children.Insert(num, list[num]);
		}
		Dispatcher.BeginInvoke(new Action(SyncTabIndicator), DispatcherPriority.Loaded);
	}

	private System.Windows.Controls.Button? TabButtonAt(object source)
	{
		DependencyObject? dependencyObject = source as DependencyObject;
		while (dependencyObject != null && dependencyObject != TabStrip)
		{
			if (dependencyObject is System.Windows.Controls.Button button && _tabButtons.Contains(button, null))
			{
				return button;
			}
			dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
		}
		return null;
	}

	private void TabStrip_Down(object sender, MouseButtonEventArgs e)
	{
		_tabPressed = TabButtonAt(e.OriginalSource);
		if (_tabPressed != null)
		{
			_tabPressAt = e.GetPosition(TabStrip);
			_tabHold.Stop();
			_tabHold.Start();
		}
	}

	private void TabStrip_Move(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (_tabPressed == null)
		{
			return;
		}
		System.Windows.Point position = e.GetPosition(TabStrip);
		if (!_tabDragging)
		{
			if (Math.Abs(position.X - _tabPressAt.X) > 8.0 || Math.Abs(position.Y - _tabPressAt.Y) > 8.0)
			{
				_tabHold.Stop();
			}
		}
		else
		{
			MoveTab(_tabPressed, position.X);
		}
	}

	private void TabStrip_Up(object sender, MouseButtonEventArgs e)
	{
		_tabHold.Stop();
		if (!_tabDragging)
		{
			_tabPressed = null;
			return;
		}
		e.Handled = true;
		_tabPressed?.ReleaseMouseCapture();
		EndTabDrag();
	}

	private static (ScaleTransform Scale, TranslateTransform Shift) TabTransforms(System.Windows.Controls.Button b)
	{
		if (b.RenderTransform is TransformGroup { Children: { Count: 2 } children } && children[0] is ScaleTransform item && children[1] is TranslateTransform item2)
		{
			return (Scale: item, Shift: item2);
		}
		ScaleTransform scaleTransform = new ScaleTransform();
		TranslateTransform translateTransform = new TranslateTransform();
		b.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
		b.RenderTransform = new TransformGroup
		{
			Children = 
			{
				(Transform)scaleTransform,
				(Transform)translateTransform
			}
		};
		return (Scale: scaleTransform, Shift: translateTransform);
	}

	private static double SlotX(UIElement e)
	{
		return LayoutInformation.GetLayoutSlot((FrameworkElement)e).X;
	}

	private void BeginTabDrag()
	{
		System.Windows.Controls.Button? tabPressed = _tabPressed;
		if (tabPressed != null)
		{
			_tabDragging = true;
			_tabGrab = _tabPressAt.X - SlotX(tabPressed);
			System.Windows.Controls.Panel.SetZIndex(tabPressed, 10);
			ScaleTransform item = TabTransforms(tabPressed).Scale;
			SpringEase ease = new SpringEase
			{
				Damping = 0.55,
				Stiffness = 12.0
			};
			Animate(item, ScaleTransform.ScaleXProperty, 1.14, 360, ease);
			Animate(item, ScaleTransform.ScaleYProperty, 1.14, 360, ease);
			tabPressed.Opacity = 0.9;
		}
	}

	private void MoveTab(System.Windows.Controls.Button tab, double x)
	{
		UIElementCollection kids = TabStrip.Children;
		List<UIElement> list = (from i in Enumerable.Range(0, _tabButtons.Length)
			select kids[i] into k
			where k != tab
			select k).ToList();
		int num = Enumerable.Count(list, (UIElement k) => k.Visibility == Visibility.Visible && SlotX(k) + ((FrameworkElement)k).ActualWidth / 2.0 < x - _tabGrab + tab.ActualWidth / 2.0);
		int num2 = 0;
		int num3 = 0;
		for (; num2 < list.Count; num2++)
		{
			if (num3 >= num)
			{
				break;
			}
			if (list[num2].Visibility == Visibility.Visible)
			{
				num3++;
			}
		}
		if (num2 != kids.IndexOf(tab))
		{
			Dictionary<UIElement, double> dictionary = list.ToDictionary((UIElement k) => k, SlotX);
			kids.Remove(tab);
			kids.Insert(num2, tab);
			TabStrip.UpdateLayout();
			foreach (System.Windows.Controls.Button item2 in list.OfType<System.Windows.Controls.Button>())
			{
				double value = dictionary[item2] - SlotX(item2);
				if (!(Math.Abs(value) < 0.5))
				{
					Animate(TabTransforms(item2).Shift, TranslateTransform.XProperty, 0.0, 320, new CubicEase
					{
						EasingMode = EasingMode.EaseOut
					}, 0, value);
				}
			}
		}
		TranslateTransform item = TabTransforms(tab).Shift;
		item.BeginAnimation(TranslateTransform.XProperty, null);
		item.X = x - _tabGrab - SlotX(tab);
		SyncTabIndicator();
	}

	private void EndTabDrag()
	{
		System.Windows.Controls.Button? tabPressed = _tabPressed;
		_tabPressed = null;
		_tabDragging = false;
		if (tabPressed != null)
		{
			(ScaleTransform Scale, TranslateTransform Shift) tuple = TabTransforms(tabPressed);
			ScaleTransform item = tuple.Scale;
			TranslateTransform item2 = tuple.Shift;
			SpringEase ease = new SpringEase
			{
				Damping = 0.65,
				Stiffness = 12.0
			};
			Animate(item, ScaleTransform.ScaleXProperty, 1.0, 420, ease);
			Animate(item, ScaleTransform.ScaleYProperty, 1.0, 420, ease);
			Animate(item2, TranslateTransform.XProperty, 0.0, 420, ease);
			tabPressed.Opacity = 1.0;
			System.Windows.Controls.Panel.SetZIndex(tabPressed, 0);
			_tabIndicatorUntil = _clock.Elapsed.TotalSeconds + 0.5;
			StartLiquidLoop();
			Settings.Current.TabOrder = (from i in Enumerable.Range(0, _tabButtons.Length)
				select TabIds[int.Parse((string)((System.Windows.Controls.Button)TabStrip.Children[i]).Tag)]).ToList();
			Settings.Current.Save();
		}
	}

	private void InitText()
	{
		LoadNotes();
		_notesSaveTimer.Tick += (object? _, EventArgs _) =>
		{
			SaveNotesNow();
		};
		_notesUndoTimer.Tick += (object? _, EventArgs _) =>
		{
			ResetNotesClear();
		};
		_qrTimer.Tick += (object? _, EventArgs _) =>
		{
			_qrTimer.Stop();
			RenderQr();
		};
		UpdateNotesStats();
		TodoList.ItemsSource = _todo.Items;
		_todo.Items.CollectionChanged += (object? _, NotifyCollectionChangedEventArgs _) =>
		{
			UpdateTodoStats();
		};
		foreach (TodoItem item in _todo.Items)
		{
			item.PropertyChanged += (object? _, PropertyChangedEventArgs _) =>
			{
				UpdateTodoStats();
			};
		}
		_todo.Items.CollectionChanged += (object? _, NotifyCollectionChangedEventArgs e) =>
		{
			if (e.NewItems != null)
			{
				foreach (TodoItem newItem in e.NewItems)
				{
					newItem.PropertyChanged += (object? obj, PropertyChangedEventArgs e2) =>
					{
						UpdateTodoStats();
					};
				}
			}
		};
		UpdateTodoStats();
		ShowSubTab(0);
	}

	private void TypingBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		System.Windows.Controls.Primitives.TextBoxBase textBoxBase = (System.Windows.Controls.Primitives.TextBoxBase)sender;
		if (!_typing || _typingBox != textBoxBase)
		{
			BeginTyping(textBoxBase);
		}
	}

	private void TypingBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			EndTyping(restoreFocus: true);
			e.Handled = true;
		}
	}

	private void BeginTyping(System.Windows.Controls.Primitives.TextBoxBase box)
	{
		if (!_typing)
		{
			_previousForeground = Win32.GetForegroundWindow();
			Win32.SetNoActivate(_hwnd, on: false);
			Activate();
			Win32.SetForegroundWindow(_hwnd);
		}
		_typing = true;
		SetTypingFrame(_typingBox, on: false);
		_typingBox = box;
		SetTypingFrame(box, on: true);
		box.Focus();
	}

	private void EndTyping(bool restoreFocus)
	{
		if (_typing)
		{
			_typing = false;
			SetTypingFrame(_typingBox, on: false);
			_typingBox = null;
			Keyboard.ClearFocus();
			Win32.SetNoActivate(_hwnd, on: true);
			if (restoreFocus && _previousForeground != IntPtr.Zero)
			{
				Win32.SetForegroundWindow(_previousForeground);
			}
			SaveNotesNow();
			ReleaseKeepOpen();
		}
	}

	private void SetTypingFrame(System.Windows.Controls.Primitives.TextBoxBase? box, bool on)
	{
		Border border;
		if (box != null && box == _centerReplyBox)
		{
			border = _centerReplyFrame;
		}
		else if (box == NotesBox)
		{
			border = NotesFrame;
		}
		else if (box == QrInput)
		{
			border = QrFrame;
		}
		else if (box == TodoInput)
		{
			border = TodoFrame;
		}
		else if (box == AiInput)
		{
			border = AiFrame;
		}
		else if (box == CityInput)
		{
			border = CityFrame;
		}
		else
		{
			border = ((box == PhoneReplyInput) ? PhoneReplyFrame : null);
		}
		if (border != null)
		{
			border.BorderBrush = (on ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(96, _accent.R, _accent.G, _accent.B)) : System.Windows.Media.Brushes.Transparent);
		}
	}

	private void SubTab_Click(object sender, RoutedEventArgs e)
	{
		int num = int.Parse((string)((System.Windows.Controls.Button)sender).Tag);
		ShowSubTab((num != _subTab) ? num : 0);
	}

	private void ShowSubTab(int index)
	{
		int subTab = _subTab;
		_subTab = index;
		FrameworkElement[] array = new FrameworkElement[3] { NotesView, QrView, TodoView };
		for (int i = 0; i < array.Length; i++)
		{
			bool flag = i == index;
			if (flag && array[i].Visibility != Visibility.Visible)
			{
				array[i].Visibility = Visibility.Visible;
				if (!ReducedMotion)
				{
					CubicEase ease = new CubicEase
					{
						EasingMode = EasingMode.EaseOut
					};
					TranslateTransform? translateTransform = array[i].RenderTransform as TranslateTransform;
					if (translateTransform == null)
					{
						translateTransform = (TranslateTransform)(array[i].RenderTransform = new TranslateTransform());
					}
					Animate(translateTransform, TranslateTransform.YProperty, 0.0, 300, ease, 0, (index == 0) ? (-10) : 14);
					Animate(array[i], UIElement.OpacityProperty, 1.0, 200, ease, 0, 0.0);
				}
			}
			else if (!flag)
			{
				array[i].Visibility = Visibility.Collapsed;
			}
		}
		SetFormatOn(SubQr, index == 1);
		SetFormatOn(SubTodo, index == 2);
		if (index == 1 && subTab != 1)
		{
			Dispatcher.BeginInvoke((Action)(() =>
			{
				BeginTyping(QrInput);
			}), DispatcherPriority.Loaded);
		}
		if (index == 2 && subTab != 2)
		{
			Dispatcher.BeginInvoke((Action)(() =>
			{
				BeginTyping(TodoInput);
			}), DispatcherPriority.Loaded);
		}
	}

	private void LoadNotes()
	{
		try
		{
			if (!File.Exists(NotesListPath))
			{
				goto IL_0098;
			}
			NotesFile? f = JsonSerializer.Deserialize<NotesFile>(File.ReadAllText(NotesListPath));
			if (f == null)
			{
				goto IL_0098;
			}
			List<string> notes = f.Notes;
			if (notes == null || notes.Count <= 0)
			{
				goto IL_0098;
			}
			_notes = f.Notes.Select((string text, int i) => new Note
			{
				Text = text,
				Xaml = ((f.Docs != null && i < f.Docs.Count) ? f.Docs[i] : null)
			}).ToList();
			_note = Math.Clamp(f.Active, 0, _notes.Count - 1);
			goto end_IL_0000;
			IL_0098:
			if (File.Exists(NotesPath))
			{
				_notes = new List<Note>
				{
					new Note
					{
						Text = File.ReadAllText(NotesPath)
					}
				};
			}
			end_IL_0000:;
		}
		catch (Exception ex)
		{
			Log.Error("load notes", ex);
		}
		OpenNote(_note, animate: false);
	}

	private FlowDocument NewDocument()
	{
		return new FlowDocument
		{
			PagePadding = new Thickness(0.0),
			FontFamily = NotesBox.FontFamily,
			FontSize = 13.0,
			LineHeight = 18.0
		};
	}

	private FlowDocument DocumentOf(Note note)
	{
		FlowDocument flowDocument = NewDocument();
		if (note.Xaml != null)
		{
			try
			{
				TextRange textRange = new TextRange(flowDocument.ContentStart, flowDocument.ContentEnd);
				using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(note.Xaml));
				textRange.Load(stream, System.Windows.DataFormats.Xaml);
				return flowDocument;
			}
			catch (Exception ex)
			{
				Log.Error("note formatting", ex);
				flowDocument = NewDocument();
			}
		}
		string[] array = note.Text.Replace("\r\n", "\n").Split('\n');
		foreach (string text in array)
		{
			flowDocument.Blocks.Add(new Paragraph(new Run(text)));
		}
		return flowDocument;
	}

	private static string XamlOf(FlowDocument doc)
	{
		TextRange textRange = new TextRange(doc.ContentStart, doc.ContentEnd);
		using MemoryStream memoryStream = new MemoryStream();
		textRange.Save(memoryStream, System.Windows.DataFormats.Xaml);
		return Encoding.UTF8.GetString(memoryStream.ToArray());
	}

	private string PlainText()
	{
		return new TextRange(NotesBox.Document.ContentStart, NotesBox.Document.ContentEnd).Text.TrimEnd(new char[2] { '\r', '\n' });
	}

	private void OpenNote(int index, bool animate = true)
	{
		KeepNote();
		_note = Math.Clamp(index, 0, _notes.Count - 1);
		_switchingNote = true;
		NotesBox.Document = DocumentOf(_notes[_note]);
		NotesBox.CaretPosition = NotesBox.Document.ContentEnd;
		_switchingNote = false;
		_noteDirty = false;
		ResetNotesClear();
		UpdateNotesStats();
		UpdateFormatButtons();
		RebuildNoteTabs();
		if (animate)
		{
			Animate(NotesFrame, UIElement.OpacityProperty, 1.0, 220, new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}, 0, 0.3);
		}
	}

	private void KeepNote()
	{
		if (_noteDirty && _note < _notes.Count)
		{
			_notes[_note].Xaml = XamlOf(NotesBox.Document);
			_noteDirty = false;
		}
	}

	private static string NoteTitle(string text)
	{
		string text2 = text.TrimStart().Split('\n', 2)[0].Trim();
		if (text2.Length == 0)
		{
			return "Новая заметка";
		}
		if (text2.Length <= 18)
		{
			return text2;
		}
		return text2.Substring(0, 17) + "…";
	}

	private void RebuildNoteTabs()
	{
		NoteTabs.Children.Clear();
		for (int i = 0; i < _notes.Count; i++)
		{
			int index = i;
			System.Windows.Controls.Button chip = new System.Windows.Controls.Button
			{
				Content = NoteTitle(_notes[i].Text),
				Style = (Style)FindResource("SubTab"),
				FontWeight = FontWeights.Normal,
				Margin = new Thickness(0.0, 0.0, 4.0, 0.0),
				Background = ((i == _note) ? SubTabActive : System.Windows.Media.Brushes.Transparent),
				Foreground = ((i == _note) ? FillNormal : ((System.Windows.Media.Brush)FindResource("TextSecondary"))),
				ToolTip = "ПКМ — удалить"
			};
			chip.Click += (object _, RoutedEventArgs _) =>
			{
				if (index != _note)
				{
					OpenNote(index);
				}
			};
			chip.MouseRightButtonUp += (object _, MouseButtonEventArgs e) =>
			{
				ShowMenu(chip, ((_notes.Count > 1) ? "Удалить заметку" : "Очистить заметку", true, () =>
				{
					DeleteNote(index);
				}));
				e.Handled = true;
			};
			NoteTabs.Children.Add(chip);
		}
		if (NoteTabs.Children.Count > _note)
		{
			Dispatcher.BeginInvoke((Action)(() =>
			{
				(NoteTabs.Children[_note] as FrameworkElement)?.BringIntoView();
			}), DispatcherPriority.Loaded);
		}
	}

	private void NoteAdd_Click(object sender, RoutedEventArgs e)
	{
		KeepNote();
		int num = _notes.FindIndex((Note n) => n.Text.Trim().Length == 0);
		if (num < 0)
		{
			_notes.Add(new Note());
			num = _notes.Count - 1;
		}
		OpenNote(num);
		if (NoteTabs.Children[num] is FrameworkElement sender2)
		{
			PopIn_Loaded(sender2, new RoutedEventArgs());
		}
		SaveNotesSoon();
		BeginTyping(NotesBox);
	}

	private void DeleteNote(int index)
	{
		KeepNote();
		Note removed = _notes[index];
		if (_notes.Count == 1)
		{
			_notes[0] = new Note();
		}
		else
		{
			_notes.RemoveAt(index);
		}
		_noteDirty = false;
		OpenNote(Math.Min(index, _notes.Count - 1));
		SaveNotesSoon();
		if (removed.Text.Trim().Length == 0)
		{
			return;
		}
		OfferNotesUndo(() =>
		{
			if (_notes.Count == 1 && _notes[0].Text.Length == 0)
			{
				_notes[0] = removed;
			}
			else
			{
				_notes.Insert(Math.Min(index, _notes.Count), removed);
			}
			_noteDirty = false;
			OpenNote(Math.Min(index, _notes.Count - 1));
			SaveNotesSoon();
		});
	}

	private void NoteTabs_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		NoteTabsScroll.ScrollToHorizontalOffset(NoteTabsScroll.HorizontalOffset - (double)e.Delta / 2.0);
		e.Handled = true;
	}

	private void NotesBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (!_switchingNote)
		{
			string text = _notes[_note].Text;
			string text2 = PlainText();
			_notes[_note].Text = text2;
			_noteDirty = true;
			UpdateNotesStats();
			if (NoteTitle(text) != NoteTitle(text2) && NoteTabs.Children.Count > _note && NoteTabs.Children[_note] is System.Windows.Controls.Button button)
			{
				button.Content = NoteTitle(text2);
			}
			SaveNotesSoon();
		}
	}

	private void SaveNotesSoon()
	{
		_notesSaveTimer.Stop();
		_notesSaveTimer.Start();
	}

	private void UpdateNotesStats()
	{
		string text = ((_notes.Count > _note) ? _notes[_note].Text : "");
		bool flag = text.Length == 0 && !(NotesBox.Document.Blocks.FirstBlock is List);
		NotesPlaceholder.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		int count = Regex.Matches(text, "\\S+").Count;
		NotesStats.Text = $"Слов: {count} · Символов: {text.Length}";
	}

	private void SaveNotesNow()
	{
		_notesSaveTimer.Stop();
		try
		{
			KeepNote();
			Directory.CreateDirectory(Settings.Folder);
			NotesFile value = new NotesFile
			{
				Notes = _notes.Select((Note n) => n.Text).ToList(),
				Docs = _notes.Select((Note n) => n.Xaml).ToList(),
				Active = _note
			};
			File.WriteAllText(NotesListPath, JsonSerializer.Serialize(value));
		}
		catch (Exception ex)
		{
			Log.Error("save notes", ex);
		}
	}

	private void NotesCopy_Click(object sender, RoutedEventArgs e)
	{
		if (PlainText().Length == 0)
		{
			return;
		}
		try
		{
			TextRange textRange = new TextRange(NotesBox.Document.ContentStart, NotesBox.Document.ContentEnd);
			using MemoryStream memoryStream = new MemoryStream();
			textRange.Save(memoryStream, System.Windows.DataFormats.Rtf);
			System.Windows.DataObject dataObject = new System.Windows.DataObject();
			dataObject.SetData(System.Windows.DataFormats.Rtf, Encoding.UTF8.GetString(memoryStream.ToArray()));
			dataObject.SetText(PlainText());
			ReviewClipboard.SetDataObject(dataObject, copy: true);
		}
		catch (Exception ex)
		{
			Log.Error("copy notes", ex);
		}
	}

	private void NotesClear_Click(object sender, RoutedEventArgs e)
	{
		Action? notesUndo = _notesUndo;
		if (notesUndo != null)
		{
			ResetNotesClear();
			notesUndo();
		}
		else if (PlainText().Length != 0)
		{
			KeepNote();
			Note cleared = new Note
			{
				Text = _notes[_note].Text,
				Xaml = (_notes[_note].Xaml ?? XamlOf(NotesBox.Document))
			};
			NotesBox.Document = NewDocument();
			OfferNotesUndo(() =>
			{
				_notes[_note] = cleared;
				_noteDirty = false;
				OpenNote(_note);
				SaveNotesSoon();
			});
		}
	}

	private void OfferNotesUndo(Action undo)
	{
		_notesUndo = undo;
		NotesClearButton.Content = "Вернуть";
		_notesUndoTimer.Stop();
		_notesUndoTimer.Start();
	}

	private void ResetNotesClear()
	{
		_notesUndoTimer.Stop();
		_notesUndo = null;
		NotesClearButton.Content = "Очистить";
	}

	private void AppendToNote(string text)
	{
		FlowDocument document = NotesBox.Document;
		if (PlainText().Length == 0)
		{
			document.Blocks.Clear();
		}
		string[] array = text.Replace("\r\n", "\n").Split('\n');
		foreach (string text2 in array)
		{
			document.Blocks.Add(new Paragraph(new Run(text2)));
		}
	}

	private void Fmt_Click(object sender, RoutedEventArgs e)
	{
		System.Windows.Controls.RichTextBox notesBox = NotesBox;
		if (!_typing || _typingBox != notesBox)
		{
			BeginTyping(notesBox);
		}
		TextSelection selection = notesBox.Selection;
		switch ((string)((System.Windows.Controls.Button)sender).Tag)
		{
		case "bold":
			EditingCommands.ToggleBold.Execute(null, notesBox);
			break;
		case "italic":
			EditingCommands.ToggleItalic.Execute(null, notesBox);
			break;
		case "underline":
			EditingCommands.ToggleUnderline.Execute(null, notesBox);
			break;
		case "strike":
			ToggleStrike();
			break;
		case "heading":
			ToggleHeading();
			break;
		case "bullets":
			EditingCommands.ToggleBullets.Execute(null, notesBox);
			break;
		case "numbers":
			EditingCommands.ToggleNumbering.Execute(null, notesBox);
			break;
		case "undo":
			if (notesBox.CanUndo)
			{
				notesBox.Undo();
			}
			break;
		case "redo":
			if (notesBox.CanRedo)
			{
				notesBox.Redo();
			}
			break;
		case "plain":
			selection.ClearAllProperties();
			foreach (Paragraph item in SelectedParagraphs())
			{
				item.ClearValue(TextElement.FontSizeProperty);
				item.ClearValue(TextElement.FontWeightProperty);
			}
			break;
		}
		notesBox.Focus();
		UpdateFormatButtons();
		if (sender is System.Windows.Controls.Button button && button.Content is UIElement uIElement && uIElement is FrameworkElement element)
		{
			Pop(element, 0.75);
		}
	}

	private void NotesBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.X && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
		{
			ToggleStrike();
			UpdateFormatButtons();
			e.Handled = true;
		}
		else
		{
			TypingBox_PreviewKeyDown(sender, e);
		}
	}

	private void ToggleStrike()
	{
		TextSelection selection = NotesBox.Selection;
		selection.ApplyPropertyValue(value: (selection.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection textDecorationCollection && textDecorationCollection.Contains(TextDecorations.Strikethrough[0])) ? new TextDecorationCollection() : TextDecorations.Strikethrough, formattingProperty: Inline.TextDecorationsProperty);
	}

	private void ToggleHeading()
	{
		List<Paragraph> list = SelectedParagraphs().ToList();
		bool flag = list.Count > 0 && list.All((Paragraph p) => p.FontSize >= 16.9);
		foreach (Paragraph item in list)
		{
			if (flag)
			{
				item.ClearValue(TextElement.FontSizeProperty);
				item.ClearValue(TextElement.FontWeightProperty);
			}
			else
			{
				item.FontSize = 17.0;
				item.FontWeight = FontWeights.SemiBold;
			}
		}
	}

	private IEnumerable<Paragraph> SelectedParagraphs()
	{
		TextSelection selection = NotesBox.Selection;
		Paragraph paragraph = selection.Start.Paragraph;
		Paragraph end = selection.End.Paragraph;
		if (paragraph == null)
		{
			yield break;
		}
		for (Block b = paragraph; b != null; b = b.NextBlock)
		{
			if (b is Paragraph paragraph2)
			{
				yield return paragraph2;
			}
			if (b == end)
			{
				break;
			}
		}
	}

	private void NotesBox_SelectionChanged(object sender, RoutedEventArgs e)
	{
		UpdateFormatButtons();
	}

	private void UpdateFormatButtons()
	{
		TextSelection selection = NotesBox.Selection;
		int num;
		if (selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight fontWeight && fontWeight >= FontWeights.SemiBold)
		{
			Paragraph paragraph = selection.Start.Paragraph;
			num = ((paragraph == null || !(paragraph.FontSize >= 16.9)) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		bool flag = (byte)num != 0;
		bool flag2 = selection.GetPropertyValue(TextElement.FontStyleProperty) is System.Windows.FontStyle fontStyle && fontStyle == FontStyles.Italic;
		TextDecorationCollection? obj = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
		bool flag3 = obj != null && obj.Any((TextDecoration d) => d.Location == TextDecorationLocation.Underline);
		bool flag4 = obj != null && obj.Any((TextDecoration d) => d.Location == TextDecorationLocation.Strikethrough);
		Paragraph paragraph2 = selection.Start.Paragraph;
		bool flag5 = paragraph2 != null && paragraph2.FontSize >= 16.9;
		TextMarkerStyle? textMarkerStyle = ((selection.Start.Paragraph?.Parent is ListItem { Parent: List parent }) ? new TextMarkerStyle?(parent.MarkerStyle) : ((TextMarkerStyle?)null));
		SetFormatOn(FmtBold, flag);
		SetFormatOn(FmtItalic, flag2);
		SetFormatOn(FmtUnderline, flag3);
		SetFormatOn(FmtStrike, flag4);
		SetFormatOn(FmtHeading, flag5);
		System.Windows.Controls.Button fmtBullets = FmtBullets;
		bool flag6;
		if (textMarkerStyle.HasValue)
		{
			TextMarkerStyle valueOrDefault = textMarkerStyle.GetValueOrDefault();
			if ((uint)(valueOrDefault - 1) <= 3u)
			{
				flag6 = true;
				goto IL_01e0;
			}
		}
		flag6 = false;
		goto IL_01e0;
		IL_01e0:
		SetFormatOn(fmtBullets, flag6);
		SetFormatOn(FmtNumbers, textMarkerStyle == TextMarkerStyle.Decimal);
	}

	private void SetFormatOn(System.Windows.Controls.Button button, bool on)
	{
		button.Background = (on ? SubTabActive : System.Windows.Media.Brushes.Transparent);
		button.Foreground = (on ? FillNormal : ((System.Windows.Media.Brush)FindResource("TextSecondary")));
	}

	private void QrFromNote_Click(object sender, RoutedEventArgs e)
	{
		string text = (NotesBox.Selection.IsEmpty ? PlainText() : NotesBox.Selection.Text);
		if (text.Trim().Length > 0)
		{
			QrInput.Text = text.Trim();
		}
	}

	private void QrInput_TextChanged(object sender, TextChangedEventArgs e)
	{
		QrPlaceholder.Visibility = ((QrInput.Text.Length != 0) ? Visibility.Collapsed : Visibility.Visible);
		_qrTimer.Stop();
		_qrTimer.Start();
	}

	private void RenderQr()
	{
		string text = QrInput.Text;
		if (string.IsNullOrWhiteSpace(text))
		{
			_qr = null;
			QrImage.Source = null;
			QrEmpty.Text = "Здесь появится QR-код";
			QrEmpty.Visibility = Visibility.Visible;
			return;
		}
		try
		{
			using QRCodeGenerator qRCodeGenerator = new QRCodeGenerator();
			using QRCodeData data = qRCodeGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
			byte[] graphic = new PngByteQRCode(data).GetGraphic(12);
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.StreamSource = new MemoryStream(graphic);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			_qr = bitmapImage;
			QrImage.Source = bitmapImage;
			QrEmpty.Visibility = Visibility.Collapsed;
			Pop(QrImage, 0.85);
		}
		catch (Exception)
		{
			_qr = null;
			QrImage.Source = null;
			QrEmpty.Text = "Слишком длинный текст для QR-кода";
			QrEmpty.Visibility = Visibility.Visible;
		}
	}

	private void QrFromClipboard_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (ReviewClipboard.ContainsText())
			{
				QrInput.Text = ReviewClipboard.GetText();
			}
		}
		catch (Exception ex)
		{
			Log.Error("qr paste", ex);
		}
	}

	private void QrCopy_Click(object sender, RoutedEventArgs e)
	{
		if (_qr == null)
		{
			return;
		}
		try
		{
			ReviewClipboard.SetImage(_qr);
		}
		catch (Exception ex)
		{
			Log.Error("qr copy", ex);
		}
	}

	private void QrSave_Click(object sender, RoutedEventArgs e)
	{
		if (_qr == null)
		{
			return;
		}
		Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
		{
			Title = "Сохранить QR-код",
			Filter = "PNG|*.png",
			FileName = "qr.png"
		};
		_menuOpen = true;
		try
		{
			if (saveFileDialog.ShowDialog() != true)
			{
				return;
			}
			PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
			pngBitmapEncoder.Frames.Add(BitmapFrame.Create(_qr));
			using FileStream stream = File.Create(saveFileDialog.FileName);
			pngBitmapEncoder.Save(stream);
		}
		catch (Exception ex)
		{
			Log.Error("qr save", ex);
		}
		finally
		{
			_menuOpen = false;
			ReleaseKeepOpen();
		}
	}

	private void TodoInput_TextChanged(object sender, TextChangedEventArgs e)
	{
		TodoPlaceholder.Visibility = ((TodoInput.Text.Length != 0) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void TodoInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			AddTodo();
			e.Handled = true;
		}
	}

	private void TodoAdd_Click(object sender, RoutedEventArgs e)
	{
		AddTodo();
	}

	private void AddTodo()
	{
		if (!string.IsNullOrWhiteSpace(TodoInput.Text))
		{
			_todo.Add(TodoInput.Text);
			TodoInput.Text = "";
		}
	}

	private void TodoItem_RightClick(object sender, MouseButtonEventArgs e)
	{
		object dataContext = ((FrameworkElement)sender).DataContext;
		TodoItem? item = dataContext as TodoItem;
		if (item == null)
		{
			return;
		}
		ShowMenu((FrameworkElement)sender, (item.Done ? "Вернуть в работу" : "Выполнено", true, () =>
		{
			item.Done = !item.Done;
		}), ("Копировать текст", true, () =>
		{
			try
			{
				ReviewClipboard.SetText(item.Text);
			}
			catch
			{
			}
		}), ("Удалить", true, () =>
		{
			_todo.Remove(item);
		}));
		e.Handled = true;
	}

	private void TodoClearDone_Click(object sender, RoutedEventArgs e)
	{
		_todo.ClearDone();
	}

	private void UpdateTodoStats()
	{
		int remaining = _todo.Remaining;
		int count = _todo.Items.Count;
		TodoEmpty.Visibility = ((count != 0) ? Visibility.Collapsed : Visibility.Visible);
		TextBlock todoStats = TodoStats;
		string text;
		if (count != 0)
		{
			text = ((remaining == 0) ? "Всё сделано" : $"Осталось: {remaining} из {count}");
		}
		else
		{
			text = "";
		}
		todoStats.Text = text;
		TodoClearDone.Visibility = ((count - remaining <= 0) ? Visibility.Collapsed : Visibility.Visible);
		TodoBadge.Visibility = ((remaining <= 0) ? Visibility.Collapsed : Visibility.Visible);
		TodoBadgeText.Text = ((remaining > 9) ? "9+" : remaining.ToString());
	}

	private static bool TipSeen(string id)
	{
		return Settings.Current.SeenTips.Contains(id);
	}

	private static void MarkTipSeen(string id)
	{
		if (!TipSeen(id))
		{
			Settings.Current.SeenTips.Add(id);
			Settings.Current.Save();
		}
	}

	private void ShowWelcomeTip()
	{
		Island.Services.License.Ended += () =>
		{
			if (!Island.Services.License.IsPro)
			{
				ApplyPhone();
				ApplyTabVisibility();
				ShowToast("\ue945", DeveloperBranding.IsDeveloper ? DeveloperBranding.CardTitle : "Пробный период Pro закончился", (double?)null, DeveloperBranding.IsDeveloper ? DeveloperBranding.Subtitle : "", FillNormal, ((string Text, Action Run)?)("Купить", () =>
				{
					((App)System.Windows.Application.Current).ShowSettings("license");
				}), 8000, false, (ToastExtra?)null);
			}
		};
		if (Island.Services.License.TrialJustStarted && Island.Services.License.IsTrial)
		{
			DispatcherTimer trial = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(7.5)
			};
			trial.Tick += (object? _, EventArgs _) =>
			{
				trial.Stop();
				if (_state != IslandState.Expanded)
				{
					ShowToast("\ue945", DeveloperBranding.IsDeveloper ? DeveloperBranding.CardTitle : "Island Pro открыт бесплатно на 7 дней", (double?)null, DeveloperBranding.IsDeveloper ? DeveloperBranding.Subtitle : "", FillCharging, ((string Text, Action Run)?)("Что это?", () =>
					{
						((App)System.Windows.Application.Current).ShowSettings("license");
					}), 7000, false, (ToastExtra?)null);
				}
			};
			trial.Start();
		}
		if (!Settings.Current.ClipPersist.HasValue && !Settings.Current.IsTabHidden("clip"))
		{
			double value = (TipSeen("welcome") ? 2.5 : 8.0) + ((Island.Services.License.TrialJustStarted && Island.Services.License.IsTrial) ? 7.5 : 0.0);
			DispatcherTimer ask = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(value)
			};
			ask.Tick += (object? _, EventArgs _) =>
			{
				IslandState? state = _state;
				bool flag;
				if (state.HasValue)
				{
					IslandState valueOrDefault = state.GetValueOrDefault();
					if ((uint)(valueOrDefault - 3) <= 1u)
					{
						flag = true;
						goto IL_002e;
					}
				}
				flag = false;
				goto IL_002e;
				IL_002e:
				if (flag || KeepOpen)
				{
					ask.Interval = TimeSpan.FromSeconds(6L);
				}
				else
				{
					ask.Stop();
					ShowToast("\ue77f", "Сохранять историю буфера?", (double?)null, "", FillNormal, ((string Text, Action Run)?)("Да", () =>
					{
						SetClipPersist(keep: true);
					}), 15000, false, new ToastExtra("Скопированное не пропадёт после перезапуска", ((string Text, Action Run)?)("Нет", () =>
					{
						SetClipPersist(keep: false);
					}), ToastAnim.None, (string?)null, (ImageSource?)null, false));
				}
			};
			ask.Start();
		}
		if (TipSeen("welcome"))
		{
			return;
		}
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(2L)
		};
		timer.Tick += (object? _, EventArgs _) =>
		{
			timer.Stop();
			if (_state != IslandState.Expanded)
			{
				MarkTipSeen("welcome");
				ShowToast("\ue7c9", "Наведите курсор на остров — он раскроется", null, "", FillNormal, null, 5000);
			}
		};
		timer.Start();
	}

	private static void SetClipPersist(bool keep)
	{
		Settings.Current.ClipPersist = keep;
		Settings.Current.Save();
		Settings.NotifyChanged();
	}

	private void UpdateTip()
	{
		FrameworkElement? frameworkElement = ((_state == IslandState.Expanded) ? _shownTab : null);
		if (_tipFor != null && _tipFor != frameworkElement)
		{
			HideTip(resize: false);
		}
		if (frameworkElement != null && _tipFor != frameworkElement && Tips.TryGetValue(frameworkElement, out (string, string) value) && !TipSeen(value.Item1))
		{
			_tipFor = frameworkElement;
			_tipId = value.Item1;
			_tipShownAt = DateTime.Now;
			TipText.Text = value.Item2;
			TipBar.Visibility = Visibility.Visible;
			TipBar.Margin = new Thickness(14.0, 0.0, 14.0, 0.0);
			TipBar.Measure(new System.Windows.Size(372.0, double.PositiveInfinity));
			_tipHeight = Math.Max(36.0, TipBar.DesiredSize.Height) + 6.0;
			TipBar.IsHitTestVisible = true;
			Animate(TipBar, UIElement.OpacityProperty, 1.0, 260, new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}, 280, 0.0);
			if (_tipTimer == null)
			{
				_tipTimer = CreateTipTimer();
			}
			_tipTimer.Stop();
			_tipTimer.Start();
		}
	}

	private DispatcherTimer CreateTipTimer()
	{
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(10L)
		};
		timer.Tick += (object? _, EventArgs _) =>
		{
			timer.Stop();
			HideTip(resize: true);
		};
		return timer;
	}

	private void TipClose_Click(object sender, RoutedEventArgs e)
	{
		if (_tipId != null)
		{
			MarkTipSeen(_tipId);
		}
		HideTip(resize: true);
	}

	private void HideTip(bool resize)
	{
		if (_tipFor != null)
		{
			if (_tipId != null && DateTime.Now - _tipShownAt > TimeSpan.FromSeconds(2.5))
			{
				MarkTipSeen(_tipId);
			}
			_tipFor = null;
			_tipId = null;
			_tipTimer?.Stop();
			TipBar.IsHitTestVisible = false;
			FadeOutAndCollapse(TipBar, 120, new CubicEase
			{
				EasingMode = EasingMode.EaseIn
			});
			if (resize && _state == IslandState.Expanded)
			{
				Resize(400.0, 46.0 + TabHeight(_shownTab));
			}
		}
	}

	private double WithTip(double h)
	{
		if (_tipFor == null || _state != IslandState.Expanded || _tipFor != _shownTab)
		{
			return h;
		}
		TipBar.Margin = new Thickness(14.0, h - 8.0, 14.0, 0.0);
		return h + _tipHeight;
	}

	private void InitListening()
	{
		DispatcherTimer dispatcherTimer = new DispatcherTimer();
		dispatcherTimer.Interval = TimeSpan.FromSeconds(10L);
		dispatcherTimer.Tick += (object? _, EventArgs _) =>
		{
			if (Settings.Current.TrackHistory)
			{
				Island.Services.MediaState? current = _media.Current;
				if ((object)current != null && current.IsPlaying && Trackable(current))
				{
					ListenStats.Current.Add(current.Artist, current.Title, 10);
				}
			}
		};
		dispatcherTimer.Start();
	}

	private static string Duration(int seconds)
	{
		int num = seconds / 3600;
		int num2 = seconds % 3600 / 60;
		if (num > 0)
		{
			return $"{num} ч {num2:00} мин";
		}
		return $"{Math.Max(1, num2)} мин";
	}

	private UIElement StatsView()
	{
		ListenSummary listenSummary = ListenStats.Current.Summary();
		StackPanel panel = new StackPanel
		{
			Margin = new Thickness(8.0, 2.0, 8.0, 8.0)
		};
		System.Windows.Media.Brush primary = (System.Windows.Media.Brush)FindResource("TextPrimary");
		System.Windows.Media.Brush tertiary = (System.Windows.Media.Brush)FindResource("TextTertiary");
		System.Windows.Media.Brush brush = (System.Windows.Media.Brush)FindResource("TextSecondary");
		SolidColorBrush bar = new SolidColorBrush(_accent);
		if (listenSummary.Seconds < 60)
		{
			UIElementCollection children = panel.Children;
			string text = (Settings.Current.TrackHistory ? "Пока мало данных. Послушайте музыку, и здесь появятся итоги недели" : "Итоги выключены в настройках, «Музыка»");
			System.Windows.Media.Brush brush2 = tertiary;
			Thickness? margin = new Thickness(0.0, 12.0, 0.0, 12.0);
			children.Add(Text(text, 12.0, brush2, null, margin));
			return panel;
		}
		panel.Children.Add(Text("ЗА НЕДЕЛЮ", 10.0, tertiary, FontWeights.SemiBold));
		panel.Children.Add(Text(Duration(listenSummary.Seconds), 26.0, primary, FontWeights.Bold, new Thickness(0.0, 0.0, 0.0, 2.0)));
		string text2 = $"музыка в {listenSummary.DaysWithMusic} из 7 дней";
		int? bestHour = listenSummary.BestHour;
		if (bestHour.HasValue)
		{
			int valueOrDefault = bestHour.GetValueOrDefault();
			text2 += $", чаще всего с {valueOrDefault:00} до {(valueOrDefault + 3) % 24:00}";
		}
		panel.Children.Add(Text(text2, 11.5, brush));
		int num = Math.Max(1, listenSummary.PerDay.Max());
		Grid grid = new Grid
		{
			Margin = new Thickness(0.0, 12.0, 0.0, 4.0),
			Height = 58.0
		};
		for (int i = 0; i < listenSummary.PerDay.Length; i++)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			Grid grid2 = new Grid();
			grid2.RowDefinitions.Add(new RowDefinition());
			grid2.RowDefinitions.Add(new RowDefinition
			{
				Height = GridLength.Auto
			});
			Border element = new Border
			{
				Width = 16.0,
				Height = Math.Max(3.0, 38.0 * (double)listenSummary.PerDay[i] / (double)num),
				CornerRadius = new CornerRadius(5.0),
				VerticalAlignment = VerticalAlignment.Bottom,
				Background = ((listenSummary.PerDay[i] > 0) ? bar : OutRowHover),
				ToolTip = ((listenSummary.PerDay[i] > 0) ? Duration(listenSummary.PerDay[i]) : "тишина")
			};
			grid2.Children.Add(element);
			string text3 = listenSummary.DayNames[i];
			System.Windows.Media.Brush brush3 = ((i == listenSummary.PerDay.Length - 1) ? primary : tertiary);
			Thickness? margin = new Thickness(0.0, 3.0, 0.0, 0.0);
			TextBlock textBlock = Text(text3, 9.5, brush3, null, margin);
			textBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
			Grid.SetRow(textBlock, 1);
			grid2.Children.Add(textBlock);
			Grid.SetColumn(grid2, i);
			grid.Children.Add(grid2);
		}
		panel.Children.Add(grid);
		Top("ИСПОЛНИТЕЛИ", listenSummary.Artists);
		Top("ТРЕКИ", listenSummary.Tracks);
		return panel;
		static TextBlock Text(string text4, double size, System.Windows.Media.Brush foreground, FontWeight? weight = null, Thickness? thickness = null)
		{
			return new TextBlock
			{
				Text = text4,
				FontFamily = OutFont,
				FontSize = size,
				Foreground = foreground,
				FontWeight = (weight ?? FontWeights.Normal),
				TextWrapping = TextWrapping.Wrap,
				Margin = (thickness ?? new Thickness(0.0))
			};
		}
		void Top(string title, List<(string Name, int Seconds)> items)
		{
			if (items.Count == 0)
			{
				return;
			}
			panel.Children.Add(Text(title, 10.0, tertiary, FontWeights.SemiBold, new Thickness(0.0, 10.0, 0.0, 4.0)));
			int num2 = Math.Max(1, items[0].Seconds);
			foreach (var item3 in items)
			{
				string item = item3.Name;
				int item2 = item3.Seconds;
				Grid grid3 = new Grid
				{
					Margin = new Thickness(0.0, 2.0, 0.0, 2.0)
				};
				grid3.ColumnDefinitions.Add(new ColumnDefinition());
				grid3.ColumnDefinitions.Add(new ColumnDefinition
				{
					Width = GridLength.Auto
				});
				TextBlock textBlock2 = Text(item, 12.0, primary);
				textBlock2.TextWrapping = TextWrapping.NoWrap;
				textBlock2.TextTrimming = TextTrimming.CharacterEllipsis;
				string text4 = Duration(item2);
				System.Windows.Media.Brush brush4 = tertiary;
				Thickness? margin2 = new Thickness(8.0, 1.0, 0.0, 0.0);
				TextBlock element2 = Text(text4, 10.5, brush4, null, margin2);
				Grid.SetColumn(element2, 1);
				Border element3 = new Border
				{
					Height = 3.0,
					CornerRadius = new CornerRadius(2.0),
					HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
					Margin = new Thickness(0.0, 14.0, 0.0, 0.0),
					Width = Math.Max(4.0, 250.0 * (double)item2 / (double)num2),
					Background = bar,
					Opacity = 0.7
				};
				grid3.Children.Add(textBlock2);
				grid3.Children.Add(element2);
				grid3.Children.Add(element3);
				grid3.Height = 22.0;
				panel.Children.Add(grid3);
			}
		}
	}

	private void Cover_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (_media.Current == null)
		{
			return;
		}
		e.Handled = true;
		if (e.ClickCount == 2)
		{
			_coverPressed = false;
			if (PlayerControllable)
			{
				_media.PlayPauseAsync();
			}
		}
		else
		{
			_coverDown = e.GetPosition(this);
			_coverPressed = true;
			CoverArt.CaptureMouse();
		}
	}

	private async void Cover_MouseUp(object sender, MouseButtonEventArgs e)
	{
		if (!_coverPressed)
		{
			return;
		}
		_coverPressed = false;
		CoverArt.ReleaseMouseCapture();
		e.Handled = true;
		double num = e.GetPosition(this).X - _coverDown.X;
		if (Math.Abs(num) < 28.0)
		{
			return;
		}
		Island.Services.MediaState? current = _media.Current;
		if ((object)current != null && PlayerControllable)
		{
			if (num < 0.0 && current.CanNext)
			{
				NudgeCover(1);
				await _media.NextAsync();
			}
			else if (num > 0.0 && current.CanPrevious)
			{
				NudgeCover(-1);
				await _media.PreviousAsync();
			}
		}
	}

	private void Cover_MouseWheel(object sender, MouseWheelEventArgs e)
	{
		e.Handled = true;
		Island.Services.MediaState? current = _media.Current;
		if ((object)current != null)
		{
			float? num = _pendingMusicVolume ?? _volume.GetAppVolume(current.SourceId);
			if (!num.HasValue)
			{
				ShowCoverNote("Плеер ещё не выводит звук");
				return;
			}
			double num2 = Math.Clamp(Math.Round(((double)num.Value + ((e.Delta > 0) ? 0.04 : (-0.04))) * 100.0) / 100.0, 0.0, 1.0);
			MusicVolume.Value = num2;
			SetMusicVolume(num2);
			ShowCoverNote($"Громкость плеера {num2 * 100.0:0}%");
		}
	}

	private void ShowCoverNote(string text)
	{
		SourceText.Effect = null;
		SourceText.Text = text;
		_offsetShownUntil = DateTime.Now.AddSeconds(1.2);
		_lyricIndex = -2;
		_lyricShown = null;
		if (_coverVolumeBack == null)
		{
			_coverVolumeBack = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(1.25)
			};
		}
		_coverVolumeBack.Stop();
		_coverVolumeBack.Tick -= CoverNoteDone;
		_coverVolumeBack.Tick += CoverNoteDone;
		_coverVolumeBack.Start();
	}

	private void CoverNoteDone(object? sender, EventArgs e)
	{
		_coverVolumeBack?.Stop();
		UpdateLyrics();
	}

	private void Cover_RightClick(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		ShowTrackInfo();
	}

	private void Title_Click(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		ShowTrackInfo();
	}

	private void ShowTrackInfo()
	{
		Popup? infoPopup = _infoPopup;
		if (infoPopup != null && infoPopup.IsOpen)
		{
			infoPopup.IsOpen = false;
			return;
		}
		Island.Services.MediaState? m = _media.Current;
		if ((object)m == null)
		{
			return;
		}
		System.Windows.Media.Brush primary = (System.Windows.Media.Brush)FindResource("TextPrimary");
		System.Windows.Media.Brush tertiary = (System.Windows.Media.Brush)FindResource("TextTertiary");
		StackPanel list = new StackPanel
		{
			Width = 292.0
		};
		list.Children.Add(new TextBlock
		{
			Text = (string.IsNullOrEmpty(m.Title) ? "Неизвестный трек" : m.Title),
			FontFamily = OutFont,
			FontSize = 15.0,
			FontWeight = FontWeights.Bold,
			Foreground = primary,
			TextWrapping = TextWrapping.Wrap,
			MaxHeight = 42.0,
			Margin = new Thickness(8.0, 4.0, 8.0, 0.0)
		});
		if (m.Artist.Length > 0)
		{
			list.Children.Add(new TextBlock
			{
				Text = m.Artist,
				FontFamily = OutFont,
				FontSize = 12.5,
				Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
				Margin = new Thickness(8.0, 1.0, 8.0, 6.0),
				TextTrimming = TextTrimming.CharacterEllipsis
			});
		}
		Fact("Альбом", m.Album);
		if (!string.Equals(m.AlbumArtist, m.Artist, StringComparison.OrdinalIgnoreCase))
		{
			Fact("Исполнитель альбома", m.AlbumArtist);
		}
		Fact("Жанр", m.Genre);
		if (m.TrackNumber > 0)
		{
			Fact("Номер в альбоме", m.TrackNumber.ToString());
		}
		if (m.Duration > TimeSpan.Zero)
		{
			Fact("Длительность", m.Duration.ToString((m.Duration.TotalHours >= 1.0) ? "h\\:mm\\:ss" : "m\\:ss"));
		}
		Fact("Тип", m.Kind switch
		{
			"music" => "Музыка", 
			"video" => "Видео", 
			"image" => "Изображение", 
			_ => null, 
		});
		Fact("Источник", m.SourceApp);
		string value;
		if (Settings.Current.Lyrics)
		{
			value = ((_lyrics.Lines != null) ? "найден, подсвечивается по словам" : "не найден");
		}
		else
		{
			value = "выключен в настройках";
		}
		Fact("Текст песни", value);
		if (m.Album.Length == 0 && m.Genre.Length == 0)
		{
			list.Children.Add(new TextBlock
			{
				Text = "Этот плеер не сообщил Windows больше. Год и качество звука плееры Windows не отдают.",
				FontFamily = OutFont,
				FontSize = 10.5,
				Foreground = tertiary,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(8.0, 4.0, 8.0, 2.0)
			});
		}
		Popup popup = new Popup
		{
			PlacementTarget = CoverArt,
			Placement = PlacementMode.Bottom,
			StaysOpen = false,
			AllowsTransparency = true,
			PopupAnimation = PopupAnimation.Fade,
			HorizontalOffset = -8.0,
			VerticalOffset = 2.0,
			Child = new Border
			{
				Margin = new Thickness(14.0),
				Padding = new Thickness(4.0),
				Background = OutCard,
				CornerRadius = new CornerRadius(10.0),
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 52, 59)),
				BorderThickness = new Thickness(1.0),
				Effect = new DropShadowEffect
				{
					BlurRadius = 16.0,
					ShadowDepth = 3.0,
					Direction = 270.0,
					Opacity = 0.45,
					Color = Colors.Black
				},
				Child = list
			}
		};
		WrapPanel wrapPanel = new WrapPanel
		{
			Margin = new Thickness(8.0, 10.0, 8.0, 4.0)
		};
		wrapPanel.Children.Add(Button((m.SourceApp.Length > 0) ? ("Открыть " + m.SourceApp) : "Открыть плеер", () =>
		{
			OpenPlayerApp(m);
		}));
		wrapPanel.Children.Add(Button("Копировать название", () =>
		{
			try
			{
				ReviewClipboard.SetText(string.Join(" — ", new string[2] { m.Artist, m.Title }.Where((string s) => s.Length > 0)));
			}
			catch (Exception ex)
			{
				Log.Caught("copy track name", ex);
			}
		}));
		list.Children.Add(wrapPanel);
		_infoPopup = popup;
		_menuOpen = true;
		popup.Closed += (object? _, EventArgs _) =>
		{
			_menuOpen = false;
			Dispatcher.BeginInvoke(new Action(ReleaseKeepOpen));
		};
		popup.IsOpen = true;
		Border Button(string text, Action click)
		{
			Border b = new Border
			{
				Padding = new Thickness(12.0, 6.0, 12.0, 7.0),
				Margin = new Thickness(0.0, 0.0, 6.0, 0.0),
				CornerRadius = new CornerRadius(6.0),
				Background = OutRow,
				Cursor = System.Windows.Input.Cursors.Hand,
				Child = new TextBlock
				{
					Text = text,
					FontFamily = OutFont,
					FontSize = 12.0,
					FontWeight = FontWeights.SemiBold,
					Foreground = primary
				}
			};
			b.MouseEnter += (object _, System.Windows.Input.MouseEventArgs _) =>
			{
				b.Background = OutRowHover;
			};
			b.MouseLeave += (object _, System.Windows.Input.MouseEventArgs _) =>
			{
				b.Background = OutRow;
			};
			b.MouseLeftButtonUp += (object _, MouseButtonEventArgs _) =>
			{
				popup.IsOpen = false;
				click();
			};
			return b;
		}
		void Fact(string label, string? text)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				Grid grid = new Grid
				{
					Margin = new Thickness(8.0, 3.0, 8.0, 3.0)
				};
				grid.ColumnDefinitions.Add(new ColumnDefinition
				{
					Width = new GridLength(104.0)
				});
				grid.ColumnDefinitions.Add(new ColumnDefinition());
				grid.Children.Add(new TextBlock
				{
					Text = label,
					FontFamily = OutFont,
					FontSize = 11.5,
					Foreground = tertiary
				});
				TextBlock element = new TextBlock
				{
					Text = text,
					FontFamily = OutFont,
					FontSize = 12.0,
					Foreground = primary,
					TextWrapping = TextWrapping.Wrap
				};
				Grid.SetColumn(element, 1);
				grid.Children.Add(element);
				list.Children.Add(grid);
			}
		}
	}

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint hWnd, int cmd);

	private void OpenPlayerApp(Island.Services.MediaState m)
	{
		try
		{
			string text;
			if (!m.SourceId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				text = m.SourceId;
			}
			else
			{
				string sourceId = m.SourceId;
				text = sourceId.Substring(0, sourceId.Length - 4);
			}
			string name = text;
			Process? process = Process.GetProcesses().FirstOrDefault((Process p) =>
			{
				try
				{
					return p.MainWindowHandle != IntPtr.Zero && string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase);
				}
				catch (Exception)
				{
					return false;
				}
			});
			if (process != null)
			{
				ShowWindow(process.MainWindowHandle, 9);
				Win32.SetForegroundWindow(process.MainWindowHandle);
				Log.Write("player window shown: " + process.ProcessName);
			}
			else if (m.SourceId.Contains('!'))
			{
				ReviewSafety.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + m.SourceId)
				{
					UseShellExecute = true
				});
				Log.Write("player app started: " + m.SourceId);
			}
			else
			{
				Log.Write("player window not found: " + m.SourceId);
				ShowToast("", "Окно плеера не найдено", null, "", FillNormal);
			}
		}
		catch (Exception ex)
		{
			Log.Error("open player", ex);
		}
	}

	private bool Trackable(Island.Services.MediaState m)
	{
		if (!string.IsNullOrWhiteSpace(m.Title))
		{
			if (_media.IsBrowserFallback)
			{
				return !string.IsNullOrEmpty(m.Artist);
			}
			return true;
		}
		return false;
	}

	private void OnTrackForStore(Island.Services.MediaState m)
	{
		if (Trackable(m) && m.IsPlaying && Settings.Current.TrackHistory)
		{
			TrackStore.Current.Played(m.Title, m.Artist, m.SourceApp);
		}
	}

	private void ShowTracks()
	{
		StackPanel body = new StackPanel();
		Grid header = new Grid
		{
			Margin = new Thickness(2.0, 0.0, 2.0, 6.0)
		};
		StackPanel stackPanel = new StackPanel
		{
			Width = 300.0
		};
		stackPanel.Children.Add(header);
		stackPanel.Children.Add(body);
		Popup popup = new Popup
		{
			PlacementTarget = MoreButton,
			Placement = PlacementMode.Bottom,
			StaysOpen = false,
			AllowsTransparency = true,
			PopupAnimation = PopupAnimation.Fade,
			HorizontalOffset = MoreButton.ActualWidth - 300.0 + 6.0,
			VerticalOffset = 2.0,
			Child = new Border
			{
				Margin = new Thickness(14.0),
				Padding = new Thickness(4.0),
				Background = OutCard,
				CornerRadius = new CornerRadius(10.0),
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 52, 59)),
				BorderThickness = new Thickness(1.0),
				Effect = new DropShadowEffect
				{
					BlurRadius = 16.0,
					ShadowDepth = 3.0,
					Direction = 270.0,
					Opacity = 0.45,
					Color = Colors.Black
				},
				Child = stackPanel
			}
		};
		Rebuild();
		_tracksPopup = popup;
		_menuOpen = true;
		popup.Closed += (object? _, EventArgs _) =>
		{
			_menuOpen = false;
			Dispatcher.BeginInvoke(new Action(ReleaseKeepOpen));
		};
		popup.IsOpen = true;
		Border Chip(string text, bool active, Action click)
		{
			Border border = new Border();
			border.Padding = new Thickness(10.0, 5.0, 10.0, 6.0);
			border.Margin = new Thickness(0.0, 0.0, 4.0, 0.0);
			border.CornerRadius = new CornerRadius(6.0);
			border.Background = (active ? OutActive : OutRow);
			border.Cursor = System.Windows.Input.Cursors.Hand;
			border.Child = new TextBlock
			{
				Text = text,
				FontFamily = OutFont,
				FontSize = 12.0,
				FontWeight = FontWeights.SemiBold,
				Foreground = (active ? OutActiveText : ((System.Windows.Media.Brush)FindResource("TextPrimary")))
			};
			border.MouseLeftButtonUp += (object _, MouseButtonEventArgs _) =>
			{
				click();
			};
			return border;
		}
		void Rebuild()
		{
			StackPanel stackPanel2 = new StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Left
			};
			stackPanel2.Children.Add(Chip("История", !_statsTab, () =>
			{
				_statsTab = false;
				Rebuild();
			}));
			stackPanel2.Children.Add(Chip("Итоги", _statsTab, () =>
			{
				_statsTab = true;
				Rebuild();
			}));
			System.Windows.Controls.Button button = new System.Windows.Controls.Button
			{
				Style = (Style)FindResource("PinButton"),
				Width = 28.0,
				Height = 26.0,
				Margin = new Thickness(0.0),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
				ToolTip = "Поделиться текущим треком: карточка с обложкой и строкой песни",
				Content = new TextBlock
				{
					Text = "\ue72d",
					FontFamily = (System.Windows.Media.FontFamily)FindResource("Icons"),
					FontSize = 13.0,
					Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary")
				}
			};
			button.Click += (object _, RoutedEventArgs _) =>
			{
				popup.IsOpen = false;
				ShareCurrentTrack();
			};
			header.Children.Clear();
			header.Children.Add(stackPanel2);
			header.Children.Add(button);
			body.Children.Clear();
			if (_statsTab)
			{
				body.Children.Add(StatsView());
			}
			else
			{
				List<TrackEntry> list = TrackStore.Current.History.Take(20).ToList();
				if (list.Count == 0)
				{
					body.Children.Add(new TextBlock
					{
						Text = (Settings.Current.TrackHistory ? "Здесь появятся последние треки" : "История выключена в настройках, «Музыка»"),
						FontFamily = OutFont,
						FontSize = 12.0,
						TextWrapping = TextWrapping.Wrap,
						Margin = new Thickness(8.0, 14.0, 8.0, 16.0),
						Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary")
					});
				}
				else
				{
					StackPanel stackPanel3 = new StackPanel();
					foreach (TrackEntry item in list)
					{
						StackPanel stackPanel4 = new StackPanel
						{
							VerticalAlignment = VerticalAlignment.Center
						};
						stackPanel4.Children.Add(new TextBlock
						{
							Text = item.Title,
							FontFamily = OutFont,
							FontSize = 13.0,
							FontWeight = FontWeights.SemiBold,
							TextTrimming = TextTrimming.CharacterEllipsis,
							Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary")
						});
						stackPanel4.Children.Add(new TextBlock
						{
							Text = string.Join(" · ", new string[2]
							{
								item.Artist,
								AgeText(item.At)
							}.Where((string s) => s.Length > 0)),
							FontFamily = OutFont,
							FontSize = 10.5,
							TextTrimming = TextTrimming.CharacterEllipsis,
							Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary")
						});
						Grid child = new Grid
						{
							ColumnDefinitions = 
							{
								new ColumnDefinition
								{
									Width = new GridLength(1.0, GridUnitType.Star)
								},
								new ColumnDefinition
								{
									Width = GridLength.Auto
								}
							},
							Children = { (UIElement)stackPanel4 }
						};
						TrackEntry e = item;
						Border row = new Border
						{
							Height = 40.0,
							Margin = new Thickness(0.0, 2.0, 0.0, 2.0),
							Padding = new Thickness(12.0, 0.0, 8.0, 0.0),
							CornerRadius = new CornerRadius(6.0),
							Background = OutRow,
							Cursor = System.Windows.Input.Cursors.Hand,
							Child = child,
							ToolTip = "Найти в Яндекс Музыке. ПКМ: на YouTube"
						};
						row.MouseEnter += (object _, System.Windows.Input.MouseEventArgs _) =>
						{
							row.Background = OutRowHover;
						};
						row.MouseLeave += (object _, System.Windows.Input.MouseEventArgs _) =>
						{
							row.Background = OutRow;
						};
						row.MouseLeftButtonUp += (object _, MouseButtonEventArgs _) =>
						{
							popup.IsOpen = false;
							Launch("https://music.yandex.ru/search?text=" + Uri.EscapeDataString((e.Artist + " " + e.Title).Trim()));
						};
						row.MouseRightButtonUp += (object _, MouseButtonEventArgs _) =>
						{
							popup.IsOpen = false;
							Launch("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString((e.Artist + " " + e.Title).Trim()));
						};
						stackPanel3.Children.Add(row);
					}
					body.Children.Add(new ScrollViewer
					{
						MaxHeight = 280.0,
						VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
						Content = stackPanel3
					});
				}
			}
		}
	}

	private static string AgeText(DateTime at)
	{
		int days = (DateTime.Today - at.Date).Days;
		if (days < 7)
		{
			return days switch
			{
				0 => $"сегодня {at:HH:mm}", 
				1 => $"вчера {at:HH:mm}", 
				_ => $"{at:ddd HH:mm}", 
			};
		}
		return $"{at:dd.MM}";
	}

	private void ShareCurrentTrack()
	{
		try
		{
			Island.Services.MediaState? current = _media.Current;
			if ((object)current == null || !Trackable(current))
			{
				ShowToast("", "Сейчас ничего не играет", null, "", FillNormal);
				return;
			}
			string? text = (LyricsActive ? _lyricShown : null);
			BitmapSource bitmapSource = RenderShareCard(current, string.IsNullOrWhiteSpace(text) ? null : text.Trim());
			ReviewClipboard.SetImage(bitmapSource);
			string text2 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Island");
			Directory.CreateDirectory(text2);
			string text3 = string.Concat((current.Artist + " - " + current.Title).Select((char c) => (!System.IO.Path.GetInvalidFileNameChars().Contains(c)) ? c : '_'));
			string path = System.IO.Path.Combine(text2, ((text3.Length > 60) ? text3.Substring(0, 60) : text3) + ".png");
			PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
			pngBitmapEncoder.Frames.Add(BitmapFrame.Create(bitmapSource));
			using (FileStream stream = File.Create(path))
			{
				pngBitmapEncoder.Save(stream);
			}
			Log.Write("share card made");
			ShowToast("", "Карточка скопирована", (double?)null, "", FillCharging, ((string Text, Action Run)?)("Показать", () =>
			{
				Launch("explorer.exe", "/select,\"" + path + "\"");
			}), 5000, false, (ToastExtra?)null);
		}
		catch (Exception ex)
		{
			Log.Error("share card", ex);
			ShowToast("", "Не удалось сделать карточку", null, "", FillNormal);
		}
	}

	private BitmapSource RenderShareCard(Island.Services.MediaState m, string? lyric)
	{
		System.Windows.Media.Color accent = m.Accent;
		SolidColorBrush white = System.Windows.Media.Brushes.White;
		SolidColorBrush foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(179, byte.MaxValue, byte.MaxValue, byte.MaxValue));
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		Border border = new Border
		{
			Width = 96.0,
			Height = 96.0,
			CornerRadius = new CornerRadius(18.0),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 38, 38)),
			Effect = new DropShadowEffect
			{
				BlurRadius = 20.0,
				ShadowDepth = 4.0,
				Opacity = 0.5,
				Color = Colors.Black
			}
		};
		if (m.Cover != null)
		{
			border.Background = new ImageBrush(m.Cover)
			{
				Stretch = Stretch.UniformToFill
			};
		}
		grid.Children.Add(border);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(18.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = m.Title,
			FontFamily = OutFont,
			FontSize = 22.0,
			FontWeight = FontWeights.Bold,
			Foreground = white,
			TextWrapping = TextWrapping.Wrap,
			MaxHeight = 62.0,
			TextTrimming = TextTrimming.CharacterEllipsis
		});
		if (m.Artist.Length > 0)
		{
			stackPanel.Children.Add(new TextBlock
			{
				Text = m.Artist,
				FontFamily = OutFont,
				FontSize = 15.0,
				Foreground = foreground,
				Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
				TextTrimming = TextTrimming.CharacterEllipsis
			});
		}
		Grid.SetColumn(stackPanel, 1);
		grid.Children.Add(stackPanel);
		StackPanel stackPanel2 = new StackPanel();
		stackPanel2.Children.Add(grid);
		if (lyric != null)
		{
			stackPanel2.Children.Add(new TextBlock
			{
				Text = "«" + lyric + "»",
				FontFamily = OutFont,
				FontSize = 21.0,
				FontWeight = FontWeights.SemiBold,
				Foreground = white,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 24.0, 0.0, 0.0),
				LineHeight = 28.0
			});
		}
		stackPanel2.Children.Add(new TextBlock
		{
			Text = string.Join(" · ", new string[2] { "Island", m.SourceApp }.Where((string s) => !string.IsNullOrEmpty(s))),
			FontFamily = OutFont,
			FontSize = 11.0,
			Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(128, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Margin = new Thickness(0.0, 22.0, 0.0, 0.0)
		});
		Border border2 = new Border
		{
			Width = 440.0,
			Padding = new Thickness(26.0),
			CornerRadius = new CornerRadius(30.0),
			Child = stackPanel2,
			Background = new LinearGradientBrush(Mix(accent, 0.55), System.Windows.Media.Color.FromRgb(18, 18, 22), 62.0)
		};
		border2.Measure(new System.Windows.Size(440.0, double.PositiveInfinity));
		border2.Arrange(new Rect(border2.DesiredSize));
		border2.UpdateLayout();
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap((int)Math.Ceiling(border2.ActualWidth * 2.0), (int)Math.Ceiling(border2.ActualHeight * 2.0), 192.0, 192.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(border2);
		renderTargetBitmap.Freeze();
		return renderTargetBitmap;
		static System.Windows.Media.Color Mix(System.Windows.Media.Color c, double k)
		{
			return System.Windows.Media.Color.FromRgb((byte)((double)(int)c.R * k), (byte)((double)(int)c.G * k), (byte)((double)(int)c.B * k));
		}
	}

	private void UpdateBubble()
	{
		string text = "";
		bool flag = TwoIslands;
		bool flag2;
		if (flag)
		{
			IslandState? state = _state;
			if (state.HasValue)
			{
				IslandState valueOrDefault = state.GetValueOrDefault();
				if ((uint)valueOrDefault <= 1u)
				{
					flag2 = true;
					goto IL_0036;
				}
			}
			flag2 = false;
			goto IL_0036;
		}
		goto IL_0039;
		IL_0039:
		if (flag)
		{
			if (DownloadsActive && _state == IslandState.CompactMedia)
			{
				text = "download";
			}
			else
			{
				int? num = PhoneUnreadForBubble();
				if (num.HasValue)
				{
					text = "phone:" + num.GetValueOrDefault();
				}
			}
		}
		if (Settings.Current.TwoIslandsStyle == "strip")
		{
			SetStrip(text);
			text = "";
		}
		else
		{
			SetStrip("");
		}
		bool flag3 = text.StartsWith("phone:");
		if (flag3)
		{
			PhoneNotification[] array = CenterItems();
			PhoneNotification phoneNotification = array.OrderByDescending((PhoneNotification n) => n.Time).First();
			ImageSource? imageSource = Picture(phoneNotification.Avatar);
			BubbleAvatarBrush.ImageSource = imageSource;
			BubbleAppBrush.ImageSource = ((imageSource == null) ? AppIcon(phoneNotification) : null);
			Show(BubbleAvatarRound, imageSource != null);
			Show(BubbleAvatarSquare, imageSource == null);
			BubbleCountText.Text = ((array.Length > 9) ? "9+" : array.Length.ToString());
		}
		Show(BubbleDownload, text == "download");
		Show(BubblePhone, flag3);
		BubbleWave.Stroke = (flag3 ? FillLow : BubbleSpinner.Stroke);
		AnimateDownload(text == "download");
		_waveTimer.IsEnabled = flag3 && !ReducedMotion;
		bool flag4 = text.Length > 0;
		if (flag4 && _bubbleShown && text != _bubbleKind)
		{
			Pop(BubbleInner, 0.7);
			SendWave();
		}
		_bubbleKind = text;
		if (flag4 == _bubbleShown)
		{
			return;
		}
		_bubbleShown = flag4;
		if (flag4)
		{
			Bubble.Visibility = Visibility.Visible;
			if (flag3)
			{
				SendWave();
			}
		}
		MoveDrop(flag4 ? 1 : 0);
		return;
		IL_0036:
		flag = flag2;
		goto IL_0039;
	}

	private int? PhoneUnreadForBubble()
	{
		if (!PhoneOn || !Settings.Current.PhoneOnCollapsed)
		{
			return null;
		}
		if (MiniPill && (_state == IslandState.Idle || !_miniLyrics))
		{
			return null;
		}
		int num = CenterItems().Length;
		if (num <= 0)
		{
			return null;
		}
		return num;
	}

	private void MoveDrop(double target)
	{
		_dropTarget = target;
		if (ReducedMotion)
		{
			_dropP = target;
			_dropV = 0.0;
			FinishDrop();
		}
		else if (!_dropLoop)
		{
			_dropLoop = true;
			_dropLastT = _clock.Elapsed.TotalSeconds;
			CompositionTarget.Rendering += OnDropFrame;
		}
	}

	private void OnDropFrame(object? sender, EventArgs e)
	{
		double totalSeconds = _clock.Elapsed.TotalSeconds;
		double num = Math.Clamp(totalSeconds - _dropLastT, 1.0 / 240.0, 0.05);
		_dropLastT = totalSeconds;
		double num2 = ((_dropTarget > 0.0) ? 95 : 160);
		double num3 = ((_dropTarget > 0.0) ? 9 : 22);
		_dropV += (num2 * (_dropTarget - _dropP) - num3 * _dropV) * num;
		_dropP += _dropV * num;
		BuildShape();
		if (!(Math.Abs(_dropTarget - _dropP) > 0.002) && !(Math.Abs(_dropV) > 0.02))
		{
			_dropP = _dropTarget;
			_dropV = 0.0;
			CompositionTarget.Rendering -= OnDropFrame;
			_dropLoop = false;
			FinishDrop();
		}
	}

	private void FinishDrop()
	{
		if (_dropTarget == 0.0)
		{
			Bubble.Visibility = Visibility.Collapsed;
			AnimateDownload(on: false);
			_waveTimer.IsEnabled = false;
		}
		BuildShape();
	}

	private (Geometry Body, Geometry Rim, bool Joined)? BubbleDrop(double islandRight, double islandReach)
	{
		if (_dropP <= 0.01 || Root.Visibility != Visibility.Visible)
		{
			return null;
		}
		double num = Math.Max(0.0, _dropP);
		double num2 = 28.0 * (0.5 + 0.5 * Math.Min(1.0, num));
		double num3 = (0.0 - num2) / 2.0 - 2.0;
		double num4 = 28.0;
		double num5 = islandRight + num3 + (num4 - num3) * num;
		double num6 = 18.0;
		double num7 = Math.Clamp(Math.Abs(_dropV) * 0.025, 0.0, 0.16);
		double num8 = 1.0 + Math.Min(0.06, _waveAmp * 0.012);
		double num9 = num2 / 2.0 * (1.0 + num7) * num8;
		double num10 = num2 / 2.0 * (1.0 - num7) * num8;
		EllipseGeometry ellipseGeometry = new EllipseGeometry(new System.Windows.Point(num5, num6), num9, num10);
		ellipseGeometry.Freeze();
		BubbleShift.X = num5 - Bubble.Width / 2.0;
		BubbleShift.Y = num6 - Bubble.Height / 2.0;
		Bubble.Opacity = Math.Clamp((num - 0.55) / 0.35, 0.0, 1.0);
		double num11 = num5 - num9 - islandReach;
		if (num11 >= 9.0)
		{
			return (ellipseGeometry, ellipseGeometry, false);
		}
		double num12 = islandReach - 4.0;
		double num13 = num5;
		double num14 = Math.Max(1.2, num10 * 0.6 * (1.0 - Math.Max(0.0, num11) / 9.0));
		double x = (num12 + num13) / 2.0;
		PathFigure pathFigure = new PathFigure
		{
			StartPoint = new System.Windows.Point(num12, num6 - num10 * 0.95),
			IsClosed = true
		};
		pathFigure.Segments.Add(new QuadraticBezierSegment(new System.Windows.Point(x, num6 - num14), new System.Windows.Point(num13, num6 - num10 * 0.85), isStroked: true));
		pathFigure.Segments.Add(new LineSegment(new System.Windows.Point(num13, num6 + num10 * 0.85), isStroked: true));
		pathFigure.Segments.Add(new QuadraticBezierSegment(new System.Windows.Point(x, num6 + num14), new System.Windows.Point(num12, num6 + num10 * 0.95), isStroked: true));
		CombinedGeometry combinedGeometry = new CombinedGeometry(GeometryCombineMode.Union, ellipseGeometry, new PathGeometry
		{
			Figures = { pathFigure }
		});
		combinedGeometry.Freeze();
		return (combinedGeometry, combinedGeometry, true);
	}

	private void AnimateDownload(bool on)
	{
		on &= !ReducedMotion;
		if (on != _downloadAnim)
		{
			_downloadAnim = on;
			if (!on)
			{
				BubbleArrowShift.BeginAnimation(TranslateTransform.YProperty, null);
				BubbleSpin.BeginAnimation(RotateTransform.AngleProperty, null);
				return;
			}
			DoubleAnimation doubleAnimation = new DoubleAnimation(-2.0, 2.0, TimeSpan.FromMilliseconds(600L))
			{
				AutoReverse = true,
				RepeatBehavior = RepeatBehavior.Forever,
				EasingFunction = new SineEase()
			};
			DoubleAnimation doubleAnimation2 = new DoubleAnimation(0.0, 360.0, TimeSpan.FromMilliseconds(1100L))
			{
				RepeatBehavior = RepeatBehavior.Forever
			};
			Timeline.SetDesiredFrameRate(doubleAnimation, 30);
			Timeline.SetDesiredFrameRate(doubleAnimation2, 30);
			BubbleArrowShift.BeginAnimation(TranslateTransform.YProperty, doubleAnimation);
			BubbleSpin.BeginAnimation(RotateTransform.AngleProperty, doubleAnimation2);
		}
	}

	private void SendWave()
	{
		if (!ReducedMotion)
		{
			CubicEase ease = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			Animate(BubbleWaveScale, ScaleTransform.ScaleXProperty, 1.75, 900, ease, 0, 1.0);
			Animate(BubbleWaveScale, ScaleTransform.ScaleYProperty, 1.75, 900, ease, 0, 1.0);
			Animate(BubbleWave, UIElement.OpacityProperty, 0.0, 900, ease, 0, 0.9);
		}
	}

	private void Bubble_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
	{
		Pop(BubbleInner, 0.85);
	}

	private void Bubble_Click(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		if (_bubbleKind == "download")
		{
			try
			{
				ReviewSafety.Start(new ProcessStartInfo("explorer.exe", "shell:Downloads")
				{
					UseShellExecute = true
				});
				return;
			}
			catch (Exception ex)
			{
				Log.Error("open downloads", ex);
				return;
			}
		}
		if (_bubbleKind.StartsWith("phone:"))
		{
			SelectTab(7);
			SetHover(value: true, 0);
		}
	}

	private void SetStrip(string kind)
	{
		string text;
		if (kind == "download")
		{
			text = "download";
		}
		else
		{
			text = (kind.StartsWith("phone:") ? "phone" : "");
		}
		if (text == "phone" && kind != _stripKind && _stripKind.StartsWith("phone:"))
		{
			_stripLevel = 1.4;
		}
		_stripKind = kind;
		if (text.Length > 0)
		{
			Shape.StripColor = ((text == "download") ? StripDownload : StripPhone);
			if (!_stripLoop)
			{
				_stripStart = _clock.Elapsed.TotalSeconds;
			}
		}
		if (!_stripLoop && (text.Length != 0 || !(_stripLevel < 0.01)))
		{
			_stripLoop = true;
			CompositionTarget.Rendering += OnStripFrame;
		}
	}

	private void OnStripFrame(object? sender, EventArgs e)
	{
		double totalSeconds = _clock.Elapsed.TotalSeconds;
		if (!(totalSeconds - _stripFrameT < 1.0 / 30.0))
		{
			double num = Math.Min(0.1, totalSeconds - _stripFrameT);
			_stripFrameT = totalSeconds;
			double num2 = totalSeconds - _stripStart;
			bool flag = _stripKind == "download";
			bool flag2 = _stripKind.StartsWith("phone:");
			double num3;
			if (!flag && !flag2)
			{
				num3 = 0.0;
			}
			else if (ReducedMotion)
			{
				num3 = 1.0;
			}
			else
			{
				num3 = (flag ? 1.0 : (0.45 + 0.55 * (0.5 + 0.5 * Math.Sin(num2 * Math.PI * 2.0 / 1.8))));
			}
			_stripLevel += (num3 - _stripLevel) * Math.Min(1.0, num * (double)((_stripLevel > 1.0) ? 4 : 7));
			Shape.StripLevel = Math.Min(1.0, _stripLevel);
			Shape.StripPhase = ((flag && !ReducedMotion) ? (num2 / 1.6 % 1.0) : (-1.0));
			Shape.InvalidateVisual();
			if (!(num3 > 0.0) && !(_stripLevel > 0.01))
			{
				_stripLevel = 0.0;
				Shape.StripLevel = 0.0;
				Shape.StripColor = null;
				Shape.InvalidateVisual();
				CompositionTarget.Rendering -= OnStripFrame;
				_stripLoop = false;
			}
		}
	}

	private static void Show(UIElement e, bool on)
	{
		e.Visibility = ((!on) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void ApplyTwoIslands()
	{
		if (_twoApplied != TwoIslands)
		{
			_twoApplied = TwoIslands;
			if (!_toastTimer.IsEnabled)
			{
				ApplyState(BaseState());
			}
		}
		UpdateBubble();
	}

	private void InitUpdates()
	{
		if (DeveloperAccess.Enabled || ReviewSafety.Enabled) return;
		UpdateService.Changed += OnUpdatesChanged;
		UpdateService.DownloadChanged += OnDownloadChanged;
		System.Windows.Application.Current.Exit += (object _, ExitEventArgs _) =>
		{
			ApplyUpdateOnExit();
		};
		_updateTimer.Tick += async (object? _, EventArgs _) =>
		{
			_updateTimer.Interval = TimeSpan.FromMinutes(1L);
			DateTime? lastUpdateCheck = Settings.Current.LastUpdateCheck;
			if (!(UpdateService.Latest == null) && lastUpdateCheck.HasValue)
			{
				DateTime utcNow = DateTime.UtcNow;
				DateTime? dateTime = lastUpdateCheck;
				if (!(utcNow - dateTime > CheckEvery))
				{
					if (_readyToastPending)
					{
						ShowUpdateReadyToast();
					}
					else
					{
						MaybeShowUpdateToast();
					}
					return;
				}
			}
			await UpdateService.CheckAsync();
		};
		_updateTimer.Start();
		if (UpdateService.Outdated)
		{
			ApplyTabVisibility();
		}
	}

	private void OnUpdatesChanged()
	{
		ApplyTabVisibility();
		MaybeShowUpdateToast();
	}

	private void MaybeShowUpdateToast()
	{
		if (!UpdateService.UpdateAvailable || _updating || UpdateService.Downloading || UpdateService.ReadyExe != null)
		{
			return;
		}
		if (_updateToastShown && !UpdateService.Outdated)
		{
			DateTime? updateRemindAt = Settings.Current.UpdateRemindAt;
			if (updateRemindAt.HasValue)
			{
				DateTime valueOrDefault = updateRemindAt.GetValueOrDefault();
				if (DateTime.UtcNow < valueOrDefault)
				{
					return;
				}
			}
		}
		IslandState? state = _state;
		bool flag;
		if (state.HasValue)
		{
			IslandState valueOrDefault2 = state.GetValueOrDefault();
			if ((uint)(valueOrDefault2 - 3) <= 1u)
			{
				flag = true;
				goto IL_007f;
			}
		}
		flag = false;
		goto IL_007f;
		IL_007f:
		if (!flag && !KeepOpen)
		{
			_updateToastShown = true;
			Settings.Current.UpdateRemindAt = DateTime.UtcNow + RemindEvery;
			Settings.Current.Save();
			Version version = UpdateService.Latest.Version;
			PetReact("update");
			ShowToast("\ue896", $"Вышла Island {version.Major}.{version.Minor}.{version.Build}", (double?)null, "", FillCharging, ((string Text, Action Run)?)("Обновить", StartUpdate), 12000, false, new ToastExtra(UpdateService.Outdated ? "Эта версия устарела — нужно обновиться" : "Скачается в фоне, настройки останутся", ((string Text, Action Run)?)("Позже", () =>
			{
				Settings.Current.UpdateRemindAt = DateTime.UtcNow + RemindEvery;
				Settings.Current.Save();
			}), ToastAnim.Connect, (string?)null, (ImageSource?)null, false));
		}
	}

	private void UpdateNow_Click(object sender, RoutedEventArgs e)
	{
		StartUpdate();
	}

	public void InstallUpdate()
	{
		StartUpdate();
	}

	private void UpdatePage_Click(object sender, RoutedEventArgs e)
	{
		Launch(UpdateService.Latest?.PageUrl ?? "https://github.com/qxezecwry/Island/releases/latest");
	}

	private async void StartUpdate()
	{
		if (_updating)
		{
			return;
		}
		if (UpdateService.ReadyExe != null)
		{
			RestartIntoUpdate();
			return;
		}
		if (UpdateService.Downloading)
		{
			ShowToast("", "Обновление скачивается", UpdateService.DownloadProgress, $"{Math.Round(UpdateService.DownloadProgress * 100.0)}%", FillCharging, null, 2500);
			return;
		}
		if (UpdateService.Latest?.ZipUrl == null)
		{
			await UpdateService.CheckAsync();
		}
		if (!UpdateService.CanSelfUpdate || UpdateService.Latest?.ZipUrl == null)
		{
			UpdatePage_Click(this, new RoutedEventArgs());
			return;
		}
		_hover = false;
		ShowToast("", "Скачиваю обновление", null, "", FillCharging, null, 3500, symbolFont: false, new ToastExtra("Островом можно пользоваться, я скажу, когда всё будет готово"));
		try
		{
			await UpdateService.StartDownload();
		}
		catch (Exception ex)
		{
			Log.Error("update download", ex);
			ShowToast("", "Не удалось скачать обновление", (double?)null, "", FillLow, ((string Text, Action Run)?)("Скачать вручную", () =>
			{
				UpdatePage_Click(this, new RoutedEventArgs());
			}), 9000, false, new ToastExtra("Новую версию можно скачать со страницы релиза"));
			return;
		}
		ShowUpdateReadyToast();
	}

	private void OnDownloadChanged()
	{
		TextBlock updatePanelText = UpdatePanelText;
		string text;
		if (UpdateService.ReadyExe != null)
		{
			text = "Новая версия скачана — нажмите «Обновить», Island перезапустится";
		}
		else
		{
			text = (UpdateService.Downloading ? $"Скачивается новая версия: {Math.Round(UpdateService.DownloadProgress * 100.0)}%. Островом можно пользоваться" : "Чтобы пользоваться островом дальше, установите новую — это займёт минуту, настройки сохранятся");
		}
		updatePanelText.Text = text;
	}

	private void ShowUpdateReadyToast()
	{
		Version? readyVersion = UpdateService.ReadyVersion;
		if ((object)readyVersion == null || _updating)
		{
			return;
		}
		IslandState? state = _state;
		bool flag;
		if (state.HasValue)
		{
			IslandState valueOrDefault = state.GetValueOrDefault();
			if ((uint)(valueOrDefault - 3) <= 1u)
			{
				flag = true;
				goto IL_0036;
			}
		}
		flag = false;
		goto IL_0036;
		IL_0036:
		if (flag || KeepOpen)
		{
			_readyToastPending = true;
			return;
		}
		_readyToastPending = false;
		ShowToast("", $"Island {readyVersion.Major}.{readyVersion.Minor}.{readyVersion.Build} скачана", (double?)null, "", FillCharging, ((string Text, Action Run)?)("Перезапустить", RestartIntoUpdate), 15000, false, new ToastExtra("Перезапуск займёт пару секунд. Или она установится сама, когда Island закроется", ((string Text, Action Run)?)("Позже", () =>
		{
		}), ToastAnim.Done, (string?)null, (ImageSource?)null, false));
	}

	private void RestartIntoUpdate()
	{
		if (_updating)
		{
			return;
		}
		try
		{
			_updating = true;
			string text = UpdateService.Apply();
			Log.Write($"Updating to {UpdateService.ReadyVersion}: restarting");
			((App)System.Windows.Application.Current).ReleaseInstanceLock();
			ReviewSafety.Start(new ProcessStartInfo(text, "--updated")
			{
				UseShellExecute = true,
				WorkingDirectory = System.IO.Path.GetDirectoryName(text)
			});
			System.Windows.Application.Current.Shutdown();
		}
		catch (Exception ex)
		{
			Log.Error("update", ex);
			_updating = false;
			ShowToast("", "Не удалось обновиться", (double?)null, "", FillLow, ((string Text, Action Run)?)("Скачать вручную", () =>
			{
				UpdatePage_Click(this, new RoutedEventArgs());
			}), 9000, false, new ToastExtra("Новую версию можно скачать со страницы релиза"));
		}
	}

	private void ApplyUpdateOnExit()
	{
		if (_updating || UpdateService.ReadyExe == null)
		{
			return;
		}
		try
		{
			UpdateService.Apply();
			Log.Write($"Update to {UpdateService.ReadyVersion} put in place on exit");
		}
		catch (Exception ex)
		{
			Log.Error("update on exit", ex);
		}
	}

	private static double RadiusFor(double h, double w)
	{
		return Math.Min(Math.Min(h / 2.0, Math.Clamp(Pro ? Settings.Current.CornerRadius : 0.75, 0.1, 1.0) * 48.0), w / 2.0);
	}

	private static bool TabLocked(int index)
	{
		bool flag = !Pro;
		if (flag)
		{
			bool flag2 = ((index == 3 || (uint)(index - 5) <= 1u) ? true : false);
			flag = flag2;
		}
		return flag;
	}

	public MainWindow()
	{
		InitializeComponent();
		if (DeveloperBranding.WorkspaceReview || Social.SocialBuild.TestWindow)
		{
			ShowInTaskbar = true;
			ShowActivated = true;
			Title = "Island — " + DeveloperBranding.CardTitle + " (UI test)";
			Settings.Current.HideInFullscreen = false;
		}
		_panels = new FrameworkElement[7] { IdlePanel, CompactPanel, MiniPanel, ActivityPanel, ExpandedPanel, ToastPanel, GamePanel };
		_tabPanels = new FrameworkElement[11]
		{
			HomePanel, ShelfPanel, ClipPanel, TextPanel, CalendarPanel, WeatherPanel, AiPanel, PhonePanel, MonitorPanel, ProPanel,
			UpdatePanel
		};
		_tabButtons = new System.Windows.Controls.Button[9] { TabHome, TabShelf, TabClip, TabText, TabCalendar, TabWeather, TabAi, TabPhone, TabMonitor };
		_tabLabels = new TextBlock[9] { TabHomeLabel, TabShelfLabel, TabClipLabel, TabTextLabel, TabCalendarLabel, TabWeatherLabel, TabAiLabel, TabPhoneLabel, TabMonitorLabel };
		InitTabBadges();
		_compactBars = new System.Windows.Shapes.Rectangle[4] { CBar1, CBar2, CBar3, CBar4 };
		_expandedBars = new System.Windows.Shapes.Rectangle[4] { EBar1, EBar2, EBar3, EBar4 };
		FrameworkElement[] panels = _panels;
		foreach (FrameworkElement obj in panels)
		{
			obj.RenderTransformOrigin = new System.Windows.Point(0.5, 0.0);
			obj.RenderTransform = new TransformGroup
			{
				Children = 
				{
					(Transform)new ScaleTransform(0.94, 0.94),
					(Transform)new TranslateTransform(0.0, -4.0)
				}
			};
		}
		panels = _tabPanels;
		foreach (FrameworkElement obj2 in panels)
		{
			obj2.Opacity = 0.0;
			obj2.IsHitTestVisible = false;
			obj2.Visibility = Visibility.Collapsed;
			obj2.RenderTransform = new TranslateTransform();
		}
		panels = _panels;
		for (int num = 0; num < panels.Length; num++)
		{
			panels[num].Visibility = Visibility.Collapsed;
		}
		if (Island.Services.License.BotUrl == null)
		{
			ProBuyButton.Visibility = Visibility.Collapsed;
		}
		System.Windows.Shapes.Rectangle[] compactBars = _compactBars;
		for (int num = 0; num < compactBars.Length; num++)
		{
			compactBars[num].RenderTransform = new ScaleTransform(1.0, 0.3);
		}
		compactBars = _expandedBars;
		for (int num = 0; num < compactBars.Length; num++)
		{
			compactBars[num].RenderTransform = new ScaleTransform(1.0, 0.3);
		}
		ApplyAccent(MediaService.DefaultAccent);
		UpdateTabButtons(instant: true);
		ClipList.ItemsSource = _clipboard.Items;
		_clipboard.Items.CollectionChanged += (object? _, NotifyCollectionChangedEventArgs _) =>
		{
			UpdateClipEmpty();
		};
		_clipboard.Load();
		UpdateClipEmpty();
		InitShelfAndPins();
		InitText();
		InitControls();
		ApplyPlayerLayout();
		InitMarquee();
		InitVisualizer();
		InitCalendar();
		InitLiquid();
		InitActivity();
		InitActions();
		InitAi();
		InitTabOrder();
		InitUpdates();
		InitHotkey();
		InitHalloween();
		InitPhone();
		InitSocial();
		InitPet();
		PetDemo();
		ApplyTabVisibility();
		ShowWelcomeTip();
		OpenDebugTab();
		OfferFirewall();
		Settings.Changed += ApplySettings;
		SourceInitialized += (object? _, EventArgs _) =>
		{
			_hwnd = new WindowInteropHelper(this).Handle;
			if (!DeveloperBranding.WorkspaceReview && !Social.SocialBuild.TestWindow) Win32.MakeOverlay(_hwnd);
			_clipboard.Init(_hwnd);
		};
		Loaded += OnLoaded;
		Deactivated += (object? _, EventArgs _) =>
		{
			EndTyping(restoreFocus: false);
		};
		Closed += (object? _, EventArgs _) =>
		{
			SaveNotesNow();
			RemoveHotkeyHook();
			_volume.Dispose();
			_bluetooth.Dispose();
			_clipboard.Dispose();
			_downloads.Dispose();
		};
		Pill.SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			OnPillSizeChanged();
		};
		_toastTimer.Tick += (object? _, EventArgs _) =>
		{
			_toastTimer.Stop();
			if (_phoneCardShown)
			{
				if (PhoneCardHeld)
				{
					_toastTimer.Start();
				}
				else
				{
					PhoneCardDone();
				}
			}
			else
			{
				_toastAction = (_toastAction2 = null);
				_toastInteractive = false;
				if (!ShowNextPhone())
				{
					ApplyState(BaseState());
				}
			}
		};
		_hoverTimer.Tick += (object? _, EventArgs _) =>
		{
			OnHoverSettled();
		};
		_tickTimer.Tick += (object? _, EventArgs _) =>
		{
			OnTick();
		};
		_weatherTimer.Tick += async (object? _, EventArgs _) =>
		{
			await _weather.RefreshAsync();
		};
		_fullscreenTimer.Tick += (object? _, EventArgs _) =>
		{
			UpdateGameMode();
			UpdateHidden();
			UpdateMonitorWatch();
		};
		SystemEvents.DisplaySettingsChanged += (object? _, EventArgs _) =>
		{
			Dispatcher.BeginInvoke(new Action(PositionWindow));
		};
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		PositionWindow();
		PlayEntrance();
		_fullscreenTimer.Start();
		_tickTimer.Start();
		_weatherTimer.Start();
		_volume.VolumeChanged += OnVolumeChanged;
		_volume.OutputDeviceChanged += (string name) =>
		{
			if (Settings.Current.ToastDevices && (DateTime.Now - _bluetoothToastAt).TotalSeconds > 5.0)
			{
				ShowToast(DeviceGlyph(name), name, _volume.Muted ? 0f : _volume.Level, $"{Math.Round(_volume.Level * 100f)}", FillNormal, null, 3000, symbolFont: false, new ToastExtra("Звук теперь здесь · колёсико — громкость", null, ToastAnim.Connect, null, null, Volume: true));
			}
		};
		_bluetooth.ConnectionChanged += (string name, bool on) =>
		{
			if (Settings.Current.ToastDevices)
			{
				_bluetoothToastAt = DateTime.Now;
				OnBluetoothChanged(name, on);
			}
		};
		try
		{
			_bluetooth.Init();
		}
		catch (Exception ex)
		{
			Log.Error("bluetooth init", ex);
		}
		_power.Changed += OnPowerChanged;
		_media.Changed += OnMediaChanged;
		_weather.Changed += OnWeatherChanged;
		try
		{
			_volume.Init();
		}
		catch (Exception ex2)
		{
			Log.Error("volume init", ex2);
		}
		InitOutputs();
		InitMonitor();
		InitGameMode();
		InitDetach();
		try
		{
			_power.Init();
		}
		catch (Exception ex3)
		{
			Log.Error("power init", ex3);
		}
		UpdateIdle();
		ApplyState(IslandState.Idle);
		try
		{
			await _media.InitAsync();
		}
		catch (Exception ex4)
		{
			Log.Error("media init", ex4);
		}
		await _controls.InitAsync();
		OnNetworkMaybeChanged();
		await _weather.RefreshAsync();
		await _calendar.RefreshAsync();
		if (DeveloperBranding.WorkspaceReview)
		{
			_hover = true;
			ApplyState(IslandState.Expanded);
			Activate();
		}
	}

	private void OnTick()
	{
		if (_state == IslandState.Idle)
		{
			UpdateIdle();
		}
		else if (_state == IslandState.CompactMedia && MiniPill)
		{
			UpdateMini();
		}
		else if (MediaShown)
		{
			UpdateProgress();
		}
		else if (HomeShown)
		{
			UpdateInfo();
		}
		if (++_ticks % 120 == 0 && _shownTab == CalendarPanel && _state == IslandState.Expanded)
		{
			RenderCalendar();
		}
		if (_ticks % 4 == 0)
		{
			PollBrowserAudio();
		}
		if (_ticks % 10 == 0)
		{
			UpdateTabBadges();
		}
		if (_ticks % 2 == 0)
		{
			KeepOnTop();
		}
		if (_state == IslandState.Expanded && _hover && !KeepOpen && !_hoverTimer.IsEnabled && !IsCursorOverPill())
		{
			SetHover(value: false, 0);
		}
	}

	private bool IsCursorOverPill()
	{
		if (!Win32.GetCursorPos(out var pt) || PresentationSource.FromVisual(Pill) == null)
		{
			return false;
		}
		System.Windows.Point point = Pill.PointToScreen(new System.Windows.Point(0.0, 0.0));
		System.Windows.Point point2 = Pill.PointToScreen(new System.Windows.Point(Pill.ActualWidth, Pill.ActualHeight));
		if ((double)pt.X >= point.X && (double)pt.X <= point2.X && (double)pt.Y >= point.Y)
		{
			return (double)pt.Y <= point2.Y;
		}
		return false;
	}

	private IslandState BaseState()
	{
		if (ToastHeld)
		{
			return IslandState.Toast;
		}
		if (_hover || KeepOpen)
		{
			return IslandState.Expanded;
		}
		if (_gameActive)
		{
			return IslandState.Game;
		}
		if (DownloadsActive && (!TwoIslands || !MusicOnCollapsed))
		{
			return IslandState.Activity;
		}
		if (!MusicOnCollapsed)
		{
			return IslandState.Idle;
		}
		return IslandState.CompactMedia;
	}

	private FrameworkElement CurrentTabPanel()
	{
		if (UpdateService.Outdated)
		{
			return UpdatePanel;
		}
		if (!TabLocked(_tab))
		{
			return _tabPanels[_tab];
		}
		ProTitle.Text = DeveloperBranding.IsDeveloper ? "island killed · «" + TabNames[_tab] + "» требует Pro" : "«" + TabNames[_tab] + "» — в Island Pro";
		return ProPanel;
	}

	private double TabHeight(FrameworkElement panel)
	{
		if (panel != HomePanel)
		{
			return panel.Height;
		}
		if (!_homeHasMedia)
		{
			return InfoPanel.Height + 60.0;
		}
		return MediaPanel.Height;
	}

	private void ApplyState(IslandState s)
	{
		if (s == _state)
		{
			return;
		}
		IslandState? state = _state;
		bool flag;
		if (state.HasValue)
		{
			IslandState valueOrDefault = state.GetValueOrDefault();
			if ((uint)(valueOrDefault - 3) <= 1u)
			{
				flag = true;
				goto IL_0044;
			}
		}
		flag = false;
		goto IL_0044;
		IL_00c0:
		_state = s;
		if (s != IslandState.Expanded)
		{
			UpdateTip();
		}
		FrameworkElement target;
		double num2;
		double num3;
		double num7;
		switch (s)
		{
		case IslandState.CompactMedia:
			if (!MiniPill || (_miniLyrics = MiniLyrics))
			{
				target = CompactPanel;
				num2 = 34.0;
				num3 = 300.0;
				break;
			}
			goto IL_0123;
		case IslandState.Idle:
			if (MiniPill)
			{
				goto IL_0123;
			}
			goto default;
		case IslandState.Activity:
			target = ActivityPanel;
			num2 = 34.0;
			num3 = 340.0;
			break;
		case IslandState.Toast:
		{
			double num5 = ToastPanel.Width + 2.0;
			double num6 = ToastPanel.Height + 2.0;
			target = ToastPanel;
			num2 = num6;
			num3 = num5;
			break;
		}
		case IslandState.Game:
		{
			double num4 = GameWidth();
			target = GamePanel;
			num2 = 44.0;
			num3 = num4;
			break;
		}
		case IslandState.Expanded:
		{
			FrameworkElement frameworkElement = CurrentTabPanel();
			ShowTabPanel(frameworkElement, 0, instant: true);
			double num = 46.0 + TabHeight(frameworkElement);
			target = ExpandedPanel;
			num2 = num;
			num3 = 400.0;
			Dispatcher.BeginInvoke((Action)(() =>
			{
				SyncTabIndicator();
				if (MediaShown)
				{
					UpdateProgress();
				}
			}), DispatcherPriority.Loaded);
			break;
		}
		default:
			{
				target = IdlePanel;
				num2 = 34.0;
				num3 = 252.0;
				break;
			}
			IL_0123:
			num7 = MiniWidth();
			target = MiniPanel;
			num2 = 34.0;
			num3 = num7;
			break;
		}
		RefreshVisibleContent();
		UpdateMarqueeSoon();
		Resize(num3, num2);
		CrossFade(target, num3 * num2 > Pill.ActualWidth * Pill.ActualHeight);
		UpdateEqualizers();
		UpdateLyricsTimer();
		StartActivityAnimation(s == IslandState.Activity);
		UpdateBubble();
		ApplyStateScale(instant: false);
		SyncBackdrop();
		if (s == IslandState.Expanded && Pro && Settings.Current.WaterEffects && Settings.Current.RimAnimation)
		{
			StartAmbient();
		}
		return;
		IL_00b2:
		_tab = 0;
		UpdateTabButtons(instant: true);
		goto IL_00c0;
		IL_0044:
		if (_state == IslandState.Expanded && s != IslandState.Toast)
		{
			ShowDeferredSoon();
		}
		if (s == IslandState.Expanded && _tab != 0 && !_dragIn)
		{
			string openTab = Settings.Current.OpenTab;
			if (openTab == "home")
			{
				goto IL_00b2;
			}
			if (openTab == "music")
			{
				Island.Services.MediaState? current = _media.Current;
				if ((object)current != null && current.IsPlaying)
				{
					goto IL_00b2;
				}
			}
		}
		goto IL_00c0;
	}

	private void Resize(double w, double h)
	{
		h = WithTip(h);
		bool growing = w * h > Pill.ActualWidth * Pill.ActualHeight;
		AnimateSize(Pill, w, h, AnimationStyle, growing, StartRipple);
	}

	internal static void AnimateSize(FrameworkElement target, double w, double h, string style, bool growing, Action<double>? ripple)
	{
		switch (style)
		{
		case "spring":
			Animate(target, FrameworkElement.HeightProperty, h, 700, new SpringEase
			{
				Damping = (growing ? 0.5 : 0.62),
				Stiffness = 11.0
			});
			Animate(target, FrameworkElement.WidthProperty, w, 700, new SpringEase
			{
				Damping = (growing ? 0.5 : 0.62),
				Stiffness = 11.0
			});
			return;
		case "smooth":
		{
			CubicEase ease3 = new CubicEase
			{
				EasingMode = EasingMode.EaseInOut
			};
			Animate(target, FrameworkElement.HeightProperty, h, 380, ease3);
			Animate(target, FrameworkElement.WidthProperty, w, 380, ease3);
			return;
		}
		case "jelly":
			Animate(target, FrameworkElement.HeightProperty, h, 900, new SpringEase
			{
				Damping = (growing ? 0.38 : 0.6),
				Stiffness = 13.0
			});
			Animate(target, FrameworkElement.WidthProperty, w, 900, new SpringEase
			{
				Damping = (growing ? 0.42 : 0.6),
				Stiffness = 12.0
			}, 70);
			return;
		case "stretch":
			if (growing)
			{
				Animate(target, FrameworkElement.WidthProperty, w, 520, new SpringEase
				{
					Damping = 0.7,
					Stiffness = 11.0
				});
				Animate(target, FrameworkElement.HeightProperty, h, 620, new SpringEase
				{
					Damping = 0.62,
					Stiffness = 10.0
				}, 140);
			}
			else
			{
				Animate(target, FrameworkElement.HeightProperty, h, 420, new SpringEase
				{
					Damping = 0.85,
					Stiffness = 12.0
				});
				Animate(target, FrameworkElement.WidthProperty, w, 460, new SpringEase
				{
					Damping = 0.8,
					Stiffness = 11.0
				}, 110);
			}
			return;
		case "bounce":
			if (growing)
			{
				Animate(target, FrameworkElement.HeightProperty, h, 720, new BounceEase
				{
					Bounces = 2,
					Bounciness = 3.2,
					EasingMode = EasingMode.EaseOut
				});
				Animate(target, FrameworkElement.WidthProperty, w, 560, new SpringEase
				{
					Damping = 0.72,
					Stiffness = 11.0
				}, 30);
			}
			else
			{
				BackEase ease4 = new BackEase
				{
					Amplitude = 0.35,
					EasingMode = EasingMode.EaseIn
				};
				Animate(target, FrameworkElement.WidthProperty, w, 320, ease4);
				Animate(target, FrameworkElement.HeightProperty, h, 360, ease4, 40);
			}
			return;
		case "elastic":
		{
			IEasingFunction easingFunction2;
			if (!growing)
			{
				IEasingFunction easingFunction = new SpringEase
				{
					Damping = 0.65,
					Stiffness = 12.0
				};
				easingFunction2 = easingFunction;
			}
			else
			{
				IEasingFunction easingFunction = new ElasticEase
				{
					Oscillations = 2,
					Springiness = 5.0,
					EasingMode = EasingMode.EaseOut
				};
				easingFunction2 = easingFunction;
			}
			IEasingFunction ease2 = easingFunction2;
			Animate(target, FrameworkElement.HeightProperty, h, growing ? 900 : 600, ease2);
			Animate(target, FrameworkElement.WidthProperty, w, growing ? 900 : 600, ease2, 40);
			return;
		}
		case "snap":
		{
			BackEase ease = new BackEase
			{
				Amplitude = 0.25,
				EasingMode = EasingMode.EaseOut
			};
			Animate(target, FrameworkElement.HeightProperty, h, 230, ease);
			Animate(target, FrameworkElement.WidthProperty, w, 230, ease);
			return;
		}
		case "bubble":
			Animate(target, FrameworkElement.HeightProperty, h, 640, new SpringEase
			{
				Damping = (growing ? 0.32 : 0.6),
				Stiffness = 16.0
			});
			Animate(target, FrameworkElement.WidthProperty, w, 640, new SpringEase
			{
				Damping = (growing ? 0.32 : 0.6),
				Stiffness = 16.0
			});
			if (growing)
			{
				ripple?.Invoke(2.5);
			}
			return;
		}
		if (growing)
		{
			Animate(target, FrameworkElement.HeightProperty, h, 600, new SpringEase
			{
				Damping = 0.68,
				Stiffness = 10.0
			});
			Animate(target, FrameworkElement.WidthProperty, w, 620, new SpringEase
			{
				Damping = 0.74,
				Stiffness = 10.0
			}, 45);
		}
		else
		{
			Animate(target, FrameworkElement.WidthProperty, w, 440, new SpringEase
			{
				Damping = 0.92,
				Stiffness = 11.0
			});
			Animate(target, FrameworkElement.HeightProperty, h, 520, new SpringEase
			{
				Damping = 0.8,
				Stiffness = 11.0
			}, 45);
		}
		if (style == "wave")
		{
			ripple?.Invoke(growing ? 5.0 : 3.5);
		}
	}

	private void CrossFade(FrameworkElement target, bool growing)
	{
		FrameworkElement[] panels = _panels;
		foreach (FrameworkElement frameworkElement in panels)
		{
			bool flag = (frameworkElement.IsHitTestVisible = frameworkElement == target);
			TransformGroup transformGroup = (TransformGroup)frameworkElement.RenderTransform;
			ScaleTransform target2 = (ScaleTransform)transformGroup.Children[0];
			TranslateTransform target3 = (TranslateTransform)transformGroup.Children[1];
			if (flag)
			{
				bool flag3 = frameworkElement.Opacity < 0.05 || frameworkElement.Visibility != Visibility.Visible;
				frameworkElement.Visibility = Visibility.Visible;
				int delayMs = (growing ? 90 : 60);
				SpringEase ease = new SpringEase
				{
					Damping = 0.85,
					Stiffness = 10.0
				};
				Animate(frameworkElement, UIElement.OpacityProperty, 1.0, 260, new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}, delayMs);
				Animate(target2, ScaleTransform.ScaleXProperty, 1.0, 520, ease, delayMs, flag3 ? new double?(0.92) : ((double?)null));
				Animate(target2, ScaleTransform.ScaleYProperty, 1.0, 520, ease, delayMs, flag3 ? new double?(0.92) : ((double?)null));
				Animate(target3, TranslateTransform.YProperty, 0.0, 520, ease, delayMs, flag3 ? new double?(-6.0) : ((double?)null));
			}
			else
			{
				CubicEase ease2 = new CubicEase
				{
					EasingMode = EasingMode.EaseIn
				};
				FadeOutAndCollapse(frameworkElement, 120, ease2);
				Animate(target2, ScaleTransform.ScaleXProperty, 0.96, 160, ease2);
				Animate(target2, ScaleTransform.ScaleYProperty, 0.96, 160, ease2);
			}
		}
	}

	private static void FadeOutAndCollapse(FrameworkElement p, int ms, IEasingFunction ease)
	{
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds((!ReducedMotion) ? ms : 0))
		{
			EasingFunction = ease
		};
		doubleAnimation.Completed += (object? _, EventArgs _) =>
		{
			if (!p.IsHitTestVisible)
			{
				p.Visibility = Visibility.Collapsed;
			}
		};
		p.BeginAnimation(UIElement.OpacityProperty, doubleAnimation, HandoffBehavior.SnapshotAndReplace);
	}

	internal static void Animate(IAnimatable target, DependencyProperty prop, double to, int ms, IEasingFunction? ease, int delayMs = 0, double? from = null)
	{
		DoubleAnimation animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds((!ReducedMotion) ? ms : 0))
		{
			From = from,
			EasingFunction = ease,
			BeginTime = TimeSpan.FromMilliseconds((!ReducedMotion) ? delayMs : 0)
		};
		target.BeginAnimation(prop, animation, HandoffBehavior.SnapshotAndReplace);
	}

	private void Tab_Click(object sender, RoutedEventArgs e)
	{
		SelectTab(int.Parse((string)((System.Windows.Controls.Button)sender).Tag));
	}

	private void SelectTab(int index)
	{
		if (index == _tab)
		{
			return;
		}
		int direction = ((index > _tab) ? 1 : (-1));
		if (_typing)
		{
			EndTyping(restoreFocus: true);
		}
		Log.Write($"tab {_tab} -> {index}");
		_tab = index;
		UpdateTabButtons();
		FrameworkElement frameworkElement = CurrentTabPanel();
		if (_state == IslandState.Expanded && frameworkElement != _shownTab)
		{
			ShowTabPanel(frameworkElement, direction, instant: false);
			RefreshVisibleContent();
			UpdateMarqueeSoon();
			Resize(400.0, 46.0 + TabHeight(frameworkElement));
			UpdateEqualizers();
			if (frameworkElement == WeatherPanel && _weather.IsStale)
			{
				_weather.RefreshAsync();
			}
		}
	}

	private void ProKey_Click(object sender, RoutedEventArgs e)
	{
		((App)System.Windows.Application.Current).ShowSettings("license");
	}

	private void ProBuy_Click(object sender, RoutedEventArgs e)
	{
		string? buyUrl = Island.Services.License.BuyUrl;
		if (buyUrl != null)
		{
			ReviewSafety.Start(new ProcessStartInfo(buyUrl)
			{
				UseShellExecute = true
			});
		}
	}

	private void ShowTabPanel(FrameworkElement target, int direction, bool instant)
	{
		_shownTab = target;
		UpdateTip();
		FrameworkElement[] tabPanels = _tabPanels;
		foreach (FrameworkElement frameworkElement in tabPanels)
		{
			bool flag = (frameworkElement.IsHitTestVisible = frameworkElement == target);
			TranslateTransform translateTransform = (TranslateTransform)frameworkElement.RenderTransform;
			if (instant)
			{
				frameworkElement.BeginAnimation(UIElement.OpacityProperty, null);
				translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
				frameworkElement.Opacity = (flag ? 1 : 0);
				frameworkElement.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
				translateTransform.X = 0.0;
			}
			else if (flag)
			{
				frameworkElement.Visibility = Visibility.Visible;
				SpringEase ease = new SpringEase
				{
					Damping = 0.86,
					Stiffness = 10.0
				};
				Animate(frameworkElement, UIElement.OpacityProperty, 1.0, 240, new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}, 60);
				Animate(translateTransform, TranslateTransform.XProperty, 0.0, 480, ease, 60, (double)direction * 28.0);
			}
			else
			{
				CubicEase ease2 = new CubicEase
				{
					EasingMode = EasingMode.EaseIn
				};
				FadeOutAndCollapse(frameworkElement, 110, ease2);
				Animate(translateTransform, TranslateTransform.XProperty, (double)(-direction) * 20.0, 160, ease2);
			}
		}
		UpdateGlow(instant);
	}

	private void UpdateGlow(bool instant)
	{
		LiquidShape shape = Shape;
		DependencyProperty tintOpacityProperty = LiquidShape.TintOpacityProperty;
		double to;
		if (!_homeHasMedia || BackdropHidesTint)
		{
			to = 0.0;
		}
		else
		{
			to = (Pro ? Settings.Current.MusicTint : 0.4);
		}
		Animate(shape, tintOpacityProperty, to, (!instant) ? 300 : 0, new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		});
		SyncBackdrop();
	}

	private void ApplySettings()
	{
		ApplyTabVisibility();
		ApplyPlayerLayout();
		ApplyTabOrder();
		UpdateAiState();
		if (Settings.Current.ClipPersist == true)
		{
			_clipboard.Save();
		}
		else
		{
			ClipboardService.Forget();
		}
		UpdateGlow(instant: false);
		_lyricIndex = -2;
		_lyricShown = null;
		UpdateLyrics();
		RebuildDock();
		if (_weather.City != Settings.Current.WeatherCity)
		{
			_weather.SetCityAsync(Settings.Current.WeatherCity);
		}
		UpdateIdle();
		ApplyAppearance();
		UpdateGlowEffect();
		OnDownloadsChanged();
		if (_shownTab == CalendarPanel)
		{
			RenderCalendar();
		}
		string text = string.Join("\n", Settings.Current.CalendarFeeds);
		if (text != _appliedFeeds)
		{
			_appliedFeeds = text;
			_calendar.RefreshAsync();
		}
		ApplyHotkey();
		ApplyHalloween();
		ApplyPhone();
		ApplyPet();
		ApplyMiniPill();
		ApplyTwoIslands();
		IslandState? state = _state;
		bool flag;
		if (state.HasValue)
		{
			IslandState valueOrDefault = state.GetValueOrDefault();
			if ((uint)valueOrDefault <= 1u)
			{
				flag = true;
				goto IL_013c;
			}
		}
		flag = false;
		goto IL_013c;
		IL_013c:
		if (flag && !_toastTimer.IsEnabled)
		{
			ApplyState(BaseState());
		}
		if (MiniPill)
		{
			UpdateMini();
		}
		UpdateTabBadges();
		UpdateHidden();
	}

	private void ApplyTabVisibility()
	{
		for (int i = 1; i < TabIds.Length; i++)
		{
			bool flag = Settings.Current.IsTabHidden(TabIds[i]) || (i == 7 && !PhoneOn);
			_tabButtons[i].Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
			_tabButtons[i].Opacity = (TabLocked(i) ? 0.45 : 1.0);
		}
		if (_state == IslandState.Expanded && CurrentTabPanel() != _shownTab)
		{
			FrameworkElement frameworkElement = CurrentTabPanel();
			ShowTabPanel(frameworkElement, 0, instant: false);
			RefreshVisibleContent();
			Resize(400.0, 46.0 + TabHeight(frameworkElement));
		}
		if (Settings.Current.IsTabHidden("clip"))
		{
			_clipboard.Items.Clear();
		}
		if (Settings.Current.IsTabHidden(TabIds[_tab]) || (_tab == 7 && !PhoneOn))
		{
			SelectTab(0);
		}
		Dispatcher.BeginInvoke(new Action(SyncTabIndicator), DispatcherPriority.Loaded);
	}

	private void UpdateTabButtons(bool instant = false)
	{
		System.Windows.Media.Brush brush = (System.Windows.Media.Brush)FindResource("TextSecondary");
		for (int i = 0; i < _tabButtons.Length; i++)
		{
			_tabButtons[i].Foreground = ((i == _tab) ? FillNormal : brush);
		}
		AnimateTabs(instant);
	}

	private void RefreshVisibleContent()
	{
		if (_state == IslandState.Idle)
		{
			UpdateIdle();
		}
		if (_state == IslandState.Expanded)
		{
			UpdateTabBadges();
			if (MediaShown)
			{
				UpdateProgress();
			}
			else if (HomeShown)
			{
				UpdateInfo();
			}
			else if (_shownTab == ClipPanel)
			{
				ClipList.Items.Refresh();
			}
			else if (_shownTab == WeatherPanel)
			{
				UpdateWeather();
			}
			else if (_shownTab == CalendarPanel)
			{
				RenderCalendar();
			}
			else if (_shownTab == AiPanel)
			{
				UpdateAiState();
			}
			else if (_shownTab == PhonePanel)
			{
				RenderPhoneCenter();
			}
		}
	}

	private void UpdateHomeLayout()
	{
		bool flag = _media.Current != null;
		if (flag != _homeHasMedia)
		{
			_homeHasMedia = flag;
			MediaPanel.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
			InfoPanel.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
			PinsBar.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
			UpdateGlow(instant: false);
			if (HomeShown)
			{
				Resize(400.0, 46.0 + TabHeight(HomePanel));
				RefreshVisibleContent();
			}
		}
	}

	private void Pill_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (!OpenOnClick || _state == IslandState.Expanded || (_state == IslandState.Toast && _toastInteractive))
		{
			SetHover(value: true, Settings.Current.HoverDelayMs);
		}
	}

	private void Pill_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (OpenOnClick && _state != IslandState.Expanded && (_state != IslandState.Toast || !_toastInteractive))
		{
			SetHover(value: true, 0);
			e.Handled = true;
		}
	}

	private void Pill_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
	{
		SetHover(value: false, 260);
	}

	private void SetHover(bool value, int delayMs)
	{
		_pendingHover = value;
		_hoverTimer.Stop();
		if (_hover != value)
		{
			_hoverTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
			_hoverTimer.Start();
		}
	}

	private void OnHoverSettled()
	{
		_hoverTimer.Stop();
		_hover = _pendingHover;
		if (_state == IslandState.Toast && _toastInteractive)
		{
			if (_hover)
			{
				_toastTimer.Stop();
			}
			else
			{
				_toastTimer.Start();
			}
			PhoneCardHover(_hover);
			return;
		}
		if (_hover)
		{
			_toastTimer.Stop();
		}
		if (!_toastTimer.IsEnabled)
		{
			ApplyState(BaseState());
		}
	}

	private void ReleaseKeepOpen()
	{
		if (!KeepOpen)
		{
			if (IsCursorOverPill())
			{
				_hover = true;
			}
			else
			{
				SetHover(value: false, 200);
			}
			if (!_hoverTimer.IsEnabled)
			{
				ApplyState(BaseState());
			}
		}
	}

	private void UpdateIdle()
	{
		if (MiniPill)
		{
			UpdateMini();
		}
		DateTime now = DateTime.Now;
		string text = now.ToString("HH:mm");
		if (IdleClock.Text != text)
		{
			bool flag = IdleClock.Text.Length == 0;
			IdleClock.Text = text;
			if (!flag && !ReducedMotion)
			{
				TranslateTransform? translateTransform = IdleClock.RenderTransform as TranslateTransform;
				if (translateTransform == null)
				{
					translateTransform = new TranslateTransform();
					IdleClock.RenderTransform = translateTransform;
				}
				CubicEase ease = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				};
				Animate(translateTransform, TranslateTransform.YProperty, 0.0, 380, ease, 0, 9.0);
				Animate(IdleClock, UIElement.OpacityProperty, 1.0, 300, ease, 0, 0.0);
			}
		}
		WeatherNow? weatherNow = ((Pro && Settings.Current.IdleShowWeather) ? _weather.Current : null);
		IdleWeather.Visibility = ((!(weatherNow != null)) ? Visibility.Collapsed : Visibility.Visible);
		if (weatherNow != null)
		{
			var (text2, _, color) = WeatherService.Describe(weatherNow.Code, weatherNow.IsDay);
			SetText(IdleWeatherIcon, text2);
			IdleWeatherIcon.Foreground = Freeze(new SolidColorBrush(color));
			SetText(IdleWeatherText, WeatherService.FormatTemp(weatherNow.Temp));
		}
		bool idleShowBattery = Settings.Current.IdleShowBattery;
		IdleRightIcon.Visibility = ((!idleShowBattery) ? Visibility.Collapsed : Visibility.Visible);
		IdleRightText.Visibility = ((!idleShowBattery) ? Visibility.Collapsed : Visibility.Visible);
		IdleWeather.Margin = new Thickness(0.0, 0.0, idleShowBattery ? 10 : 0, 0.0);
		if (idleShowBattery)
		{
			if (_power.HasBattery)
			{
				int percent = _power.Percent;
				SetText(IdleRightIcon, BatteryGlyph(percent, _power.IsPluggedIn));
				TextBlock idleRightIcon = IdleRightIcon;
				System.Windows.Media.Brush foreground;
				if (_power.IsPluggedIn)
				{
					foreground = FillCharging;
				}
				else
				{
					foreground = ((percent <= 20) ? FillLow : FillNormal);
				}
				idleRightIcon.Foreground = foreground;
				SetText(IdleRightText, $"{percent}%");
			}
			else
			{
				SetText(IdleRightIcon, _volume.Muted ? "\ue74f" : "");
				SetText(IdleRightText, now.ToString("ddd, d MMM", Ru).Replace(".", ""));
			}
		}
	}

	private void UpdateInfo()
	{
		DateTime now = DateTime.Now;
		SetText(ClockText, now.ToString("HH:mm"));
		SetText(DateText, now.ToString("dddd, d MMMM", Ru));
		if (_power.HasBattery)
		{
			int percent = _power.Percent;
			InfoBattery.Visibility = Visibility.Visible;
			SetText(InfoBatteryIcon, BatteryGlyph(percent, _power.IsPluggedIn));
			TextBlock infoBatteryIcon = InfoBatteryIcon;
			System.Windows.Media.Brush foreground;
			if (_power.IsPluggedIn)
			{
				foreground = FillCharging;
			}
			else
			{
				foreground = ((percent <= 20) ? FillLow : FillNormal);
			}
			infoBatteryIcon.Foreground = foreground;
			SetText(InfoBatteryLabel, _power.IsPluggedIn ? "Зарядка" : "Батарея");
			SetText(InfoBatteryText, $"{percent}%");
		}
		else
		{
			InfoBattery.Visibility = Visibility.Collapsed;
		}
		SetText(InfoAudioIcon, _volume.Muted ? "\ue74f" : DeviceGlyph(_volume.DeviceName));
		SetText(InfoAudioLabel, string.IsNullOrEmpty(_volume.DeviceName) ? "Звук" : _volume.DeviceName);
		SetText(InfoAudioText, _volume.Muted ? "выкл" : $"{Math.Round(_volume.Level * 100f)}%");
	}

	private static void SetText(TextBlock tb, string text)
	{
		if (tb.Text != text)
		{
			tb.Text = text;
		}
	}

	private void PlayerSwitch_Click(object sender, RoutedEventArgs e)
	{
		_media.SwitchPlayer();
		Pop(PlayerSwitch, 0.85);
	}

	private void OnMediaChanged()
	{
		Island.Services.MediaState? current = _media.Current;
		if (current != null)
		{
			string text = (string.IsNullOrEmpty(current.Title) ? "Неизвестный трек" : current.Title);
			TitleText.Text = text;
			ArtistText.Text = current.Artist;
			if (!LyricsActive || !LyricsInCompact)
			{
				CompactTitle.Text = text;
			}
			if (DateTime.Now >= _offsetShownUntil)
			{
				if (!LyricsActive)
				{
					ShowTitleLarge(current);
				}
				else
				{
					ShowTitleSmall();
				}
			}
			int playerCount = _media.PlayerCount;
			PlayerSwitch.Visibility = ((playerCount <= 1) ? Visibility.Collapsed : Visibility.Visible);
			PlayerSwitch.Tag = $"{_media.PlayerIndex + 1} из {playerCount}";
			PlayerSwitch.ToolTip = "Сейчас: " + current.SourceApp + ". Нажмите — следующий плеер";
			CoverBrush.ImageSource = current.Cover;
			CompactCoverBrush.ImageSource = current.Cover;
			MiniCoverBrush.ImageSource = current.Cover;
			string text2 = (current.IsPlaying ? "\ue769" : "\ue768");
			if (PlayPauseIcon.Text != text2)
			{
				PlayPauseIcon.Text = text2;
				Pop(PlayPauseIcon, 0.5);
			}
			ApplyAccent(current.Accent);
			SetBackdropCover(current.Cover, _accent);
			bool flag = !_media.IsBrowserFallback || _media.BrowserControllable;
			PlayerButtons.IsEnabled = flag;
			PlayerButtons.Opacity = (flag ? 1.0 : 0.3);
			PlayerButtons.ToolTip = (flag ? null : "Этот браузер не даёт управлять музыкой со стороны. Кнопки работают с ВК, пока его вкладка открыта");
			SetSkipButton(PrevButton, current.CanPrevious, "Предыдущий", current.SourceApp + ": это первый трек в списке");
			SetSkipButton(NextButton, current.CanNext, "Следующий", current.SourceApp + ": дальше в списке треков нет");
			OnTrackMaybeChanged(current);
			OnTrackForStore(current);
			AnimateTrackChange(current);
		}
		UpdateHomeLayout();
		UpdateMarqueeSoon();
		if (MediaShown)
		{
			UpdateProgress();
		}
		if (!_toastTimer.IsEnabled)
		{
			ApplyState(BaseState());
		}
		UpdateEqualizers();
		UpdateLyricsTimer();
		UpdateLyrics();
		UpdateGlowEffect();
	}

	private void ApplyAccent(System.Windows.Media.Color c, bool force = false)
	{
		if (Pro)
		{
			System.Windows.Media.Color? color = ParseColor(Settings.Current.Accent);
			if (color.HasValue)
			{
				System.Windows.Media.Color valueOrDefault = color.GetValueOrDefault();
				c = valueOrDefault;
			}
		}
		if (!(c == _accent) || force)
		{
			_accent = c;
			UpdateGlowEffect();
			SolidColorBrush solidColorBrush = Freeze(new SolidColorBrush(c));
			System.Windows.Shapes.Rectangle[] compactBars = _compactBars;
			for (int i = 0; i < compactBars.Length; i++)
			{
				compactBars[i].Fill = solidColorBrush;
			}
			compactBars = _expandedBars;
			for (int i = 0; i < compactBars.Length; i++)
			{
				compactBars[i].Fill = solidColorBrush;
			}
			ProgressFill.Background = solidColorBrush;
			MusicVolume.Fill = solidColorBrush;
			ApplyCoverGlow(c);
			RadialGradientBrush f = new RadialGradientBrush
			{
				Center = new System.Windows.Point(0.1, 0.05),
				GradientOrigin = new System.Windows.Point(0.1, 0.05),
				RadiusX = 0.95,
				RadiusY = 1.1,
				GradientStops = 
				{
					new GradientStop(System.Windows.Media.Color.FromArgb(128, c.R, c.G, c.B), 0.0),
					new GradientStop(System.Windows.Media.Color.FromArgb(0, c.R, c.G, c.B), 1.0)
				}
			};
			Shape.Tint = Freeze(f);
		}
	}

	private void UpdateProgress()
	{
		Island.Services.MediaState? current = _media.Current;
		if (current == null)
		{
			ProgressRow.Visibility = Visibility.Hidden;
			return;
		}
		ProgressRow.Visibility = Visibility.Visible;
		UpdateMusicVolume(current);
		TimeSpan timeSpan = current.Position;
		if (current.IsPlaying)
		{
			timeSpan += DateTimeOffset.Now - current.PositionUpdatedAt;
		}
		if (timeSpan < TimeSpan.Zero)
		{
			timeSpan = TimeSpan.Zero;
		}
		bool flag = current.Duration > TimeSpan.Zero;
		ProgressFill.Opacity = (flag ? 1.0 : 0.3);
		ProgressHit.Cursor = (flag ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow);
		Grid progressHit = ProgressHit;
		object toolTip;
		if (flag)
		{
			toolTip = ((_seekRefused != null && _seekRefused == current.SourceId) ? (current.SourceApp + " не даёт перематывать со стороны") : null);
		}
		else
		{
			toolTip = "Плеер не сообщает длину трека — перемотка недоступна";
		}
		progressHit.ToolTip = toolTip;
		if (!flag)
		{
			SetText(ElapsedText, Format(timeSpan));
			SetText(RemainingText, "");
			ProgressFill.BeginAnimation(FrameworkElement.WidthProperty, null);
			ProgressFill.Width = ProgressTrack.ActualWidth;
			return;
		}
		if (timeSpan > current.Duration)
		{
			timeSpan = current.Duration;
		}
		if (!_scrub.HasValue)
		{
			SetText(ElapsedText, Format(timeSpan));
			SetText(RemainingText, "-" + Format(current.Duration - timeSpan));
			ProgressFill.BeginAnimation(FrameworkElement.WidthProperty, null);
			ProgressFill.Width = ProgressTrack.ActualWidth * (timeSpan.TotalSeconds / current.Duration.TotalSeconds);
		}
	}

	private static string Format(TimeSpan t)
	{
		if (!(t.TotalHours >= 1.0))
		{
			return t.ToString("m\\:ss");
		}
		return t.ToString("h\\:mm\\:ss");
	}

	private void UpdateEqualizers()
	{
		UpdateMusicMotion();
	}

	private static void SetSkipButton(System.Windows.Controls.Button button, bool can, string tip, string why)
	{
		button.IsEnabled = can;
		button.Opacity = (can ? 1.0 : 0.35);
		button.ToolTip = (can ? tip : why);
	}

	private async void Prev_Click(object sender, RoutedEventArgs e)
	{
		NudgeCover(-1);
		await _media.PreviousAsync();
	}

	private async void Next_Click(object sender, RoutedEventArgs e)
	{
		NudgeCover(1);
		await _media.NextAsync();
	}

	private async void PlayPause_Click(object sender, RoutedEventArgs e)
	{
		await _media.PlayPauseAsync();
	}

	private double ScrubFraction(System.Windows.Input.MouseEventArgs e)
	{
		if (!(ProgressTrack.ActualWidth > 0.0))
		{
			return 0.0;
		}
		return Math.Clamp(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, 0.0, 1.0);
	}

	private void Progress_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		Island.Services.MediaState? current = _media.Current;
		if ((object)current != null && current.Duration.Ticks > 0)
		{
			_scrub = ScrubFraction(e);
			ProgressHit.CaptureMouse();
			ShowScrub();
			e.Handled = true;
		}
	}

	private void Progress_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (_scrub.HasValue)
		{
			_scrub = ScrubFraction(e);
			ShowScrub();
		}
	}

	private async void Progress_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		double? scrub = _scrub;
		if (scrub.HasValue)
		{
			double valueOrDefault = scrub.GetValueOrDefault();
			_scrub = null;
			ProgressHit.ReleaseMouseCapture();
			if (!(await _media.SeekAsync(valueOrDefault)))
			{
				_seekRefused = _media.Current?.SourceId;
				ProgressFill.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(260L))
				{
					AutoReverse = false,
					RepeatBehavior = new RepeatBehavior(2.0)
				});
				UpdateProgress();
			}
		}
	}

	private void Progress_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
	{
		_scrub = null;
	}

	private void ShowScrub()
	{
		double? scrub = _scrub;
		if (scrub.HasValue)
		{
			double valueOrDefault = scrub.GetValueOrDefault();
			Island.Services.MediaState? current = _media.Current;
			if ((object)current != null && current.Duration.Ticks > 0)
			{
				TimeSpan timeSpan = TimeSpan.FromTicks((long)((double)current.Duration.Ticks * valueOrDefault));
				ProgressFill.BeginAnimation(FrameworkElement.WidthProperty, null);
				ProgressFill.Width = ProgressTrack.ActualWidth * valueOrDefault;
				SetText(ElapsedText, Format(timeSpan));
				SetText(RemainingText, "-" + Format(current.Duration - timeSpan));
			}
		}
	}

	private async void ClipItem_Click(object sender, RoutedEventArgs e)
	{
		if (((FrameworkElement)sender).DataContext is ClipItem item)
		{
			await _clipboard.PasteAsync(item);
		}
	}

	private void ClipItem_RightClick(object sender, MouseButtonEventArgs e)
	{
		if (((FrameworkElement)sender).DataContext is ClipItem item)
		{
			_clipboard.Remove(item);
		}
		e.Handled = true;
	}

	private void ClipClear_Click(object sender, RoutedEventArgs e)
	{
		_clipboard.Items.Clear();
	}

	private void UpdateClipEmpty()
	{
		bool flag = _clipboard.Items.Count == 0;
		ClipEmpty.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		ClipClear.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		ClipHint.Visibility = (flag ? Visibility.Hidden : Visibility.Visible);
	}

	private void OnWeatherChanged()
	{
		WeatherNow? current = _weather.Current;
		if (current == null)
		{
			TabWeatherIcon.Text = "☀";
			TabWeatherIcon.ClearValue(TextBlock.ForegroundProperty);
		}
		else
		{
			var (text, _, color) = WeatherService.Describe(current.Code, current.IsDay);
			TabWeatherIcon.Text = text;
			TabWeatherIcon.Foreground = Freeze(new SolidColorBrush(color));
		}
		if (_state == IslandState.Idle)
		{
			UpdateIdle();
		}
		if (_shownTab == WeatherPanel)
		{
			UpdateWeather();
		}
	}

	private void UpdateWeather()
	{
		WeatherNow? current = _weather.Current;
		bool flag = string.IsNullOrWhiteSpace(Settings.Current.WeatherCity);
		if (current == null || _cityEditing)
		{
			WeatherBody.Visibility = Visibility.Collapsed;
			WeatherStatusPanel.Visibility = Visibility.Visible;
			WeatherStatus.Text = (_cityEditing ? "Впишите свой город — погода будет для него" : (_weather.Problem ?? "Загружаю погоду…"));
			bool flag2 = (_cityEditing | flag) || (current == null && _weather.Problem != null);
			CityFrame.Visibility = ((!flag2) ? Visibility.Collapsed : Visibility.Visible);
			CityCancel.Visibility = ((!_cityEditing || !(current != null)) ? Visibility.Collapsed : Visibility.Visible);
			return;
		}
		WeatherStatusPanel.Visibility = Visibility.Collapsed;
		WeatherBody.Visibility = Visibility.Visible;
		CityChange.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		var (text, text2, color) = WeatherService.Describe(current.Code, current.IsDay);
		WeatherGlyph.Text = text;
		WeatherGlyph.Foreground = Freeze(new SolidColorBrush(color));
		WeatherTemp.Text = WeatherService.FormatTemp(current.Temp);
		WeatherDesc.Text = text2;
		WeatherCity.Text = current.City;
		WeatherCity.Visibility = (string.IsNullOrEmpty(current.City) ? Visibility.Collapsed : Visibility.Visible);
		WeatherFeels.Text = "Ощущается как " + WeatherService.FormatTemp(current.FeelsLike);
		WeatherHiLo.Text = "↑ " + WeatherService.FormatTemp(current.Max) + "   ↓ " + WeatherService.FormatTemp(current.Min);
		WeatherWind.Text = $"Ветер {Math.Round(current.Wind)} м/с";
		WeatherHumidity.Text = $"Влажность {current.Humidity}%";
		RenderForecastStrip(current);
	}

	private void CityChange_Click(object sender, RoutedEventArgs e)
	{
		_cityEditing = true;
		UpdateWeather();
		BeginTyping(CityInput);
	}

	private void CityCancel_Click(object sender, RoutedEventArgs e)
	{
		_cityEditing = false;
		CityInput.Text = "";
		EndTyping(restoreFocus: true);
		UpdateWeather();
	}

	private void CitySave_Click(object sender, RoutedEventArgs e)
	{
		SaveCity();
	}

	private void CityInput_TextChanged(object sender, TextChangedEventArgs e)
	{
		CityPlaceholder.Visibility = ((CityInput.Text.Length != 0) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void CityInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			SaveCity();
			e.Handled = true;
		}
	}

	private void SaveCity()
	{
		string text = CityInput.Text.Trim();
		if (text.Length != 0)
		{
			Settings.Current.WeatherCity = text;
			Settings.Current.Save();
			_cityEditing = false;
			CityInput.Text = "";
			EndTyping(restoreFocus: true);
			WeatherStatus.Text = "Загружаю погоду…";
			Settings.NotifyChanged();
			UpdateWeather();
		}
	}

	private void WeatherMode_Click(object sender, RoutedEventArgs e)
	{
		_weatherDays = (string)((System.Windows.Controls.Button)sender).Tag == "1";
		WeatherNow? current = _weather.Current;
		if ((object)current != null)
		{
			RenderForecastStrip(current);
		}
	}

	private void RenderForecastStrip(WeatherNow w)
	{
		WeatherHoursBtn.Background = (_weatherDays ? System.Windows.Media.Brushes.Transparent : SubTabActive);
		WeatherDaysBtn.Background = (_weatherDays ? SubTabActive : System.Windows.Media.Brushes.Transparent);
		WeatherHoursBtn.Foreground = (_weatherDays ? ((System.Windows.Media.Brush)FindResource("TextSecondary")) : FillNormal);
		WeatherDaysBtn.Foreground = (_weatherDays ? FillNormal : ((System.Windows.Media.Brush)FindResource("TextSecondary")));
		HourlyStrip.Children.Clear();
		if (_weatherDays)
		{
			HourlyStrip.Columns = Math.Max(1, w.Days.Count);
			foreach (DayForecast day in w.Days)
			{
				(string Glyph, string Text, System.Windows.Media.Color Tint) tuple = WeatherService.Describe(day.Code, isDay: true);
				string item = tuple.Glyph;
				string item2 = tuple.Text;
				System.Windows.Media.Color item3 = tuple.Tint;
				string label;
				if (day.Date == DateTime.Today)
				{
					label = "Сегодня";
				}
				else
				{
					label = ((day.Date == DateTime.Today.AddDays(1.0)) ? "Завтра" : Ru.DateTimeFormat.GetAbbreviatedDayName(day.Date.DayOfWeek));
				}
				FrameworkElement frameworkElement = ForecastCell(label, item, item3, WeatherService.FormatTemp(day.Max), WeatherService.FormatTemp(day.Min));
				frameworkElement.ToolTip = Capitalize(day.Date.ToString("dddd, d MMMM", Ru)) + ": " + item2.ToLower(Ru) + ((day.RainChance > 0) ? $", осадки {day.RainChance}%" : "");
				HourlyStrip.Children.Add(frameworkElement);
			}
		}
		HourlyStrip.Visibility = ((!_weatherDays) ? Visibility.Collapsed : Visibility.Visible);
		HourlyScroll.Visibility = (_weatherDays ? Visibility.Collapsed : Visibility.Visible);
		if (_weatherDays)
		{
			for (int i = 0; i < HourlyStrip.Children.Count; i++)
			{
				Stagger((FrameworkElement)HourlyStrip.Children[i], i);
			}
			return;
		}
		HourlyRow.Children.Clear();
		DateTime dateTime = DateTime.Now.Date.AddHours(DateTime.Now.Hour);
		int current2 = -1;
		foreach (HourForecast hour in w.Hours)
		{
			(string Glyph, string Text, System.Windows.Media.Color Tint) tuple2 = WeatherService.Describe(hour.Code, hour.IsDay);
			string item4 = tuple2.Glyph;
			System.Windows.Media.Color item5 = tuple2.Tint;
			bool flag = hour.Time == dateTime;
			string label2;
			if (flag)
			{
				label2 = "Сейчас";
			}
			else
			{
				label2 = ((hour.Time.Date > DateTime.Today && hour.Time.Hour == 0) ? "Завтра" : hour.Time.ToString("HH:mm"));
			}
			FrameworkElement frameworkElement2 = ForecastCell(label2, item4, item5, WeatherService.FormatTemp(hour.Temp), null);
			frameworkElement2.Width = 50.0;
			if (hour.Time < dateTime)
			{
				frameworkElement2.Opacity = 0.4;
			}
			if (flag)
			{
				current2 = HourlyRow.Children.Count;
				frameworkElement2 = new Border
				{
					Child = frameworkElement2,
					Background = SubTabActive,
					CornerRadius = new CornerRadius(12.0),
					Padding = new Thickness(0.0, 6.0, 0.0, 6.0)
				};
			}
			HourlyRow.Children.Add(frameworkElement2);
		}
		Dispatcher.BeginInvoke((Action)(() =>
		{
			HourlyScroll.ScrollToHorizontalOffset(Math.Max(0, (current2 - 1) * 50));
		}), DispatcherPriority.Loaded);
		int num = Math.Max(0, current2 - 1);
		for (int num2 = num; num2 < Math.Min(HourlyRow.Children.Count, num + 8); num2++)
		{
			Stagger((FrameworkElement)HourlyRow.Children[num2], num2 - num);
		}
	}

	private void HourlyScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		HourlyScroll.ScrollToHorizontalOffset(HourlyScroll.HorizontalOffset - (double)e.Delta / 120.0 * 100.0);
		e.Handled = true;
	}

	private void HourlyScroll_MouseDown(object sender, MouseButtonEventArgs e)
	{
		_hourlyPress = e.GetPosition(HourlyScroll);
		_hourlyPressOffset = HourlyScroll.HorizontalOffset;
		HourlyScroll.CaptureMouse();
	}

	private void HourlyScroll_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		System.Windows.Point? hourlyPress = _hourlyPress;
		if (hourlyPress.HasValue)
		{
			System.Windows.Point valueOrDefault = hourlyPress.GetValueOrDefault();
			if (e.LeftButton == MouseButtonState.Pressed)
			{
				HourlyScroll.ScrollToHorizontalOffset(_hourlyPressOffset - (e.GetPosition(HourlyScroll).X - valueOrDefault.X));
			}
		}
	}

	private void HourlyScroll_MouseUp(object sender, MouseButtonEventArgs e)
	{
		_hourlyPress = null;
		HourlyScroll.ReleaseMouseCapture();
	}

	private FrameworkElement ForecastCell(string label, string glyph, System.Windows.Media.Color tint, string main, string? secondary)
	{
		StackPanel stackPanel = new StackPanel
		{
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Background = System.Windows.Media.Brushes.Transparent
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = label,
			FontSize = 10.5,
			Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary"),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = glyph,
			FontFamily = (System.Windows.Media.FontFamily)FindResource("Symbols"),
			FontSize = 16.0,
			Foreground = Freeze(new SolidColorBrush(tint)),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 2.0, 0.0, 1.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = main,
			FontSize = 12.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = FillNormal,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center
		});
		if (secondary != null)
		{
			stackPanel.Children.Add(new TextBlock
			{
				Text = secondary,
				FontSize = 11.0,
				Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary"),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center
			});
		}
		return stackPanel;
	}

	private void OnVolumeChanged(float level, bool muted)
	{
		OnPetVolume(level, muted);
		if (Settings.Current.ToastVolume)
		{
			string icon;
			if (muted || level == 0f)
			{
				icon = "\ue74f";
			}
			else if ((double)level < 0.34)
			{
				icon = "\ue993";
			}
			else
			{
				icon = (((double)level < 0.67) ? "\ue994" : "\ue995");
			}
			string label = (muted ? "Без звука" : "Громкость");
			double? level2 = (muted ? 0f : level);
			string value = $"{Math.Round(level * 100f)}";
			System.Windows.Media.Brush fillNormal = FillNormal;
			ToastExtra extra = new ToastExtra(null, null, ToastAnim.None, null, null, Volume: true);
			ShowToast(icon, label, level2, value, fillNormal, null, 0, symbolFont: false, extra);
		}
	}

	private void OnPowerChanged(PowerChange change)
	{
		if (_power.HasBattery && Settings.Current.ToastBattery)
		{
			int percent = _power.Percent;
			bool isPluggedIn = _power.IsPluggedIn;
			if (isPluggedIn && change != PowerChange.Low)
			{
				PetReact("charge");
			}
			string label;
			if (change == PowerChange.Low)
			{
				label = "Низкий заряд";
			}
			else
			{
				label = (isPluggedIn ? "Зарядка" : "От батареи");
			}
			System.Windows.Media.Brush fill;
			if (change == PowerChange.Low)
			{
				fill = FillLow;
			}
			else
			{
				fill = (isPluggedIn ? FillCharging : FillNormal);
			}
			ShowToast(BatteryGlyph(percent, isPluggedIn), label, (double)percent / 100.0, $"{percent}%", fill);
		}
	}

	private void ShowToast(string icon, string label, double? level, string value, System.Windows.Media.Brush fill, (string Text, Action Run)? action = null, int durationMs = 0, bool symbolFont = false, ToastExtra? extra = null)
	{
		_socialActive=null;
		if (KeepOpen || DeferToast(() =>
		{
			ShowToast(icon, label, level, value, fill, action, durationMs, symbolFont, extra);
		}, level.HasValue))
		{
			return;
		}
		if ((object)extra == null)
		{
			extra = new ToastExtra();
		}
		bool flag = PhoneCard.Visibility == Visibility.Visible;
		HidePhoneCard();
		bool num = (_state != IslandState.Toast) | flag;
		bool flag2 = ToastIcon.Text != icon || extra.Anim != ToastAnim.None;
		bool flag3 = ToastLabel.Text != label;
		ToastIcon.Text = icon;
		ToastIconGhost.Text = icon;
		ToastIcon.FontFamily = (System.Windows.Media.FontFamily)FindResource(symbolFont ? "Symbols" : "Icons");
		ToastLabel.Text = label;
		ToastSub.Text = extra.Sub ?? "";
		ToastSub.Visibility = ((extra.Sub == null) ? Visibility.Collapsed : Visibility.Visible);
		if (ToastValue.Text != value && _state == IslandState.Toast)
		{
			Pop(ToastValue, 0.8);
		}
		ToastValue.Text = value;
		ToastBar.Visibility = ((!level.HasValue) ? Visibility.Collapsed : Visibility.Visible);
		ToastBar.Cursor = (extra.Volume ? System.Windows.Input.Cursors.Hand : null);
		if (level.HasValue)
		{
			ToastFill.Background = fill;
			Animate(ToastFill, FrameworkElement.WidthProperty, ToastBar.Width * Math.Clamp(level.Value, 0.0, 1.0), 220, new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			});
		}
		ToastThumbBrush.ImageSource = extra.Thumb;
		ToastThumb.Visibility = ((extra.Thumb == null) ? Visibility.Collapsed : Visibility.Visible);
		ToastIcon.Visibility = ((extra.Thumb != null) ? Visibility.Hidden : Visibility.Visible);
		_toastDragPath = extra.DragPath;
		ToastIconHost.Cursor = ((extra.DragPath != null) ? System.Windows.Input.Cursors.SizeAll : null);
		ToastIconHost.ToolTip = ((extra.DragPath != null) ? "Перетащите в любое окно или папку" : null);
		_toastAction = action?.Run;
		ToastAction.Content = action?.Text;
		ToastAction.Visibility = ((!action.HasValue) ? Visibility.Collapsed : Visibility.Visible);
		_toastAction2 = extra.Second?.Run;
		ToastAction2.Content = extra.Second?.Text;
		ToastAction2.Visibility = ((!extra.Second.HasValue) ? Visibility.Collapsed : Visibility.Visible);
		_toastVolume = extra.Volume;
		_toastInteractive = action.HasValue || extra.Second.HasValue || extra.DragPath != null || extra.Volume;
		double num2 = ((action.HasValue && extra.Second.HasValue) ? 456 : ((action.HasValue || extra.Second.HasValue || label.Length > 26 || (extra.Sub?.Length ?? 0) > 34) ? 378 : 318));
		double num3 = ((extra.Sub != null) ? 50 : 38);
		if (!num && (Math.Abs(ToastPanel.Width - num2) > 1.0 || Math.Abs(ToastPanel.Height - num3) > 1.0))
		{
			ToastPanel.Width = num2;
			ToastPanel.Height = num3;
			Resize(num2 + 2.0, num3 + 2.0);
		}
		ToastPanel.Width = num2;
		ToastPanel.Height = num3;
		ApplyState(IslandState.Toast);
		if (num | flag2)
		{
			PlayToastAnim(extra.Anim);
		}
		if ((num | flag3) && !ReducedMotion)
		{
			CubicEase ease = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			Animate(ToastLabelShift, TranslateTransform.XProperty, 0.0, 380, ease, 40, 12.0);
			Animate(ToastLabel, UIElement.OpacityProperty, 1.0, 260, ease, 40, 0.0);
			if (extra.Sub != null)
			{
				Animate(ToastSub, UIElement.OpacityProperty, 1.0, 300, ease, 140, 0.0);
			}
		}
		_toastTimer.Stop();
		_toastTimer.Interval = TimeSpan.FromMilliseconds((durationMs > 0) ? ((double)durationMs) : (Settings.Current.ToastSeconds * 1000.0));
		if (!_hover || !_toastInteractive)
		{
			_toastTimer.Start();
		}
	}

	private void ToastAction_Click(object sender, RoutedEventArgs e)
	{
		RunToastAction(_toastAction);
	}

	private void ToastAction2_Click(object sender, RoutedEventArgs e)
	{
		RunToastAction(_toastAction2);
	}

	private void RunToastAction(Action? run)
	{
		_toastAction = (_toastAction2 = null);
		_toastInteractive = false;
		_toastTimer.Stop();
		_hover = false;
		ApplyState(BaseState());
		run?.Invoke();
	}

	private static string DeviceGlyph(string name)
	{
		string text = name.ToLowerInvariant();
		if (!text.Contains("наушник") && !text.Contains("headphone") && !text.Contains("headset") && !text.Contains("airpods") && !text.Contains("buds") && !text.Contains("гарнитур"))
		{
			return "\ue7f5";
		}
		return "\ue7f6";
	}

	private static string BatteryGlyph(int percent, bool charging)
	{
		int num = Math.Clamp(percent / 10, 0, 10);
		int utf;
		if (charging)
		{
			utf = ((num == 10) ? 59454 : (59482 + num));
		}
		else
		{
			utf = ((num == 10) ? 59455 : (59472 + num));
		}
		return char.ConvertFromUtf32(utf);
	}

	private void Launch(string target, string? args = null)
	{
		try
		{
			ReviewSafety.Start(new ProcessStartInfo(target)
			{
				UseShellExecute = true,
				Arguments = (args ?? "")
			});
		}
		catch (Exception ex)
		{
			Log.Error("launch " + target, ex);
			ShowToast("\ue783", "Не удалось открыть", null, "", FillNormal);
		}
	}

	private static T Freeze<T>(T f) where T : Freezable
	{
		f.Freeze();
		return f;
	}

}
