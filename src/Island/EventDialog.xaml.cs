using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Island.Native;
using Island.Services;

namespace Island;

public partial class EventDialog : Window, IComponentConnector
{
	private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

	private static readonly string[] QuickTimes = new string[7] { "09:00", "10:00", "12:00", "14:00", "16:00", "18:00", "20:00" };

	private DateTime _selected = DateTime.Today;

	private DateTime _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

	public LocalEvent? Result { get; private set; }

	public EventDialog(DateTime? date = null)
	{
		InitializeComponent();
		SourceInitialized += (object? _, EventArgs _) =>
		{
			Win32.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
		};
		if (date.HasValue)
		{
			DateTime valueOrDefault = date.GetValueOrDefault();
			_selected = valueOrDefault.Date;
			_month = new DateTime(valueOrDefault.Year, valueOrDefault.Month, 1);
		}
		string[] array = new string[7] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
		foreach (string text in array)
		{
			WeekdayRow.Children.Add(new TextBlock
			{
				Text = text,
				FontSize = 11.0,
				Foreground = (Brush)FindResource("T.Text3"),
				HorizontalAlignment = HorizontalAlignment.Center
			});
		}
		array = QuickTimes;
		foreach (string time in array)
		{
			RadioButton radioButton = new RadioButton
			{
				Style = (Style)FindResource("T.Chip"),
				Content = time,
				GroupName = "time"
			};
			radioButton.Checked += (object _, RoutedEventArgs _) =>
			{
				TimeBox.Text = time;
			};
			TimeChips.Children.Add(radioButton);
		}
		UpdateDuration();
		RenderMonth();
		Loaded += (object _, RoutedEventArgs _) =>
		{
			TitleBox.Focus();
		};
	}

	private void RenderMonth()
	{
		MonthTitle.Text = Capitalize(Ru.DateTimeFormat.GetMonthName(_month.Month)) + " " + _month.Year;
		DaysGrid.Children.Clear();
		DateTime dateTime = _month.AddDays(-((int)(_month.DayOfWeek + 6) % 7));
		Brush brush = (Brush)FindResource("T.Accent");
		for (int i = 0; i < 42; i++)
		{
			DateTime dateTime2 = dateTime.AddDays(i);
			bool flag = dateTime2.Month == _month.Month;
			bool flag2 = dateTime2 == _selected;
			bool flag3 = dateTime2 == DateTime.Today;
			Button button = new Button
			{
				Style = (Style)FindResource("DayCell"),
				Content = dateTime2.Day.ToString(CultureInfo.InvariantCulture),
				Background = (flag2 ? brush : Brushes.Transparent),
				BorderBrush = ((flag3 && !flag2) ? brush : Brushes.Transparent),
				Foreground = (flag2 ? Brushes.Black : (flag ? ((Brush)FindResource("T.Text")) : ((Brush)FindResource("T.Text3")))),
				FontWeight = ((flag2 | flag3) ? FontWeights.SemiBold : FontWeights.Normal)
			};
			DateTime d = dateTime2;
			button.Click += (object _, RoutedEventArgs _) =>
			{
				_selected = d;
				if (d.Month != _month.Month)
				{
					_month = new DateTime(d.Year, d.Month, 1);
				}
				RenderMonth();
			};
			DaysGrid.Children.Add(button);
		}
	}

	private void PrevMonth_Click(object sender, RoutedEventArgs e)
	{
		_month = _month.AddMonths(-1);
		RenderMonth();
	}

	private void NextMonth_Click(object sender, RoutedEventArgs e)
	{
		_month = _month.AddMonths(1);
		RenderMonth();
	}

	private void Today_Click(object sender, RoutedEventArgs e)
	{
		_selected = DateTime.Today;
		_month = new DateTime(_selected.Year, _selected.Month, 1);
		RenderMonth();
	}

	private static string Capitalize(string s)
	{
		if (s.Length != 0)
		{
			return char.ToUpper(s[0], Ru) + s.Substring(1);
		}
		return s;
	}

	private void Kind_Checked(object sender, RoutedEventArgs e)
	{
		if (TimePanel != null)
		{
			TimePanel.Visibility = ((KindTimed.IsChecked != true) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private void TimeBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (TimeChips == null)
		{
			return;
		}
		foreach (RadioButton child in TimeChips.Children)
		{
			child.IsChecked = (string)child.Content == TimeBox.Text.Trim();
		}
	}

	private void DurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		UpdateDuration();
	}

	private void UpdateDuration()
	{
		if (DurationText != null)
		{
			int num = (int)(Math.Round(DurationSlider.Value / 15.0) * 15.0);
			TextBlock durationText = DurationText;
			string text;
			if (num < 60)
			{
				text = $"{num} мин";
			}
			else
			{
				text = ((num % 60 == 0) ? $"{num / 60} ч" : $"{num / 60} ч {num % 60} мин");
			}
			durationText.Text = text;
		}
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		string text = TitleBox.Text.Trim();
		if (text.Length == 0)
		{
			ShowError("Введите название");
			TitleBox.Focus();
			return;
		}
		bool valueOrDefault = KindAllDay.IsChecked == true;
		DateTime date = _selected.Date;
		int durationMinutes = (int)(Math.Round(DurationSlider.Value / 15.0) * 15.0);
		if (!valueOrDefault)
		{
			if (!TimeSpan.TryParseExact(TimeBox.Text.Trim(), new string[2] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out var result) || result >= TimeSpan.FromDays(1))
			{
				ShowError("Время в формате ЧЧ:ММ, например 14:30");
				TimeBox.Focus();
				return;
			}
			date += result;
		}
		string text2 = LinkBox.Text.Trim();
		if (text2.Length > 0 && !text2.Contains("://"))
		{
			text2 = "https://" + text2;
		}
		Result = new LocalEvent
		{
			Title = text,
			Start = date,
			DurationMinutes = durationMinutes,
			AllDay = valueOrDefault,
			Link = ((text2.Length > 0) ? text2 : null)
		};
		DialogResult = true;
	}

	private void ShowError(string text)
	{
		ErrorText.Text = text;
		ErrorText.Visibility = Visibility.Visible;
	}
}
