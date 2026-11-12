using System;
using System.IO;

namespace Island.Services;

public sealed class DownloadItem
{
	internal long LastSize;

	internal DateTime LastCheck = DateTime.Now;

	internal bool Plain;

	internal DateTime LastGrowth = DateTime.Now;

	public string TempPath { get; set; } = "";

	public long Size { get; set; }

	public double Speed { get; set; }

	public string Name
	{
		get
		{
			string text = Path.GetFileName(TempPath);
			string[] tempExtensions = DownloadsService.TempExtensions;
			foreach (string text2 in tempExtensions)
			{
				if (text.EndsWith(text2, StringComparison.OrdinalIgnoreCase))
				{
					string text3 = text;
					int length = text2.Length;
					text = text3.Substring(0, text3.Length - length);
				}
			}
			if (!text.StartsWith("Unconfirmed", StringComparison.OrdinalIgnoreCase))
			{
				return text;
			}
			return "Новая загрузка";
		}
	}
}
