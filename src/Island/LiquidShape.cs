using System;
using System.Windows;
using System.Windows.Media;

namespace Island;

public sealed class LiquidShape : FrameworkElement
{
	private static readonly Pen DefaultPen = FrozenPen(new LinearGradientBrush(new GradientStopCollection
	{
		new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0),
		new GradientStop(Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.55),
		new GradientStop(Color.FromArgb(56, byte.MaxValue, byte.MaxValue, byte.MaxValue), 1.0)
	}, new Point(0.0, 0.0), new Point(0.0, 1.0)));

	private static readonly Brush Sheen = Frozen(new LinearGradientBrush(new GradientStopCollection
	{
		new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0),
		new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.6),
		new GradientStop(Color.FromArgb(12, byte.MaxValue, byte.MaxValue, byte.MaxValue), 1.0)
	}, new Point(0.0, 0.0), new Point(0.0, 1.0)));

	private Geometry? _fill;

	private Geometry? _outline;

	private Brush _fillBrush = Brushes.Black;

	private Pen? _pen = DefaultPen;

	public static readonly DependencyProperty TintOpacityProperty = DependencyProperty.Register("TintOpacity", typeof(double), typeof(LiquidShape), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	private Brush? _tint;

	private double _tintBoost;

	private Geometry? _extraFill;

	private Geometry? _extraOutline;

	public double TintOpacity
	{
		get
		{
			return (double)GetValue(TintOpacityProperty);
		}
		set
		{
			SetValue(TintOpacityProperty, value);
		}
	}

	public double TintBoost
	{
		get
		{
			return _tintBoost;
		}
		set
		{
			if (!(Math.Abs(value - _tintBoost) < 0.002))
			{
				_tintBoost = value;
				InvalidateVisual();
			}
		}
	}

	public Brush? Tint
	{
		get
		{
			return _tint;
		}
		set
		{
			_tint = value;
			InvalidateVisual();
		}
	}

	public Action<DrawingContext, Rect>? Backdrop { get; set; }

	public Geometry? FillGeometry => _fill;

	public Color? StripColor { get; set; }

	public double StripLevel { get; set; }

	public double StripPhase { get; set; } = -1.0;

	public LiquidShape()
	{
		IsHitTestVisible = false;
	}

	public void SetLook(Color fill, bool outline)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(fill);
		solidColorBrush.Freeze();
		_fillBrush = solidColorBrush;
		_pen = (outline ? DefaultPen : null);
		InvalidateVisual();
	}

	public void Update(Geometry fill, Geometry outline, Geometry? extraFill = null, Geometry? extraOutline = null)
	{
		_fill = fill;
		_outline = outline;
		_extraFill = extraFill;
		_extraOutline = extraOutline;
		InvalidateVisual();
	}

	protected override void OnRender(DrawingContext dc)
	{
		if (_extraFill != null)
		{
			dc.DrawGeometry(_fillBrush, null, _extraFill);
			dc.DrawGeometry(Sheen, null, _extraFill);
			double num = Math.Min(1.0, TintOpacity + ((TintOpacity > 0.005) ? _tintBoost : 0.0));
			if (_tint != null && num > 0.005)
			{
				dc.PushOpacity(num);
				dc.DrawGeometry(_tint, null, _extraFill);
				dc.Pop();
			}
			if (_extraOutline != null && _pen != null)
			{
				dc.DrawGeometry(null, _pen, _extraOutline);
			}
		}
		if (_fill != null)
		{
			dc.DrawGeometry(_fillBrush, null, _fill);
			dc.DrawGeometry(Sheen, null, _fill);
		}
		double num2 = Math.Min(1.0, TintOpacity + ((TintOpacity > 0.005) ? _tintBoost : 0.0));
		if (_fill != null && _tint != null && num2 > 0.005)
		{
			dc.PushOpacity(num2);
			dc.DrawGeometry(_tint, null, _fill);
			dc.Pop();
		}
		if (_fill != null && Backdrop != null)
		{
			dc.PushClip(_fill);
			Backdrop(dc, _fill.Bounds);
			dc.Pop();
		}
		if (_outline != null && _pen != null)
		{
			dc.DrawGeometry(null, _pen, _outline);
		}
		DrawStrip(dc);
	}

	private void DrawStrip(DrawingContext dc)
	{
		Color? stripColor = StripColor;
		if (!stripColor.HasValue)
		{
			return;
		}
		Color valueOrDefault = stripColor.GetValueOrDefault();
		if (!(StripLevel < 0.01) && _fill != null)
		{
			Rect bounds = _fill.Bounds;
			RectangleGeometry clipGeometry = new RectangleGeometry(new Rect(bounds.X - 4.0, bounds.Y + bounds.Height * 0.4, bounds.Width + 8.0, bounds.Height));
			LinearGradientBrush opacityMask = new LinearGradientBrush(new GradientStopCollection
			{
				new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.4),
				new GradientStop(Color.FromArgb(byte.MaxValue, 0, 0, 0), 0.85)
			}, new Point(0.0, 0.0), new Point(0.0, 1.0));
			Brush brush;
			if (StripPhase >= 0.0)
			{
				double num = -0.25 + StripPhase * 1.5;
				Color color = Color.FromArgb(70, valueOrDefault.R, valueOrDefault.G, valueOrDefault.B);
				brush = new LinearGradientBrush(new GradientStopCollection
				{
					new GradientStop(color, num - 0.3),
					new GradientStop(valueOrDefault, num),
					new GradientStop(color, num + 0.3)
				}, new Point(0.0, 0.0), new Point(1.0, 0.0));
			}
			else
			{
				brush = new SolidColorBrush(valueOrDefault);
			}
			Geometry geometry = _outline ?? _fill;
			dc.PushClip(clipGeometry);
			dc.PushOpacityMask(opacityMask);
			dc.PushOpacity(StripLevel);
			dc.PushOpacity(0.28);
			dc.DrawGeometry(null, new Pen(brush, 5.0), geometry);
			dc.Pop();
			dc.DrawGeometry(null, new Pen(brush, 1.8), geometry);
			dc.Pop();
			dc.Pop();
			dc.Pop();
		}
	}

	private static Pen FrozenPen(Brush brush)
	{
		Pen pen = new Pen(brush, 1.0);
		pen.Freeze();
		return pen;
	}

	private static Brush Frozen(Brush brush)
	{
		brush.Freeze();
		return brush;
	}
}
