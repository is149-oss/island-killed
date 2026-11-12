using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Island.Services;

namespace Island.Pets;

public sealed class PetView : Canvas
{
	private sealed class Part
	{
		public readonly ScaleTransform S;

		public readonly RotateTransform R = new RotateTransform();

		public readonly TranslateTransform T = new TranslateTransform();

		public double Tx;

		public double Ty;

		public double Rot;

		public double Sx = 1.0;

		public double Sy = 1.0;

		public readonly FrameworkElement? Element;

		public Part(FrameworkElement? e, double cx, double cy)
		{
			Element = e;
			S = new ScaleTransform(1.0, 1.0, cx, cy);
			R.CenterX = cx;
			R.CenterY = cy;
			if (e != null)
			{
				e.RenderTransform = new TransformGroup
				{
					Children = 
					{
						(Transform)S,
						(Transform)R,
						(Transform)T
					}
				};
			}
		}
	}

	private struct Pose
	{
		public (double, double, double, double, double) Cat;

		public (double, double, double, double, double) Body;

		public (double, double, double, double, double) Head;

		public (double, double, double, double, double) Tail;

		public (double, double, double, double, double) EarL;

		public (double, double, double, double, double) EarR;

		public (double, double, double, double, double) PawL;

		public (double, double, double, double, double) PawR;

		public (double, double, double, double, double) Mouth;

		public (double, double, double, double, double) Shadow;

		public (double, double, double, double, double) Paper;

		public (double, double, double, double, double) Sweat;

		public (double, double, double, double, double) Cloud;

		public (double, double, double, double, double) Ring;

		public double CatOpacity;

		public double ShadowOpacity;

		public double PaperOpacity;

		public double SweatOpacity;

		public double Eye;

		public bool WideTail;

		public (double X, double Y)? LookOverride;
	}

	public readonly string Kind;

	public readonly string Crop;

	private readonly MiniSvg _svg;

	private readonly Canvas _art;

	private readonly Canvas _fx;

	private readonly Random _random = new Random();

	private readonly Rect _view;

	private double _stateStart;

	private double _now;

	private double _lastTick = -1.0;

	private double _nextFx;

	private double _nextBlink;

	private double _blinkUntil;

	private double _secondBlink;

	private int _zCount;

	private readonly Part _cat;

	private readonly Part _body;

	private readonly Part _head;

	private readonly Part _look;

	private readonly Part _tail;

	private readonly Part _earL;

	private readonly Part _earR;

	private readonly Part _eyeL;

	private readonly Part _eyeR;

	private readonly Part _pawL;

	private readonly Part _pawR;

	private readonly Part _mouth;

	private readonly Part _shadow;

	private readonly Part _paper;

	private readonly Part _sweat;

	private readonly Part _bubble;

	private readonly Part _phones;

	private readonly Part _cloud;

	private readonly Part _ring;

	private readonly List<TranslateTransform> _irises = new List<TranslateTransform>();

	private double _ix;

	private double _iy;

	private double _catOpacity = 1.0;

	private static readonly Dictionary<string, Rect> CatCrops = new Dictionary<string, Rect>
	{
		["hang"] = new Rect(52.0, 44.0, 136.0, 120.0),
		["body"] = new Rect(26.0, 40.0, 188.0, 188.0),
		["full"] = new Rect(0.0, 0.0, 240.0, 240.0)
	};

	private static readonly Dictionary<string, Rect> BlockCrops = new Dictionary<string, Rect>
	{
		["hang"] = new Rect(40.0, 58.0, 160.0, 141.0),
		["body"] = new Rect(26.0, 40.0, 188.0, 188.0),
		["full"] = new Rect(0.0, 0.0, 240.0, 240.0)
	};

	private static readonly Dictionary<string, (double, double)> LookAt = new Dictionary<string, (double, double)>
	{
		["typing"] = (0.0, 3.6),
		["download"] = (0.5, 3.8),
		["notify"] = (3.5, -3.5),
		["curious"] = (2.5, -3.0),
		["think"] = (-3.5, -3.5),
		["offline"] = (0.0, 3.0),
		["sleep"] = (0.0, 1.0),
		["tired"] = (0.0, 2.5),
		["copy"] = (2.5, -2.5),
		["mute"] = (0.0, 1.0)
	};

	private static readonly string[] NoteColors = new string[4] { "#c9b6ff", "#ffd166", "#8fe3ff", "#ff9fc0" };

	public string State { get; private set; } = "idle";

	public string Bubble { get; private set; } = "";

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

