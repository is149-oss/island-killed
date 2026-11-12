using System;
using System.IO;

namespace Island.Services.Phone;

public sealed class IncomingFile
{
	public string Id { get; init; } = "";

	public DateTime LastReport { get; set; }

	public string Name { get; init; } = "";

	public string Mime { get; init; } = "";

	public long Size { get; init; }

	public bool Clipboard { get; init; }

	public string TempPath { get; init; } = "";

	public FileStream? Stream { get; set; }

	public long Received { get; set; }

	public void Discard()
	{
		try
		{
			Stream?.Dispose();
			File.Delete(TempPath);
		}
		catch (Exception)
		{
		}
	}
}
