using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Island.Native;

namespace Island.Services;

public static class Log
{
	private static readonly object Gate = new object();

	private static readonly string Dir = Settings.Folder;

	public static readonly string FilePath = Path.Combine(Dir, "island.log");

	private static readonly string OldPath = Path.Combine(Dir, "island.old.log");

	private const long MaxBytes = 2000000L;

	public static void Write(string message)
	{
		Append("INFO ", message);
	}

	public static void Warn(string message)
	{
		Append("WARN ", message);
	}

	public static void Error(string where, Exception ex)
	{
		Append("ERROR", $"{where}: {ex}");
	}

	public static void Caught(string where, Exception ex)
	{
		Append("WARN ", $"{where}: {ex.GetType().Name}: {ex.Message}");
	}

	private static void Append(string level, string message)
	{
		try
		{
			lock (Gate)
			{
				Directory.CreateDirectory(Dir);
				if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2000000)
				{
					File.Move(FilePath, OldPath, overwrite: true);
				}
				File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}:{Environment.CurrentManagedThreadId}] {level} {Clean(message)}{Environment.NewLine}");
			}
		}
		catch (Exception)
		{
		}
	}

	private static string Clean(string text)
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (folderPath.Length <= 3)
		{
			return text;
		}
		return text.Replace(folderPath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
	}

	public static void Session()
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("==================== Island session ====================");
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(13, 2, stringBuilder2);
			handler.AppendLiteral("version      ");
			handler.AppendFormatted(UpdateService.Current);
			handler.AppendFormatted(Environment.GetCommandLineArgs().Contains("--updated") ? " (just updated)" : "");
			stringBuilder3.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
			handler.AppendLiteral("exe          ");
			handler.AppendFormatted(Environment.ProcessPath);
			stringBuilder4.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(26, 3, stringBuilder2);
			handler.AppendLiteral("os           ");
			handler.AppendFormatted(RuntimeInformation.OSDescription);
			handler.AppendLiteral(" (");
			handler.AppendFormatted(RuntimeInformation.OSArchitecture);
			handler.AppendLiteral("), process ");
			handler.AppendFormatted(RuntimeInformation.ProcessArchitecture);
			stringBuilder5.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
			handler.AppendLiteral(".net         ");
			handler.AppendFormatted(RuntimeInformation.FrameworkDescription);
			stringBuilder6.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(19, 2, stringBuilder2);
			handler.AppendLiteral("culture      ");
			handler.AppendFormatted(CultureInfo.CurrentCulture.Name);
			handler.AppendLiteral(" / UI ");
			handler.AppendFormatted(CultureInfo.CurrentUICulture.Name);
			stringBuilder7.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(36, 2, stringBuilder2);
			handler.AppendLiteral("memory       ");
			handler.AppendFormatted(Environment.WorkingSet / 1048576);
			handler.AppendLiteral(" MB working set, ");
			handler.AppendFormatted(Environment.ProcessorCount);
			handler.AppendLiteral(" cores");
			stringBuilder8.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder9 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
			handler.AppendLiteral("windows up   ");
			handler.AppendFormatted(TimeSpan.FromMilliseconds(Environment.TickCount64), "d\\.hh\\:mm");
			stringBuilder9.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder10 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
			handler.AppendLiteral("elevated     ");
			handler.AppendFormatted(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator));
			stringBuilder10.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder11 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
			handler.AppendLiteral("screens      ");
			handler.AppendFormatted(string.Join("; ", Screen.AllScreens.Select((Screen s) => $"{s.Bounds.Width}x{s.Bounds.Height}@{s.Bounds.X},{s.Bounds.Y}{(s.Primary ? " primary" : "")}")));
			stringBuilder11.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder12 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
			handler.AppendLiteral("refresh      ");
			handler.AppendFormatted(Win32.GetRefreshRate());
			handler.AppendLiteral(" Hz");
			stringBuilder12.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder13 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(34, 3, stringBuilder2);
			handler.AppendLiteral("pro          key=");
			handler.AppendFormatted(License.HasKey);
			handler.AppendLiteral(", trial=");
			handler.AppendFormatted(License.IsTrial);
			handler.AppendLiteral(", active=");
			handler.AppendFormatted(License.IsPro);
			stringBuilder13.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder14 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
			handler.AppendLiteral("autostart    ");
			handler.AppendFormatted(Autostart.IsEnabled);
			stringBuilder14.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder15 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
			handler.AppendLiteral("settings     ");
			handler.AppendFormatted(Settings.Current.SafeJson(indented: false));
			stringBuilder15.AppendLine(ref handler);
			stringBuilder.Append("========================================================");
			Append("INFO ", stringBuilder.ToString());
			Settings.Current.MarkLogged();
		}
		catch (Exception ex)
		{
			Append("WARN ", "session header: " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	public static string? CreateReport()
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"Island-log-{DateTime.Now:yyyyMMdd-HHmm}.zip");
			lock (Gate)
			{
				using ZipArchive zipArchive = ZipFile.Open(text, ZipArchiveMode.Create);
				string[] array = new string[2] { FilePath, OldPath };
				foreach (string path in array)
				{
					if (!File.Exists(path))
					{
						continue;
					}
					ZipArchiveEntry zipArchiveEntry = zipArchive.CreateEntry(Path.GetFileName(path), CompressionLevel.Optimal);
					using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
					using Stream destination = zipArchiveEntry.Open();
					fileStream.CopyTo(destination);
				}
				using StreamWriter streamWriter = new StreamWriter(zipArchive.CreateEntry("settings.json", CompressionLevel.Optimal).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				streamWriter.Write(Settings.Current.SafeJson(indented: true));
			}
			Write("report created: " + Path.GetFileName(text));
			return text;
		}
		catch (Exception ex)
		{
			Caught("report", ex);
			return null;
		}
	}
}