	public PetView(string kind, string crop, double height)
	{
		bool flag;
		switch (kind)
		{
		case "black":
		case "ginger":
		case "block":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		Kind = (flag ? kind : "black");
		Crop = crop;
		_svg = new MiniSvg(PetArt.Markup(Kind), PetArt.Painter(Kind));
		_art = _svg.Root;
		_fx = (Canvas)_svg.One("fx");
		Children.Add(_art);
		ClipToBounds = true;
		_view = ((Kind == "block") ? BlockCrops : CatCrops)[crop];
		SetHeight(height);
		bool flag2 = Kind == "block";
		_cat = new Part(_svg.One("cat"), 120.0, 222.0);
		_body = new Part(_svg.One("body"), 120.0, flag2 ? 180 : 216);
		_head = new Part(_svg.One("head"), 120.0, 150.0);
		_look = new Part(_svg.One("look"), 120.0, 150.0);
		bool flag3 = Kind == "black";
		_tail = new Part(_svg.One(flag3 ? "tail-left" : "tail-right"), flag3 ? 85 : 155, 195.0);
		_earL = new Part(_svg.One("ear-l"), flag2 ? 64 : 92, flag2 ? 129 : 84);
		_earR = new Part(_svg.One("ear-r"), flag2 ? 176 : 148, flag2 ? 129 : 84);
		_eyeL = new Part(_svg.One("eye-l"), flag2 ? 97 : 98, flag2 ? 128 : 116);
		_eyeR = new Part(_svg.One("eye-r"), flag2 ? 143 : 142, flag2 ? 128 : 116);
		_pawL = new Part(_svg.One("paw-l"), flag2 ? 89 : 104, flag2 ? 214 : 212);
		_pawR = new Part(_svg.One("paw-r"), flag2 ? 151 : 136, flag2 ? 214 : 212);
		_mouth = new Part(_svg.One("mouth-open"), 120.0, flag2 ? 147 : 137);
		_shadow = new Part(_svg.One("shadow"), 120.0, 223.0);
		_paper = new Part(_svg.One("paper"), 157.0, 197.0);
		_sweat = new Part(_svg.One("sweat"), 172.0, 88.0);
		_bubble = new Part(_svg.One("bubble"), 180.0, 64.0);
		_phones = new Part(_svg.One("phones"), 120.0, 60.0);
		_cloud = new Part(_svg.One("cloud"), 120.0, 30.0);
		_ring = new Part(_svg.One("ring"), 200.0, 32.0);
		foreach (FrameworkElement item2 in _svg.All("iris"))
		{
			TranslateTransform item = (TranslateTransform)(item2.RenderTransform = new TranslateTransform());
			_irises.Add(item);
		}
		bool flag4 = Kind == "black";
		string[] array = new string[4] { "sclera", "legs", "sheen", "tail-left" };
		foreach (string cls in array)
		{
			SetShown(cls, flag4);
		}
		array = new string[3] { "stripe", "tail-s", "tail-right" };
		foreach (string cls2 in array)
		{
			SetShown(cls2, !flag4);
		}
		if (flag4)
		{
			foreach (FrameworkElement item3 in _svg.All("pupil"))
			{
				if (item3 is Path path)
				{
					item3.RenderTransform = new ScaleTransform(0.68, 0.74, path.Data.Bounds.X + path.Data.Bounds.Width / 2.0, path.Data.Bounds.Y + path.Data.Bounds.Height / 2.0);
				}
			}
		}
		ShowFor(State, Bubble);
	}

	public static double HeightFor(string kind, string crop, double width)
	{
		Rect rect = ((kind == "block") ? BlockCrops : CatCrops)[crop];
		return width * rect.Height / rect.Width;
	}

	public void SetHeight(double height)
	{
		double num = height / _view.Height;
		Width = _view.Width * num;
		Height = height;
		_art.RenderTransform = new TransformGroup
		{
			Children = 
			{
				(Transform)new TranslateTransform(0.0 - _view.X, 0.0 - _view.Y),
				(Transform)new ScaleTransform(num, num)
			}
		};
	}

	public void SetState(string state, string bubble, bool restart = false)
	{
		if ((state != State) | restart)
		{
			bool num = (state != State) | restart;
			State = state;
			_stateStart = _now;
			_nextFx = _now;
			bool flag = num;
			if (flag)
			{
				bool flag2 = ((state == "happy" || state == "bt") ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				Burst();
			}
		}
		if (bubble != Bubble || (restart && bubble.Length > 0))
		{
			Bubble = bubble;
			if (bubble.Length > 0 && !ReducedMotion)
			{
				Pop(_bubble);
			}
		}
		ShowFor(State, Bubble);
		if (state == "bt")
		{
			Pop(_phones);
		}
	}

	private static void Pop(Part part)
	{
		ElasticEase easingFunction = new ElasticEase
		{
			Oscillations = 1,
			Springiness = 5.0,
			EasingMode = EasingMode.EaseOut
		};
		part.S.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(550L))
		{
			EasingFunction = easingFunction
		});
		part.S.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(550L))
		{
			EasingFunction = easingFunction
		});
	}

	private void SetShown(string cls, bool on)
	{
		foreach (FrameworkElement item in _svg.All(cls))
		{
			item.Visibility = ((!on) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private void ShowFor(string st, string bubble)
	{
		bool flag;
		switch (st)
		{
		case "music":
		case "happy":
		case "pet":
		case "bt":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool flag2 = flag;
		switch (st)
		{
		case "sleep":
		case "wake":
		case "mute":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bool flag3 = flag;
		SetShown("eyes-open", !flag2 && !flag3);
		SetShown("eyes-happy", flag2);
		SetShown("eyes-closed", flag3);
		flag = ((st == "tired" || st == "offline") ? true : false);
		SetShown("lids", flag);
		switch (st)
		{
		case "startle":
		case "alarm":
		case "wake":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		SetShown("mouth-open", flag);
		flag = ((st == "tired" || st == "startle") ? true : false);
		SetShown("sweat", flag);
		flag = ((st == "music" || st == "bt") ? true : false);
		SetShown("phones", flag);
		SetShown("bar", st == "download");
		SetShown("paper", st == "copy");
		SetShown("cloud", st == "offline");
		SetShown("bubble", bubble.Length > 0);
		string[] iconNames = PetArt.IconNames;
		foreach (string text in iconNames)
		{
			SetShown("ic-" + text, text == bubble);
		}
		foreach (FrameworkElement item in _svg.All("blush"))
		{
			flag = ((st == "pet" || st == "happy") ? true : false);
			FrameworkElement frameworkElement = item;
			double opacity;
			if (flag)
			{
				opacity = 0.85;
			}
			else if (Kind == "black")
			{
				opacity = 0.22;
			}
			else
			{
				opacity = ((Kind == "block") ? 0.35 : 0.45);
			}
			frameworkElement.Opacity = opacity;
		}
		if (Crop == "hang")
		{
			iconNames = new string[7] { "tail", "paw", "shadow", "bar", "paper", "bubble", "cloud" };
			foreach (string cls in iconNames)
			{
				SetShown(cls, on: false);
			}
			if (Kind != "block")
			{
				SetShown("body", on: false);
			}
		}
		else if (Crop == "body")
		{
			iconNames = new string[5] { "shadow", "bar", "paper", "bubble", "cloud" };
			foreach (string cls2 in iconNames)
			{
				SetShown(cls2, on: false);
			}
		}
		if (Crop != "hang" && Kind == "black")
		{
			SetShown("tail-right", on: false);
		}
		if (Crop != "hang" && Kind != "black")
		{
			SetShown("tail-left", on: false);
		}
	}

	public void Tick(double now, Point? lookAt)
	{
		_now = now;
		if (_lastTick < 0.0)
		{
			_nextBlink = now + 1.0 + _random.NextDouble() * 3.0;
		}
		double num = ((_lastTick < 0.0) ? (1.0 / 30.0) : Math.Clamp(now - _lastTick, 1.0 / 240.0, 0.25));
		_lastTick = now;
		double ts = now - _stateStart;
		Pose pose = PoseFor(State, ts, now);
		double num2 = 1.0 - Math.Pow(0.5, num * 30.0);
		Move(_cat, pose.Cat, num2);
		_catOpacity += (pose.CatOpacity - _catOpacity) * num2;
		if (_cat.Element != null)
		{
			_cat.Element.Opacity = _catOpacity;
		}
		Move(_body, pose.Body, num2);
		Move(_head, pose.Head, num2);
		Move(_tail, pose.Tail, num2);
		Move(_earL, pose.EarL, num2);
		Move(_earR, pose.EarR, num2);
		Move(_pawL, pose.PawL, num2);
		Move(_pawR, pose.PawR, num2);
		Move(_mouth, pose.Mouth, num2);
		Move(_shadow, pose.Shadow, num2);
		if (_shadow.Element != null)
		{
			_shadow.Element.Opacity = pose.ShadowOpacity;
		}
		Move(_paper, pose.Paper, 1.0);
		if (_paper.Element != null)
		{
			_paper.Element.Opacity = pose.PaperOpacity;
		}
		Move(_sweat, pose.Sweat, 1.0);
		if (_sweat.Element != null)
		{
			_sweat.Element.Opacity = pose.SweatOpacity;
		}
		Move(_cloud, pose.Cloud, num2);
		Move(_ring, pose.Ring, 1.0);
		foreach (Path item in _svg.All("tail-o"))
		{
			item.StrokeThickness = (pose.WideTail ? 28 : 22);
		}
		string[] array = new string[2] { "tail-f", "tail-s" };
		foreach (string cls in array)
		{
			foreach (Path item2 in _svg.All(cls))
			{
				item2.StrokeThickness = (pose.WideTail ? 22 : 16);
			}
		}
		double num3 = 1.0;
		if (now >= _nextBlink)
		{
			_blinkUntil = now + 0.15;
			_secondBlink = ((_random.NextDouble() < 0.2) ? (now + 0.26) : 0.0);
			_nextBlink = now + 2.2 + _random.NextDouble() * 3.5;
		}
		if (_secondBlink > 0.0 && now >= _secondBlink)
		{
			_blinkUntil = now + 0.15;
			_secondBlink = 0.0;
		}
		if (now < _blinkUntil)
		{
			num3 = 0.08;
		}
		Part[] array2 = new Part[2] { _eyeL, _eyeR };
		foreach (Part part in array2)
		{
			part.Sx += (pose.Eye - part.Sx) * num2;
			part.S.ScaleX = part.Sx;
			part.S.ScaleY = ((now < _blinkUntil) ? num3 : part.Sx);
		}
		Look(pose.LookOverride, lookAt, num);
		Effects(now);
	}

	private static void Move(Part part, (double Tx, double Ty, double Rot, double Sx, double Sy) to, double k)
	{
		part.Tx += (to.Tx - part.Tx) * k;
		part.Ty += (to.Ty - part.Ty) * k;
		part.Rot += (to.Rot - part.Rot) * k;
		part.Sx += (to.Sx - part.Sx) * k;
		part.Sy += (to.Sy - part.Sy) * k;
		part.T.X = part.Tx;
		part.T.Y = part.Ty;
		part.R.Angle = part.Rot;
		if (!part.S.HasAnimatedProperties)
		{
			part.S.ScaleX = part.Sx;
			part.S.ScaleY = part.Sy;
		}
	}

	private void Look((double X, double Y)? fixedLook, Point? cursor, double dt)
	{
		double num = 0.0;
		double num2 = 0.0;
		if (fixedLook.HasValue)
		{
			(num, num2) = fixedLook.GetValueOrDefault();
		}
		else if (cursor.HasValue)
		{
			Point valueOrDefault = cursor.GetValueOrDefault();
			if (PresentationSource.FromVisual(_art) != null)
			{
				Point point = _art.PointToScreen(new Point(120.0, 116.0));
				double num3 = valueOrDefault.X - point.X;
				double num4 = valueOrDefault.Y - point.Y;
				double num5 = Math.Max(1.0, Math.Sqrt(num3 * num3 + num4 * num4));
				double num6 = ((ActualWidth > 0.0) ? ActualWidth : Width);
				double num7 = num5 / (num5 + num6 * 0.5);
				num = num3 / num5 * num7 * 4.2;
				num2 = num4 / num5 * num7 * 3.6;
			}
		}
		double num8 = 1.0 - Math.Pow(0.92, dt * 60.0);
		_ix += (num - _ix) * num8;
		_iy += (num2 - _iy) * num8;
		foreach (TranslateTransform iris in _irises)
		{
			iris.X = _ix;
			iris.Y = _iy;
		}
		_look.R.Angle = _ix * 1.3;
		_look.T.X = _ix * 0.6;
		_look.T.Y = _iy * 0.5;
	}

	private static (double, double, double, double, double) T(double tx = 0.0, double ty = 0.0, double rot = 0.0, double sx = 1.0, double sy = 1.0)
	{
		return (tx, ty, rot, sx, sy);
	}

	private static double Track(double fraction, params double[] keys)
	{
		fraction = Math.Clamp(fraction, 0.0, 1.0);
		for (int i = 0; i + 3 < keys.Length; i += 2)
		{
			double num = keys[i];
			double num2 = keys[i + 2];
			if (!(fraction > num2))
			{
				double num3 = ((num2 > num) ? ((fraction - num) / (num2 - num)) : 1.0);
				num3 = num3 * num3 * (3.0 - 2.0 * num3);
				return keys[i + 1] + (keys[i + 3] - keys[i + 1]) * num3;
			}
		}
		return keys[^1];
	}

	private static double Loop(double t, double period, params double[] keys)
	{
		return Track(t % period / period, keys);
	}

	private static double Once(double t, double dur, params double[] keys)
	{
		return Track(t / dur, keys);
	}

	private static Pose PoseFor(string st, double ts, double t)
	{
		Pose p = new Pose
		{
			Cat = T(),
			Body = T(0.0, 0.0, 0.0, Loop(t, 4.0, 0.0, 1.0, 0.5, 1.01, 1.0, 1.0), Loop(t, 4.0, 0.0, 1.0, 0.5, 1.025, 1.0, 1.0)),
			Head = T(0.0, Loop(t, 4.0, 0.0, 0.0, 0.5, 1.2, 1.0, 0.0)),
			Tail = T(0.0, 0.0, Loop(t, 4.6, 0.0, -4.0, 0.5, 6.0, 1.0, -4.0)),
			EarL = T(),
			EarR = T(),
			PawL = T(),
			PawR = T(),
			Mouth = T(),
			Shadow = T(),
			Paper = T(),
			Sweat = T(),
			Cloud = T(),
			Ring = T(),
			CatOpacity = 1.0,
			ShadowOpacity = 0.22,
			Eye = 1.0,
			LookOverride = (LookAt.TryGetValue(st, out var value) ? new (double, double)?(value) : (((double, double)?)null))
		};
		switch (st)
		{
		case "sleep":
			p.Head = T(0.0, Loop(t, 5.0, 0.0, 12.0, 0.5, 13.5, 1.0, 12.0), 8.0);
			p.Body = T(0.0, 0.0, 0.0, Loop(t, 5.0, 0.0, 1.03, 0.5, 1.045, 1.0, 1.03), Loop(t, 5.0, 0.0, 0.95, 0.5, 0.985, 1.0, 0.95));
			p.Tail = T(0.0, 0.0, sway(9.0));
			Ears(-11.0, 11.0);
			break;
		case "music":
			p.Head = T(0.0, Loop(t, 1.2, 0.0, 0.0, 0.25, 2.0, 0.5, 0.0, 0.75, 2.0, 1.0, 0.0), Loop(t, 1.2, 0.0, -4.0, 0.25, 0.0, 0.5, 4.0, 0.75, 0.0, 1.0, -4.0));
			p.Body = T(0.0, 0.0, 0.0, Loop(t, 0.6, 0.0, 1.0, 0.5, 1.02, 1.0, 1.0), Loop(t, 0.6, 0.0, 1.0, 0.5, 0.98, 1.0, 1.0));
			p.Tail = T(0.0, 0.0, tailBeat(2.4));
			break;
		case "typing":
			p.Head = T(0.0, 3.0);
			p.PawL = T(0.0, Loop(t, 0.55, 0.0, 0.0, 0.25, -6.0, 0.5, 0.0, 1.0, 0.0), Loop(t, 0.55, 0.0, 0.0, 0.25, -4.0, 0.5, 0.0, 1.0, 0.0));
			p.PawR = T(0.0, Loop(t, 0.55, 0.0, 0.0, 0.5, 0.0, 0.75, -6.0, 1.0, 0.0), Loop(t, 0.55, 0.0, 0.0, 0.5, 0.0, 0.75, 4.0, 1.0, 0.0));
			p.Tail = T(0.0, 0.0, twitch(1.4));
			break;
		case "tired":
		case "offline":
		{
			double period = ((st == "tired") ? 4.5 : 5.0);
			p.Head = T(0.0, Loop(t, period, 0.0, 6.0, 0.5, 7.2, 1.0, 6.0), -3.0);
			p.Tail = T(0.0, 0.0, sway((st == "tired") ? 8 : 10));
			Ears(-24.0, 24.0);
			if (st == "tired")
			{
				p.Sweat = T(0.0, Loop(t, 3.0, 0.0, 0.0, 0.7, 6.0, 1.0, 8.0));
				p.SweatOpacity = Loop(t, 3.0, 0.0, 0.0, 0.15, 1.0, 0.7, 0.9, 1.0, 0.0);
			}
			else
			{
				p.Cloud = T(0.0, Loop(t, 4.0, 0.0, 0.0, 0.5, 2.0, 1.0, 0.0));
			}
			break;
		}
		case "happy":
		case "notify":
		case "curious":
		case "bt":
		{
			Hop(1.0, big: false);
			p.Tail = T(0.0, 0.0, (st == "curious") ? twitch(1.6) : tailBeat(1.6));
			if ((st == "notify" || st == "curious") ? true : false)
			{
				Ears(8.0, -8.0);
			}
			double eye;
			if (st == "notify")
			{
				eye = 1.12;
			}
			else
			{
				eye = ((st == "curious") ? 1.15 : 1.0);
			}
			p.Eye = eye;
			if (st == "curious")
			{
				p.Head = T(0.0, Loop(t, 2.6, 0.0, 0.0, 0.5, 1.0, 1.0, 0.0), Loop(t, 2.6, 0.0, 10.0, 0.5, 6.0, 1.0, 10.0));
			}
			break;
		}
		case "startle":
			Hop(0.9, big: true);
			p.Tail = T(0.0, 0.0, -14.0);
			p.WideTail = true;
			Ears(-38.0, 38.0);
			p.Eye = 1.25;
			p.Sweat = T(0.0, Once(ts, 1.8, 0.0, 0.0, 0.7, 6.0, 1.0, 8.0));
			p.SweatOpacity = Once(ts, 1.8, 0.0, 0.0, 0.15, 1.0, 0.7, 0.9, 1.0, 0.0);
			break;
		case "mute":
			p.Head = T(0.0, 4.0);
			p.PawL = T(-4.0, -6.0, -10.0);
			p.PawR = T(4.0, -6.0, 10.0);
			Ears(-38.0, 38.0);
			break;
		case "volume":
		{
			double num4 = ts / 0.9;
			if (num4 < 2.0)
			{
				Ears(Track(num4 % 1.0, 0.0, 0.0, 0.25, -16.0, 0.5, 0.0, 0.7, -7.0, 1.0, 0.0), Track(Math.Max(0.0, ts - 0.1) / 0.9 % 1.0, 0.0, 0.0, 0.25, 16.0, 0.5, 0.0, 0.7, 7.0, 1.0, 0.0));
			}
			break;
		}
		case "download":
			p.Head = T(0.0, 4.0);
			p.PawR = T(0.0, Loop(t, 2.2, 0.0, 0.0, 0.55, 0.0, 0.72, -9.0, 0.88, 1.0, 1.0, 0.0), Loop(t, 2.2, 0.0, 0.0, 0.55, 0.0, 0.72, -8.0, 0.88, 0.0, 1.0, 0.0));
			p.Tail = T(0.0, 0.0, twitch(1.1));
			break;
		case "copy":
		{
			double fraction = Math.Min(1.0, ts / 1.7);
			p.PawR = T(Track(fraction, 0.0, 0.0, 0.32, 8.0, 0.46, 8.0, 0.8, 2.0, 1.0, 2.0), Track(fraction, 0.0, 0.0, 0.32, -36.0, 0.46, -36.0, 0.8, -4.0, 1.0, -4.0), Track(fraction, 0.0, 0.0, 0.32, -30.0, 0.46, -30.0, 0.8, -8.0, 1.0, -8.0));
			p.Paper = T(Track(fraction, 0.0, -4.0, 0.35, 6.0, 0.45, 6.0, 0.75, 0.0, 1.0, 0.0), Track(fraction, 0.0, -150.0, 0.35, -42.0, 0.45, -40.0, 0.75, -6.0, 1.0, -6.0), Track(fraction, 0.0, -40.0, 0.35, -12.0, 0.45, -14.0, 0.75, -6.0, 1.0, -6.0));
			p.PaperOpacity = Track(fraction, 0.0, 0.0, 0.15, 1.0, 1.0, 1.0);
			break;
		}
		case "pounce":
		{
			double num2 = ts / 1.6;
			if (num2 < 1.0)
			{
				p.Cat = T(Track(num2, 0.0, 0.0, 0.28, 0.0, 0.48, 0.0, 0.7, 10.0, 0.88, 3.0, 1.0, 0.0), Track(num2, 0.0, 0.0, 0.28, 4.0, 0.48, 4.0, 0.7, -28.0, 0.88, 0.0, 1.0, 0.0), Track(num2, 0.0, 0.0, 0.28, 0.0, 0.38, -1.0, 0.48, 1.0, 0.7, 5.0, 0.88, 0.0, 1.0, 0.0), Track(num2, 0.0, 1.0, 0.28, 1.06, 0.48, 1.06, 0.7, 0.97, 0.88, 1.03, 1.0, 1.0), Track(num2, 0.0, 1.0, 0.28, 0.9, 0.48, 0.9, 0.7, 1.05, 0.88, 0.97, 1.0, 1.0));
				double num3 = Track(num2, 0.0, 1.0, 0.5, 0.7, 1.0, 1.0);
				p.Shadow = T(0.0, 0.0, 0.0, num3, num3);
			}
			p.Eye = 1.15;
			break;
		}
		case "pet":
			p.Cat = T(0.0, Loop(t, 0.28, 0.0, 0.0, 0.5, -0.5, 1.0, 0.0));
			p.Head = T(0.0, Loop(t, 2.2, 0.0, 0.0, 0.5, 1.0, 1.0, 0.0), Loop(t, 2.2, 0.0, -3.0, 0.5, 3.0, 1.0, -3.0));
			p.Tail = T(0.0, 0.0, tailBeat(1.6));
			Ears(-11.0, 11.0);
			break;
		case "alarm":
			p.Cat = T(Loop(t, 0.8, 0.0, 0.0, 0.25, -2.0, 0.75, 2.0, 1.0, 0.0), 0.0, Loop(t, 0.8, 0.0, 0.0, 0.25, -1.0, 0.75, 1.0, 1.0, 0.0));
			p.Ring = T(0.0, 0.0, Loop(t, 0.4, 0.0, -10.0, 0.5, 10.0, 1.0, -10.0));
			Ears(-16.0, 16.0);
			p.Eye = 1.2;
			break;
		case "think":
			p.Head = T(0.0, Loop(t, 2.8, 0.0, 0.0, 0.5, 1.0, 1.0, 0.0), Loop(t, 2.8, 0.0, -10.0, 0.5, -6.0, 1.0, -10.0));
			p.Tail = T(0.0, 0.0, sway(3.2));
			break;
		case "wake":
		{
			double num = ts / 2.2;
			if (num < 1.0)
			{
				p.Cat = T(0.0, 0.0, 0.0, Track(num, 0.0, 1.0, 0.4, 0.96, 0.65, 0.98, 1.0, 1.0), Track(num, 0.0, 1.0, 0.4, 1.07, 0.65, 1.05, 1.0, 1.0));
				p.Mouth = T(0.0, 0.0, 0.0, Track(num, 0.0, 0.4, 0.35, 1.4, 0.65, 1.4, 1.0, 0.4), Track(num, 0.0, 0.4, 0.35, 1.8, 0.65, 1.8, 1.0, 0.4));
			}
			break;
		}
		case "hide":
			p.Cat = T(0.0, 50.0, 0.0, 0.7, 0.7);
			p.CatOpacity = 0.0;
			p.ShadowOpacity = 0.0;
			break;
		}
		return p;
		void Ears(double l, double r)
		{
			p.EarL = T(0.0, 0.0, l);
			p.EarR = T(0.0, 0.0, r);
		}
		void Hop(double dur, bool big)
		{
			double num5 = ts / dur;
			if (!(num5 >= 1.0))
			{
				p.Cat = (big ? T(0.0, Track(num5, 0.0, 0.0, 0.15, 0.0, 0.4, -30.0, 0.68, 0.0, 0.84, -3.0, 1.0, 0.0), 0.0, Track(num5, 0.0, 1.0, 0.15, 1.06, 0.4, 0.96, 0.68, 1.04, 1.0, 1.0), Track(num5, 0.0, 1.0, 0.15, 0.93, 0.4, 1.05, 0.68, 0.96, 1.0, 1.0)) : T(0.0, Track(num5, 0.0, 0.0, 0.2, 0.0, 0.5, -20.0, 0.78, 0.0, 1.0, 0.0), 0.0, Track(num5, 0.0, 1.0, 0.2, 1.05, 0.5, 0.98, 0.78, 1.03, 1.0, 1.0), Track(num5, 0.0, 1.0, 0.2, 0.95, 0.5, 1.03, 0.78, 0.97, 1.0, 1.0)));
				double num6 = Track(num5, 0.0, 1.0, 0.5, 0.7, 1.0, 1.0);
				p.Shadow = T(0.0, 0.0, 0.0, num6, num6);
				p.ShadowOpacity = Track(num5, 0.0, 0.22, 0.5, 0.13, 1.0, 0.22);
			}
		}
		double sway(double period2)
		{
			return Loop(t, period2, 0.0, -4.0, 0.5, 6.0, 1.0, -4.0);
		}
		double tailBeat(double period2)
		{
			return Loop(t, period2, 0.0, -11.0, 0.5, 10.0, 1.0, -11.0);
		}
		double twitch(double period2)
		{
			return Loop(t, period2, 0.0, -2.0, 0.5, 5.0, 1.0, -2.0);
		}
	}

	private void Effects(double now)
	{
		if (ReducedMotion || (Crop == "body" && State != "music" && State != "sleep" && State != "pet"))
		{
			return;
		}
		double num = State switch
		{
			"music" => 0.65, 
			"sleep" => 1.3, 
			"pet" => 0.34, 
			_ => 0.0, 
		};
		if (num != 0.0 && !(now < _nextFx))
		{
			_nextFx = now + num;
			switch (State)
			{
			case "music":
				Spawn((_random.Next(2) == 0) ? "♪" : "♫", 156.0 + _random.NextDouble() * 30.0, 92.0, 16.0 + _random.NextDouble() * 8.0, 10.0 + _random.NextDouble() * 24.0, NoteColors[_random.Next(4)], 2.8);
				break;
			case "sleep":
				Spawn("z", 150.0, 84.0, 13 + _zCount++ % 3 * 5, 14.0, "#cfd8ff", 3.4, bold: true);
				break;
			case "pet":
				Spawn("♥", 88.0 + _random.NextDouble() * 64.0, 78.0, 14.0 + _random.NextDouble() * 8.0, (_random.NextDouble() - 0.5) * 30.0, "#ff6b8a", 2.2);
				break;
			}
		}
	}

	public void Burst()
	{
		if (!ReducedMotion)
		{
			for (int i = 0; i < 7; i++)
			{
				Spawn("✦", 60.0 + _random.NextDouble() * 120.0, 50.0 + _random.NextDouble() * 90.0, 10.0 + _random.NextDouble() * 12.0, 0.0, "#FFD166", 1.3, bold: false, spark: true, (double)i * 0.07);
			}
		}
	}

	private void Spawn(string glyph, double x, double y, double size, double dx, string color, double seconds, bool bold = false, bool spark = false, double delay = 0.0)
	{
		if (_fx.Children.Count <= 24)
		{
			TextBlock text = new TextBlock
			{
				Text = glyph,
				FontSize = size,
				FontFamily = new FontFamily("Segoe UI Symbol"),
				FontWeight = (bold ? FontWeights.ExtraBold : FontWeights.Normal),
				Foreground = MiniSvg.Color(color),
				RenderTransformOrigin = new Point(0.5, 0.5),
				Opacity = 0.0,
				IsHitTestVisible = false
			};
			Canvas.SetLeft(text, x - size * 0.3);
			Canvas.SetTop(text, y - size);
			ScaleTransform scaleTransform = new ScaleTransform(0.6, 0.6);
			TranslateTransform translateTransform = new TranslateTransform();
			RotateTransform rotateTransform = new RotateTransform();
			text.RenderTransform = new TransformGroup
			{
				Children = 
				{
					(Transform)scaleTransform,
					(Transform)rotateTransform,
					(Transform)translateTransform
				}
			};
			_fx.Children.Add(text);
			TimeSpan value = TimeSpan.FromSeconds(delay);
			TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
			CubicEase easingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
			{
				BeginTime = value,
				Duration = timeSpan
			};
			if (spark)
			{
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.35)));
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
				DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames2 = new DoubleAnimationUsingKeyFrames
				{
					BeginTime = value,
					Duration = timeSpan
				};
				doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
				doubleAnimationUsingKeyFrames2.KeyFrames.Add(new EasingDoubleKeyFrame(1.1, KeyTime.FromPercent(0.35), easingFunction));
				doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
				scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, doubleAnimationUsingKeyFrames2);
				scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, doubleAnimationUsingKeyFrames2);
				rotateTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0.0, 80.0, timeSpan)
				{
					BeginTime = value
				});
			}
			else
			{
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.2)));
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.8, KeyTime.FromPercent(0.7)));
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
				translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, dx, timeSpan)
				{
					BeginTime = value,
					EasingFunction = easingFunction
				});
				translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0.0, -70.0, timeSpan)
				{
					BeginTime = value,
					EasingFunction = easingFunction
				});
				scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1.1, timeSpan)
				{
					BeginTime = value,
					EasingFunction = easingFunction
				});
				scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, 1.1, timeSpan)
				{
					BeginTime = value,
					EasingFunction = easingFunction
				});
			}
			doubleAnimationUsingKeyFrames.Completed += (object? _, EventArgs _) =>
			{
				_fx.Children.Remove(text);
			};
			text.BeginAnimation(UIElement.OpacityProperty, doubleAnimationUsingKeyFrames);
		}
	}
}
