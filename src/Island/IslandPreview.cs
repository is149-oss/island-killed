using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Island.Services;

namespace Island;

public sealed class IslandPreview : Border
{
	public enum Kind
	{
		Look,
		Strip
	}

	private const double CollapsedW = 300.0;

	private const double CollapsedH = 34.0;

	private const double IdleW = 252.0;

	private const double ExpandedW = 400.0;

	private const double ExpandedH = 174.0;

	private readonly Kind _kind;

	private readonly LiquidShape _shape = new LiquidShape();

	private readonly Border _pill = new Border
	{
		HorizontalAlignment = HorizontalAlignment.Center,
		VerticalAlignment = VerticalAlignment.Top,
		ClipToBounds = true
	};

	private readonly Grid _host = new Grid();

	private readonly Grid _collapsed = new Grid();

	private readonly Grid _expanded = new Grid();

	private readonly ScaleTransform _scale = new ScaleTransform();

	private readonly ScaleTransform _hideScale = new ScaleTransform();

	private readonly TranslateTransform _hideShift = new TranslateTransform();

	private readonly Grid _content = new Grid();

	private readonly Canvas _decor = new Canvas
	{
		IsHitTestVisible = false
	};

	private int _hidePlay;

	private readonly DispatcherTimer _back = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(1400L)
	};

	private string _style = "drop";

	private double _lastW = -1.0;

	private double _lastH = -1.0;

	private double _rippleAmp;

	private double _rippleStart;

	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private bool _expandedNow;

	private bool _compact;

	private static Settings S => Settings.Current;

	public bool Compact
	{
		get
		{
			return _compact;
		}
		set
		{
			if (_compact != value)
			{
				_compact = value;
				FitHeight();
			}
		}
	}

	private double Scale => Math.Clamp((_kind != Kind.Strip && (!_compact || _expandedNow)) ? S.UiScale : (S.UiScaleCollapsed ?? S.UiScale), 0.75, 1.5);

	internal double FullHeight => 40.0 + 174.0 * Scale;

	internal double CompactHeight => 30.0 + 34.0 * Scale + 16.0;

	private static Color Accent => Parse(S.Accent) ?? Color.FromRgb(byte.MaxValue, 158, 122);

	public IslandPreview(Kind kind)
	{
		_kind = kind;
		CornerRadius = new CornerRadius(14.0);
		ClipToBounds = true;
		Background = new LinearGradientBrush(Color.FromRgb(43, 48, 72), Color.FromRgb(23, 25, 36), 75.0);
		BorderBrush = new SolidColorBrush(Color.FromArgb(48, byte.MaxValue, byte.MaxValue, byte.MaxValue));
		BorderThickness = new Thickness(1.0);
		_host.LayoutTransform = _scale;
		_host.VerticalAlignment = VerticalAlignment.Top;
		_host.Children.Add(_shape);
		_pill.Width = ((kind == Kind.Look) ? 300.0 : 252.0);
		_pill.Height = 34.0;
		_content.Children.Add(_collapsed);
		_content.Children.Add(_expanded);
		_expanded.Opacity = 0.0;
		_pill.Child = _content;
		_host.RenderTransformOrigin = new Point(0.5, 0.0);
		_host.RenderTransform = new TransformGroup
		{
			Children = 
			{
				(Transform)_hideScale,
				(Transform)_hideShift
			}
		};
		_host.Children.Add(_pill);
		Child = new Grid
		{
			Children = 
			{
				(UIElement)_decor,
				(UIElement)_host
			}
		};
		_back.Tick += (object? _, EventArgs _) =>
		{
			_back.Stop();
			Collapse();
		};
		Loaded += (object _, RoutedEventArgs _) =>
		{
			CompositionTarget.Rendering += OnFrame;
		};
		Unloaded += (object _, RoutedEventArgs _) =>
		{
			CompositionTarget.Rendering -= OnFrame;
		};
		Refresh();
	}

	public void Play(string? style = null)
	{
		if (_kind != Kind.Look)
		{
			return;
		}
		_style = style ?? S.AnimationStyle;
		_back.Stop();
		if (_expandedNow)
		{
			Collapse();
			DispatcherTimer again = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(520L)
			};
			again.Tick += (object? _, EventArgs _) =>
			{
				again.Stop();
				Expand();
			};
			again.Start();
		}
		else
		{
			Expand();
		}
	}

	public void SnapCompact()
	{
		_compact = true;
		FitHeight(0, instant: true);
	}

	private void FitHeight(int delay = 0, bool instant = false)
	{
		double num = ((_kind == Kind.Look && (!_compact || _expandedNow)) ? FullHeight : CompactHeight);
		if (instant || double.IsNaN(Height))
		{
			BeginAnimation(FrameworkElement.HeightProperty, null);
			Height = num;
		}
		else
		{
			MainWindow.Animate(this, FrameworkElement.HeightProperty, num, (!instant) ? 340 : 0, new CubicEase
			{
				EasingMode = EasingMode.EaseInOut
			}, delay);
		}
	}

	private void Expand()
	{
		_expandedNow = true;
		if (_compact)
		{
			FitHeight();
		}
		MainWindow.AnimateSize(_pill, 400.0, 174.0, _style, growing: true, StartRipple);
		Fade(_collapsed, 0.0, 120, 0);
		Fade(_expanded, 1.0, 260, 90);
		_back.Start();
	}

	private void Collapse()
	{
		_expandedNow = false;
		MainWindow.AnimateSize(_pill, 300.0, 34.0, _style, growing: false, StartRipple);
		if (_compact)
		{
			FitHeight(420);
		}
		Fade(_expanded, 0.0, 120, 0);
		Fade(_collapsed, 1.0, 260, 60);
	}

	public void PlayHide(string style)
	{
		if (_kind != Kind.Look)
		{
			return;
		}
		int play = ++_hidePlay;
		if (style == "none")
		{
			_host.BeginAnimation(UIElement.OpacityProperty, null);
			_host.Opacity = 0.0;
			Back();
			return;
		}
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = MainWindow.AnimateLeave(_host, _content, _hideScale, _hideShift, style);
		doubleAnimationUsingKeyFrames.Completed += (object? _, EventArgs _) =>
		{
			Back();
		};
		_host.BeginAnimation(UIElement.OpacityProperty, doubleAnimationUsingKeyFrames);
		void Back()
		{
			DispatcherTimer wait = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(450L)
			};
			wait.Tick += (object? _, EventArgs _) =>
			{
				wait.Stop();
				if (play == _hidePlay)
				{
					if (style == "none")
					{
						_host.Opacity = 1.0;
					}
					else
					{
						MainWindow.AnimateEnter(_host, _content, _hideScale, _hideShift, style);
					}
				}
			};
			wait.Start();
		}
	}

	private static void Fade(UIElement e, double to, int ms, int delay)
	{
		MainWindow.Animate(e, UIElement.OpacityProperty, to, ms, new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		}, delay);
	}

	private void StartRipple(double amplitude)
	{
		_rippleAmp = amplitude;
		_rippleStart = _clock.Elapsed.TotalSeconds;
	}

	public void Refresh()
	{
		ScaleTransform scale = _scale;
		double scaleX = (_scale.ScaleY = Scale);
		scale.ScaleX = scaleX;
		FitHeight(0, instant: true);
		Color fill = Parse(S.IslandColor) ?? Colors.Black;
		_shape.SetLook(fill, S.Outline);
		_shape.Effect = (S.Shadow ? new DropShadowEffect
		{
			BlurRadius = 18.0,
			ShadowDepth = 3.0,
			Direction = 270.0,
			Opacity = 0.55,
			Color = Colors.Black
		} : null);
		BuildContent();
		BuildDecor();
		_lastW = -1.0;
	}

	private static Color? Parse(string? hex)
	{
		if (string.IsNullOrEmpty(hex) || hex == "auto")
		{
			return null;
		}
		try
		{
			return (Color)ColorConverter.ConvertFromString(hex);
		}
		catch (FormatException)
		{
			return null;
		}
	}

	private void BuildContent()
	{
		_collapsed.Children.Clear();
		_expanded.Children.Clear();
		SolidColorBrush solidColorBrush = new SolidColorBrush(Accent);
		LinearGradientBrush background = new LinearGradientBrush(Accent, Color.FromRgb(106, 61, 216), 45.0);
		SolidColorBrush fg = new SolidColorBrush(Color.FromArgb(153, byte.MaxValue, byte.MaxValue, byte.MaxValue));
		if (_kind == Kind.Strip)
		{
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Margin = new Thickness(16.0, 0.0, 0.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center
			};
			stackPanel.Children.Add(Text("12:34", 13.0, Brushes.White, FontWeights.SemiBold));
			if (S.IdleShowKeyboard)
			{
				stackPanel.Children.Add(new Border
				{
					Margin = new Thickness(7.0, 1.0, 0.0, 0.0),
					Height = 15.0,
					CornerRadius = new CornerRadius(5.0),
					Padding = new Thickness(4.0, 0.0, 4.0, 0.0),
					Background = new SolidColorBrush(Color.FromArgb(36, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
					VerticalAlignment = VerticalAlignment.Center,
					Child = Text("RU", 9.5, fg, FontWeights.Bold)
				});
			}
			StackPanel stackPanel2 = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				HorizontalAlignment = HorizontalAlignment.Right,
				Margin = new Thickness(0.0, 0.0, 15.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center
			};
			if (License.IsPro && S.IdleShowWeather)
			{
				stackPanel2.Children.Add(Text("☁ 14°", 12.0, Brushes.White, FontWeights.SemiBold, new Thickness(0.0, 0.0, S.IdleShowBattery ? 10 : 0, 0.0)));
			}
			if (S.IdleShowBattery)
			{
				stackPanel2.Children.Add(Text("ср, 29 окт", 12.0, fg, FontWeights.Normal));
			}
			Grid grid = new Grid
			{
				Width = 252.0,
				Height = 34.0,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Top
			};
			grid.Children.Add(stackPanel);
			grid.Children.Add(stackPanel2);
			_collapsed.Children.Add(grid);
			return;
		}
		Grid grid2 = new Grid
		{
			Width = 298.0,
			Height = 32.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top
		};
		grid2.Children.Add(new Border
		{
			Width = 22.0,
			Height = 22.0,
			CornerRadius = new CornerRadius(6.0),
			Background = background,
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(6.0, 0.0, 0.0, 0.0)
		});
		grid2.Children.Add(Text("Название трека", 12.0, Brushes.White, FontWeights.SemiBold, new Thickness(38.0, 0.0, 0.0, 0.0)));
		StackPanel stackPanel3 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 13.0, 0.0)
		};
		double[] array = new double[4] { 8.0, 12.0, 6.0, 10.0 };
		foreach (double height in array)
		{
			stackPanel3.Children.Add(new Rectangle
			{
				Width = 3.0,
				Height = height,
				RadiusX = 1.5,
				RadiusY = 1.5,
				Fill = solidColorBrush,
				Margin = new Thickness(1.5, 0.0, 1.5, 0.0),
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		grid2.Children.Add(stackPanel3);
		_collapsed.Children.Add(grid2);
		Grid grid3 = new Grid
		{
			Width = 398.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top
		};
		StackPanel stackPanel4 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		stackPanel4.Children.Add(new Border
		{
			Width = 74.0,
			Height = 26.0,
			CornerRadius = new CornerRadius(13.0),
			Background = new SolidColorBrush(Color.FromArgb(46, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Child = Text("Плеер", 12.0, Brushes.White, FontWeights.SemiBold, default, center: true)
		});
		for (int j = 0; j < 6; j++)
		{
			stackPanel4.Children.Add(new Border
			{
				Width = 14.0,
				Height = 14.0,
				Margin = new Thickness(11.0, 0.0, 0.0, 0.0),
				CornerRadius = new CornerRadius(4.0),
				Background = new SolidColorBrush(Color.FromArgb(64, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		grid3.Children.Add(stackPanel4);
		Grid grid4 = new Grid
		{
			Margin = new Thickness(16.0, 50.0, 14.0, 0.0),
			VerticalAlignment = VerticalAlignment.Top
		};
		grid4.Children.Add(new Border
		{
			Width = 56.0,
			Height = 56.0,
			CornerRadius = new CornerRadius(12.0),
			Background = background,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top
		});
		StackPanel stackPanel5 = new StackPanel
		{
			Margin = new Thickness(70.0, 4.0, 0.0, 0.0)
		};
		stackPanel5.Children.Add(Text("Название трека", 14.0, Brushes.White, FontWeights.SemiBold));
		stackPanel5.Children.Add(Text("Исполнитель", 12.0, fg, FontWeights.Normal, new Thickness(0.0, 3.0, 0.0, 0.0)));
		grid4.Children.Add(stackPanel5);
		grid4.Children.Add(new Ellipse
		{
			Width = 38.0,
			Height = 38.0,
			Fill = Brushes.White,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 8.0, 36.0, 0.0)
		});
		Grid grid5 = new Grid
		{
			Height = 4.0,
			Margin = new Thickness(0.0, 80.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Top
		};
		grid5.Children.Add(new Border
		{
			CornerRadius = new CornerRadius(2.0),
			Background = new SolidColorBrush(Color.FromArgb(51, byte.MaxValue, byte.MaxValue, byte.MaxValue))
		});
		grid5.Children.Add(new Border
		{
			CornerRadius = new CornerRadius(2.0),
			Background = solidColorBrush,
			HorizontalAlignment = HorizontalAlignment.Left,
			Width = 140.0
		});
		grid4.Children.Add(grid5);
		grid3.Children.Add(grid4);
		_expanded.Children.Add(grid3);
	}

	private void BuildDecor()
	{
		_decor.Children.Clear();
		if (!MainWindow.HalloweenNow)
		{
			return;
		}
		Ellipse element = new Ellipse
		{
			Width = 26.0,
			Height = 26.0,
			Fill = new RadialGradientBrush(Color.FromRgb(byte.MaxValue, 244, 200), Color.FromRgb(232, 210, 138))
			{
				GradientOrigin = new Point(0.35, 0.35)
			},
			Effect = new DropShadowEffect
			{
				Color = Color.FromRgb(byte.MaxValue, 232, 160),
				BlurRadius = 22.0,
				ShadowDepth = 0.0,
				Opacity = 0.55
			}
		};
		Canvas.SetLeft(element, 34.0);
		Canvas.SetTop(element, (_kind == Kind.Look) ? 56 : 22);
		_decor.Children.Add(element);
		bool flag = !SystemParameters.ClientAreaAnimation || S.ReduceMotion;
		(double, double, double, int)[] array = new (double, double, double, int)[2]
		{
			(0.8, 0.3, 32.0, 260),
			(0.88, 0.52, 24.0, 210)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(double, double, double, int) tuple = array[i];
			double x = tuple.Item1;
			double y = tuple.Item2;
			double item = tuple.Item3;
			int item2 = tuple.Item4;
			ScaleTransform scaleTransform = new ScaleTransform();
			Image bat = new Image
			{
				Source = MainWindow.BatImage(),
				Width = item,
				Height = item / 2.0,
				RenderTransformOrigin = new Point(0.5, 0.5),
				RenderTransform = scaleTransform,
				Effect = new DropShadowEffect
				{
					Color = Color.FromRgb(180, 156, byte.MaxValue),
					BlurRadius = 8.0,
					ShadowDepth = 0.0,
					Opacity = 0.7
				}
			};
			_decor.Children.Add(bat);
			_decor.SizeChanged += (object _, SizeChangedEventArgs _) =>
			{
				Place();
			};
			Place();
			if (!flag)
			{
				scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, 0.45, TimeSpan.FromMilliseconds(item2))
				{
					AutoReverse = true,
					RepeatBehavior = RepeatBehavior.Forever
				});
			}
			void Place()
			{
				Canvas.SetLeft(bat, _decor.ActualWidth * x);
				Canvas.SetTop(bat, Math.Max(8.0, (Height - 10.0) * y));
			}
		}
	}

	private static TextBlock Text(string s, double size, Brush fg, FontWeight weight, Thickness margin = default(Thickness), bool center = false)
	{
		return new TextBlock
		{
			Text = s,
			FontSize = size,
			Foreground = fg,
			FontWeight = weight,
			Margin = margin,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = (center ? HorizontalAlignment.Center : HorizontalAlignment.Left),
			FontFamily = AppFont.Current
		};
	}

	private void OnFrame(object? sender, EventArgs e)
	{
		double actualWidth = _pill.ActualWidth;
		double actualHeight = _pill.ActualHeight;
		double totalSeconds = _clock.Elapsed.TotalSeconds;
		double num = _rippleAmp * Math.Exp((0.0 - (totalSeconds - _rippleStart)) * 2.8);
		if (num < 0.05)
		{
			num = 0.0;
		}
		if (actualWidth != _lastW || actualHeight != _lastH || num != 0.0)
		{
			_lastW = actualWidth;
			_lastH = actualHeight;
			if (!(actualWidth <= 0.0) && !(actualHeight <= 0.0))
			{
				double num2 = (_host.ActualWidth - actualWidth) / 2.0;
				double num3 = num2 + actualWidth;
				double num4 = Math.Min(Math.Min(actualHeight / 2.0, Math.Clamp(S.CornerRadius, 0.1, 1.0) * 48.0), (num3 - num2) / 2.0);
				double val = (7.0 + 6.0 * Math.Clamp((actualHeight - 34.0) / 150.0, 0.0, 1.0)) * S.NeckScale;
				val = Math.Min(val, Math.Max(0.0, actualHeight - num4));
				MainWindow.Bottom b = new MainWindow.Bottom(0.0, 0.0, 0.0, 0.0, 0.0, num, totalSeconds - _rippleStart);
				_shape.Update(MainWindow.Outline(num2, num3, actualHeight, num4, val, b, closed: true), MainWindow.Outline(num2, num3, actualHeight, num4, val, b, closed: false));
			}
		}
	}
}
