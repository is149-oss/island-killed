using System;

namespace Island.Services;

public sealed record ReleaseInfo(Version Version, string Title, string PageUrl, string? ZipUrl, long ZipSize);
