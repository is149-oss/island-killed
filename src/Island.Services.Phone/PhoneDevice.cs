using System;

namespace Island.Services.Phone;

public sealed class PhoneDevice
{
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";

	public string? Model { get; set; }

	public string? Os { get; set; }

	public DateTime PairedAt { get; set; }

	public DateTime? LastSeen { get; set; }

	public string Key { get; set; } = "";
}
