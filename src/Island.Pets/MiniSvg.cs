using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Linq;

namespace Island.Pets;

public sealed class MiniSvg
{
	public delegate void Paint(string cls, Style style);

	public sealed class Style
	{
		public Brush? Fill;

		public Brush? Stroke;

		public bool FillSet;

		public bool StrokeSet;

		public double? StrokeWidth;

		public double? Opacity;

		public PenLineCap? Cap;

		public PenLineJoin? Join;

		public double[]? Dash;
	}

	public readonly Canvas Root = new Canvas();

	private readonly Dictionary<string, List<FrameworkElement>> _byClass = new Dictionary<string, List<FrameworkElement>>();

	private readonly Paint _paint;

	public MiniSvg(string markup, Paint paint)
	{
		_paint = paint;
		foreach (XElement item in XElement.Parse("<g>" + markup + "</g>").Elements())
		{
			Add(Root, item, new Style());
		}
	}

	public IReadOnlyList<FrameworkElement> All(string cls)
	{
		if (!_byClass.TryGetValue(cls, out List<FrameworkElement>? value))
		{
			return Array.Empty<FrameworkElement>();
		}
		return value;
	}

	public FrameworkElement? One(string cls)
	{
		return All(cls).FirstOrDefault();
	}

	private void Register(XElement e, FrameworkElement element)
	{
		string[] array = Classes(e);
		foreach (string key in array)
		{
			if (!_byClass.TryGetValue(key, out List<FrameworkElement>? value))
			{
				value = (_byClass[key] = new List<FrameworkElement>());
			}
			value.Add(element);
		}
	}

