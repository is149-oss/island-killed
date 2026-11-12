using System;

namespace Island.Services.Phone;

public sealed record PhoneFile(PhoneDevice Device, string Name, string Path, string Mime, long Size, bool Clipboard)
{
	public bool IsImage => Mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
