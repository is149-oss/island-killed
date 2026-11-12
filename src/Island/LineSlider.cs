using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Island;

public sealed class LineSlider : Grid
{
	private static readonly Brush TrackBrush = Frozen(Color.FromArgb(46, byte.MaxValue, byte.MaxValue, byte.MaxValue));

	private readonly Border _track = new Border
	{
		Height = 3.0,
		CornerRadius = new CornerRadius(1.5),
		VerticalAlignment = VerticalAlignment.Center
	};

	private readonly Border _fill = new Border
	{
		Height = 3.0,
		CornerRadius = new CornerRadius(1.5),
		HorizontalAlignment = HorizontalAlignment.Left,
		VerticalAlignment = VerticalAlignment.Center
	};

	private readonly Ellipse _knob = new Ellipse
	{
		Width = 11.0,
		Height = 11.0,
		Fill = Brushes.White,
		HorizontalAlignment = HorizontalAlignment.Left,
		VerticalAlignment = VerticalAlignment.Center,
		RenderTransformOrigin = new Point(0.5, 0.5),
		Effect = new DropShadowEffect
		{
			ShadowDepth = 0.0,
			BlurRadius = 6.0,
			Opacity = 0.45
		}
	};

	private readonly ScaleTransform _knobScale = new ScaleTransform(0.0, 0.0);

	private readonly TranslateTransform _knobShift = new TranslateTransform();

	private double _v;

	public bool IsDragging => IsMouseCaptured;

	public Brush Fill
	{
		get
		{
			return _fill.Background;
		}
		set
		{
			_fill.Background = value;
		}
	}

	public double Value
	{
		get
		{
			return _v;
		}
		set
		{
			_v = Math.Clamp(value, 0.0, 1.0);
			Layout();
		}
	}

	public event Action<double>? ValueChanged;

	public event Action<bool>? HoverChanged;

	public LineSlider()
	{
		Height = 16.0;
		Background = Brushes.Transparent;
		Cursor = Cursors.Hand;
		_track.Background = TrackBrush;
		_fill.Background = Brushes.White;
		_knob.RenderTransform = new TransformGroup
		{
			Children = 
			{
				(Transform)_knobScale,
				(Transform)_knobShift
			}
		};
		Children.Add(_track);
		Children.Add(_fill);
		Children.Add(_knob);
		SizeChanged += (object _, SizeChangedEventArgs _) =>
		{
			Layout();
		};
		MouseEnter += (object _, MouseEventArgs _) =>
		{
			Hover(on: true);
		};
		MouseLeave += (object _, MouseEventArgs _) =>
		{
			if (!IsMouseCaptured)
			{
				Hover(on: false);
			}
		};
		MouseLeftButtonDown += (object _, MouseButtonEventArgs e) =>
		{
			CaptureMouse();
			SetFromPointer(e);
			e.Handled = true;
		};
		MouseMove += (object _, MouseEventArgs e) =>
		{
			if (IsMouseCaptured)
			{
				SetFromPointer(e);
			}
		};
		MouseLeftButtonUp += (object _, MouseButtonEventArgs e) =>
		{
			ReleaseMouseCapture();
			if (!IsMouseOver)
			{
				Hover(on: false);
			}
			e.Handled = true;
		};
		MouseWheel += (object _, MouseWheelEventArgs e) =>
		{
			SetFromUser(_v + ((e.Delta > 0) ? 0.04 : (-0.04)));
			e.Handled = true;
		};
	}

	private void Hover(bool on)
	{
		CubicEase cubicEase = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		BackEase backEase = new BackEase
		{
			Amplitude = 0.5,
			EasingMode = EasingMode.EaseOut
		};
		double toValue = (on ? 5 : 3);
		_track.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(toValue, TimeSpan.FromMilliseconds(160L))
		{
			EasingFunction = cubicEase
		});
		_fill.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(toValue, TimeSpan.FromMilliseconds(160L))
		{
			EasingFunction = cubicEase
		});
		ScaleTransform knobScale = _knobScale;
		DependencyProperty scaleXProperty = ScaleTransform.ScaleXProperty;
		DoubleAnimation doubleAnimation = new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 260 : 140));
		IEasingFunction easingFunction2;
		if (!on)
		{
			IEasingFunction easingFunction = cubicEase;
			easingFunction2 = easingFunction;
		}
		else
		{
			IEasingFunction easingFunction = backEase;
			easingFunction2 = easingFunction;
		}
		doubleAnimation.EasingFunction = easingFunction2;
		knobScale.BeginAnimation(scaleXProperty, doubleAnimation);
		ScaleTransform knobScale2 = _knobScale;
		DependencyProperty scaleYProperty = ScaleTransform.ScaleYProperty;
		DoubleAnimation doubleAnimation2 = new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 260 : 140));
		IEasingFunction easingFunction3;
		if (!on)
		{
			IEasingFunction easingFunction = cubicEase;
			easingFunction3 = easingFunction;
		}
		else
		{
			IEasingFunction easingFunction = backEase;
			easingFunction3 = easingFunction;
		}
		doubleAnimation2.EasingFunction = easingFunction3;
		knobScale2.BeginAnimation(scaleYProperty, doubleAnimation2);
		HoverChanged?.Invoke(on);
	}

	private void SetFromPointer(MouseEventArgs e)
	{
		SetFromUser((ActualWidth > 0.0) ? (e.GetPosition(this).X / ActualWidth) : 0.0);
	}

	private void SetFromUser(double v)
	{
		Value = v;
		ValueChanged?.Invoke(_v);
	}

	private void Layout()
	{
		double num = ActualWidth * _v;
		_fill.Width = num;
		_knobShift.X = Math.Clamp(num - _knob.Width / 2.0, 0.0, Math.Max(0.0, ActualWidth - _knob.Width));
	}

	private static Brush Frozen(Color c)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(c);
		solidColorBrush.Freeze();
		return solidColorBrush;
	}
}
