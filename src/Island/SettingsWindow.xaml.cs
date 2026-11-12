using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Island.Native;
using Island.Pets;
using Island.Services;
using Island.Services.Phone;

namespace Island;

public partial class SettingsWindow : Window, IComponentConnector
{
	private sealed record Theme(string Name, string Island, string Accent, double Corner, double Neck, bool Outline, bool Shadow, string Open, string Hide, bool Rim, bool Water, double Wave);

	private Canvas? _sideDecor;

	private Grid? _spiderDecor;

	private Image? _titlePumpkin;

	private bool? _spookyShown;

	private static readonly Theme[] Themes = new Theme[7]
	{
		new Theme("Классика", "#000000", "auto", 0.75, 1.0, Outline: true, Shadow: true, "drop", "drop", Rim: true, Water: true, 1.0),
		new Theme("Минимал", "#1C1C1E", "#E5E5EA", 0.5, 0.5, Outline: false, Shadow: false, "smooth", "fade", Rim: false, Water: false, 0.5),
		new Theme("Неон", "#0B1426", "#3DF5FF", 0.9, 1.3, Outline: true, Shadow: true, "elastic", "drop", Rim: true, Water: true, 1.5),
		new Theme("Мягкий", "#1A0F24", "#C39BFF", 1.0, 1.6, Outline: true, Shadow: true, "bubble", "fade", Rim: true, Water: true, 1.0),
		new Theme("Лес", "#0D1A12", "#5ED87A", 0.7, 1.0, Outline: true, Shadow: true, "jelly", "fold", Rim: true, Water: true, 1.2),
		new Theme("Закат", "#1E120C", "#FFB86B", 0.85, 1.2, Outline: true, Shadow: true, "spring", "drop", Rim: true, Water: true, 1.0),
		new Theme("Щелчок", "#000000", "#4FA3FF", 0.4, 0.4, Outline: false, Shadow: true, "snap", "fold", Rim: false, Water: false, 0.5)
	};

	private static readonly (string Id, string Title, bool Pro, string? Section)[] TabList = new (string, string, bool, string)[8]
	{
		("shelf", "Полка", false, null),
		("clip", "Буфер обмена", false, "Буфер обмена"),
		("calendar", "Календарь", false, "Календарь"),
		("text", "Текст", true, null),
		("weather", "Погода", true, "Погода"),
		("ai", "ИИ-чат", true, "ИИ-чат"),
		("phone", "Телефон", true, null),
		("monitor", "Мониторинг", false, null)
	};

	private PetView? _petPreview;

	private DispatcherTimer? _petPreviewTimer;

	private (string State, string Bubble, double Until)? _petPreviewShow;

	private static readonly (string Text, string State, string Bubble)[] PetShows = new (string, string, string)[13]
	{
		("Покой", "idle", ""),
		("Сон", "sleep", ""),
		("Музыка", "music", ""),
		("Сообщение", "notify", "mail"),
		("Звонок", "alarm", "phone"),
		("Испуг", "startle", "caps"),
		("Радость", "happy", "check"),
		("Мурчит", "pet", ""),
		("Загрузка", "download", "down"),
		("Думает", "think", "dots"),
		("Устал", "tired", "battery"),
		("Без сети", "offline", "wifioff"),
		("Ловит листок", "copy", "")
	};

	private PhonePairing? _pairing;

	private PhoneLinkService? _hookedLink;

	private DispatcherTimer? _pairingClock;

	internal const string AppDownloadUrl = "https://github.com/qxezecwry/Island/releases/latest/download/IslandLink.apk";

	private bool _appQr;