	private static string[] Classes(XElement e)
	{
		return (((string?)e.Attribute("class")) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
	}

	private void Add(Canvas parent, XElement e, Style inherited)
	{
		Style style = new Style
		{
			Fill = inherited.Fill,
			FillSet = inherited.FillSet,
			Stroke = inherited.Stroke,
			StrokeSet = inherited.StrokeSet,
			StrokeWidth = inherited.StrokeWidth,
			Cap = inherited.Cap,
			Join = inherited.Join,
			Dash = inherited.Dash
		};
		ApplyAttributes(style, (string a) => (string?)e.Attribute(a));
		string[] array = Classes(e);
		foreach (string cls in array)
		{
			_paint(cls, style);
		}
		string? text = (string?)e.Attribute("style");
		if (text != null)
		{
			Dictionary<string, string> map = (from p in text.Split(';', StringSplitOptions.RemoveEmptyEntries)
				select p.Split(':', 2) into p
				where p.Length == 2
				select p).ToDictionary((string[] p) => p[0].Trim(), (string[] p) => p[1].Trim());
			ApplyAttributes(style, (string a) => (!map.TryGetValue(a, out var value)) ? null : value);
		}
		string localName = e.Name.LocalName;
		FrameworkElement frameworkElement;
		if (localName == "g")
		{
			frameworkElement = Group(e, style);
		}
		else
		{
			frameworkElement = ((!(localName == "text")) ? Shape(e, style) : Text(e, style));
		}
		FrameworkElement? frameworkElement2 = frameworkElement;
		if (frameworkElement2 == null)
		{
			return;
		}
		string? text2 = (string?)e.Attribute("transform");
		if (text2 != null)
		{
			RotateTransform? rotateTransform = Rotation(text2);
			if (rotateTransform != null)
			{
				frameworkElement2.RenderTransform = rotateTransform;
			}
		}
		double? opacity = style.Opacity;
		if (opacity.HasValue)
		{
			double valueOrDefault = opacity.GetValueOrDefault();
			frameworkElement2.Opacity = valueOrDefault;
		}
		Register(e, frameworkElement2);
		parent.Children.Add(frameworkElement2);
	}

	private Canvas Group(XElement e, Style style)
	{
		Canvas canvas = new Canvas();
		Style inherited = new Style
		{
			Fill = style.Fill,
			FillSet = style.FillSet,
			Stroke = style.Stroke,
			StrokeSet = style.StrokeSet,
			StrokeWidth = style.StrokeWidth,
			Cap = style.Cap,
			Join = style.Join,
			Dash = style.Dash
		};
		foreach (XElement item in e.Elements())
		{
			Add(canvas, item, inherited);
		}
		return canvas;
	}

	private static FrameworkElement? Shape(XElement e, Style s)
	{
		Geometry? geometry = e.Name.LocalName switch
		{
			"path" => Geometry.Parse(((string?)e.Attribute("d")) ?? ""), 
			"rect" => new RectangleGeometry(new Rect(N("x"), N("y"), N("width"), N("height")), N("rx"), N("ry", N("rx"))), 
			"circle" => new EllipseGeometry(new Point(N("cx"), N("cy")), N("r"), N("r")), 
			"ellipse" => new EllipseGeometry(new Point(N("cx"), N("cy")), N("rx"), N("ry")), 
			"line" => new LineGeometry(new Point(N("x1"), N("y1")), new Point(N("x2"), N("y2"))), 
			_ => null, 
		};
		if (geometry == null)
		{
			return null;
		}
		geometry.Freeze();
		double width = s.StrokeWidth ?? 1.0;
		Path path = new Path
		{
			Data = geometry,
			Fill = (s.FillSet ? s.Fill : ((e.Name.LocalName == "line") ? null : Brushes.Black)),
			Stroke = (s.StrokeSet ? s.Stroke : null),
			StrokeThickness = width,
			StrokeStartLineCap = s.Cap.GetValueOrDefault(),
			StrokeEndLineCap = s.Cap.GetValueOrDefault(),
			StrokeDashCap = s.Cap.GetValueOrDefault(),
			StrokeLineJoin = s.Join.GetValueOrDefault()
		};
		if (s.Dash != null)
		{
			path.StrokeDashArray = new DoubleCollection(s.Dash.Select((double d) => d / width));
		}
		return path;
		double N(string name, double fallback = 0.0)
		{
			if (!double.TryParse((string?)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				return fallback;
			}
			return result;
		}
	}

	private static FrameworkElement Text(XElement e, Style s)
	{
		double num = (double.TryParse((string?)e.Attribute("font-size"), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 12.0);
		double num2 = double.Parse(((string?)e.Attribute("x")) ?? "0", CultureInfo.InvariantCulture);
		double num3 = double.Parse(((string?)e.Attribute("y")) ?? "0", CultureInfo.InvariantCulture);
		TextBlock textBlock = new TextBlock
		{
			Text = e.Value,
			FontSize = num,
			Foreground = (s.Fill ?? Brushes.Black),
			FontFamily = new FontFamily("Segoe UI")
		};
		string? text = (string?)e.Attribute("font-weight");
		textBlock.FontWeight = ((text != null && int.TryParse(text, out var result2) && result2 >= 700) ? FontWeights.ExtraBold : FontWeights.Normal);
		TextBlock textBlock2 = textBlock;
		textBlock2.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
		bool flag = (string?)e.Attribute("text-anchor") == "middle";
		Canvas.SetLeft(textBlock2, flag ? (num2 - textBlock2.DesiredSize.Width / 2.0) : num2);
		Canvas.SetTop(textBlock2, num3 - num * 0.98);
		return textBlock2;
	}

	private static RotateTransform? Rotation(string transform)
	{
		if (!transform.StartsWith("rotate("))
		{
			return null;
		}
		double[] array = (from v in transform.Substring(7, transform.Length - 1 - 7).Split(new char[2] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
			select double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
		return new RotateTransform(array[0], (array.Length > 2) ? array[1] : 0.0, (array.Length > 2) ? array[2] : 0.0);
	}

	private static void ApplyAttributes(Style s, Func<string, string?> get)
	{
		string? text = get("fill");
		if (text != null)
		{
			s.Fill = Color(text);
			s.FillSet = true;
		}
		string? text2 = get("stroke");
		if (text2 != null)
		{
			s.Stroke = Color(text2);
			s.StrokeSet = true;
		}
		string? text3 = get("stroke-width");
		if (text3 != null)
		{
			s.StrokeWidth = double.Parse(text3, CultureInfo.InvariantCulture);
		}
		string? text4 = get("opacity");
		if (text4 != null)
		{
			s.Opacity = double.Parse(text4, CultureInfo.InvariantCulture);
		}
		string? text5 = get("stroke-linecap");
		if (text5 != null)
		{
			s.Cap = ((text5 == "round") ? PenLineCap.Round : ((text5 == "square") ? PenLineCap.Square : PenLineCap.Flat));
		}
		string? text6 = get("stroke-linejoin");
		if (text6 != null)
		{
			s.Join = ((text6 == "round") ? PenLineJoin.Round : PenLineJoin.Miter);
		}
	}

	public static Brush? Color(string value)
	{
		value = value.Trim();
		if (value == "none")
		{
			return null;
		}
		if (value.StartsWith('#') && value.Length == 9)
		{
			value = "#" + value.Substring(7, 2) + value.Substring(1, 6);
		}
		Brush? brush = (Brush)new BrushConverter().ConvertFromString(value);
		brush.Freeze();
		return brush;
	}
}
