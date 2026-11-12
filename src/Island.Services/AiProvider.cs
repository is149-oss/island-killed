namespace Island.Services;

public sealed record AiProvider(string Id, string Name, string BaseUrl, string KeysUrl, string KeyPrefix, bool Free = true, bool Anthropic = false, string[]? Preferred = null);