	private readonly DispatcherTimer _commitTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(160L)
	};

	private readonly List<(string Id, string Title, string Glyph, Action Build)> _pages;

	private readonly Dictionary<string, RadioButton> _navItems = new Dictionary<string, RadioButton>();

	private string _page = "";

	private bool _rebuilding;

	private TextBlock? _pinnedTitle;

	private Canvas? _pinnedFrame;

	private bool _pinnedCompact;

	private bool _proRows;

	private Border? _proBar;

	private Settings? _proSnapshot;

	private readonly HashSet<string> _proTried = new HashSet<string>();

	private readonly List<IslandPreview> _previews = new List<IslandPreview>();

	private const string RowWrap = "row+divider";

	private static double PreviewNow => (double)Environment.TickCount64 / 1000.0;

	private static Settings S => Settings.Current;

	private bool PreviewOnly
	{
		get
		{
			if (_proRows)
			{
				return !Island.Services.License.IsPro;
			}
			return false;
		}
	}

	private void InitHalloween()
	{
		Action changed = () =>
		{
			Dispatcher.BeginInvoke(new Action(ApplyHalloween));
		};
		Settings.Changed += changed;
		Closed += (object? _, EventArgs _) =>
		{
			Settings.Changed -= changed;
		};
		ApplyHalloween();
	}

	private void ApplyHalloween()
	{
		bool halloweenNow = MainWindow.HalloweenNow;
		if (halloweenNow == _spookyShown)
		{
			return;
		}
		_spookyShown = halloweenNow;
		if (halloweenNow && _sideDecor == null)
		{
			BuildHalloween();
		}
		Visibility visibility = ((!halloweenNow) ? Visibility.Collapsed : Visibility.Visible);
		UIElement[] array = new UIElement[3] { _sideDecor, _spiderDecor, _titlePumpkin };
		foreach (UIElement uIElement in array)
		{
			if (uIElement != null)
			{
				uIElement.Visibility = visibility;
			}
		}
		foreach (IslandPreview preview in _previews)
		{
			preview.Refresh();
		}
	}

	private void BuildHalloween()
	{
		_sideDecor = new Canvas
		{
			IsHitTestVisible = false
		};
		Grid.SetRowSpan(_sideDecor, 3);
		Panel.SetZIndex(_sideDecor, 1);
		_sideDecor.Children.Add(new System.Windows.Shapes.Path
		{
			Data = Cobweb(new Point(232.0, 0.0), 70.0),
			Stroke = new SolidColorBrush(Color.FromArgb(48, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			StrokeThickness = 0.8
		});
		Sidebar.Children.Add(_sideDecor);
		RotateTransform rotateTransform = new RotateTransform();
		_titlePumpkin = new Image
		{
			Source = MainWindow.PumpkinImage(),
			Width = 16.0,
			Height = 16.0,
			Margin = new Thickness(6.0, 1.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			RenderTransformOrigin = new Point(0.5, 0.9),
			RenderTransform = rotateTransform,
			ToolTip = "С Хеллоуином! Выключается во «Внешнем виде» → «Праздничные темы»"
		};
		TitleLine.Children.Add(_titlePumpkin);
		Rectangle rectangle = new Rectangle
		{
			Width = 1.0,
			Height = 38.0,
			Fill = new SolidColorBrush(Color.FromArgb(96, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		Image element = new Image
		{
			Source = MainWindow.SpiderImage(),
			Width = 20.0,
			Height = 20.0
		};
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(rectangle);
		stackPanel.Children.Add(element);
		RotateTransform rotateTransform2 = new RotateTransform();
		_spiderDecor = new Grid
		{
			Width = 20.0,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 0.0, 176.0, 0.0),
			IsHitTestVisible = false,
			RenderTransformOrigin = new Point(0.5, 0.0),
			RenderTransform = rotateTransform2
		};
		_spiderDecor.Children.Add(stackPanel);
		Grid.SetRowSpan(_spiderDecor, 3);
		Panel.SetZIndex(_spiderDecor, 5);
		ContentArea.Children.Add(_spiderDecor);
		if (SystemParameters.ClientAreaAnimation && !S.ReduceMotion)
		{
			SineEase easingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseInOut
			};
			rotateTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-7.0, 7.0, TimeSpan.FromSeconds(1.6))
			{
				AutoReverse = true,
				RepeatBehavior = RepeatBehavior.Forever,
				EasingFunction = easingFunction
			});
			rotateTransform2.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-6.0, 6.0, TimeSpan.FromSeconds(2.8))
			{
				AutoReverse = true,
				RepeatBehavior = RepeatBehavior.Forever,
				EasingFunction = easingFunction
			});
			rectangle.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(30.0, 52.0, TimeSpan.FromSeconds(4.2))
			{
				AutoReverse = true,
				RepeatBehavior = RepeatBehavior.Forever,
				EasingFunction = easingFunction
			});
		}
	}

	private static Geometry Cobweb(Point corner, double size)
	{
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			double[] array = new double[5] { 92.0, 112.0, 135.0, 158.0, 178.0 };
			double[] array2 = array;
			foreach (double angle in array2)
			{
				streamGeometryContext.BeginFigure(corner, isFilled: false, isClosed: false);
				streamGeometryContext.LineTo(At(angle, size), isStroked: true, isSmoothJoin: false);
			}
			array2 = new double[3]
			{
				size * 0.3,
				size * 0.55,
				size * 0.8
			};
			foreach (double num in array2)
			{
				streamGeometryContext.BeginFigure(At(array[0], num), isFilled: false, isClosed: false);
				for (int j = 1; j < array.Length; j++)
				{
					double angle2 = (array[j - 1] + array[j]) / 2.0;
					streamGeometryContext.QuadraticBezierTo(At(angle2, num * 0.82), At(array[j], num), isStroked: true, isSmoothJoin: false);
				}
			}
		}
		streamGeometry.Freeze();
		return streamGeometry;
		Point At(double num2, double r)
		{
			return new Point(corner.X + Math.Cos(num2 * Math.PI / 180.0) * r, corner.Y + Math.Sin(num2 * Math.PI / 180.0) * r);
		}
	}

	private void BuildIsland()
	{
		Group("Раскрытие");
		Grid? delay = null;
		Card(ChoiceRow("Раскрывать", "Как открыть остров", new (string, string)[2]
		{
			("Наведением", "hover"),
			("Кликом", "click")
		}, S.OpenOnClick ? "click" : "hover", (string v) =>
		{
			S.OpenOnClick = v == "click";
			if (delay != null)
			{
				Show(delay, !S.OpenOnClick);
			}
		}, "\ue7c9"), delay = SliderRow("Задержка раскрытия", "Сколько держать курсор до раскрытия", 0.0, 800.0, 10.0, S.HoverDelayMs, (double v) => (!(v < 5.0)) ? $"{v:0} мс" : "сразу", (double v) =>
		{
			S.HoverDelayMs = (int)v;
		}, "\ue916"), ChoiceRow("При раскрытии открывать", "С какой вкладки начинать", new (string, string)[3]
		{
			("Последнюю вкладку", "last"),
			("Плеер, если играет музыка", "music"),
			("Всегда плеер", "home")
		}, S.OpenTab, (string v) =>
		{
			S.OpenTab = v;
		}, "\ue8a9"));
		Show(delay, !S.OpenOnClick);
		Group("Свёрнутый остров");
		Preview(IslandPreview.Kind.Strip);
		bool miniPill = S.MiniPill;
		List<UIElement> list = new List<UIElement>();
		list.Add(ChoiceRow("Режим", "Мини: в покое только часы. Обложка, кто написал, Caps Lock и низкий заряд появляются сбоку по необходимости, остров подрастает", new (string, string)[2]
		{
			("Обычный", "classic"),
			("Мини-остров", "mini")
		}, miniPill ? "mini" : "classic", (string v) =>
		{
			S.MiniPill = v == "mini";
			RebuildSoon();
		}, "\ue7c4"));
		list.Add(SwitchRow("Музыка", "Обложка и название трека (или строка песни). Выкл — только часы, музыка лишь в раскрытом острове", S.CollapsedMusic, (bool v) =>
		{
			S.CollapsedMusic = v;
		}, "\ue8d6"));
		list.Add(SwitchRow(miniPill ? "Низкий заряд" : "Заряд батареи или дата", miniPill ? "Значок батареи при заряде ≤ 20 %" : "На компьютере без батареи показывается дата", S.IdleShowBattery, (bool v) =>
		{
			S.IdleShowBattery = v;
		}, "\ue83f"));
		list.Add(SwitchRow(miniPill ? "Caps Lock" : "Раскладка и Caps Lock", (miniPill ? "Жёлтый индикатор, пока включён. " : "Значок языка и жёлтый индикатор, пока Caps Lock включён. ") + "Плашка при нажатии — в «Уведомлениях»", S.IdleShowKeyboard, (bool v) =>
		{
			S.IdleShowKeyboard = v;
		}, "\ue765"));
		List<UIElement> list2 = list;
		list2.Add(Locked(WithBadge(SwitchRow("Погода", "Температура рядом с часами", Island.Services.License.IsPro && S.IdleShowWeather, (bool v) =>
		{
			S.IdleShowWeather = v;
		}, "\ue753"))));
		list2.Add(Locked(WithBadge(SwitchRow("Строка песни", "Вместо названия трека строка, которую поют. Тот же переключатель — в «Музыке»", Island.Services.License.IsPro && S.LyricsInCompact, (bool v) =>
		{
			S.LyricsInCompact = v;
		}, "\ue8d6"))));
		Card(list2.ToArray());
		Group("Два дела сразу");
		Card(ChoiceRow("Второе дело", "Как показать второе событие, например загрузку при музыке. По очереди — одно за раз. Капля — маленький круглый остров рядом. Полоска — светящийся низ острова", new (string, string)[3]
		{
			("По очереди", "off"),
			("Капля рядом", "drop"),
			("Полоска по низу", "strip")
		}, (!S.TwoIslands) ? "off" : ((S.TwoIslandsStyle == "strip") ? "strip" : "drop"), (string v) =>
		{
			S.TwoIslands = v != "off";
			if (v != "off")
			{
				S.TwoIslandsStyle = v;
			}
		}, "\ue8a9"));
		Hint("Капля кликабельна: открывает папку загрузок или вкладку «Телефон». Сообщения с телефона видны, если включены в «Телефоне».");
		Group("Скрытие");
		Card(HotkeyRow(), SwitchRow("Прятать в полноэкранных приложениях", "Игры, видео на весь экран, презентации", S.HideInFullscreen, (bool v) =>
		{
			S.HideInFullscreen = v;
		}, "\ue740"));
		Hint("Нажатие — спрятать или вернуть (и поверх игры). Удержание — обратное действие, пока клавиша нажата.");
		Group("Прятать в приложениях");
		StackPanel stackPanel = new StackPanel();
		Card(stackPanel);
		BuildHideApps(stackPanel);
		Hint("Пока окно программы из списка впереди, острова не видно. Скрыть совсем — меню значка Island в трее.");
		Group("Игровой режим");
		List<UIElement> list3 = new List<UIElement> { SwitchRow("Мониторинг в игре", "Игра на весь экран: остров остаётся на экране и вместо часов показывает FPS и нагрузку. Выключено: остров ведёт себя как обычно. Тот же переключатель — галочка во вкладке «Мониторинг»", S.GameMode, (bool v) =>
		{
			S.GameMode = v;
		}, "\ue7fc") };
		(string, string, string)[] array = new (string, string, string)[6]
		{
			("fps", "FPS", "Кадры в секунду у игры на переднем плане"),
			("cpu", "Процессор", "Загрузка всех ядер"),
			("ram", "Память", "Занято из оперативной памяти"),
			("gpu", "Видеокарта", "Самый загруженный блок видеокарты, как в диспетчере задач"),
			("disk", "Диск", "Как занят диск чтением и записью"),
			("net", "Сеть", "Большая из скоростей приёма и отправки, Мбит/с")
		};
		for (int num = 0; num < array.Length; num++)
		{
			(string Id, string Title, string Text) m = array[num];
			list3.Add(SwitchRow(m.Title, m.Text, S.GameBarItems.Contains(m.Id), (bool v) =>
			{
				if (v && !S.GameBarItems.Contains(m.Id))
				{
					S.GameBarItems.Add(m.Id);
				}
				if (!v)
				{
					S.GameBarItems.Remove(m.Id);
				}
			}, "\ue9d9"));
		}
		list3.Add(FpsMeter.CanTrace() ? Row("Счётчик FPS", "Доступ есть. Считает игры на DirectX 11 и 12; Vulkan и OpenGL пока не считает, у них будет прочерк", null, "\ue73e") : ButtonRow("Счётчик FPS", "Кадры Windows показывает только администраторам и членам группы «Пользователи журнала производительности». Нажмите «Разрешить», подтвердите запрос Windows и перезайдите в систему. Без этого вместо FPS будет прочерк", "Разрешить", AllowFpsCounter, "\ue72e"));
		list3.Add(ButtonRow("Показать на острове", "На 8 секунд, чтобы увидеть, как это выглядит", "Показать", () =>
		{
			((App)Application.Current).Island?.PreviewGameBar();
		}, "\ue7b3"));
		Card(list3.ToArray());
		Hint("Игра определяется по окну на весь экран: браузеры, видеоплееры и рабочий стол не считаются. Если остров не виден поверх игры, переключите игру в режим «Без рамки». Наведение курсора раскрывает остров как обычно.");
	}

	private void AllowFpsCounter()
	{
		try
		{
			string text = (Environment.UserDomainName + "\\" + Environment.UserName).Replace("'", "''");
			using Process? process = ReviewSafety.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"Add-LocalGroupMember -SID S-1-5-32-559 -Member '" + text + "'\"")
			{
				UseShellExecute = true,
				Verb = "runas",
				WindowStyle = ProcessWindowStyle.Hidden
			});
			process?.WaitForExit();
			Log.Write($"fps: group membership asked, exit code {process?.ExitCode}");
			MessageBox.Show("Готово. Чтобы Windows применил группу, выйдите из системы и зайдите снова (или перезагрузитесь). После этого FPS появится в игре.", "Island");
		}
		catch (Win32Exception)
		{
		}
		catch (Exception ex2)
		{
			Log.Error("fps group", ex2);
			MessageBox.Show("Не получилось добавить в группу. Запустите Island от администратора один раз или добавьте себя в «Пользователи журнала производительности» вручную.", "Island");
		}
	}

	private void RebuildSoon()
	{
		Dispatcher.BeginInvoke(new Action(RebuildPage), DispatcherPriority.Background);
	}

	private static Grid Locked(Grid row)
	{
		if (Island.Services.License.IsPro)
		{
			return row;
		}
		row.IsEnabled = false;
		row.Opacity = 0.45;
		return row;
	}

	private Grid WithBadge(Grid row)
	{
		if (Island.Services.License.IsPro)
		{
			return row;
		}
		StackPanel stackPanel = row.Children.OfType<StackPanel>().First();
		TextBlock element = (TextBlock)stackPanel.Children[0];
		stackPanel.Children.RemoveAt(0);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		stackPanel2.Children.Add(element);
		stackPanel2.Children.Add(ProBadge());
		stackPanel.Children.Insert(0, stackPanel2);
		return row;
	}

	private void BuildLook()
	{
		IslandPreview preview = Preview(IslandPreview.Kind.Look, pinned: true);
		if (!_rebuilding)
		{
			DispatcherTimer demo = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(700L)
			};
			demo.Tick += (object? _, EventArgs _) =>
			{
				demo.Stop();
				preview.Play();
			};
			demo.Start();
		}
		Button play = new Button
		{
			Content = "Показать раскрытие",
			Style = (Style)FindResource("T.Button"),
			Padding = new Thickness(12.0, 5.0, 12.0, 5.0),
			FontSize = 12.0
		};
		play.Click += (object _, RoutedEventArgs _) =>
		{
			preview.Play();
		};
		StickyHost.Children.Remove(preview);
		preview.Margin = new Thickness(0.0);
		preview.IsHitTestVisible = false;
		Canvas frame = new Canvas
		{
			Margin = new Thickness(0.0, 8.0, 0.0, 4.0),
			Height = preview.Height
		};
		frame.Children.Add(preview);
		frame.Children.Add(play);
		frame.SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			Place();
		};
		play.SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			Place();
		};
		preview.SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			Place();
		};
		StickyHost.Children.Add(frame);
		_pinnedFrame = frame;
		ProGroup("Темы", preview: true, () => new UIElement[1] { ThemeTiles() });
		ProGroup("Цвет", preview: true, () => new UIElement[2]
		{
			SwatchRow("Остров", new (string, string)[6]
			{
				("Чёрный", "#000000"),
				("Графит", "#1C1C1E"),
				("Ночной синий", "#0B1426"),
				("Баклажан", "#1A0F24"),
				("Лесной", "#0D1A12"),
				("Шоколад", "#1E120C")
			}, () => S.IslandColor, (string v) =>
			{
				S.IslandColor = v;
			}, allowCustom: true, "Цвет фона острова"),
			SwatchRow("Акцент", new (string, string)[6]
			{
				("Из обложки трека", "auto"),
				("Мята", "#5ED87A"),
				("Небо", "#4FA3FF"),
				("Лаванда", "#C39BFF"),
				("Персик", "#FFB86B"),
				("Роза", "#FF7EB6")
			}, () => S.Accent, (string v) =>
			{
				S.Accent = v;
			}, allowCustom: true, "Цвет выделений")
		});
		ProGroup("Форма", preview: true, () => new UIElement[6]
		{
			SliderRow("Размер свёрнутого", "Остров в покое и плашки: часы, музыка, уведомления", 0.75, 1.5, 0.05, S.UiScaleCollapsed ?? S.UiScale, (double v) => $"{v * 100.0:0}%", (double v) =>
			{
				S.UiScaleCollapsed = v;
			}, "\ue73f"),
			SliderRow("Размер раскрытого", "Остров с вкладками", 0.75, 1.5, 0.05, S.UiScale, (double v) => $"{v * 100.0:0}%", (double v) =>
			{
				S.UiScale = v;
			}, "\ue740"),
			SliderRow("Скругление", "Углы раскрытого острова. Свёрнутый всегда капсула", 0.1, 1.0, 0.05, S.CornerRadius, (double v) => $"{v * 100.0:0}%", (double v) =>
			{
				S.CornerRadius = v;
			}, "\ue71d"),
			SliderRow("Слияние с краем экрана", "Вогнутые «плечи» перехода в верх экрана", 0.0, 2.0, 0.05, S.NeckScale, (double v) => (!(v < 0.01)) ? $"{v * 100.0:0}%" : "нет", (double v) =>
			{
				S.NeckScale = v;
			}, "\ue7f4"),
			SwitchRow("Светлая кромка", "Блик по нижнему краю, остров виден на тёмных обоях", S.Outline, (bool v) =>
			{
				S.Outline = v;
			}, "\ue706"),
			SwitchRow("Тень", "Тень под островом", S.Shadow, (bool v) =>
			{
				S.Shadow = v;
			}, "\ue81e")
		});
		Group("Шрифт");
		Card(ChoiceRow("Шрифт приложения", "Свои шрифты: положите .ttf или .otf в папку %AppData%\\Island\\Fonts и откройте эту страницу заново — они появятся в списке", (from c in AppFont.Choices()
			select (Name: c.Name, Id: c.Id)).ToArray(), S.Font, (string v) =>
		{
			S.Font = v;
			AppFont.Apply();
		}, ""));
		Group("Анимация");
		Grid? open = null;
		Grid? hide = null;
		Grid grid = SwitchRow("Меньше анимации", "Без пружин и сдвигов, изменения мгновенные", S.ReduceMotion, (bool v) =>
		{
			S.ReduceMotion = v;
			if (open != null)
			{
				Dim(open, v);
			}
			if (hide != null)
			{
				Dim(hide, v);
			}
		}, "\ue916");
		_proRows = true;
		open = WithBadge(ChoiceRow("Раскрытие", "Стиль открытия. Наведение на вариант — превью", new (string, string)[10]
		{
			("Капля", "drop"),
			("Пружина", "spring"),
			("Плавно", "smooth"),
			("Волна", "wave"),
			("Желе", "jelly"),
			("Тянучка", "stretch"),
			("Мяч", "bounce"),
			("Резинка", "elastic"),
			("Щелчок", "snap"),
			("Пузырь", "bubble")
		}, S.AnimationStyle, (string v) =>
		{
			S.AnimationStyle = v;
			preview.Play(v);
		}, "\ue768", (string v) =>
		{
			preview.Play(v);
		}));
		_proRows = false;
		hide = ChoiceRow("Скрытие", "Как остров уходит и возвращается. Наведение — превью", new (string, string)[4]
		{
			("Капля вверх", "drop"),
			("Схлопывание", "fold"),
			("Растворение", "fade"),
			("Без анимации", "none")
		}, S.HideAnimation ? S.HideStyle : "none", (string v) =>
		{
			S.HideAnimation = v != "none";
			if (v != "none")
			{
				S.HideStyle = v;
			}
			preview.PlayHide(v);
		}, "\ue7b3", (string v) =>
		{
			preview.PlayHide(v);
		});
		Card(grid, open, hide);
		Dim(open, S.ReduceMotion);
		Dim(hide, S.ReduceMotion);
		Grid? strength = null;
		ProGroup("Живые эффекты", preview: true, () => new UIElement[4]
		{
			SwitchRow("Живая кромка", "Нижний край раскрытого острова колышется, под музыку сильнее", S.RimAnimation, (bool v) =>
			{
				S.RimAnimation = v;
				ShowStrength();
			}, "\ue945"),
			SwitchRow("Живая капля", "Низ колышется под музыку и тянется к курсору", S.WaterEffects, (bool v) =>
			{
				S.WaterEffects = v;
				ShowStrength();
			}, "\ue945"),
			(strength = SliderRow("Сила волны", "Высота волн и пульсации боков под музыку", 0.0, 2.0, 0.05, S.WaterStrength, (double v) => $"{v * 100.0:0}%", (double v) =>
			{
				S.WaterStrength = v;
			}, "\ue945")),
			SliderRow("Притяжение к курсору", "Насколько низ и бока тянутся к указателю", 0.0, 2.0, 0.05, S.AttractStrength, (double v) => (!(v < 0.01)) ? $"{v * 100.0:0}%" : "выкл.", (double v) =>
			{
				S.AttractStrength = v;
			}, "\ue7c9")
		});
		ShowStrength();
		void Place()
		{
			preview.Width = frame.ActualWidth;
			Canvas.SetLeft(play, frame.ActualWidth - play.ActualWidth - 10.0);
			Canvas.SetTop(play, frame.ActualHeight - play.ActualHeight - 12.0);
			bool flag = preview.ActualHeight > frame.ActualHeight + 4.0;
			play.IsHitTestVisible = !flag;
			MainWindow.Animate(play, UIElement.OpacityProperty, (!flag) ? 1 : 0, 160, null);
		}
		void ShowStrength()
		{
			if (strength != null)
			{
				Show(strength, S.RimAnimation || S.WaterEffects);
			}
		}
	}

	private static bool IsCurrent(Theme th)
	{
		if (string.Equals(S.IslandColor, th.Island, StringComparison.OrdinalIgnoreCase) && string.Equals(S.Accent, th.Accent, StringComparison.OrdinalIgnoreCase) && S.AnimationStyle == th.Open)
		{
			return Math.Abs(S.CornerRadius - th.Corner) < 0.01;
		}
		return false;
	}

	private UIElement ThemeTiles()
	{
		bool previewOnly = PreviewOnly;
		WrapPanel wrapPanel = new WrapPanel
		{
			Margin = new Thickness(12.0, 12.0, 6.0, 6.0)
		};
		Theme[] themes = Themes;
		foreach (Theme th in themes)
		{
			Color color = (Color)ColorConverter.ConvertFromString(th.Island);
			Color color2 = ((th.Accent == "auto") ? Color.FromRgb(byte.MaxValue, 158, 122) : ((Color)ColorConverter.ConvertFromString(th.Accent)));
			Grid grid = new Grid
			{
				Height = 54.0,
				ClipToBounds = true
			};
			grid.Children.Add(new Border
			{
				CornerRadius = new CornerRadius(8.0),
				Background = new LinearGradientBrush(Color.FromRgb(43, 48, 72), Color.FromRgb(23, 25, 36), 75.0)
			});
			Border border = new Border
			{
				Width = 74.0,
				Height = 22.0,
				VerticalAlignment = VerticalAlignment.Top,
				HorizontalAlignment = HorizontalAlignment.Center,
				Background = new SolidColorBrush(color),
				CornerRadius = new CornerRadius(0.0, 0.0, 11.0 * th.Corner + 2.0, 11.0 * th.Corner + 2.0),
				BorderBrush = (th.Outline ? new SolidColorBrush(Color.FromArgb(64, byte.MaxValue, byte.MaxValue, byte.MaxValue)) : null),
				BorderThickness = new Thickness(th.Outline ? 1 : 0, 0.0, th.Outline ? 1 : 0, th.Outline ? 1 : 0),
				Effect = (th.Shadow ? new DropShadowEffect
				{
					BlurRadius = 8.0,
					ShadowDepth = 2.0,
					Direction = 270.0,
					Opacity = 0.6
				} : null)
			};
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			stackPanel.Children.Add(new Border
			{
				Width = 10.0,
				Height = 10.0,
				CornerRadius = new CornerRadius(3.0),
				Background = new LinearGradientBrush(color2, Color.FromRgb(106, 61, 216), 45.0)
			});
			double[] array = new double[3] { 5.0, 8.0, 4.0 };
			foreach (double height in array)
			{
				stackPanel.Children.Add(new Border
				{
					Width = 2.0,
					Height = height,
					Margin = new Thickness(2.0, 0.0, 0.0, 0.0),
					CornerRadius = new CornerRadius(1.0),
					Background = new SolidColorBrush(color2),
					VerticalAlignment = VerticalAlignment.Center
				});
			}
			border.Child = stackPanel;
			grid.Children.Add(border);
			StackPanel stackPanel2 = new StackPanel
			{
				Width = 112.0
			};
			stackPanel2.Children.Add(grid);
			stackPanel2.Children.Add(new TextBlock
			{
				Text = th.Name,
				FontSize = 12.0,
				Margin = new Thickness(2.0, 6.0, 0.0, 0.0),
				HorizontalAlignment = HorizontalAlignment.Center
			});
			Border tile = new Border
			{
				Child = stackPanel2,
				Padding = new Thickness(6.0),
				Margin = new Thickness(0.0, 0.0, 8.0, 8.0),
				CornerRadius = new CornerRadius(12.0),
				Background = (Brush)FindResource("T.Field"),
				BorderThickness = new Thickness(1.5),
				BorderBrush = (IsCurrent(th) ? ((Brush)FindResource("T.Accent")) : Brushes.Transparent),
				Cursor = Cursors.Hand,
				ToolTip = "Применить тему «" + th.Name + "»",
				RenderTransformOrigin = new Point(0.5, 0.5),
				RenderTransform = new ScaleTransform()
			};
			AutomationName(tile, "Тема " + th.Name);
			tile.MouseEnter += (object _, MouseEventArgs _) =>
			{
				tile.Background = (Brush)FindResource("T.FieldHover");
			};
			tile.MouseLeave += (object _, MouseEventArgs _) =>
			{
				tile.Background = (Brush)FindResource("T.Field");
			};
			tile.MouseLeftButtonUp += (object _, MouseButtonEventArgs _) =>
			{
				Pop(tile);
				S.IslandColor = th.Island;
				S.Accent = th.Accent;
				S.CornerRadius = th.Corner;
				S.NeckScale = th.Neck;
				S.Outline = th.Outline;
				S.Shadow = th.Shadow;
				S.AnimationStyle = th.Open;
				S.HideAnimation = true;
				S.HideStyle = th.Hide;
				S.RimAnimation = th.Rim;
				S.WaterEffects = th.Water;
				S.WaterStrength = th.Wave;
				Apply(previewOnly);
				RebuildPage();
				Dispatcher.BeginInvoke((Action)(() =>
				{
					if (_previews.Count > 0)
					{
						_previews[0].Play(th.Open);
					}
				}), DispatcherPriority.Loaded);
			};
			wrapPanel.Children.Add(tile);
		}
		return wrapPanel;
	}

	private void BuildMusic()
	{
		Group("Плеер");
		Card(SwitchRow("Громкость плеера", "Линия под треком. Меняет громкость только этого приложения", S.PlayerVolume, (bool v) =>
		{
			S.PlayerVolume = v;
		}, "\ue767"));
		Hint("Яндекс Браузер — в «Экспериментальном».");
		Grid? glowStrength = null;
		ProGroup("Оформление плеера", preview: false, () => new UIElement[5]
		{
			ChoiceRow("Фон", "Живой фон из обложки на свёрнутом и раскрытом острове", new (string, string)[3]
			{
				("Размытая обложка", "blur"),
				("Сияние", "aurora"),
				("Без фона", "none")
			}, S.MusicBackdrop, (string v) =>
			{
				S.MusicBackdrop = v;
			}, "\ue790"),
			SliderRow("Окраска цветом обложки", "Насколько остров окрашивается в цвет трека", 0.0, 1.0, 0.05, S.MusicTint, (double v) => (!(v < 0.01)) ? $"{v * 100.0:0}%" : "нет", (double v) =>
			{
				S.MusicTint = v;
			}, "\ue790"),
			ChoiceRow("Анимация музыки", "Движение раскрытого плеера в такт", new (string, string)[3]
			{
				("Пульс и полоски", "full"),
				("Только пульс", "pulse"),
				("Без анимации", "off")
			}, S.MusicVisual, (string v) =>
			{
				S.MusicVisual = v;
			}, "\ue8d6"),
			SwitchRow("Свечение вокруг острова", "Пока играет музыка, свечение цветом трека", S.Glow, (bool v) =>
			{
				S.Glow = v;
				if (glowStrength != null)
				{
					Show(glowStrength, v);
				}
			}, "\ue706"),
			(glowStrength = SliderRow("Яркость свечения", null, 0.1, 1.0, 0.05, S.GlowStrength, (double v) => $"{v * 100.0:0}%", (double v) =>
			{
				S.GlowStrength = v;
			}, "\ue706"))
		});
		Show(glowStrength, S.Glow);
		ProGroup("Тексты песен", preview: false, () => new UIElement[3]
		{
			SwitchRow("Синхронный текст", "Строки с lrclib.net и NetEase Music. Отправляются только название, исполнитель и длина трека", S.Lyrics, (bool v) =>
			{
				S.Lyrics = v;
			}, "\ue8d6"),
			SwitchRow("Строка песни на свёрнутом острове", "Иначе там название трека, текст только в раскрытом плеере. Тот же переключатель в «Острове»", S.LyricsInCompact, (bool v) =>
			{
				S.LyricsInCompact = v;
			}, "\ue7f4"),
			SliderRow("Сдвиг текста", "Если строки спешат или опаздывают. Быстрее — колёсиком над строкой в плеере. Bluetooth-наушники опаздывают на 0,2–0,3 с: сдвиньте текст позже", -1500.0, 1500.0, 50.0, S.LyricsOffsetMs, (double v) =>
			{
				string result = ((!(Math.Abs(v) < 25.0)) ? ((v > 0.0) ? $"раньше на {v / 1000.0:0.##} с" : $"позже на {(0.0 - v) / 1000.0:0.##} с") : "точно");
				return result;
			}, (double v) =>
			{
				S.LyricsOffsetMs = (int)Math.Round(v);
			}, "\ue916")
		});
		Group("История и сохранённое");
		Card(SwitchRow("Запоминать треки", "Последние 100 треков и сколько вы слушали музыку (для «Итогов») в списке плеера, значок часов. Лежат только на этом компьютере", S.TrackHistory, (bool v) =>
		{
			S.TrackHistory = v;
		}, "\ue81c"), ButtonRow("Очистить историю", "Удалит список последних треков и итоги", "Очистить", () =>
		{
			TrackStore.Current.ClearHistory();
			ListenStats.Current.Clear();
		}, "\ue74d"));
	}

	private void BuildAlerts()
	{
		Card(SliderRow("Длительность показа", "Сколько держатся короткие плашки: громкость, батарея, сеть", 1.0, 6.0, 0.1, S.ToastSeconds, (double v) => $"{v:0.0} с", (double v) =>
		{
			S.ToastSeconds = v;
		}, "\ue916"));
		Group("Системные");
		Card(SwitchRow("Громкость", "Плашка громкости. Колёсико над ней меняет звук", S.ToastVolume, (bool v) =>
		{
			S.ToastVolume = v;
		}, "\ue767"), SwitchRow("Вместо ползунка Windows", "Стандартный ползунок не появляется, громкость только на острове. Пока остров скрыт, работает обычный", S.ReplaceVolumeOsd, (bool v) =>
		{
			S.ReplaceVolumeOsd = v;
		}, "\ue767"), SwitchRow("Батарея и зарядка", "Плашка заряда", S.ToastBattery, (bool v) =>
		{
			S.ToastBattery = v;
		}, "\ue83f"), SwitchRow("Наушники и Bluetooth", "Подключение, отключение, смена аудиовыхода", S.ToastDevices, (bool v) =>
		{
			S.ToastDevices = v;
		}, "\ue702"), SwitchRow("Сеть и VPN", "Смена Wi‑Fi, подключение и отключение VPN", S.ToastNetwork, (bool v) =>
		{
			S.ToastNetwork = v;
		}, "\ue701"), SwitchRow("Caps Lock", "Плашка на пару секунд при нажатии. Постоянный значок — в «Острове»", S.ToastCapsLock, (bool v) =>
		{
			S.ToastCapsLock = v;
		}, "\ue765"), SwitchRow("Копирование", "Что скопировано и действия: QR-код, в заметки, на полку, сохранить картинку", S.ToastCopy, (bool v) =>
		{
			S.ToastCopy = v;
		}, "\ue8c8"));
		Group("Активности");
		Button button = new Button
		{
			Content = "Подключить календари",
			Style = (Style)FindResource("T.GhostButton")
		};
		button.Click += (object _, RoutedEventArgs _) =>
		{
			OpenPage("tabs");
			ScrollToSection("Календарь");
		};
		Grid grid = SwitchRow("Напоминания о событиях", "За 10 минут до начала, с кнопкой «Подключиться»", S.CalendarReminders, (bool v) =>
		{
			S.CalendarReminders = v;
		}, "\ue787");
		grid.Children.OfType<StackPanel>().First().Children.Add(button);
		button.HorizontalAlignment = HorizontalAlignment.Left;
		button.Margin = new Thickness(-14.0, 4.0, 0.0, 0.0);
		Card(SwitchRow("Загрузки", "Прогресс скачивания, в конце плашка с файлом", S.Downloads, (bool v) =>
		{
			S.Downloads = v;
		}, "\ue896"), grid);
	}

	private void BuildTabs()
	{
		Group("Мои вкладки");
		StackPanel stackPanel = new StackPanel();
		List<string> order = S.TabOrder;
		foreach (var item in TabList.OrderBy(((string Id, string Title, bool Pro, string Section) t) =>
		{
			int num = order.IndexOf(t.Id);
			return (num >= 0) ? num : int.MaxValue;
		}))
		{
			stackPanel.Children.Add(TabListRow(item, stackPanel));
		}
		Button button = new Button
		{
			Content = "Сбросить порядок",
			Style = (Style)FindResource("T.GhostButton"),
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(6.0, 4.0, 0.0, 8.0)
		};
		button.Click += (object _, RoutedEventArgs _) =>
		{
			S.TabOrder.Clear();
			Commit();
			OpenPage("tabs");
		};
		StackPanel stackPanel2 = new StackPanel();
		stackPanel2.Children.Add(stackPanel);
		stackPanel2.Children.Add(button);
		Card(stackPanel2);
		Hint("Порядок: тяните за ≡ или зажмите вкладку на острове. «Плеер» всегда на месте: музыка, без неё — часы и закреплённые программы.");
		Card(SwitchRow("Отметки на вкладках", "Красная точка или число, если что-то ждёт: уведомления, ответ чата, скорое событие календаря", S.TabBadges, (bool v) =>
		{
			S.TabBadges = v;
		}, "\uea8f"));
		Group("Буфер обмена");
		Card(SwitchRow("Хранить историю после перезапуска", "Тексты, ссылки, файлы, до 5 картинок в папке данных. Секретное от менеджеров паролей не записывается никогда", S.ClipPersist == true, (bool v) =>
		{
			S.ClipPersist = v;
		}, "\ue81c"));
		Group("Календарь");
		StackPanel stackPanel3 = new StackPanel();
		Card(stackPanel3);
		BuildFeeds(stackPanel3);
		Hint("Ссылка Google: «Настройки и общий доступ» → «Закрытый адрес в формате iCal». Подходят Outlook, Яндекс Календарь, Apple (.ics).");
		Group("Погода", pro: true);
		Border border = Card(CityRow());
		if (!Island.Services.License.IsPro)
		{
			border.IsEnabled = false;
			border.Opacity = 0.45;
		}
		BuildAiSection();
	}

	private Grid TabListRow((string Id, string Title, bool Pro, string? Section) tab, StackPanel list)
	{
		Grid row = new Grid
		{
			Margin = new Thickness(8.0, 4.0, 12.0, 4.0),
			MinHeight = 40.0,
			Tag = tab.Id,
			Background = Brushes.Transparent
		};
		row.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		row.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		row.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		row.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		TextBlock grip = new TextBlock
		{
			Text = "\ue700",
			FontFamily = (FontFamily)FindResource("T.Icons"),
			FontSize = 13.0,
			Foreground = (Brush)FindResource("T.Text3"),
			VerticalAlignment = VerticalAlignment.Center,
			Padding = new Thickness(8.0, 8.0, 10.0, 8.0),
			Cursor = Cursors.SizeNS,
			Background = Brushes.Transparent,
			ToolTip = "Тяните, чтобы поменять порядок"
		};
		row.Children.Add(grip);
		CheckBox box = new CheckBox
		{
			Style = (Style)FindResource("T.Switch"),
			IsChecked = !S.IsTabHidden(tab.Id),
			Margin = new Thickness(0.0, 0.0, 14.0, 0.0)
		};
		AutomationName(box, tab.Title);
		box.Click += (object _, RoutedEventArgs _) =>
		{
			S.HiddenTabs.Remove(tab.Id);
			if (box.IsChecked != true)
			{
				S.HiddenTabs.Add(tab.Id);
			}
			Commit();
		};
		Grid.SetColumn(box, 1);
		row.Children.Add(box);
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = tab.Title,
			FontSize = 13.5,
			VerticalAlignment = VerticalAlignment.Center
		});
		if (tab.Pro && !Island.Services.License.IsPro)
		{
			stackPanel.Children.Add(ProBadge());
		}
		Grid.SetColumn(stackPanel, 2);
		row.Children.Add(stackPanel);
		string? section = tab.Section;
		if (section != null)
		{
			Button button = new Button
			{
				Content = "\ue713",
				FontFamily = (FontFamily)FindResource("T.Icons"),
				Style = (Style)FindResource("T.GhostButton"),
				Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
				ToolTip = "Настройки вкладки"
			};
			button.Click += (object _, RoutedEventArgs _) =>
			{
				ScrollToSection(section);
			};
			Grid.SetColumn(button, 3);
			row.Children.Add(button);
		}
		HoverGlow(row);
		double startY = 0.0;
		double slot = 0.0;
		int from = 0;
		int target = 0;
		Brush? rest = null;
		DropShadowEffect lift = new DropShadowEffect
		{
			BlurRadius = 16.0,
			ShadowDepth = 3.0,
			Direction = 270.0,
			Opacity = 0.5,
			Color = Colors.Black
		};
		grip.MouseLeftButtonDown += (object _, MouseButtonEventArgs e) =>
		{
			grip.CaptureMouse();
			e.Handled = true;
			startY = e.GetPosition(list).Y;
			slot = row.ActualHeight + row.Margin.Top + row.Margin.Bottom;
			from = (target = list.Children.IndexOf(row));
			Panel.SetZIndex(row, 10);
			row.Effect = lift;
			rest = row.Background;
			row.Background = (Brush)FindResource("T.CardHover");
			MainWindow.Animate(row, UIElement.OpacityProperty, 0.96, 120, null);
		};
		grip.MouseMove += (object _, MouseEventArgs e) =>
		{
			if (grip.IsMouseCaptured)
			{
				double value = e.GetPosition(list).Y - startY;
				value = Math.Clamp(value, (double)(-from) * slot, (double)(list.Children.Count - 1 - from) * slot);
				TranslateTransform translateTransform = Shift(row);
				translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
				translateTransform.Y = value;
				int num = Math.Clamp(from + (int)Math.Round(value / slot), 0, list.Children.Count - 1);
				if (num != target)
				{
					target = num;
					for (int i = 0; i < list.Children.Count; i++)
					{
						UIElement uIElement = list.Children[i];
						if (uIElement != row)
						{
							double to = ((from < target && i > from && i <= target) ? (0.0 - slot) : ((from > target && i >= target && i < from) ? slot : 0.0));
							MainWindow.Animate(Shift(uIElement), TranslateTransform.YProperty, to, 200, new CubicEase
							{
								EasingMode = EasingMode.EaseOut
							});
						}
					}
				}
			}
		};
		grip.MouseLeftButtonUp += (object _, MouseButtonEventArgs _) =>
		{
			if (grip.IsMouseCaptured)
			{
				grip.ReleaseMouseCapture();
				DoubleAnimation doubleAnimation = new DoubleAnimation((double)(target - from) * slot, TimeSpan.FromMilliseconds(160L))
				{
					EasingFunction = new CubicEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				doubleAnimation.Completed += (object? obj, EventArgs e2) =>
				{
					foreach (UIElement child in list.Children)
					{
						Shift(child).BeginAnimation(TranslateTransform.YProperty, null);
						Shift(child).Y = 0.0;
					}
					if (target != from)
					{
						list.Children.Remove(row);
						list.Children.Insert(target, row);
						SaveTabOrder(list);
					}
					Panel.SetZIndex(row, 0);
					row.Effect = null;
					row.Background = rest;
					MainWindow.Animate(row, UIElement.OpacityProperty, 1.0, 120, null);
				};
				Shift(row).BeginAnimation(TranslateTransform.YProperty, doubleAnimation);
			}
		};
		return row;
		static TranslateTransform Shift(UIElement e)
		{
			TranslateTransform? translateTransform = e.RenderTransform as TranslateTransform;
			if (translateTransform == null)
			{
				translateTransform = (TranslateTransform)(e.RenderTransform = new TranslateTransform());
			}
			return translateTransform;
		}
	}

	private static void SaveTabOrder(StackPanel list)
	{
		List<string> list2 = (from g in list.Children.OfType<Grid>()
			select (string)g.Tag).ToList();
		List<string> list3 = ((S.TabOrder.Count > 0) ? S.TabOrder.ToList() : new List<string> { "home", "shelf", "clip", "text", "calendar", "weather", "ai", "phone", "monitor" });
		foreach (string item in new string[1] { "home" }.Concat(list2))
		{
			if (!list3.Contains(item))
			{
				list3.Add(item);
			}
		}
		int num = 0;
		for (int num2 = 0; num2 < list3.Count; num2++)
		{
			if (list3[num2] != "home" && list2.Contains(list3[num2]))
			{
				list3[num2] = list2[num++];
			}
		}
		S.TabOrder = list3;
		Commit();
	}

	private void BuildAiSection()
	{
		AiProvider provider = AiProviders.Get(S.AiProvider);
		Group("ИИ-чат", pro: true);
		StackPanel stackPanel = new StackPanel();
		Border border = Card(stackPanel);
		if (!Island.Services.License.IsPro)
		{
			border.IsEnabled = false;
			border.Opacity = 0.45;
		}
		stackPanel.Children.Add(ChoiceRow("Сервис", "OpenRouter, Groq, Gemini: есть бесплатные модели. DeepSeek, Claude, ChatGPT: платные, нужен ключ с балансом. Свой сервер: любой API как у OpenAI (прокси, Ollama, LM Studio)", new (string, string)[7]
		{
			("OpenRouter — рекомендуем", "openrouter"),
			("Groq", "groq"),
			("Google Gemini", "gemini"),
			("DeepSeek", "deepseek"),
			("Claude", "anthropic"),
			("ChatGPT", "openai"),
			("Свой сервер", "custom")
		}, provider.Id, (string v) =>
		{
			S.AiProvider = v;
			S.AiModel = null;
			Dispatcher.BeginInvoke((Action)(() =>
			{
				OpenPage("tabs");
				ScrollToSection("ИИ-чат");
			}));
		}, "\ue774"));
		TextBlock status = new TextBlock
		{
			FontSize = 12.0,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
			Visibility = Visibility.Collapsed
		};
		WrapPanel models = new WrapPanel
		{
			Margin = new Thickness(0.0, 8.0, 0.0, -6.0)
		};
		bool custom = provider.Id == AiProviders.Custom.Id;
		Grid modelRow = Row("Модель", provider.Free ? "Список бесплатных моделей сервиса" : "Модели этого сервиса", null, "\ue8bd");
		modelRow.Children.OfType<StackPanel>().First().Children.Add(models);
		if (custom)
		{
			TextBox url = new TextBox
			{
				Style = (Style)FindResource("T.TextBox"),
				Tag = "http://localhost:11434/v1",
				Text = (S.AiCustomUrl ?? ""),
				Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
			};
			AutomationName(url, "Адрес сервера");
			DispatcherTimer urlWait = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(700L)
			};
			urlWait.Tick += (object? _, EventArgs _) =>
			{
				urlWait.Stop();
				S.AiCustomUrl = ((url.Text.Trim().Length > 0) ? url.Text.Trim() : null);
				Commit();
				string? text4 = AiProviders.Key(AiProviders.Get(S.AiProvider));
				if (text4 != null)
				{
					Check(text4, save: false);
				}
			};
			url.TextChanged += (object _, TextChangedEventArgs _) =>
			{
				status.Visibility = Visibility.Collapsed;
				urlWait.Stop();
				urlWait.Start();
			};
			Grid grid = Row("Адрес сервера", "Где у сервера API в стиле OpenAI, обычно заканчивается на /v1", null, "\ue774");
			grid.Children.OfType<StackPanel>().First().Children.Add(url);
			stackPanel.Children.Add(Divider());
			stackPanel.Children.Add(grid);
			TextBox model = new TextBox
			{
				Style = (Style)FindResource("T.TextBox"),
				Tag = "например llama3.1",
				Text = (S.AiModel ?? ""),
				Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
			};
			AutomationName(model, "Модель");
			model.LostKeyboardFocus += (object _, KeyboardFocusChangedEventArgs _) =>
			{
				S.AiModel = ((model.Text.Trim().Length > 0) ? model.Text.Trim() : null);
				Commit();
			};
			Grid grid2 = Row("Модель вручную", "Если сервер не показал список ниже — впишите название модели", null, "\ue70f");
			grid2.Children.OfType<StackPanel>().First().Children.Add(model);
			stackPanel.Children.Add(Divider());
			stackPanel.Children.Add(grid2);
		}
		string? text = AiKeyStore.Load(provider);
		Grid grid3;
		if (text != null)
		{
			string text2;
			if (text.Length <= 14)
			{
				text2 = "•••";
			}
			else
			{
				text2 = text.Substring(0, 8) + "…" + text.Substring(text.Length - 4);
			}
			string text3 = text2;
			Button button = new Button
			{
				Content = "Удалить",
				Style = (Style)FindResource("T.GhostButton")
			};
			button.Click += (object _, RoutedEventArgs _) =>
			{
				AiKeyStore.Save(provider, null);
				Settings.NotifyChanged();
				OpenPage("tabs");
				ScrollToSection("ИИ-чат");
			};
			grid3 = Row("API-ключ " + text3, "Шифруется для учётной записи Windows", button, "\ue8d7");
			if (Island.Services.License.IsPro)
			{
				Check(text, save: false);
			}
		}
		else
		{
			TextBox box = new TextBox
			{
				Style = (Style)FindResource("T.TextBox"),
				Tag = (custom ? "не обязательно" : (provider.KeyPrefix + "…")),
				Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
			};
			AutomationName(box, "API-ключ");
			DispatcherTimer wait = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(600L)
			};
			wait.Tick += (object? _, EventArgs _) =>
			{
				wait.Stop();
				string text4 = box.Text.Trim();
				if (text4.Length >= (custom ? 1 : 20))
				{
					Check(text4, save: true);
				}
			};
			box.TextChanged += (object _, TextChangedEventArgs _) =>
			{
				status.Visibility = Visibility.Collapsed;
				wait.Stop();
				wait.Start();
			};
			grid3 = Row("API-ключ", custom ? "Если сервер просит ключ — вставьте его. Ollama и LM Studio обходятся без ключа" : "Вставьте ключ, который выдал сервис, — он проверится сам", null, "\ue8d7");
			grid3.Children.OfType<StackPanel>().First().Children.Add(box);
			if (custom && Island.Services.License.IsPro && !string.IsNullOrWhiteSpace(S.AiCustomUrl))
			{
				Check("", save: false);
			}
		}
		grid3.Children.OfType<StackPanel>().First().Children.Add(status);
		stackPanel.Children.Add(Divider());
		stackPanel.Children.Add(grid3);
		StackPanel guide = AiGuide(provider);
		guide.Visibility = Visibility.Collapsed;
		string ask = (provider.Free ? "Нет ключа? Получить бесплатно  ▾" : "Нет ключа? Как получить  ▾");
		Button toggle = new Button
		{
			Content = ask,
			Style = (Style)FindResource("T.GhostButton"),
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(8.0, 0.0, 0.0, 8.0)
		};
		toggle.Click += (object _, RoutedEventArgs _) =>
		{
			bool flag = guide.Visibility != Visibility.Visible;
			Show(guide, flag);
			toggle.Content = (flag ? "Скрыть инструкцию  ▴" : ask);
		};
		if (text == null && !custom)
		{
			stackPanel.Children.Add(toggle);
			stackPanel.Children.Add(guide);
		}
		stackPanel.Children.Add(Divider());
		stackPanel.Children.Add(modelRow);
		modelRow.Visibility = Visibility.Collapsed;
		stackPanel.Children.Add(Divider());
		stackPanel.Children.Add(SwitchRow("Короткие ответы", "Просит модель отвечать в несколько предложений", S.AiShortAnswers, (bool v) =>
		{
			S.AiShortAnswers = v;
		}, "\ue8bd"));
		Hint("Сообщения идут сервису напрямую с ПК. Island их не хранит, история стирается при закрытии острова. Не отправляйте пароли и личные данные.");
		async void Check(string key, bool save)
		{
			AiProvider p = AiProviders.Get(S.AiProvider);
			status.Foreground = (Brush)FindResource("T.Text2");
			status.Text = (custom ? "Проверяю сервер…" : "Проверяю ключ…");
			status.Visibility = Visibility.Visible;
			try
			{
				List<string> list = await AiService.ListModelsAsync(p, key);
				if (save)
				{
					AiKeyStore.Save(p, key);
					Settings.NotifyChanged();
				}
				string text4;
				if (custom)
				{
					text4 = $"Сервер отвечает — моделей: {list.Count}";
				}
				else
				{
					text4 = (p.Free ? $"Ключ работает — бесплатных моделей: {list.Count}" : $"Ключ работает — моделей: {list.Count}");
				}
				Say(text4, bad: false);
				if (list.Count > 0)
				{
					ShowModels(list);
				}
			}
			catch (AiException ex)
			{
				Say(ex.Message, bad: true);
				Show(modelRow, on: false);
			}
			catch (Exception)
			{
				Say(custom ? "Сервер не отвечает — проверьте адрес и что он запущен" : "Нет связи с сервисом — проверьте интернет или VPN", bad: true);
				if (save)
				{
					AiKeyStore.Save(p, key);
				}
			}
		}
		void Say(string text4, bool bad)
		{
			status.Text = (bad ? "✗ " : "✓ ") + text4;
			status.Foreground = (bad ? ((Brush)FindResource("T.Danger")) : new SolidColorBrush(Color.FromRgb(94, 216, 122)));
			status.Visibility = Visibility.Visible;
		}
		void ShowModels(List<string> list)
		{
			models.Children.Clear();
			string groupName = Guid.NewGuid().ToString();
			List<(string, string)> list2 = new List<(string, string)>();
			list2.Add(("По умолчанию", null));
			list2.AddRange(from m in list.Take(18)
				select (MainWindow.ShortModel(m), m));
			foreach (var item2 in list2)
			{
				string item = item2.Item1;
				string id = item2.Item2;
				RadioButton radioButton = new RadioButton
				{
					Style = (Style)FindResource("T.Chip"),
					Content = item,
					GroupName = groupName,
					IsChecked = (id == S.AiModel),
					ToolTip = id
				};
				radioButton.Checked += (object _, RoutedEventArgs _) =>
				{
					S.AiModel = id;
					Commit();
				};
				models.Children.Add(radioButton);
			}
			Show(modelRow, on: true);
		}
	}

	private Border Divider()
	{
		return new Border
		{
			Height = 1.0,
			Background = (Brush)FindResource("T.Divider"),
			Margin = new Thickness(16.0, 0.0, 16.0, 0.0)
		};
	}

	private StackPanel AiGuide(AiProvider provider)
	{
		((string, string)[], string) tuple = provider.Id switch
		{
			"groq" => (new (string, string)[3]
			{
				("Откройте сайт Groq", "Войдите через Google, GitHub или почту — карта не нужна"),
				("API Keys → Create API Key", "Название — любое, например «Island»"),
				("Скопируйте ключ", "Он начинается с gsk_ и показывается один раз. Вставьте его в поле выше")
			}, "Все модели Groq бесплатны в пределах лимитов в минуту и в день, ответы очень быстрые. В некоторых странах сервис открывается только через VPN."), 
			"gemini" => (new (string, string)[3]
			{
				("Откройте Google AI Studio", "Войдите в аккаунт Google"),
				("Нажмите «Create API key»", "Если спросит проект — выберите любой или создайте новый"),
				("Скопируйте ключ", "Он начинается с AIza. Вставьте его в поле выше")
			}, "Бесплатный уровень Gemini API с дневными лимитами. В некоторых странах, в том числе в России, сервис работает только через VPN."), 
			"deepseek" => (new (string, string)[3]
			{
				("Откройте платформу DeepSeek", "Войдите по почте или через Google"),
				("Пополните баланс", "Раздел Top up. Запросы платные, но недорогие"),
				("API keys → Create new API key", "Ключ начинается с sk- и показывается один раз. Вставьте его в поле выше")
			}, "Без денег на балансе DeepSeek отвечает ошибкой. Island сам выберет модель deepseek-chat."), 
			"anthropic" => (new (string, string)[3]
			{
				("Откройте консоль Anthropic", "Войдите или зарегистрируйтесь"),
				("Plans & Billing → пополните баланс", "API оплачивается отдельно от подписки Claude Pro"),
				("API Keys → Create Key", "Ключ начинается с sk-ant- и показывается один раз. Вставьте его в поле выше")
			}, "Island сам выберет самую новую модель Sonnet. В некоторых странах, в том числе в России, сервис работает только через VPN."), 
			"openai" => (new (string, string)[3]
			{
				("Откройте платформу OpenAI", "Войдите в аккаунт OpenAI"),
				("Billing → пополните баланс", "API оплачивается отдельно от подписки ChatGPT Plus"),
				("API keys → Create new secret key", "Ключ начинается с sk- и показывается один раз. Вставьте его в поле выше")
			}, "В некоторых странах, в том числе в России, сервис работает только через VPN."), 
			_ => (new (string, string)[3]
			{
				("Откройте OpenRouter", "Войдите через Google, GitHub или почту — карта не нужна"),
				("Keys → Create Key", "Название — любое, лимит можно не задавать"),
				("Скопируйте ключ", "Он начинается с sk-or- и показывается один раз. Вставьте его в поле выше")
			}, "Бесплатные модели помечены «:free» — остров показывает только их. Без пополнения баланса доступно около 50 запросов в день; лимиты задаёт OpenRouter, и они могут меняться."), 
		};
		(string, string)[] item = tuple.Item1;
		string item2 = tuple.Item2;
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
		};
		for (int i = 0; i < item.Length; i++)
		{
			var (value, description) = item[i];
			stackPanel.Children.Add((i == 0) ? ButtonRow($"{i + 1}. {value}", description, "Открыть сайт", () =>
			{
				ReviewSafety.Start(new ProcessStartInfo(provider.KeysUrl)
				{
					UseShellExecute = true
				});
			}, "\ue774") : Row($"{i + 1}. {value}", description, null, (i == 1) ? "\ue710" : "\ue8c8"));
		}
		stackPanel.Children.Add(new TextBlock
		{
			Text = item2,
			FontSize = 12.0,
			Foreground = (Brush)FindResource("T.Text3"),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(62.0, 0.0, 16.0, 6.0)
		});
		return stackPanel;
	}

	private void BuildSystem()
	{
		Card(SwitchRow("Запускать вместе с Windows", null, Autostart.IsEnabled, (bool v) =>
		{
			Autostart.Set(v);
		}, "\ue7e8"));
		int hz = Win32.GetRefreshRate();
		Group("Производительность");
		Card(SliderRow("Частота кадров", $"Справа без ограничения, как у монитора ({hz} Гц). 30 — для слабых ПК. Эффекты меняются сразу, анимации раскрытия — после перезапуска", 30.0, 250.0, 10.0, (S.MaxFps == 0) ? 250 : S.MaxFps, (double v) => (v >= 245.0) ? $"макс · {hz}" : $"{v:0} к/с", (double v) =>
		{
			S.MaxFps = ((!(v >= 245.0)) ? ((int)v) : 0);
		}, "\ue9f5"), ButtonRow("Папка с данными", "Настройки, заметки, задачи, календарь, журнал ошибок", "Открыть", () =>
		{
			ReviewSafety.Start(new ProcessStartInfo(Settings.Folder)
			{
				UseShellExecute = true
			});
		}, "\ue8b7"));
		BuildProxy();
		Group("Обновления");
		StackPanel stackPanel = new StackPanel();
		Card(stackPanel);
		BuildUpdates(stackPanel);
		Group("Сброс");
		Button reset = new Button
		{
			Content = "Сбросить",
			Style = (Style)FindResource("T.Button")
		};
		DispatcherTimer undo = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(4L)
		};
		undo.Tick += (object? _, EventArgs _) =>
		{
			undo.Stop();
			reset.Content = "Сбросить";
		};
		reset.Click += (object _, RoutedEventArgs _) =>
		{
			if (!undo.IsEnabled)
			{
				reset.Content = "Точно сбросить?";
				undo.Start();
			}
			else
			{
				undo.Stop();
				S.ResetAll();
				OpenPage("system");
			}
		};
		Card(Row("Сбросить все настройки", "Всё как после установки. Ключ Pro, полка, закреплённые программы и календари останутся", reset, "\ue72c"));
	}

	private void BuildProxy()
	{
		Group("Подключение");
		TextBox box = new TextBox
		{
			Style = (Style)FindResource("T.TextBox"),
			Tag = "127.0.0.1:8080",
			Text = S.ProxyAddress,
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		AutomationName(box, "Адрес прокси");
		Grid grid = Row("Прокси", "Для погоды и текстов песен. Пусто — напрямую. ИИ-чат ходит как Windows, без этого прокси", null, "\ue774");
		TextBlock hint = new TextBlock
		{
			Text = "HTTP или SOCKS5 без логина и пароля: 127.0.0.1:8080 (HTTP) или socks5://127.0.0.1:1080",
			FontSize = 12.0,
			Foreground = (Brush)FindResource("T.Text3"),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
		};
		StackPanel stackPanel = grid.Children.OfType<StackPanel>().First();
		stackPanel.Children.Add(box);
		stackPanel.Children.Add(hint);
		box.LostFocus += (object _, RoutedEventArgs _) =>
		{
			Save();
		};
		box.KeyDown += (object _, KeyEventArgs e) =>
		{
			if (e.Key == Key.Return)
			{
				Save();
			}
		};
		Card(grid);
		void Save()
		{
			string text = box.Text.Trim();
			bool flag = text.Length == 0 || NetProxy.Parse(text) != null;
			hint.Text = (flag ? "HTTP или SOCKS5 без логина и пароля: 127.0.0.1:8080 (HTTP) или socks5://127.0.0.1:1080" : "Не похоже на адрес: нужно «адрес:порт» или «socks5://адрес:порт»");
			hint.Foreground = (Brush)FindResource(flag ? "T.Text3" : "T.Danger");
			if (flag && !(text == S.ProxyAddress))
			{
				S.ProxyAddress = text;
				CommitSoon();
			}
		}
	}

	private void BuildLab()
	{
		Hint("Работает, но не везде или не всегда точно. Может измениться.");
		Group("Музыка");
		Card(SwitchRow("Музыка из Яндекс Браузера", "Браузер не сообщает Windows о треке. Остров слышит звук и находит вкладку со значком звука. ВКонтакте: исполнитель, песня, кнопки, пока вкладка открыта. Нет длины трека и перемотки", S.BrowserAudio, (bool v) =>
		{
			S.BrowserAudio = v;
		}, "\ue774"), WithBadge(SwitchRow("Круги под биты", "Вместо фона световые круги от обложки на каждый удар. Удары ловятся по звуку, иногда пропускаются", S.ExpRipples, (bool v) =>
		{
			S.ExpRipples = v;
		}, "\ue8d6")));
	}

	private void BuildHelp()
	{
		Group("Основное");
		Card(Row("Раскрыть остров", "Наведите курсор, уведите — свернётся. Клик и задержка — в «Острове»", null, "\ue7c9"), Row("Вкладки", "Вверху раскрытого острова. Порядок: зажать и перетащить. Лишние прячутся в «Вкладках»", null, "\ue8a9"), Row("Настройки", "Шестерёнка справа от вкладок или значок Island в трее", null, "\ue713"), Row("Открепить остров", "Булавка справа от шестерёнки. Остров можно перетащить в любое место экрана: у верхнего края он держится как обычно, в остальных местах становится пилюлей. Раскрывается туда, где есть место: вниз, вверх, влево или вправо", null, "\ue718"), Row("Полноэкранные игры и видео", "Остров прячется сам. Выключается в «Остров» → «Скрытие»", null, "\ue740"));
		Group("Мышь: скрытые жесты");
		Card(Row("Правая кнопка мыши", "Меню у файлов полки, закреплённых программ, задач, событий. В «Буфере» удаляет запись", null, "\ue712"), Row("Перетаскивание", "Файлы на свёрнутый остров идут на «Полку». Программу или файл на панель под часами (без музыки) остров закрепит", null, "\ue8b7"), Row("Колёсико", "Над линией и плашкой громкости меняет звук. Над строкой песни сдвигает текст. Над закреплёнными программами листает их", null, "\ue767"), Row("Двойной клик", "По дню в календаре: расписание дня. По обложке трека: пауза", null, "\ue787"), Row("Обложка трека", "Свайп влево или вправо: следующий или предыдущий трек. Колёсико: громкость плеера. Правая кнопка или клик по названию: о треке (альбом, жанр, источник, кнопка «Открыть плеер»)", null, "\ue8d6"), Row("Полоса прогресса трека", "Клик или перетаскивание перематывает. Если плеер не умеет, полоса мигнёт", null, "\ue768"));
		Group("Музыка");
		Card(Row("Откуда берётся трек", "Из системного плеера Windows: Spotify, Яндекс Музыка, браузеры, Telegram — всё с обложкой в меню громкости. Яндекс Браузер — в «Экспериментальном»", null, "\ue8d6"), Row("Текст песни", "Из открытых баз, подсветка по словам. Есть не для всех песен. Сдвиг: колёсико над строкой или «Сдвиг текста» в «Музыке»", null, "\ue8d6"), Row("Telegram и некоторые сайты", "Не сообщают длину трека: время считает остров, перемотки нет. «Назад» и «вперёд» тускнеют, когда листать некуда", null, "\ue8bd"));
		Group("Как спрятать остров");
		Card(Row("На время", "Трей → «Скрыть остров» или своя клавиша («Остров» → «Скрытие»): нажать — спрятать или вернуть, держать — обратное", null, "\ue765"), Row("В отдельных программах", "«Остров» → «Прятать в приложениях»: пока такое окно впереди, острова не видно", null, "\ue7b3"));
		Group("Активности и уведомления");
		Card(Row("Загрузки", "Пока браузер или Telegram качает в «Загрузки»: размер, скорость, в конце кнопка «Показать»", null, "\ue896"), Row("Устройства", "Наушники, мышь, Bluetooth, смена Wi‑Fi и VPN, Caps Lock", null, "\ue702"), Row("Плашки с действиями", "После копирования: ссылку открыть, текст в QR-код или заметки, файл на полку, картинку сохранить. Пока курсор на плашке, она не исчезает", null, "\ue8c8"), Row("Значок можно перетащить", "Значок скачанного или скопированного файла и есть файл: тащите в окно или папку", null, "\ue8b7"));
		Group("Если что-то не работает");
		Card(ButtonRow("Собрать журнал для разработчика", "Архив на рабочем столе: журнал работы, настройки без ключей, адресов и файлов, данные о системе. Отправьте его в поддержку", "Собрать", () =>
		{
			string? text = Log.CreateReport();
			if (text != null)
			{
				ReviewSafety.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + text + "\"")
				{
					UseShellExecute = true
				});
			}
			else
			{
				MessageBox.Show("Не получилось собрать архив. Журнал лежит в папке данных (кнопка «Папка с данными» в «Остров»)", "Island");
			}
		}, "\ue9d9"));
		Group("Подсказки");
		Card(ButtonRow("Показать подсказки заново", "При первом открытии вкладки снова появится подсказка", "Сбросить", () =>
		{
			S.ResetTips();
		}, "\ue82f"));
	}

	private void BuildLicense()
	{
		if (DeveloperAccess.Enabled)
		{
			Card(Row(DeveloperBranding.CardTitle + " · " + DeveloperBranding.Subtitle, "Вьебано богом", null, "\ue735"));
			return;
		}
		StackPanel keyArea = new StackPanel
		{
			Visibility = Visibility.Collapsed,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		BuildKeyEntry(keyArea);
		string title;
		string description;
		if (DeveloperBranding.IsDeveloper)
		{
			title = DeveloperBranding.CardTitle;
			description = DeveloperBranding.Subtitle + ". Это статус сборки, не активация: проверки лицензии и доступ к функциям сохранены.";
		}
		else if (Island.Services.License.HasKey)
		{
			DateTime? expires = Island.Services.License.Expires;
			string text;
			if (expires.HasValue)
			{
				DateTime valueOrDefault = expires.GetValueOrDefault();
				text = $"Island Pro до {valueOrDefault.ToLocalTime():dd.MM.yyyy}";
			}
			else
			{
				text = "Island Pro навсегда";
			}
			title = text;
			description = "Все функции открыты";
		}
		else if (Island.Services.License.IsTrial)
		{
			title = "Пробный период: осталось " + FormatLeft(Island.Services.License.TrialEnds - DateTime.UtcNow);
			description = $"Все функции Pro открыты до {Island.Services.License.TrialEnds.ToLocalTime():dd.MM HH:mm}. Чтобы они остались и потом, нужен ключ";
		}
		else
		{
			title = "Pro-функции отключены";
			description = "Пробный период закончился. Ключ из Telegram-бота вернёт их сразу";
		}
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center
		};
		if (Island.Services.License.HasKey)
		{
			Button button = new Button
			{
				Content = "\ue712",
				FontFamily = (FontFamily)FindResource("T.Icons"),
				Style = (Style)FindResource("T.GhostButton"),
				ToolTip = "Ещё"
			};
			Button remove = new Button
			{
				Content = "Удалить ключ",
				Style = (Style)FindResource("T.Button"),
				Visibility = Visibility.Collapsed,
				Margin = new Thickness(0.0, 0.0, 6.0, 0.0)
			};
			button.Click += (object _, RoutedEventArgs _) =>
			{
				remove.Visibility = ((remove.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible);
			};
			remove.Click += (object _, RoutedEventArgs _) =>
			{
				Island.Services.License.Deactivate();
				AfterLicenseChange();
			};
			stackPanel.Children.Add(remove);
			stackPanel.Children.Add(button);
		}
		else
		{
			Button button2 = new Button
			{
				Content = "Ввести ключ",
				Style = (Style)FindResource("T.Button"),
				Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
			};
			button2.Click += (object _, RoutedEventArgs _) =>
			{
				Show(keyArea, keyArea.Visibility != Visibility.Visible);
			};
			stackPanel.Children.Add(button2);
			string? url = Island.Services.License.BuyUrl;
			if (url != null)
			{
				Button button3 = new Button
				{
					Content = "Купить",
					Style = (Style)FindResource("T.AccentButton")
				};
				button3.Click += (object _, RoutedEventArgs _) =>
				{
					ReviewSafety.Start(new ProcessStartInfo(url)
					{
						UseShellExecute = true
					});
				};
				stackPanel.Children.Add(button3);
			}
		}
		Grid grid = Row(title, description, stackPanel, Island.Services.License.IsPro ? "\ue735" : "\ue734");
		grid.Children.OfType<StackPanel>().First().Children.Add(keyArea);
		Button copy = new Button
		{
			Content = "Скопировать",
			Style = (Style)FindResource("T.Button")
		};
		copy.Click += (object _, RoutedEventArgs _) =>
		{
			try
			{
				ReviewClipboard.SetText(DeviceId.Code);
				copy.Content = "Скопировано";
			}
			catch (Exception ex)
			{
				Log.Error("copy device code", ex);
			}
		};
		Card(grid, Row("Код компьютера · " + DeviceId.Code, "Ключ из бота работает только на ПК с этим кодом. «Купить» передаёт код боту сам. Переустановка Windows код не меняет", copy, "\ue770"));
		Group("Что входит");
		Card(ProLink("Оформление", "Цвет острова и акцент, масштаб, скругление, слияние с краем, кромка и тень", "look", "Цвет", "\ue790"), ProLink("Анимации", "Десять стилей раскрытия и живые эффекты: кромка, капля, притяжение к курсору", "look", "Анимация", "\ue768"), ProLink("Плеер", "Фон из обложки, окраска и свечение цветом трека, анимация в такт", "music", "Оформление плеера", "\ue8d6"), ProLink("Тексты песен", "Синхронно с треком, с подсветкой слов — и строкой на свёрнутом острове", "music", "Тексты песен", "\ue8d6"), ProLink("Вкладки «Текст», «Погода» и «ИИ-чат»", "Заметки, QR-коды и задачи; погода по часам; чат с бесплатными моделями", "tabs", "Мои вкладки", "\ue8a9"));
		Hint("Бесплатно — всё остальное: плеер и его громкость, полка, буфер обмена, календарь, загрузки, плашки с действиями, уведомления, закреплённые программы.");
	}

	private Grid ProLink(string title, string text, string page, string section, string glyph)
	{
		Button button = new Button
		{
			Content = "Открыть  →",
			Style = (Style)FindResource("T.GhostButton")
		};
		button.Click += (object _, RoutedEventArgs _) =>
		{
			OpenPage(page);
			ScrollToSection(section);
		};
		Grid grid = Row(title, text, button, glyph);
		HoverGlow(grid);
		return grid;
	}

	private void BuildKeyEntry(StackPanel host)
	{
		TextBox box = new TextBox
		{
			Style = (Style)FindResource("T.TextBox"),
			Tag = "ISLAND-XXXXXX-XXXXXX-…"
		};
		AutomationName(box, "Ключ Island Pro");
		Button button = new Button
		{
			Content = "Активировать",
			Style = (Style)FindResource("T.AccentButton"),
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		TextBlock error = new TextBlock
		{
			FontSize = 12.0,
			Foreground = (Brush)FindResource("T.Danger"),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
			Visibility = Visibility.Collapsed
		};
		button.Click += (object _, RoutedEventArgs _) =>
		{
			Activate();
		};
		box.KeyDown += (object _, KeyEventArgs e) =>
		{
			if (e.Key == Key.Return)
			{
				Activate();
			}
		};
		box.TextChanged += (object _, TextChangedEventArgs _) =>
		{
			error.Visibility = Visibility.Collapsed;
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
		grid.Children.Add(box);
		Grid.SetColumn(button, 1);
		grid.Children.Add(button);
		host.Children.Add(new TextBlock
		{
			Text = "Вставьте ключ целиком, как его прислал бот",
			FontSize = 12.0,
			Foreground = (Brush)FindResource("T.Text3"),
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
		});
		host.Children.Add(grid);
		host.Children.Add(error);
		void Activate()
		{
			string? text = Island.Services.License.Activate(box.Text);
			if (text != null)
			{
				error.Text = text;
				error.Visibility = Visibility.Visible;
			}
			else
			{
				AfterLicenseChange();
			}
		}
	}

	private Grid HotkeyRow()
	{
		Button button = new Button
		{
			Style = (Style)FindResource("T.Button"),
			MinWidth = 150.0
		};
		Button clear = new Button
		{
			Content = "Убрать",
			Style = (Style)FindResource("T.GhostButton"),
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		TextBlock note = new TextBlock
		{
			FontSize = 12.0,
			Foreground = (Brush)FindResource("T.Danger"),
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
			Visibility = Visibility.Collapsed
		};
		bool listening = false;
		button.Click += (object _, RoutedEventArgs _) =>
		{
			if (listening)
			{
				Stop();
			}
			else
			{
				listening = true;
				note.Visibility = Visibility.Collapsed;
				MainWindow.HotkeyPaused = true;
				PreviewKeyDown += OnKey;
				ShowKey();
			}
		};
		button.LostKeyboardFocus += (object _, KeyboardFocusChangedEventArgs _) =>
		{
			Stop();
		};
		clear.Click += (object _, RoutedEventArgs _) =>
		{
			S.HotkeyVk = 0;
			Commit();
			ShowKey();
		};
		ShowKey();
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		stackPanel.Children.Add(clear);
		stackPanel.Children.Add(button);
		Grid grid = Row("Горячая клавиша", "Нажмите кнопку, затем сочетание, например Ctrl + Alt + I", stackPanel, "\ue765");
		grid.Children.OfType<StackPanel>().First().Children.Add(note);
		return grid;
		void OnKey(object sender, KeyEventArgs e)
		{
			e.Handled = true;
			Key key = ((e.Key == Key.System) ? e.SystemKey : e.Key);
			if (((uint)(key - 70) > 1u && (uint)(key - 116) > 5u && key != Key.ImeProcessed) || 1 == 0)
			{
				if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
				{
					Stop();
				}
				else
				{
					bool flag = ((key == Key.Back || key == Key.Delete) ? true : false);
					if (flag && Keyboard.Modifiers == ModifierKeys.None)
					{
						S.HotkeyVk = 0;
						Commit();
						Stop();
					}
					else
					{
						int num = (int)((Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 1u : 0u) | (uint)(Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? 2 : 0) | (uint)(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 4 : 0)) | (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) ? 8 : 0);
						switch (key)
						{
						case Key.Tab:
						case Key.Return:
						case Key.Space:
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
						case Key.A:
						case Key.B:
						case Key.C:
						case Key.D:
						case Key.E:
						case Key.F:
						case Key.G:
						case Key.H:
						case Key.I:
						case Key.J:
						case Key.K:
						case Key.L:
						case Key.M:
						case Key.N:
						case Key.O:
						case Key.P:
						case Key.Q:
						case Key.R:
						case Key.S:
						case Key.T:
						case Key.U:
						case Key.V:
						case Key.W:
						case Key.X:
						case Key.Y:
						case Key.Z:
						case Key.Oem1:
						case Key.OemPlus:
						case Key.OemComma:
						case Key.OemMinus:
						case Key.OemPeriod:
						case Key.Oem2:
						case Key.Oem3:
						case Key.AbntC1:
						case Key.AbntC2:
						case Key.Oem4:
						case Key.Oem5:
						case Key.Oem6:
						case Key.Oem7:
						case Key.Oem8:
						case Key.Oem102:
							flag = true;
							break;
						default:
							flag = false;
							break;
						}
						if (flag && (num & -5) == 0)
						{
							note.Text = "Добавьте Ctrl, Alt или Win — одиночная буква перестала бы печататься";
							note.Visibility = Visibility.Visible;
						}
						else
						{
							note.Visibility = Visibility.Collapsed;
							S.HotkeyVk = KeyInterop.VirtualKeyFromKey(key);
							S.HotkeyMods = num;
							Commit();
							Stop();
						}
					}
				}
			}
		}
		void ShowKey()
		{
			button.Content = (listening ? "Нажмите клавиши…" : MainWindow.HotkeyText(S.HotkeyVk, S.HotkeyMods));
			clear.Visibility = ((S.HotkeyVk == 0 || listening) ? Visibility.Collapsed : Visibility.Visible);
		}
		void Stop()
		{
			if (listening)
			{
				listening = false;
				MainWindow.HotkeyPaused = false;
				PreviewKeyDown -= OnKey;
				ShowKey();
			}
		}
	}

	private void BuildUpdates(StackPanel host)
	{
		host.Children.Clear();
		if (DeveloperAccess.Enabled)
		{
			host.Children.Add(Row("Обновления отключены", "island killed: проверка версии, скачивание и установка обновлений автора отключены. Обновляйте проект вручную.", null, "\ue72c"));
			return;
		}
		Version current = UpdateService.Current;
		DateTime? lastUpdateCheck = S.LastUpdateCheck;
		string text;
		if (lastUpdateCheck.HasValue)
		{
			DateTime valueOrDefault = lastUpdateCheck.GetValueOrDefault();
			text = $"Проверено {valueOrDefault.ToLocalTime():dd.MM HH:mm}. Island проверяет сам при запуске и каждые несколько часов";
		}
		else
		{
			text = "Island проверяет сам при запуске и каждые несколько часов";
		}
		string text2 = text;
		Button check = new Button
		{
			Content = "Проверить",
			Style = (Style)FindResource("T.Button")
		};
		check.Click += async (object _, RoutedEventArgs _) =>
		{
			check.IsEnabled = false;
			check.Content = "Проверяю…";
			bool flag = await UpdateService.CheckAsync();
			BuildUpdates(host);
			if (!flag && host.Children.Count > 0 && host.Children[0] is Grid grid)
			{
				grid.Children.OfType<StackPanel>().First().Children.Add(new TextBlock
				{
					Text = "Нет связи с GitHub — проверьте интернет",
					FontSize = 12.0,
					Foreground = (Brush)FindResource("T.Danger"),
					Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
				});
			}
		};
		host.Children.Add(Row($"Island {current.Major}.{current.Minor}.{current.Build}", UpdateService.UpdateAvailable ? "Есть новая версия" : text2, check, "\ue72c"));
		ReleaseInfo? latest = UpdateService.Latest;
		Version v;
		Button install;
		if ((object)latest != null && UpdateService.UpdateAvailable)
		{
			host.Children.Add(Divider());
			v = latest.Version;
			install = new Button
			{
				Style = (Style)FindResource("T.AccentButton")
			};
			ShowState();
			Action changed = () =>
			{
				ShowState();
			};
			UpdateService.DownloadChanged += changed;
			install.Unloaded += (object _, RoutedEventArgs _) =>
			{
				UpdateService.DownloadChanged -= changed;
			};
			install.Click += (object _, RoutedEventArgs _) =>
			{
				((App)Application.Current).Island?.InstallUpdate();
				ShowState();
			};
			Button button = new Button
			{
				Content = "Что нового",
				Style = (Style)FindResource("T.GhostButton"),
				Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
			};
			button.Click += (object _, RoutedEventArgs _) =>
			{
				ReviewSafety.Start(new ProcessStartInfo(latest.PageUrl)
				{
					UseShellExecute = true
				});
			};
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Horizontal
			};
			stackPanel.Children.Add(button);
			stackPanel.Children.Add(install);
			host.Children.Add(Row(latest.Title, UpdateService.CanSelfUpdate ? "Скачается в фоне — островом можно пользоваться. Потом перезапуск, настройки и ключ сохранятся" : "Это тестовая сборка — новая версия откроется на странице релиза", stackPanel, "\ue896"));
		}
		void ShowState()
		{
			bool flag = UpdateService.ReadyExe != null;
			bool downloading = UpdateService.Downloading;
			install.IsEnabled = !downloading;
			Button button2 = install;
			object content;
			if (flag)
			{
				content = "Перезапустить и обновить";
			}
			else
			{
				content = (downloading ? $"Скачивается… {Math.Round(UpdateService.DownloadProgress * 100.0)}%" : $"Обновить до {v.Major}.{v.Minor}.{v.Build}");
			}
			button2.Content = content;
		}
	}

	private void BuildHideApps(StackPanel host)
	{
		host.Children.Clear();
		foreach (string name in S.HideInApps.ToList())
		{
			Button button = new Button
			{
				Content = "Удалить",
				Style = (Style)FindResource("T.GhostButton")
			};
			button.Click += (object _, RoutedEventArgs _) =>
			{
				S.HideInApps.Remove(name);
				Changed();
			};
			host.Children.Add(Row(name + ".exe", null, button, "\ue7b3"));
			host.Children.Add(Divider());
		}
		WrapPanel wrapPanel = new WrapPanel
		{
			Margin = new Thickness(0.0, 8.0, 0.0, -6.0)
		};
		foreach (var item in MainWindow.OpenApps())
		{
			var (name2, toolTip) = item;
			if (!S.HideInApps.Contains(name2))
			{
				Button button2 = new Button
				{
					Content = name2,
					Style = (Style)FindResource("T.GhostButton"),
					Margin = new Thickness(0.0, 0.0, 6.0, 6.0),
					ToolTip = toolTip
				};
				button2.Click += (object _, RoutedEventArgs _) =>
				{
					S.HideInApps.Add(name2);
					Changed();
				};
				wrapPanel.Children.Add(button2);
			}
		}
		TextBox box = new TextBox
		{
			Style = (Style)FindResource("T.TextBox"),
			Tag = "game.exe",
			Width = 200.0
		};
		AutomationName(box, "Имя программы");
		Button button3 = new Button
		{
			Content = "Добавить",
			Style = (Style)FindResource("T.AccentButton"),
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		button3.Click += (object _, RoutedEventArgs _) =>
		{
			Add();
		};
		box.KeyDown += (object _, KeyEventArgs e) =>
		{
			if (e.Key == Key.Return)
			{
				Add();
			}
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(box);
		stackPanel.Children.Add(button3);
		Grid grid = Row((S.HideInApps.Count == 0) ? "Добавить программу" : "Ещё программа", (wrapPanel.Children.Count > 0) ? "Нажмите на открытую программу или впишите имя её файла" : "Впишите имя файла программы", null, "\ue710");
		StackPanel stackPanel2 = grid.Children.OfType<StackPanel>().First();
		if (wrapPanel.Children.Count > 0)
		{
			stackPanel2.Children.Add(wrapPanel);
		}
		stackPanel2.Children.Add(stackPanel);
		host.Children.Add(grid);
		void Add()
		{
			string text = System.IO.Path.GetFileName(box.Text.Trim().ToLowerInvariant());
			if (text.EndsWith(".exe"))
			{
				string text2 = text;
				text = text2.Substring(0, text2.Length - 4);
			}
			if (text.Length != 0 && !S.HideInApps.Contains(text))
			{
				S.HideInApps.Add(text);
				Changed();
			}
		}
		void Changed()
		{
			Commit();
			BuildHideApps(host);
		}
	}

	private Grid CityRow()
	{
		TextBox box = new TextBox
		{
			Style = (Style)FindResource("T.TextBox"),
			Tag = "Определять автоматически",
			Text = (S.WeatherCity ?? ""),
			Width = 210.0
		};
		AutomationName(box, "Город");
		Button save = new Button
		{
			Content = "Сохранить",
			Style = (Style)FindResource("T.AccentButton"),
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		save.Click += (object _, RoutedEventArgs _) =>
		{
			ApplyCity();
		};
		box.KeyDown += (object _, KeyEventArgs e) =>
		{
			if (e.Key == Key.Return)
			{
				ApplyCity();
			}
		};
		box.TextChanged += (object _, TextChangedEventArgs _) =>
		{
			save.Content = "Сохранить";
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		stackPanel.Children.Add(box);
		stackPanel.Children.Add(save);
		return Row("Город", "Например «Москва». Пусто — по местоположению", stackPanel, "\ue753");
		void ApplyCity()
		{
			S.WeatherCity = (string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim());
			Commit();
			save.Content = "Готово";
		}
	}

	private void BuildFeeds(StackPanel host)
	{
		host.Children.Clear();
		foreach (string url in S.CalendarFeeds.ToList())
		{
			Button button = new Button
			{
				Content = "Удалить",
				Style = (Style)FindResource("T.GhostButton")
			};
			button.Click += (object _, RoutedEventArgs _) =>
			{
				S.CalendarFeeds.Remove(url);
				Commit();
				BuildFeeds(host);
			};
			string text;
			if (url.Length <= 52)
			{
				text = url;
			}
			else
			{
				string text2 = url.Substring(0, 26);
				string text3 = url;
				text = text2 + "…" + text3.Substring(text3.Length - 22);
			}
			string title = text;
			host.Children.Add(Row(title, null, button, "\ue787"));
			host.Children.Add(Divider());
		}
		TextBox box = new TextBox
		{
			Style = (Style)FindResource("T.TextBox"),
			Tag = "https://calendar.google.com/…/basic.ics",
			Width = 300.0
		};
		AutomationName(box, "Ссылка на календарь");
		Button button2 = new Button
		{
			Content = "Добавить",
			Style = (Style)FindResource("T.AccentButton"),
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		button2.Click += (object _, RoutedEventArgs _) =>
		{
			Add();
		};
		box.KeyDown += (object _, KeyEventArgs e) =>
		{
			if (e.Key == Key.Return)
			{
				Add();
			}
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		stackPanel.Children.Add(box);
		stackPanel.Children.Add(button2);
		host.Children.Add(Row((S.CalendarFeeds.Count == 0) ? "Ссылки на календари (iCal)" : "Ещё календарь", null, stackPanel, "\ue710"));
		void Add()
		{
			string text4 = box.Text.Trim();
			if (text4.Length != 0 && !S.CalendarFeeds.Contains(text4))
			{
				S.CalendarFeeds.Add(text4);
				Commit();
				BuildFeeds(host);
			}
		}
	}

	private static string PetName(string kind)
	{
		if (!(kind == "ginger"))
		{
			if (kind == "block")
			{
				return "Жук лимитоед";
			}
			return "Блек";
		}
		return "Рыжик";
	}

	private Grid InDevelopment(Grid row)
	{
		StackPanel stackPanel = row.Children.OfType<StackPanel>().First();
		TextBlock element = (TextBlock)stackPanel.Children[0];
		stackPanel.Children.RemoveAt(0);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		stackPanel2.Children.Add(element);
		stackPanel2.Children.Add(new Border
		{
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
			Padding = new Thickness(7.0, 1.0, 7.0, 1.0),
			CornerRadius = new CornerRadius(6.0),
			Background = new SolidColorBrush(Color.FromArgb(46, 244, 162, 89)),
			VerticalAlignment = VerticalAlignment.Center,
			ToolTip = "Питомцы ещё дорабатываются: что-то может вести себя не так, как задумано",
			Child = new TextBlock
			{
				Text = "В разработке",
				FontSize = 10.5,
				FontWeight = FontWeights.SemiBold,
				Foreground = new SolidColorBrush(Color.FromRgb(244, 162, 89))
			}
		});
		stackPanel.Children.Insert(0, stackPanel2);
		return row;
	}

	private void BuildPetPage()
	{
		Hint("В разработке: реакции и поведение могут меняться.");
		Border border = new Border
		{
			Height = 190.0,
			CornerRadius = new CornerRadius(14.0),
			Margin = new Thickness(0.0, 4.0, 0.0, 10.0),
			Cursor = Cursors.Hand,
			Background = new RadialGradientBrush(Color.FromRgb(43, 37, 51), Color.FromRgb(22, 24, 31))
			{
				RadiusX = 0.6,
				RadiusY = 0.6,
				Center = new Point(0.5, 0.6),
				GradientOrigin = new Point(0.5, 0.6)
			},
			ToolTip = "Нажмите — погладить"
		};
		_petPreview = new PetView(S.PetKind, "full", 170.0)
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		border.Child = _petPreview;
		border.MouseLeftButtonUp += (object _, MouseButtonEventArgs _) =>
		{
			_petPreviewShow = ("pet", "", PreviewNow + 2.6);
		};
		Body.Children.Add(border);
		WrapPanel wrapPanel = new WrapPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
		};
		(string, string, string)[] petShows = PetShows;
		for (int num = 0; num < petShows.Length; num++)
		{
			(string, string, string) tuple = petShows[num];
			string item = tuple.Item1;
			string state = tuple.Item2;
			string bubble = tuple.Item3;
			Button button = new Button
			{
				Content = item,
				Style = (Style)FindResource("T.GhostButton"),
				Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
				Margin = new Thickness(0.0, 0.0, 4.0, 4.0)
			};
			button.Click += (object _, RoutedEventArgs _) =>
			{
				_petPreviewShow = (state, bubble, PreviewNow + 4.5);
			};
			wrapPanel.Children.Add(button);
		}
		Body.Children.Add(wrapPanel);
		StartPetPreview();
		Card(InDevelopment(SwitchRow("Показывать питомца", "Маленький друг в острове: следит за курсором, слушает музыку, засыпает без вас, реагирует на события", S.PetEnabled, (bool v) =>
		{
			S.PetEnabled = v;
		}, "\ueb51")), ChoiceRow("Питомец", "Кто живёт", new (string, string)[3]
		{
			("Блек", "black"),
			("Рыжик", "ginger"),
			("Жук лимитоед", "block")
		}, S.PetKind, (string v) =>
		{
			S.PetKind = v;
			RebuildSoon();
		}, "\ue77b"), ChoiceRow("Где живёт", PlaceNote(S.PetPlace), new (string, string)[3]
		{
			("В острове", "auto"),
			("Под островом", "hang"),
			("Сбоку", "side")
		}, S.PetPlace, (string v) =>
		{
			S.PetPlace = v;
			RebuildSoon();
		}, "\ue81e"), SliderRow("Размер", null, 0.7, 1.5, 0.05, S.PetSize, (double v) => $"{v * 100.0:0}%", (double v) =>
		{
			S.PetSize = v;
		}, "\ue740"));
		Group("На что реагирует");
		Grid? sleepAfter = null;
		Card(SwitchRow("События", "Сообщения и звонки, напоминания, загрузки, копирование, громкость, Caps Lock, наушники, сеть, заряд, обновления", S.PetEvents, (bool v) =>
		{
			S.PetEvents = v;
		}, "\uea8f"), SwitchRow("Музыка", "Надевает наушники, качает головой в такт", S.PetMusic, (bool v) =>
		{
			S.PetMusic = v;
		}, "\ue8d6"), SwitchRow("Печать", "Стучит лапками, пока вы печатаете", S.PetTyping, (bool v) =>
		{
			S.PetTyping = v;
		}, "\ue765"), SwitchRow("Курсор", "Следит глазами", S.PetLook, (bool v) =>
		{
			S.PetLook = v;
		}, "\ue8b0"), SwitchRow("Сон", "Засыпает без вас, ночью раньше. Просыпается, зевает, потягивается", S.PetSleep, (bool v) =>
		{
			S.PetSleep = v;
			if (sleepAfter != null)
			{
				Show(sleepAfter, v);
			}
		}, "\ue708"), sleepAfter = SliderRow("Засыпает через", "Сколько вас нет за компьютером", 1.0, 30.0, 1.0, S.PetSleepMinutes, (double v) => $"{v:0} мин", (double v) =>
		{
			S.PetSleepMinutes = (int)v;
		}, "\ue916"));
		Show(sleepAfter, S.PetSleep);
		Hint("Погладить: задержите курсор или нажмите — замурчит. В игре и видео на весь экран прячется с островом.");
	}

	private static string PlaceNote(string place)
	{
		if (!(place == "hang"))
		{
			if (place == "side")
			{
				return "Сидит рядом с островом во весь рост";
			}
			return "В острове: маленький, справа. При событии свешивается из-под острова и возвращается";
		}
		return "Всё время висит под островом, держась лапами за край. Подведёте курсор — спрячется в остров и не помешает";
	}

	private void StartPetPreview()
	{
		_petPreviewTimer?.Stop();
		_petPreviewTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(33L)
		};
		_petPreviewTimer.Tick += (object? _, EventArgs _) =>
		{
			if (_petPreview == null || _page != "pet")
			{
				_petPreviewTimer.Stop();
				return;
			}
			double previewNow = PreviewNow;
			(string, string, double)? petPreviewShow = _petPreviewShow;
			string text;
			string text2;
			if (petPreviewShow.HasValue)
			{
				(string, string, double) valueOrDefault = petPreviewShow.GetValueOrDefault();
				if (previewNow < valueOrDefault.Item3)
				{
					string item = valueOrDefault.Item1;
					string item2 = valueOrDefault.Item2;
					text = item2;
					text2 = item;
					goto IL_00a7;
				}
			}
			if (S.PetMusic)
			{
				MainWindow? island = ((App)Application.Current).Island;
				if (island != null && island.MusicNow)
				{
					text2 = "music";
					text = "";
					goto IL_00a7;
				}
			}
			text2 = "idle";
			text = "";
			goto IL_00a7;
			IL_00a7:
			petPreviewShow = _petPreviewShow;
			if (petPreviewShow.HasValue && previewNow >= petPreviewShow.GetValueOrDefault().Item3)
			{
				_petPreviewShow = null;
			}
			if (text2 != _petPreview.State || text != _petPreview.Bubble)
			{
				_petPreview.SetState(text2, text, restart: true);
			}
			Point? lookAt = ((S.PetLook && Win32.GetCursorPos(out var pt)) ? new Point?(new Point(pt.X, pt.Y)) : ((Point?)null));
			_petPreview.Tick(previewNow, lookAt);
		};
		_petPreviewTimer.Start();
		Closed += (object? _, EventArgs _) =>
		{
			_petPreviewTimer?.Stop();
		};
	}

	private void BuildPhone()
	{
		if (!Island.Services.License.IsPro)
		{
			Card(Locked(WithBadge(SwitchRow("Уведомления с телефона", "Android в той же сети шлёт на остров сообщения, звонки, почту", value: false, (bool _) =>
			{
			}, "\ue8ea"))));
			Card(ButtonRow("Связь с телефоном — в Island Pro", "Уведомления и звонки на острове, ответы на сообщения, общий буфер обмена и файлы в обе стороны", "Подробнее", () =>
			{
				OpenPage("license");
			}, "\ue734"));
			return;
		}
		List<UIElement> list = new List<UIElement> { SwitchRow("Уведомления с телефона", "Android в той же сети шлёт на остров сообщения, звонки, почту. Нужно приложение Island Link", S.PhoneLink, (bool v) =>
		{
			S.PhoneLink = v;
			_pairing = null;
			RedrawPhoneSoon();
			if (v)
			{
				AllowFirewallIfNeeded();
			}
		}, "\ue8ea") };
		PhoneLinkService? link = MainWindow.Phones;
		HookPhone(S.PhoneLink ? link : null);
		if (S.PhoneLink)
		{
			list.Add(SwitchRow("Общий буфер обмена", "Текст с ПК сразу в буфере телефона. С телефона: «Поделиться → Island Link» или плитка «Буфер → ПК» в шторке", S.PhoneClipboard, (bool v) =>
			{
				S.PhoneClipboard = v;
			}, "\ue77f"));
		}
		Card(list.ToArray());
		Group("Приложение для телефона");
		Card(_appQr ? AppDownloadPanel() : ButtonRow("Island Link для Android", "Установка один раз: наведите камеру телефона на QR-код", "Показать QR-код", () =>
		{
			_appQr = true;
			RedrawPhoneSoon();
		}, "\ue896"));
		if (!S.PhoneLink)
		{
			Hint("Включите уведомления с телефона, чтобы подключить его. После этого на острове появится вкладка «Телефон».");
			return;
		}
		Group("На острове");
		string description;
		if (S.MiniPill)
		{
			description = "Аватар и число непрочитанных рядом с часами";
		}
		else if (S.TwoIslands && S.TwoIslandsStyle == "strip")
		{
			description = "Красная полоска по низу острова, пока есть непрочитанные";
		}
		else
		{
			description = (S.TwoIslands ? "Аватар и число непрочитанных в маленьком круглом острове рядом" : "Сейчас не видно: включите мини-остров или показ второго дела в разделе «Остров»");
		}
		Grid grid = SwitchRow("Кто написал — на свёрнутом острове", description, S.PhoneOnCollapsed, (bool v) =>
		{
			S.PhoneOnCollapsed = v;
		}, "\ue77b");
		if (!S.MiniPill && !S.TwoIslands)
		{
			Button button = new Button
			{
				Content = "Открыть «Остров»",
				Style = (Style)FindResource("T.GhostButton"),
				HorizontalAlignment = HorizontalAlignment.Left,
				Margin = new Thickness(-14.0, 4.0, 0.0, 0.0)
			};
			button.Click += (object _, RoutedEventArgs _) =>
			{
				OpenPage("island");
				ScrollToSection("Свёрнутый остров");
			};
			grid.Children.OfType<StackPanel>().First().Children.Add(button);
		}
		Card(SwitchRow("Не беспокоить", "Уведомления и звонки не всплывают, копятся во вкладке «Телефон». Также кнопка с луной во вкладке", S.PhoneDnd, (bool v) =>
		{
			S.PhoneDnd = v;
		}, "\ue708"), SwitchRow("Карточка при новом уведомлении", "На несколько секунд: кто написал, что, кнопки ответа. Без неё уведомления тихо копятся. Звонки показываются всегда", S.PhoneCards, (bool v) =>
		{
			S.PhoneCards = v;
		}, "\ue8bd"), grid);
		Group("Телефоны");
		list.Clear();
		if (link != null)
		{
			foreach (PhoneDevice device in link.Devices)
			{
				string text;
				if (!link.IsConnected(device.Id))
				{
					DateTime? lastSeen = device.LastSeen;
					if (lastSeen.HasValue)
					{
						DateTime valueOrDefault = lastSeen.GetValueOrDefault();
						text = $"Не в сети · был {valueOrDefault.ToLocalTime():d MMMM, HH:mm}";
					}
					else
					{
						text = "Ещё не подключался";
					}
				}
				else
				{
					text = "Подключён";
				}
				string text2 = text;
				Button button2 = new Button
				{
					Content = "Отвязать",
					Style = (Style)FindResource("T.GhostButton")
				};
				string id = device.Id;
				button2.Click += (object _, RoutedEventArgs _) =>
				{
					link.Unpair(id);
					RedrawPhoneSoon();
				};
				string? title = ((device.Name.Length > 0) ? device.Name : (device.Model ?? "Телефон"));
				string? os = device.Os;
				list.Add(Row(title, (os != null) ? (text2 + " · " + os) : text2, button2, "\ue8ea"));
			}
			PhonePairing? pairing = _pairing;
			if ((object)pairing != null && pairing.Expires > DateTime.Now)
			{
				list.Add(PairingPanel(link, pairing));
			}
			else
			{
				list.Add(ButtonRow((link.Devices.Count == 0) ? "Подключить телефон" : "Подключить ещё один", "Отсканируйте QR-код в Island Link", "Показать QR-код", () =>
				{
					_pairing = link.StartPairing();
					RedrawPhoneSoon();
				}, "\ue8f4"));
			}
		}
		list.Add(FirewallRow());
		if (list.Count > 0)
		{
			Card(list.ToArray());
		}
		Hint("Телефон и ПК должны быть в одной сети, через один роутер. Уведомления идут напрямую, зашифрованными, без серверов.");
	}

	private Grid FirewallRow()
	{
		Button allow = new Button
		{
			Content = "Разрешить",
			Style = (Style)FindResource("T.Button"),
			Visibility = Visibility.Collapsed
		};
		Grid grid = Row("Брандмауэр Windows", "Проверяю…", allow, "\ue83d");
		UIElementCollection children = grid.Children.OfType<StackPanel>().First().Children;
		TextBlock text = (TextBlock)children[children.Count - 1];
		allow.Click += async (object _, RoutedEventArgs _) =>
		{
			allow.IsEnabled = false;
			text.Text = "Подтвердите в окне Windows…";
			bool flag = await LinkFirewall.AllowAsync();
			allow.IsEnabled = true;
			if (!flag)
			{
				text.Text = "Не получилось: разрешение не дали. Можно попробовать ещё раз";
			}
			else
			{
				Check();
			}
		};
		Check();
		return grid;
		async void Check()
		{
			bool? flag = await LinkFirewall.IsAllowedAsync();
			text.Text = ((flag == true) ? "Телефоны из вашей сети могут подключаться — в любой сети: домашней, рабочей, общедоступной" : "Windows может не пускать телефон — особенно если сеть отмечена как общедоступная. Нажмите «Разрешить» и подтвердите в окне Windows");
			allow.Visibility = ((flag == true) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private async Task AllowFirewallIfNeeded()
	{
		if (await LinkFirewall.IsAllowedAsync() == false)
		{
			await LinkFirewall.AllowAsync();
			RedrawPhoneSoon();
		}
	}

	private UIElement AppDownloadPanel()
	{
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(62.0, 12.0, 16.0, 14.0),
			Children = { (UIElement)new TextBlock
			{
				Text = "Island Link для Android",
				FontSize = 14.0,
				FontWeight = FontWeights.SemiBold,
				Foreground = (Brush)FindResource("T.Text"),
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			} }
		};
		ImageSource imageSource = Png(PhoneLinkService.QrPng("https://github.com/qxezecwry/Island/releases/latest/download/IslandLink.apk", 1));
		int pixelWidth = ((BitmapSource)imageSource).PixelWidth;
		double num = pixelWidth * Math.Max(4, 200 / pixelWidth);
		Image image = new Image
		{
			Source = imageSource,
			Width = num,
			Height = num,
			UseLayoutRounding = true
		};
		RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
		stackPanel.Children.Add(new Border
		{
			Child = image,
			Background = Brushes.White,
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(8.0),
			HorizontalAlignment = HorizontalAlignment.Left
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Наведите камеру телефона на код и откройте ссылку — скачается приложение. Откройте файл и разрешите установку. Или отправьте себе ссылку:",
			FontSize = 12.0,
			Foreground = (Brush)FindResource("T.Text2"),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 10.0, 0.0, 4.0)
		});
		TextBox element = new TextBox
		{
			Style = (Style)FindResource("T.TextBox"),
			Text = "https://github.com/qxezecwry/Island/releases/latest/download/IslandLink.apk",
			IsReadOnly = true
		};
		AutomationName(element, "Ссылка на приложение для телефона");
		stackPanel.Children.Add(element);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(-14.0, 4.0, 0.0, 0.0)
		};
		Button copy = new Button
		{
			Content = "Копировать ссылку",
			Style = (Style)FindResource("T.GhostButton")
		};
		copy.Click += (object _, RoutedEventArgs _) =>
		{
			try
			{
				ReviewClipboard.SetText("https://github.com/qxezecwry/Island/releases/latest/download/IslandLink.apk");
				copy.Content = "Скопировано";
			}
			catch (Exception)
			{
			}
		};
		Button button = new Button
		{
			Content = "Скрыть",
			Style = (Style)FindResource("T.GhostButton")
		};
		button.Click += (object _, RoutedEventArgs _) =>
		{
			_appQr = false;
			RedrawPhoneSoon();
		};
		stackPanel2.Children.Add(copy);
		stackPanel2.Children.Add(button);
		stackPanel.Children.Add(stackPanel2);
		return stackPanel;
	}

	private UIElement PairingPanel(PhoneLinkService link, PhonePairing pairing)
	{
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(62.0, 6.0, 16.0, 14.0)
		};
		ImageSource imageSource = Png(PhoneLinkService.QrPng(pairing.Uri, 1));
		int pixelWidth = ((BitmapSource)imageSource).PixelWidth;
		double num = pixelWidth * Math.Max(4, 260 / pixelWidth);
		Image image = new Image
		{
			Source = imageSource,
			Width = num,
			Height = num,
			UseLayoutRounding = true,
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
		stackPanel.Children.Add(new Border
		{
			Child = image,
			Background = Brushes.White,
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(8.0),
			HorizontalAlignment = HorizontalAlignment.Left
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Откройте Island Link на телефоне и нажмите «Сканировать QR-код». Или скопируйте ссылку и вставьте её там через «Вставить ссылку»:",
			FontSize = 12.0,
			Foreground = (Brush)FindResource("T.Text2"),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 10.0, 0.0, 4.0)
		});
		TextBox element = new TextBox
		{
			Style = (Style)FindResource("T.TextBox"),
			Text = pairing.Uri,
			IsReadOnly = true
		};
		AutomationName(element, "Ссылка для подключения");
		stackPanel.Children.Add(element);
		TextBlock left = new TextBlock
		{
			FontSize = 12.0,
			Foreground = (Brush)FindResource("T.Text3"),
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(left);
		Button button = new Button
		{
			Content = "Отмена",
			Style = (Style)FindResource("T.GhostButton"),
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(-14.0, 4.0, 0.0, 0.0)
		};
		button.Click += (object _, RoutedEventArgs _) =>
		{
			link.CancelPairing();
			_pairing = null;
			RedrawPhoneSoon();
		};
		stackPanel.Children.Add(button);
		_pairingClock?.Stop();
		_pairingClock = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1L)
		};
		_pairingClock.Tick += (object? _, EventArgs _) =>
		{
			Tick();
		};
		Tick();
		_pairingClock.Start();
		return stackPanel;
		void Tick()
		{
			TimeSpan timeSpan = pairing.Expires - DateTime.Now;
			if (timeSpan <= TimeSpan.Zero)
			{
				_pairingClock.Stop();
				_pairing = null;
				RedrawPhoneSoon();
			}
			else
			{
				left.Text = $"Код действует ещё {timeSpan:m\\:ss}";
			}
		}
	}

	private static ImageSource Png(byte[] png)
	{
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		bitmapImage.StreamSource = new MemoryStream(png);
		bitmapImage.EndInit();
		bitmapImage.Freeze();
		return bitmapImage;
	}

	private void HookPhone(PhoneLinkService? link)
	{
		if (link == _hookedLink)
		{
			return;
		}
		if (_hookedLink != null)
		{
			_hookedLink.Paired -= OnPhonePaired;
			_hookedLink.ConnectionChanged -= OnPhoneConnection;
		}
		_hookedLink = link;
		if (link != null)
		{
			link.Paired += OnPhonePaired;
			link.ConnectionChanged += OnPhoneConnection;
			Closed += (object? _, EventArgs _) =>
			{
				HookPhone(null);
			};
		}
	}

	private void OnPhonePaired(PhoneDevice device)
	{
		_pairing = null;
		RedrawPhoneSoon();
	}

	private void OnPhoneConnection(PhoneDevice device, bool connected)
	{
		RedrawPhoneSoon();
	}

	private void RedrawPhoneSoon()
	{
		Dispatcher.BeginInvoke((Action)(() =>
		{
			if (_page == "phone")
			{
				RebuildPage();
			}
		}), DispatcherPriority.Background);
	}

	public SettingsWindow()
	{
		InitializeComponent();
		SourceInitialized += (object? _, EventArgs _) =>
		{
			Win32.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
		};
		StateChanged += (object? _, EventArgs _) =>
		{
			Frame.Margin = ((WindowState == WindowState.Maximized) ? new Thickness(7.0) : new Thickness(0.0));
			MaxButton.Content = ((WindowState == WindowState.Maximized) ? "\ue923" : "\ue922");
		};
		_commitTimer.Tick += (object? _, EventArgs _) =>
		{
			_commitTimer.Stop();
			Commit();
		};
		_pages = new List<(string, string, string, Action)>
		{
			("island", "Остров", "\ue7f4", BuildIsland),
			("look", "Внешний вид", "\ue790", BuildLook),
			("music", "Музыка", "\ue8d6", BuildMusic),
			("alerts", "Уведомления", "\uea8f", BuildAlerts),
			("integrations", "Интеграции", "\ue8f2", BuildIntegrations),
			("phone", "Телефон", "\ue8ea", BuildPhone),
			("pet", "Питомец", "\ueb51", BuildPetPage),
			("tabs", "Вкладки", "\ue8a9", BuildTabs),
			("system", "Система", "\ue770", BuildSystem),
			("lab", "Экспериментальное", "\ue82f", BuildLab),
			("help", "Справка", "\ue897", BuildHelp),
			("license", DeveloperBranding.CardTitle, "\ue735", BuildLicense)
		};
		foreach (var page in _pages)
		{
			if (!(page.Id == "license"))
			{
				RadioButton radioButton = new RadioButton
				{
					Style = (Style)FindResource("T.NavItem"),
					GroupName = "nav"
				};
				StackPanel stackPanel = new StackPanel
				{
					Orientation = Orientation.Horizontal
				};
				stackPanel.Children.Add(new TextBlock
				{
					Text = page.Glyph,
					FontFamily = (FontFamily)FindResource("T.Icons"),
					FontSize = 14.0,
					Width = 26.0,
					VerticalAlignment = VerticalAlignment.Center
				});
				stackPanel.Children.Add(new TextBlock
				{
					Text = page.Title,
					VerticalAlignment = VerticalAlignment.Center
				});
				radioButton.Content = stackPanel;
				AutomationName(radioButton, page.Title);
				(string Id, string Title, string Glyph, Action Build) p = page;
				radioButton.Checked += (object _, RoutedEventArgs _) =>
				{
					ShowPage(p.Id);
				};
				_navItems[page.Id] = radioButton;
				((page.Id == "help") ? BottomNav : Nav).Children.Add(radioButton);
			}
		}
		Scroll.ScrollChanged += (object _, ScrollChangedEventArgs _) =>
		{
			UpdatePinned();
		};
		InitHalloween();
		UpdateProCard();
		OpenPage("island");
	}

	public void OpenPage(string id)
	{
		if (id == "general")
		{
			id = "island";
		}
		RadioButton value;
		if (id == "phone-app")
		{
			_appQr = true;
			if (_page == "phone")
			{
				RebuildPage();
			}
			OpenPage("phone");
			Dispatcher.BeginInvoke((Action)(() =>
			{
				ScrollToSection("Приложение для телефона");
			}), DispatcherPriority.Loaded);
		}
		else if (id == "ai")
		{
			OpenPage("tabs");
			ScrollToSection("ИИ-чат");
		}
		else if (_navItems.TryGetValue(id, out value))
		{
			if (value.IsChecked == true)
			{
				ShowPage(id);
			}
			else
			{
				value.IsChecked = true;
			}
		}
		else
		{
			ShowPage(id);
		}
	}

	private void ProCard_Click(object sender, RoutedEventArgs e)
	{
		OpenPage("license");
	}

	private void UpdateProCard()
	{
		if (DeveloperBranding.IsDeveloper)
		{
			Title = DeveloperBranding.SettingsTitle;
			ProStatus.Text = DeveloperBranding.Subtitle;
			ProIcon.Text = (Island.Services.License.IsPro ? "\ue735" : "\ue734");
			return;
		}
		TextBlock proStatus = ProStatus;
		string text;
		if (!Island.Services.License.IsTrial)
		{
			if (Island.Services.License.IsPro)
			{
				DateTime? expires = Island.Services.License.Expires;
				if (expires.HasValue)
				{
					DateTime valueOrDefault = expires.GetValueOrDefault();
					text = $"До {valueOrDefault.ToLocalTime():dd.MM.yyyy}";
				}
				else
				{
					text = "Активирован навсегда";
				}
			}
			else
			{
				text = "Pro-функции отключены";
			}
		}
		else
		{
			text = "Пробный период · осталось " + FormatLeft(Island.Services.License.TrialEnds - DateTime.UtcNow);
		}
		proStatus.Text = text;
		ProIcon.Text = (Island.Services.License.IsPro ? "\ue735" : "\ue734");
	}

	private void RebuildPage()
	{
		double offset = Scroll.VerticalOffset;
		bool pinnedCompact = _pinnedCompact;
		_rebuilding = true;
		try
		{
			ShowPage(_page);
		}
		finally
		{
			_rebuilding = false;
		}
		if (pinnedCompact && _pinnedTitle != null)
		{
			_pinnedCompact = true;
			_pinnedTitle.FontSize = 19.0;
			StickyFade.Opacity = 1.0;
			foreach (IslandPreview preview in _previews)
			{
				preview.SnapCompact();
			}
			SizePinnedFrame(animate: false);
		}
		if (_proTried.Count > 0)
		{
			ShowProBar();
		}
		Dispatcher.BeginInvoke((Action)(() =>
		{
			Scroll.ScrollToVerticalOffset(offset);
		}), DispatcherPriority.Loaded);
	}

	private void ShowPage(string id)
	{
		if (!_rebuilding)
		{
			RevertProPreview();
		}
		_page = id;
		ProCard.Tag = ((id == "license") ? "on" : null);
		if (id == "license")
		{
			foreach (RadioButton value2 in _navItems.Values)
			{
				value2.IsChecked = false;
			}
		}
		(string, string, string, Action) tuple = _pages.First(((string Id, string Title, string Glyph, Action Build) p) => p.Id == id);
		_previews.Clear();
		_proBar = null;
		Body.Children.Clear();
		StickyHost.Children.Clear();
		TextBlock textBlock = new TextBlock
		{
			Text = tuple.Item2,
			FontSize = 26.0,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(2.0, 6.0, 0.0, 6.0)
		};
		Body.Children.Add(textBlock);
		tuple.Item4();
		bool flag = StickyHost.Children.Count > 0;
		if (flag)
		{
			Body.Children.Remove(textBlock);
			StickyHost.Children.Insert(0, textBlock);
		}
		StackPanel stickyHost = StickyHost;
		Visibility visibility = (StickyFade.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible));
		stickyHost.Visibility = visibility;
		_pinnedTitle = (flag ? textBlock : null);
		if (!flag)
		{
			_pinnedFrame = null;
		}
		_pinnedCompact = false;
		StickyFade.Opacity = 0.0;
		if (_rebuilding)
		{
			return;
		}
		Scroll.ScrollToTop();
		CubicEase easingFunction = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		Body.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220L))
		{
			EasingFunction = easingFunction
		});
		BodyShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12.0, 0.0, TimeSpan.FromMilliseconds(300L))
		{
			EasingFunction = easingFunction
		});
		int num = 0;
		foreach (Border item in Body.Children.OfType<Border>().Take(8))
		{
			TranslateTransform translateTransform = (TranslateTransform)(item.RenderTransform = new TranslateTransform());
			TimeSpan value = TimeSpan.FromMilliseconds(40 + 35 * num++);
			translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10.0, 0.0, TimeSpan.FromMilliseconds(380L))
			{
				EasingFunction = easingFunction,
				BeginTime = value
			});
		}
	}

	private void SizePinnedFrame(bool animate)
	{
		if (_pinnedFrame != null && _previews.Count != 0)
		{
			double to = (_pinnedCompact ? _previews[0].CompactHeight : _previews[0].FullHeight);
			MainWindow.Animate(_pinnedFrame, FrameworkElement.HeightProperty, to, animate ? 300 : 0, new CubicEase
			{
				EasingMode = EasingMode.EaseInOut
			});
		}
	}

	private void UpdatePinned()
	{
		if (_pinnedTitle == null)
		{
			return;
		}
		bool flag = Scroll.VerticalOffset > 6.0;
		if (flag == _pinnedCompact)
		{
			return;
		}
		_pinnedCompact = flag;
		CubicEase ease = new CubicEase
		{
			EasingMode = EasingMode.EaseInOut
		};
		MainWindow.Animate(_pinnedTitle, TextBlock.FontSizeProperty, flag ? 19 : 26, 300, ease);
		MainWindow.Animate(StickyFade, UIElement.OpacityProperty, flag ? 1 : 0, 250, ease);
		foreach (IslandPreview preview in _previews)
		{
			preview.Compact = flag;
		}
		SizePinnedFrame(animate: true);
	}

	public void ScrollToSection(string title)
	{
		Dispatcher.BeginInvoke((Action)(() =>
		{
			FrameworkElement? frameworkElement = Body.Children.OfType<FrameworkElement>().FirstOrDefault((FrameworkElement t) => t.Tag as string == title);
			if (frameworkElement != null)
			{
				double y = frameworkElement.TranslatePoint(new Point(0.0, 0.0), Body).Y;
				Scroll.ScrollToVerticalOffset(Math.Max(0.0, y - 12.0));
				frameworkElement.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.3, 1.0, TimeSpan.FromMilliseconds(600L)));
			}
		}), DispatcherPriority.Loaded);
	}

	private void Minimize_Click(object sender, RoutedEventArgs e)
	{
		WindowState = WindowState.Minimized;
	}

	private void Maximize_Click(object sender, RoutedEventArgs e)
	{
		WindowState = ((WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private static string FormatLeft(TimeSpan left)
	{
		if (!(left.TotalDays >= 2.0))
		{
			if (left.TotalHours >= 1.0)
			{
				return $"{(int)left.TotalHours} ч {left.Minutes} мин";
			}
			return $"{Math.Max(1, left.Minutes)} мин";
		}
		return $"{(int)left.TotalDays} дн.";
	}

	private void AfterLicenseChange()
	{
		UpdateProCard();
		OpenPage("license");
	}

	private static void Commit()
	{
		S.Save();
		Settings.NotifyChanged();
	}

	private void CommitSoon()
	{
		_commitTimer.Stop();
		_commitTimer.Start();
	}

	protected override void OnClosed(EventArgs e)
	{
		RevertProPreview();
		if (_commitTimer.IsEnabled)
		{
			_commitTimer.Stop();
			Commit();
		}
		base.OnClosed(e);
	}

	private void Apply(bool previewOnly, bool soon = false)
	{
		if (previewOnly)
		{
			NoteProTry();
			ShowProBar();
		}
		else
		{
			if (soon)
			{
				CommitSoon();
			}
			else
			{
				Commit();
			}
			Settings? proSnapshot = _proSnapshot;
			TakeProSnapshot();
			if (proSnapshot != null)
			{
				foreach (PropertyInfo item in from p in typeof(Settings).GetProperties()
					where _proTried.Contains(p.Name)
					select p)
				{
					item.SetValue(_proSnapshot, item.GetValue(proSnapshot));
				}
			}
		}
		foreach (IslandPreview preview in _previews)
		{
			preview.Refresh();
		}
		SizePinnedFrame(animate: false);
	}

	private static string Json(object? value)
	{
		return JsonSerializer.Serialize(value);
	}

	private void NoteProTry()
	{
		foreach (PropertyInfo item in from p in typeof(Settings).GetProperties()
			where p.CanRead && p.CanWrite
			select p)
		{
			if (Json(item.GetValue(S)) != Json(item.GetValue(_proSnapshot)))
			{
				_proTried.Add(item.Name);
			}
		}
	}

	private void TakeProSnapshot()
	{
		_proSnapshot = JsonSerializer.Deserialize<Settings>(Json(S));
	}

	private void RevertProPreview()
	{
		if (_proTried.Count > 0 && _proSnapshot != null)
		{
			foreach (PropertyInfo item in from p in typeof(Settings).GetProperties()
				where _proTried.Contains(p.Name)
				select p)
			{
				item.SetValue(S, item.GetValue(_proSnapshot));
			}
			S.Save();
		}
		_proTried.Clear();
		TakeProSnapshot();
	}

	private void ShowProBar()
	{
		if (_proBar == null)
		{
			StackPanel stackPanel = new StackPanel
			{
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 16.0, 0.0)
			};
			stackPanel.Children.Add(new TextBlock
			{
				Text = "Так будет с Island Pro",
				FontSize = 13.5,
				FontWeight = FontWeights.SemiBold
			});
			stackPanel.Children.Add(new TextBlock
			{
				Text = "Это только предпросмотр — на острове ничего не изменилось. Уйдёте со страницы — всё вернётся",
				FontSize = 12.0,
				Foreground = (Brush)FindResource("T.Text2"),
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
			});
			Button button = new Button
			{
				Content = "Применить с Pro",
				Style = (Style)FindResource("T.AccentButton"),
				VerticalAlignment = VerticalAlignment.Center
			};
			button.Click += (object _, RoutedEventArgs _) =>
			{
				OpenPage("license");
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
			grid.Children.Add(stackPanel);
			Grid.SetColumn(button, 1);
			grid.Children.Add(button);
			_proBar = new Border
			{
				Background = (Brush)FindResource("T.AccentSoft"),
				CornerRadius = new CornerRadius(14.0),
				Padding = new Thickness(16.0, 12.0, 16.0, 12.0),
				Margin = new Thickness(0.0, 12.0, 0.0, 0.0),
				Child = grid
			};
			int index = ((_previews.Count > 0 && Body.Children.Contains(_previews[0])) ? (Body.Children.IndexOf(_previews[0]) + 1) : ((StickyHost.Children.Count <= 0) ? 1 : 0));
			Body.Children.Insert(index, _proBar);
			BackEase easingFunction = new BackEase
			{
				Amplitude = 0.4,
				EasingMode = EasingMode.EaseOut
			};
			_proBar.RenderTransform = new ScaleTransform(0.96, 0.96);
			_proBar.RenderTransformOrigin = new Point(0.5, 0.5);
			_proBar.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1.0, TimeSpan.FromMilliseconds(300L))
			{
				EasingFunction = easingFunction
			});
			_proBar.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1.0, TimeSpan.FromMilliseconds(300L))
			{
				EasingFunction = easingFunction
			});
			_proBar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200L)));
		}
	}

	private void ProGroup(string title, bool preview, Func<UIElement[]> rows)
	{
		Group(title, pro: true);
		_proRows = preview;
		UIElement[] rows2 = rows();
		_proRows = false;
		Border border = Card(rows2);
		if (!Island.Services.License.IsPro && !preview)
		{
			border.IsEnabled = false;
			border.Opacity = 0.45;
		}
	}

	private void Group(string title, bool pro = false)
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(4.0, 22.0, 0.0, 8.0),
			Tag = title
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 12.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = (Brush)FindResource("T.Text2"),
			VerticalAlignment = VerticalAlignment.Center
		});
		if (pro && !Island.Services.License.IsPro)
		{
			stackPanel.Children.Add(ProBadge());
		}
		Body.Children.Add(stackPanel);
	}

	private Border ProBadge()
	{
		Border border = new Border
		{
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
			Padding = new Thickness(6.0, 1.0, 7.0, 1.0),
			CornerRadius = new CornerRadius(6.0),
			Background = (Brush)FindResource("T.AccentSoft"),
			Cursor = Cursors.Hand,
			ToolTip = "Часть Island Pro — нажмите, чтобы узнать больше",
			VerticalAlignment = VerticalAlignment.Center
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "\ue72e",
			FontFamily = (FontFamily)FindResource("T.Icons"),
			FontSize = 9.0,
			Foreground = (Brush)FindResource("T.Accent"),
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 4.0, 0.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Pro",
			FontSize = 10.5,
			FontWeight = FontWeights.SemiBold,
			Foreground = (Brush)FindResource("T.Accent")
		});
		border.Child = stackPanel;
		border.MouseLeftButtonUp += (object _, MouseButtonEventArgs _) =>
		{
			OpenPage("license");
		};
		return border;
	}

	private void Hint(string text)
	{
		Body.Children.Add(new TextBlock
		{
			Text = text,
			FontSize = 12.0,
			Foreground = (Brush)FindResource("T.Text3"),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(4.0, 10.0, 0.0, 0.0)
		});
	}

	private Border Card(params UIElement[] rows)
	{
		StackPanel stackPanel = new StackPanel();
		for (int i = 0; i < rows.Length; i++)
		{
			if (i == 0)
			{
				stackPanel.Children.Add(rows[i]);
				continue;
			}
			StackPanel stackPanel2 = new StackPanel
			{
				Tag = "row+divider"
			};
			stackPanel2.Children.Add(new Border
			{
				Height = 1.0,
				Background = (Brush)FindResource("T.Divider"),
				Margin = new Thickness(16.0, 0.0, 16.0, 0.0)
			});
			stackPanel2.Children.Add(rows[i]);
			stackPanel.Children.Add(stackPanel2);
		}
		Border border = new Border
		{
			Background = (Brush)FindResource("T.Card"),
			CornerRadius = new CornerRadius(14.0),
			BorderBrush = (Brush)FindResource("T.Border"),
			BorderThickness = new Thickness(1.0),
			Child = stackPanel
		};
		Body.Children.Add(border);
		return border;
	}

	private static void Show(FrameworkElement row, bool on)
	{
		FrameworkElement frameworkElement = ((row.Parent is StackPanel { Tag: string tag } stackPanel && tag == "row+divider") ? stackPanel : row);
		Visibility visibility = ((!on) ? Visibility.Collapsed : Visibility.Visible);
		if (frameworkElement.Visibility != visibility)
		{
			frameworkElement.Visibility = visibility;
			if (on)
			{
				frameworkElement.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220L)));
			}
		}
	}

	private static void Dim(FrameworkElement row, bool dim)
	{
		row.IsEnabled = !dim;
		row.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(dim ? 0.4 : 1.0, TimeSpan.FromMilliseconds(200L)));
	}

	private Grid Row(string title, string? description, UIElement? control, string? glyph = null)
	{
		Grid grid = new Grid
		{
			Margin = new Thickness(6.0, 2.0, 6.0, 2.0),
			MinHeight = 30.0
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		if (glyph != null)
		{
			Border element = new Border
			{
				Width = 32.0,
				Height = 32.0,
				CornerRadius = new CornerRadius(8.0),
				Background = (Brush)FindResource("T.Field"),
				Margin = new Thickness(10.0, 10.0, 14.0, 10.0),
				VerticalAlignment = VerticalAlignment.Center,
				Child = new TextBlock
				{
					Text = glyph,
					FontFamily = (FontFamily)FindResource("T.Icons"),
					FontSize = 14.0,
					Foreground = (Brush)FindResource("T.Text2"),
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center
				}
			};
			grid.Children.Add(element);
		}
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness((glyph == null) ? 10 : 0, 10.0, 16.0, 10.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 13.5,
			TextWrapping = TextWrapping.Wrap
		});
		if (description != null)
		{
			stackPanel.Children.Add(new TextBlock
			{
				Text = description,
				FontSize = 12.0,
				Foreground = (Brush)FindResource("T.Text3"),
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
			});
		}
		Grid.SetColumn(stackPanel, 1);
		grid.Children.Add(stackPanel);
		if (control != null)
		{
			Border element2 = new Border
			{
				Child = control,
				Margin = new Thickness(0.0, 10.0, 10.0, 10.0),
				VerticalAlignment = VerticalAlignment.Center
			};
			Grid.SetColumn(element2, 2);
			grid.Children.Add(element2);
		}
		return grid;
	}

	private static void HoverGlow(Grid row)
	{
		SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue));
		row.Background = brush;
		row.SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			row.Clip = new RectangleGeometry(new Rect(row.RenderSize), 10.0, 10.0);
		};
		row.MouseEnter += (object _, MouseEventArgs _) =>
		{
			brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(10, byte.MaxValue, byte.MaxValue, byte.MaxValue), TimeSpan.FromMilliseconds(150L)));
		};
		row.MouseLeave += (object _, MouseEventArgs _) =>
		{
			brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), TimeSpan.FromMilliseconds(250L)));
		};
	}

	private Grid SwitchRow(string title, string? description, bool value, Action<bool> set, string? glyph = null)
	{
		bool previewOnly = PreviewOnly;
		CheckBox box = new CheckBox
		{
			Style = (Style)FindResource("T.Switch"),
			IsChecked = value
		};
		AutomationName(box, title);
		box.Click += (object _, RoutedEventArgs _) =>
		{
			set(box.IsChecked == true);
			Apply(previewOnly);
		};
		Grid row = Row(title, description, box, glyph);
		HoverGlow(row);
		row.Cursor = Cursors.Hand;
		row.MouseLeftButtonUp += (object _, MouseButtonEventArgs e) =>
		{
			if (row.IsEnabled && (!(e.OriginalSource is DependencyObject d) || !IsInside(d, box)))
			{
				box.IsChecked = box.IsChecked != true;
				set(box.IsChecked == true);
				Apply(previewOnly);
			}
		};
		return row;
	}

	private Grid SliderRow(string title, string? description, double min, double max, double step, double value, Func<double, string> format, Action<double> set, string? glyph = null)
	{
		bool previewOnly = PreviewOnly;
		Slider slider = new Slider
		{
			Style = (Style)FindResource("T.Slider"),
			Minimum = min,
			Maximum = max,
			Value = Math.Clamp(value, min, max),
			SmallChange = step,
			LargeChange = step * 5.0,
			Width = 190.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		AutomationName(slider, title);
		TextBlock label = new TextBlock
		{
			Text = format(slider.Value),
			Width = 74.0,
			TextAlignment = TextAlignment.Right,
			FontWeight = FontWeights.SemiBold,
			Foreground = (Brush)FindResource("T.Text2"),
			VerticalAlignment = VerticalAlignment.Center
		};
		slider.ValueChanged += (object _, RoutedPropertyChangedEventArgs<double> _) =>
		{
			double value2 = Math.Round(slider.Value / step) * step;
			value2 = Math.Clamp(Math.Round(value2, 3), min, max);
			label.Text = format(value2);
			set(value2);
			Apply(previewOnly, soon: true);
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		stackPanel.Children.Add(slider);
		stackPanel.Children.Add(label);
		return Row(title, description, stackPanel, glyph);
	}

	private Grid ChoiceRow(string title, string? description, (string Text, string Value)[] options, string current, Action<string> set, string? glyph = null, Action<string>? hover = null)
	{
		bool previewOnly = PreviewOnly;
		WrapPanel wrapPanel = new WrapPanel
		{
			Margin = new Thickness(0.0, 8.0, 0.0, -6.0)
		};
		string groupName = Guid.NewGuid().ToString();
		for (int i = 0; i < options.Length; i++)
		{
			(string, string) tuple = options[i];
			string item = tuple.Item1;
			string value = tuple.Item2;
			RadioButton radioButton = new RadioButton
			{
				Style = (Style)FindResource("T.Chip"),
				Content = item,
				GroupName = groupName,
				IsChecked = (value == current)
			};
			radioButton.Checked += (object _, RoutedEventArgs _) =>
			{
				set(value);
				Apply(previewOnly);
			};
			if (hover != null)
			{
				DispatcherTimer wait = new DispatcherTimer
				{
					Interval = TimeSpan.FromMilliseconds(180L)
				};
				wait.Tick += (object? _, EventArgs _) =>
				{
					wait.Stop();
					hover(value);
				};
				radioButton.MouseEnter += (object _, MouseEventArgs _) =>
				{
					wait.Start();
				};
				radioButton.MouseLeave += (object _, MouseEventArgs _) =>
				{
					wait.Stop();
				};
			}
			wrapPanel.Children.Add(radioButton);
		}
		Grid grid = Row(title, description, null, glyph);
		grid.Children.OfType<StackPanel>().First().Children.Add(wrapPanel);
		return grid;
	}

	private Grid SwatchRow(string title, (string Name, string Value)[] options, Func<string> current, Action<string> set, bool allowCustom, string? description = null)
	{
		bool previewOnly = PreviewOnly;
		StackPanel stackPanel = new StackPanel();
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		string groupName = Guid.NewGuid().ToString();
		List<(RadioButton Button, string Value)> radios = new List<(RadioButton, string)>();
		TextBox? hex = null;
		for (int i = 0; i < options.Length; i++)
		{
			(string, string) tuple = options[i];
			string item = tuple.Item1;
			string value = tuple.Item2;
			Brush fill = ((value == "auto") ? ((Brush)new LinearGradientBrush(new GradientStopCollection
			{
				new GradientStop(Color.FromRgb(byte.MaxValue, 90, 79), 0.0),
				new GradientStop(Color.FromRgb(byte.MaxValue, 201, 60), 0.33),
				new GradientStop(Color.FromRgb(94, 216, 122), 0.66),
				new GradientStop(Color.FromRgb(79, 163, byte.MaxValue), 1.0)
			}, 45.0)) : ((Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString(value))));
			RadioButton rb = new RadioButton
			{
				GroupName = groupName,
				IsChecked = string.Equals(value, current(), StringComparison.OrdinalIgnoreCase),
				ToolTip = item,
				Cursor = Cursors.Hand,
				Margin = new Thickness(2.0, 0.0, 2.0, 0.0),
				Template = SwatchTemplate(fill),
				RenderTransformOrigin = new Point(0.5, 0.5),
				RenderTransform = new ScaleTransform()
			};
			AutomationName(rb, item);
			rb.Checked += (object _, RoutedEventArgs _) =>
			{
				set(value);
				if (hex != null)
				{
					hex.Text = ((value == "auto") ? "" : value);
				}
				Apply(previewOnly);
				Pop(rb);
			};
			radios.Add((rb, value));
			stackPanel2.Children.Add(rb);
		}
		stackPanel.Children.Add(stackPanel2);
		if (allowCustom)
		{
			hex = new TextBox
			{
				Style = (Style)FindResource("T.TextBox"),
				Tag = "Свой: #RRGGBB",
				Width = 150.0,
				Margin = new Thickness(2.0, 8.0, 0.0, 0.0),
				HorizontalAlignment = HorizontalAlignment.Left,
				Text = ((current() == "auto") ? "" : current())
			};
			AutomationName(hex, title + ": свой цвет");
			hex.TextChanged += (object _, TextChangedEventArgs _) =>
			{
				string text = hex.Text.Trim();
				if (!text.StartsWith('#'))
				{
					text = "#" + text;
				}
				if (text.Length == 7)
				{
					try
					{
						ColorConverter.ConvertFromString(text);
					}
					catch (FormatException)
					{
						return;
					}
					if (!string.Equals(text, current(), StringComparison.OrdinalIgnoreCase))
					{
						set(text.ToUpperInvariant());
						foreach (var item4 in radios)
						{
							RadioButton item2 = item4.Button;
							string item3 = item4.Value;
							item2.IsChecked = string.Equals(item3, text, StringComparison.OrdinalIgnoreCase);
						}
						Apply(previewOnly, soon: true);
					}
				}
			};
			stackPanel.Children.Add(hex);
		}
		return Row(title, description, stackPanel);
	}

	private static void Pop(UIElement e)
	{
		if (e.RenderTransform is ScaleTransform scaleTransform)
		{
			DoubleAnimation animation = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(380L))
			{
				EasingFunction = new BackEase
				{
					Amplitude = 0.9,
					EasingMode = EasingMode.EaseOut
				}
			};
			scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
			scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
		}
	}

	private static ControlTemplate SwatchTemplate(Brush fill)
	{
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Grid));
		frameworkElementFactory.SetValue(FrameworkElement.WidthProperty, 28.0);
		frameworkElementFactory.SetValue(FrameworkElement.HeightProperty, 28.0);
		frameworkElementFactory.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(Ellipse), "Ring");
		frameworkElementFactory2.SetValue(Shape.StrokeProperty, new SolidColorBrush(Color.FromRgb(123, 140, byte.MaxValue)));
		frameworkElementFactory2.SetValue(Shape.StrokeThicknessProperty, 2.0);
		frameworkElementFactory2.SetValue(UIElement.OpacityProperty, 0.0);
		FrameworkElementFactory frameworkElementFactory3 = new FrameworkElementFactory(typeof(Ellipse));
		frameworkElementFactory3.SetValue(Shape.FillProperty, fill);
		frameworkElementFactory3.SetValue(Shape.StrokeProperty, new SolidColorBrush(Color.FromArgb(64, byte.MaxValue, byte.MaxValue, byte.MaxValue)));
		frameworkElementFactory3.SetValue(Shape.StrokeThicknessProperty, 1.0);
		frameworkElementFactory3.SetValue(FrameworkElement.MarginProperty, new Thickness(4.0));
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		frameworkElementFactory.AppendChild(frameworkElementFactory3);
		ControlTemplate controlTemplate = new ControlTemplate(typeof(RadioButton))
		{
			VisualTree = frameworkElementFactory
		};
		Trigger trigger = new Trigger
		{
			Property = ToggleButton.IsCheckedProperty,
			Value = true
		};
		trigger.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "Ring"));
		controlTemplate.Triggers.Add(trigger);
		return controlTemplate;
	}

	private Grid ButtonRow(string title, string? description, string buttonText, Action click, string? glyph = null)
	{
		Button button = new Button
		{
			Content = buttonText,
			Style = (Style)FindResource("T.Button")
		};
		button.Click += (object _, RoutedEventArgs _) =>
		{
			click();
		};
		return Row(title, description, button, glyph);
	}

	private IslandPreview Preview(IslandPreview.Kind kind, bool pinned = false)
	{
		IslandPreview islandPreview = new IslandPreview(kind)
		{
			Margin = new Thickness(0.0, 8.0, 0.0, pinned ? 4 : 10)
		};
		_previews.Add(islandPreview);
		(pinned ? StickyHost : Body).Children.Add(islandPreview);
		return islandPreview;
	}

	private static void AutomationName(DependencyObject element, string name)
	{
		AutomationProperties.SetName(element, name);
	}

	private static bool IsInside(DependencyObject d, DependencyObject ancestor)
	{
		for (DependencyObject dependencyObject = d; dependencyObject != null; dependencyObject = ((dependencyObject is Visual) ? VisualTreeHelper.GetParent(dependencyObject) : LogicalTreeHelper.GetParent(dependencyObject)))
		{
			if (dependencyObject == ancestor)
			{
				return true;
			}
		}
		return false;
	}
}
