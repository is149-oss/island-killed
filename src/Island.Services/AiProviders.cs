using System.Linq;

namespace Island.Services;

public static class AiProviders
{
	public static readonly AiProvider OpenRouter = new AiProvider("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "https://openrouter.ai/settings/keys", "sk-or-");

	public static readonly AiProvider Groq = new AiProvider("groq", "Groq", "https://api.groq.com/openai/v1", "https://console.groq.com/keys", "gsk_");

	public static readonly AiProvider Gemini = new AiProvider("gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "https://aistudio.google.com/apikey", "AIza");

	public static readonly AiProvider DeepSeek = new AiProvider("deepseek", "DeepSeek", "https://api.deepseek.com", "https://platform.deepseek.com/api_keys", "sk-", Free: false, Anthropic: false, new string[1] { "deepseek-chat" });

	public static readonly AiProvider Claude = new AiProvider("anthropic", "Claude", "https://api.anthropic.com/v1", "https://console.anthropic.com/settings/keys", "sk-ant-", Free: false, Anthropic: true, new string[3] { "sonnet", "opus", "haiku" });

	public static readonly AiProvider OpenAi = new AiProvider("openai", "ChatGPT (OpenAI)", "https://api.openai.com/v1", "https://platform.openai.com/api-keys", "sk-", Free: false, Anthropic: false, new string[3] { "gpt-5", "gpt-4.1", "gpt-4o" });

	public static readonly AiProvider Custom = new AiProvider("custom", "свой сервер", "", "", "", Free: false);

	public static readonly AiProvider[] All = new AiProvider[7] { OpenRouter, Groq, Gemini, DeepSeek, Claude, OpenAi, Custom };

	public static AiProvider Get(string? id)
	{
		AiProvider aiProvider = All.FirstOrDefault((AiProvider p) => p.Id == id) ?? OpenRouter;
		if (!(aiProvider == Custom))
		{
			return aiProvider;
		}
		return Custom with
		{
			BaseUrl = (Settings.Current.AiCustomUrl ?? "").Trim().TrimEnd('/')
		};
	}

	public static string? Key(AiProvider p)
	{
		string? text = AiKeyStore.Load(p);
		if (text == null)
		{
			if (!(p.Id == Custom.Id) || p.BaseUrl.Length <= 0)
			{
				return null;
			}
			text = "";
		}
		return text;
	}
}
