// Optional endpoint configuration for compatible weather and lyrics providers.
using System;

namespace Island.Services;

public static class DeveloperEndpoints
{
    public static bool CustomForecast => Configured("ISLAND_WEATHER_FORECAST_URL");
    public static bool CustomGeocoding => Configured("ISLAND_WEATHER_GEOCODING_URL");
    public static bool CustomLyrics => Configured("ISLAND_LYRICS_LRCLIB_URL");
    public static string LyricsUrl(string original)
    {
        if (!CustomLyrics || !original.StartsWith("https://lrclib.net/", StringComparison.Ordinal)) return original;
        return WeatherUrl("ISLAND_LYRICS_LRCLIB_URL", "https://lrclib.net").TrimEnd('/')
            + new Uri(original).PathAndQuery;
    }
    private static bool Configured(string name) => DeveloperAccess.Enabled
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));

    public static string WeatherUrl(string name, string original)
    {
        if (!Configured(name)) return original;
        string value = Environment.GetEnvironmentVariable(name)!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !(uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback))
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Endpoint must be HTTPS (or loopback HTTP), without credentials, query or fragment.");
        return uri.AbsoluteUri;
    }
}
