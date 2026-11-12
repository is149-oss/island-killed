using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island.Services;

public static class LinkInfo
{
	private static readonly string Dir = Path.Combine(Settings.Folder, "links");

	private static readonly HttpClient Http = CreateClient();

	private static ImageSource? _placeholder;

	public static ImageSource Placeholder => _placeholder ?? (_placeholder = Plate(null));

	private static HttpClient CreateClient()
	{
		HttpClient httpClient = ReviewSafety.Client(NetProxy.Handler());
		httpClient.Timeout = TimeSpan.FromSeconds(10L);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36 Island/1.4");
		httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en;q=0.8");
		return httpClient;
	}

	public static bool IsLink(string path)
	{
		if (!path.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
		{
			return path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	public static bool TryNormalize(string? text, bool allowBare, out string url)
	{
		url = "";
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = text.Trim().Split(new char[4] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
		if (!IsLink(text2))
		{
			if (!allowBare || !Regex.IsMatch(text2, "^[\\p{L}\\p{N}][\\p{L}\\p{N}\\-]*(\\.[\\p{L}\\p{N}\\-]+)+(/\\S*)?$"))
			{
				return false;
			}
			text2 = "https://" + text2;
		}
		if (!Uri.TryCreate(text2, UriKind.Absolute, out Uri? result) || result.Host.Length == 0 || !result.Host.Contains('.'))
		{
			return false;
		}
		url = result.AbsoluteUri;
		return true;
	}

	public static string Host(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? result))
		{
			return url;
		}
		string idnHost = result.IdnHost;
		if (!idnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
		{
			return idnHost;
		}
		return idnHost.Substring(4);
	}

	private static string Hash(string text)
	{
		return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant()))).Substring(0, 16);
	}

	private static string IconFile(string url)
	{
		return Path.Combine(Dir, Hash(Host(url)) + ".png");
	}

	private static string TitleFile(string url)
	{
		return Path.Combine(Dir, Hash(url) + ".txt");
	}

	public static ImageSource? CachedIcon(string url)
	{
		try
		{
			string text = IconFile(url);
			if (!File.Exists(text))
			{
				return null;
			}
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.UriSource = new Uri(text);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			return bitmapImage;
		}
		catch (Exception ex)
		{
			Log.Error("link icon cache", ex);
			return null;
		}
	}

	public static string? CachedTitle(string url)
	{
		try
		{
			string path = TitleFile(url);
			return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
		}
		catch (IOException)
		{
			return null;
		}
	}

	private static ImageSource Plate(BitmapSource? logo)
	{
		DrawingVisual drawingVisual = new DrawingVisual();
		RenderOptions.SetBitmapScalingMode(drawingVisual, BitmapScalingMode.HighQuality);
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			SolidColorBrush brush = ((logo != null) ? new SolidColorBrush(Color.FromRgb(243, 244, 247)) : new SolidColorBrush(Color.FromArgb(48, byte.MaxValue, byte.MaxValue, byte.MaxValue)));
			drawingContext.DrawRoundedRectangle(brush, null, new Rect(0.0, 0.0, 64.0, 64.0), 16.0, 16.0);
			if (logo != null)
			{
				double num = Math.Min(40.0 / (double)logo.PixelWidth, 40.0 / (double)logo.PixelHeight);
				double num2 = (double)logo.PixelWidth * num;
				double num3 = (double)logo.PixelHeight * num;
				drawingContext.DrawImage(logo, new Rect((64.0 - num2) / 2.0, (64.0 - num3) / 2.0, num2, num3));
			}
			else
			{
				FormattedText formattedText = new FormattedText("\ue774", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe MDL2 Assets"), 30.0, Brushes.White, 1.0);
				drawingContext.DrawText(formattedText, new Point((64.0 - formattedText.Width) / 2.0, (64.0 - formattedText.Height) / 2.0));
			}
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(64, 64, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		renderTargetBitmap.Freeze();
		return renderTargetBitmap;
	}

	public static async Task<(string? Title, ImageSource? Icon)> FetchAsync(string url)
	{
		string? title = null;
		List<(Uri Uri, int Rank)> candidates = new List<(Uri, int)>();
		Uri baseUri = new Uri(url);
		try
		{
			using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
			baseUri = response.RequestMessage?.RequestUri ?? baseUri;
			if (Host(baseUri.AbsoluteUri) == Host(url) && response.IsSuccessStatusCode && (response.Content.Headers.ContentType?.MediaType ?? "").Contains("html", StringComparison.OrdinalIgnoreCase))
			{
				string html = await ReadHead(response);
				title = ParseTitle(html);
				candidates.AddRange(ParseIcons(html, baseUri));
			}
		}
		catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException || ex is IOException) ? true : false)
		{
			Log.Error("link page " + Host(url), ex);
		}
		candidates.Add((new Uri(new Uri(url), "/favicon.ico"), 0));
		ImageSource? icon = null;
		foreach (var item2 in candidates.OrderByDescending(((Uri Uri, int Rank) c) => c.Rank).DistinctBy(((Uri Uri, int Rank) c) => c.Uri).Take(4))
		{
			Uri item = item2.Uri;
			BitmapSource? logo = await TryLogo(item);
			if (logo != null)
			{
				icon = await Application.Current.Dispatcher.InvokeAsync(() => Plate(logo));
				break;
			}
		}
		try
		{
			Directory.CreateDirectory(Dir);
			if (!string.IsNullOrEmpty(title))
			{
				File.WriteAllText(TitleFile(url), title);
			}
			if (icon is BitmapSource source)
			{
				PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
				pngBitmapEncoder.Frames.Add(BitmapFrame.Create(source));
				using FileStream stream = File.Create(IconFile(url));
				pngBitmapEncoder.Save(stream);
			}
		}
		catch (Exception ex2)
		{
			Log.Error("link cache", ex2);
		}
		return (Title: title, Icon: icon);
	}

