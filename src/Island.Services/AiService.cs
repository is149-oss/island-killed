using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Island.Services;

public static class AiService
{
	private static readonly HttpClient Http = CreateClient();

	private static HttpClient CreateClient()
	{
		HttpClient httpClient = ReviewSafety.Client(new SocketsHttpHandler
		{
			PooledConnectionLifetime = TimeSpan.FromMinutes(1L)
		});
		httpClient.Timeout = Timeout.InfiniteTimeSpan;
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Island/1.1");
		httpClient.DefaultRequestHeaders.Add("X-Title", "Island");
		return httpClient;
	}

	private static void Authorize(HttpRequestMessage request, AiProvider provider, string key)
	{
		if (provider.Anthropic)
		{
			request.Headers.Add("x-api-key", key);
			request.Headers.Add("anthropic-version", "2023-06-01");
		}
		else if (key.Length > 0)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
		}
	}

	public static async Task<List<string>> ListModelsAsync(AiProvider provider, string key, CancellationToken ct = default(CancellationToken))
	{
		if (provider.BaseUrl.Length == 0)
		{
			throw new AiException("Впишите адрес сервера в настройках, раздел «ИИ-чат»");
		}
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, provider.BaseUrl + (provider.Anthropic ? "/models?limit=100" : "/models"));
		Authorize(request, provider, key);
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(TimeSpan.FromSeconds(20L));
		using HttpResponseMessage response = await Http.SendAsync(request, cts.Token);
		string text = await response.Content.ReadAsStringAsync(cts.Token);
		if (!response.IsSuccessStatusCode)
		{
			throw new AiException(ErrorText(response.StatusCode, text, provider));
		}
		List<string> list = new List<string>();
		foreach (JsonNode? item in JsonNode.Parse(text)?["data"]?.AsArray() ?? new JsonArray())
		{
			string? text2 = item?["id"]?.GetValue<string>();
			if (string.IsNullOrEmpty(text2))
			{
				continue;
			}
			if (provider == AiProviders.Gemini)
			{
				text2 = (text2.StartsWith("models/") ? text2.Substring(7) : text2);
			}
			string text3 = text2.ToLowerInvariant();
			if (!text3.Contains("safety") && !text3.Contains("guard") && !text3.StartsWith("openrouter/"))
			{
				bool flag;
				switch (provider.Id)
				{
				case "openrouter":
					flag = text3.EndsWith(":free") || (Price(item, "prompt") == 0.0 && Price(item, "completion") == 0.0);
					break;
				case "gemini":
					flag = text3.Contains("gemini") && !text3.Contains("embedding") && !text3.Contains("tts") && !text3.Contains("image") && !text3.Contains("live") && !text3.Contains("audio");
					break;
				case "openai":
					flag = (text3.StartsWith("gpt-") || text3.StartsWith("chatgpt") || Regex.IsMatch(text3, "^o\\d")) && !new string[8] { "audio", "realtime", "transcribe", "tts", "image", "search", "instruct", "embedding" }.Any(text3.Contains);
					break;
				case "anthropic":
				case "deepseek":
					flag = true;
					break;
				default:
					flag = !text3.Contains("whisper") && !text3.Contains("tts") && !text3.Contains("guard") && !text3.Contains("embed") && !text3.Contains("orpheus") && !text3.Contains("playai");
					break;
				}
				if (flag)
				{
					list.Add(text2);
				}
			}
		}
		if (!provider.Anthropic)
		{
			list.Sort(StringComparer.OrdinalIgnoreCase);
		}
		return list;
	}

	private static double Price(JsonNode? model, string field)
	{
		if (!double.TryParse(model?["pricing"]?[field]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return -1.0;
		}
		return result;
	}

	public static async IAsyncEnumerable<string> StreamAsync(AiProvider provider, string key, string model, IEnumerable<(string Role, string Text)> messages, [EnumeratorCancellation] CancellationToken ct = default(CancellationToken))
	{
		JsonObject jsonObject = ((!provider.Anthropic) ? new JsonObject
		{
			["model"] = model,
			["stream"] = true,
			["messages"] = Messages(messages)
		} : new JsonObject
		{
			["model"] = model,
			["max_tokens"] = 2048,
			["stream"] = true,
			["system"] = string.Join("\n", from m in messages
				where m.Role == "system"
				select m.Text),
			["messages"] = Messages(messages.Where(((string Role, string Text) m) => m.Role != "system"))
		});
		using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, provider.BaseUrl + (provider.Anthropic ? "/messages" : "/chat/completions"))
		{
			Content = new StringContent(jsonObject.ToJsonString(), Encoding.UTF8, "application/json")
		})
		{
			Authorize(request, provider, key);
			using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
			wait.CancelAfter(TimeSpan.FromSeconds(120L));
			HttpResponseMessage httpResponseMessage;
			try
			{
				httpResponseMessage = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, wait.Token);
			}
			catch (OperationCanceledException) when (!ct.IsCancellationRequested)
			{
				throw new AiException(provider.Name + " не отвечает. Скорее всего, сервис недоступен из вашей сети: включите VPN или выберите прокси в настройках, раздел «Система» → «Подключение»");
			}
			wait.CancelAfter(Timeout.InfiniteTimeSpan);
			using (httpResponseMessage)
			{
				if (!httpResponseMessage.IsSuccessStatusCode)
				{
					HttpStatusCode statusCode = httpResponseMessage.StatusCode;
					throw new AiException(ErrorText(statusCode, await httpResponseMessage.Content.ReadAsStringAsync(ct), provider));
				}
				using Stream stream = await httpResponseMessage.Content.ReadAsStreamAsync(ct);
				using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
				while (true)
				{
					string? text = await reader.ReadLineAsync(ct);
					if (text == null)
					{
						yield break;
					}
					if (!text.StartsWith("data:"))
					{
						continue;
					}
					string text2 = text.Substring(5).Trim();
					if (text2 == "[DONE]")
					{
						break;
					}
					JsonNode jsonNode;
					try
					{
						jsonNode = JsonNode.Parse(text2);
					}
					catch (JsonException)
					{
						continue;
					}
					JsonNode? jsonNode2 = jsonNode?["error"];
					if (jsonNode2 != null)
					{
						throw new AiException(jsonNode2["message"]?.ToString() ?? "Ошибка сервиса");
					}
					if (provider.Anthropic)
					{
						string? text3 = jsonNode?["type"]?.ToString();
						if (text3 == "message_stop")
						{
							break;
						}
						if (text3 == "content_block_delta")
						{
							string? text4 = jsonNode?["delta"]?["text"]?.ToString();
							if (text4 != null && text4.Length > 0)
							{
								yield return text4;
							}
						}
					}
					else
					{
						string? text5 = jsonNode?["choices"]?[0]?["delta"]?["content"]?.ToString();
						if (!string.IsNullOrEmpty(text5))
						{
							yield return text5;
						}
					}
				}
			}
		}
		static JsonArray Messages(IEnumerable<(string Role, string Text)> list)
		{
			return new JsonArray(list.Select((Func<(string, string), JsonNode>)(((string Role, string Text) m) => new JsonObject
			{
				["role"] = m.Role,
				["content"] = m.Text
			})).ToArray());
		}
	}

	private static string ErrorText(HttpStatusCode status, string body, AiProvider provider)
	{
		string? text = null;
		try
		{
			text = ErrorMessage(JsonNode.Parse(body));
		}
		catch (Exception ex) when ((ex is JsonException || ex is InvalidOperationException) ? true : false)
		{
		}
		switch ((int)status)
		{
		case 401:
		case 403:
			return "Ключ не подошёл — проверьте его в настройках, раздел «ИИ-чат»";
		case 402:
			if (!provider.Free)
			{
				return "На счёте " + provider.Name + " закончились деньги — пополните баланс на сайте сервиса";
			}
			return "Эта модель платная или кончились бесплатные запросы — выберите другую модель";
		case 404:
			return "Модель недоступна — выберите другую";
		case 429:
			return "Лимит бесплатных запросов исчерпан — подождите немного или выберите другую модель";
		default:
			return text ?? $"Сервис ответил ошибкой {(int)status}";
		}
	}

	private static string? ErrorMessage(JsonNode? node)
	{
		if (node is JsonArray jsonArray)
		{
			if (jsonArray.Count > 0)
			{
				return ErrorMessage(jsonArray[0]);
			}
		}
		else if (node is JsonObject jsonObject)
		{
			if (jsonObject["error"] is JsonObject jsonObject2)
			{
				return jsonObject2["message"]?.ToString();
			}
			if (jsonObject["error"] is JsonValue jsonValue)
			{
				return jsonValue.ToString();
			}
			return jsonObject["message"]?.ToString();
		}
		return null;
	}
}
