using System;

namespace Island.Services;

public sealed class TrackEntry
{
	public string Title { get; set; } = "";

	public string Artist { get; set; } = "";

	public string App { get; set; } = "";

	public DateTime At { get; set; }
}
