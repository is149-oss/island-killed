using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Island.Services;

public static class AppFont
{
	public const string Fallback = "Segoe UI Variable Text, Segoe UI";

	public static readonly string UserDir = Path.Combine(Settings.Folder, "Fonts");

	public static FontFamily Current { get; private set; } = new FontFamily("Segoe UI Variable Text, Segoe UI");

	public static List<(string Id, string Name)> Choices()
	{
		List<(string, string)> list = new List<(string, string)>
		{
			("aminah", "Aminahs Schoolbook Sans Serif Bd"),
			("schueler", "Schueler SuperHyperXP Plus"),
			("segoe", "Segoe UI"),
			("arial", "Arial"),
			("calibri", "Calibri")
		};
		foreach (string item in UserFamilies())
		{
			list.Add(("file:" + item, item));
		}
		return list;
	}

	private static IEnumerable<string> UserFamilies()
	{
		List<string> list = new List<string>();
		try
		{
			if (!Directory.Exists(UserDir))
			{
				return list;
			}
			foreach (string item in from p in Directory.EnumerateFiles(UserDir)
				where p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
				select p)
			{
				string? text = new GlyphTypeface(new Uri(item)).FamilyNames.Values.FirstOrDefault();
				if (!string.IsNullOrWhiteSpace(text) && !list.Contains(text))
				{
					list.Add(text);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Write("font search failed: " + ex.Message);
		}
		return list;
	}

	public static FontFamily Build(string id)
	{
		if (id.StartsWith("file:"))
		{
			string text = id.Substring(5);
			if (UserFamilies().Contains(text))
			{
				return new FontFamily(new Uri(UserDir + Path.DirectorySeparatorChar), "./#" + text + ", Segoe UI Variable Text, Segoe UI");
			}
			id = "aminah";
		}
		return id switch
		{
			"segoe" => new FontFamily("Segoe UI Variable Text, Segoe UI"), 
			"arial" => new FontFamily("Arial, Segoe UI"), 
			"calibri" => new FontFamily("Calibri, Segoe UI"), 
			"schueler" => Embedded("Schueler SuperHyperXP Plus"), 
			_ => Embedded("Aminahs Schoolbook Sans Serif Bd"), 
		};
	}

	private static FontFamily Embedded(string family)
	{
		return new FontFamily(new Uri("pack://application:,,,/Assets/Fonts/"), "./#" + family + ", Segoe UI Variable Text, Segoe UI");
	}

	public static void Apply()
	{
		Current = Build(Settings.Current.Font);
		if (Application.Current != null)
		{
			Application.Current.Resources["T.Font"] = Current;
		}
	}
}