	private static async Task<string> ReadHead(HttpResponseMessage response)
	{
		using Stream stream = await response.Content.ReadAsStreamAsync();
		byte[] buffer = new byte[200000];
		int total = 0;
		int read = default;
		while (true)
		{
			bool flag = total < buffer.Length;
			if (flag)
			{
				int num;
				read = (num = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total)));
				flag = num > 0;
			}
			if (!flag)
			{
				break;
			}
			total += read;
		}
		string? text = response.Content.Headers.ContentType?.CharSet;
		Encoding encoding = Encoding.UTF8;
		try
		{
			if (!string.IsNullOrEmpty(text))
			{
				encoding = Encoding.GetEncoding(text.Trim('"'));
			}
		}
		catch (ArgumentException)
		{
		}
		return encoding.GetString(buffer, 0, total);
	}

	private static string? ParseTitle(string html)
	{
		Match match = Regex.Match(html, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
		if (!match.Success)
		{
			return null;
		}
		string text = Regex.Replace(WebUtility.HtmlDecode(match.Groups[1].Value), "\\s+", " ").Trim();
		if (text.Length != 0)
		{
			if (text.Length <= 80)
			{
				return text;
			}
			return text.Substring(0, 80);
		}
		return null;
	}

	private static IEnumerable<(Uri, int)> ParseIcons(string html, Uri baseUri)
	{
		foreach (Match item2 in Regex.Matches(html, "<link\\s[^>]*>", RegexOptions.IgnoreCase))
		{
			Match tag = item2;
			string text = Attr("rel").ToLowerInvariant();
			if (text.Contains("icon"))
			{
				string text2 = Attr("href");
				if (text2.Length != 0 && !text2.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(baseUri, text2, out Uri? result) && !text2.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && !Attr("type").Contains("svg", StringComparison.OrdinalIgnoreCase))
				{
					Match match = Regex.Match(Attr("sizes"), "(\\d+)x\\d+");
					int val = ((match != null && match.Success) ? int.Parse(match.Groups[1].Value) : 0);
					int item = (text.Contains("apple-touch") ? 1000 : 100) + Math.Min(val, 180);
					yield return (result, item);
				}
			}
			string Attr(string name)
			{
				Match match2 = Regex.Match(tag.Value, name + "\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.IgnoreCase);
				if (!match2.Success)
				{
					return "";
				}
				string value;
				if (match2.Groups[1].Success)
				{
					value = match2.Groups[1].Value;
				}
				else
				{
					value = (match2.Groups[2].Success ? match2.Groups[2].Value : match2.Groups[3].Value);
				}
				return WebUtility.HtmlDecode(value);
			}
		}
	}

	private static async Task<BitmapSource?> TryLogo(Uri uri)
	{
		_ = 1;
		try
		{
			using HttpResponseMessage response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
			if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 2000000)
			{
				return null;
			}
			byte[] array = await response.Content.ReadAsByteArrayAsync();
			if (array.Length < 100 || array[0] == 60)
			{
				return null;
			}
			using MemoryStream bitmapStream = new MemoryStream(array);
			BitmapFrame bitmapFrame = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.OrderByDescending((BitmapFrame f) => f.PixelWidth * f.PixelHeight).First();
			if (bitmapFrame.PixelWidth < 16 || bitmapFrame.PixelHeight < 16)
			{
				return null;
			}
			bitmapFrame.Freeze();
			return bitmapFrame;
		}
		catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException || ex is IOException || ex is NotSupportedException || ex is FileFormatException || ex is InvalidOperationException || ex is ArgumentException) ? true : false)
		{
			return null;
		}
	}
}
