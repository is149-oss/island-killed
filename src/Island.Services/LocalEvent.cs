using System;

namespace Island.Services;

public sealed class LocalEvent
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Title { get; set; } = "";

	public DateTime Start { get; set; }

	public int DurationMinutes { get; set; } = 60;

	public bool AllDay { get; set; }

	public string? Link { get; set; }

	public bool Birthday { get; set; }
}
