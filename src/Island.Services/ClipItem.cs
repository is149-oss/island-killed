using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island.Services;

public sealed class ClipItem
{
	public ClipKind Kind { get; private init; }

	public string? Text { get; private init; }

	public string[]? Files { get; private init; }

	public BitmapSource? Image { get; private init; }

	public BitmapSource? Thumb { get; private init; }

	public DateTime Time { get; set; } = DateTime.Now;

	public string Glyph => Kind switch
	{
		ClipKind.Files => "\ue8b7", 
		ClipKind.Image => "\ue8b9", 
		_ => "\ue8c8", 
	};

	public string Preview => Kind switch
	{
		ClipKind.Image when Image is { } image => $"Изображение {image.PixelWidth}×{image.PixelHeight}",
		ClipKind.Files when Files is { Length: > 0 } files => (files.Length == 1) ? Path.GetFileName(files[0]) : $"{Path.GetFileName(files[0])} и ещё {files.Length - 1}",
		_ => Regex.Replace(Text is { Length: > 300 } text ? text.Substring(0, 300) : Text ?? "", "\\s+", " ").Trim(),
	};

	public Visibility ThumbVisibility
	{
		get
		{
			if (Thumb == null)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	public string Age
	{
		get
		{
			TimeSpan timeSpan = DateTime.Now - Time;
			if (timeSpan.TotalMinutes < 1.0)
			{
				return "сейчас";
			}
			if (timeSpan.TotalHours < 1.0)
			{
				return $"{(int)timeSpan.TotalMinutes} мин";
			}
			if (timeSpan.TotalDays < 1.0)
			{
				return $"{(int)timeSpan.TotalHours} ч";
			}
			return Time.ToString("d MMM");
		}
	}

	public bool SameContent(ClipItem other)
	{
		bool flag = Kind == other.Kind;
		if (flag)
		{
			flag = Kind switch
			{
				ClipKind.Text => Text == other.Text, 
				ClipKind.Files => Files is not null && other.Files is not null && Files.SequenceEqual(other.Files),
				_ => false, 
			};
		}
		return flag;
	}

	public static ClipItem FromText(string text)
	{
		return new ClipItem
		{
			Kind = ClipKind.Text,
			Text = text
		};
	}

	public static ClipItem FromFiles(string[] files)
	{
		return new ClipItem
		{
			Kind = ClipKind.Files,
			Files = files
		};
	}

	public static ClipItem FromSaved(ClipKind kind, string? text, string[]? files, DateTime time)
	{
		return new ClipItem
		{
			Kind = kind,
			Text = text,
			Files = files,
			Time = time
		};
	}

	public static ClipItem FromImage(BitmapSource source)
	{
		WriteableBitmap writeableBitmap = new WriteableBitmap(source);
		writeableBitmap.Freeze();
		double num = 56.0 / (double)Math.Max(writeableBitmap.PixelWidth, writeableBitmap.PixelHeight);
		TransformedBitmap transformedBitmap = new TransformedBitmap(writeableBitmap, new ScaleTransform(num, num));
		transformedBitmap.Freeze();
		return new ClipItem
		{
			Kind = ClipKind.Image,
			Image = writeableBitmap,
			Thumb = transformedBitmap
		};
	}
}
