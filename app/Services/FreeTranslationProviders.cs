using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KaraokeStudio.Services;

/// <summary>
/// Six official translation/free-tier API adapters. Explicitly selected
/// metered cloud APIs are never silently included in automatic failover.
/// All methods use the shared per-session HttpClient and cancellation token.
/// </summary>
internal sealed partial class TranslationProviderRouter
{
    private static string Prompt(string source, string text) =>
        "Translate this single song lyric line from " + source +
        " to Russian. Return only its Russian translation, no quotes, notes or extra lines. Preserve names and punctuation.\n" +
        "LYRIC:\n" + text;

    private static string RequireKey(string key, string name)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new TranslationException(name + ": отсутствует API-ключ.");
        return key;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response,
        string name, CancellationToken ct)
    {
        EnsureHttpSuccess(response, name);
        using var body = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(body, cancellationToken: ct);
    }

    private async Task<string> AzureAsync(string text, string source, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&from=" +
            Uri.EscapeDataString(source == "zh-CN" ? "zh-Hans" : source) + "&to=ru")
        {
            Content = new StringContent(JsonSerializer.Serialize(new[] { new { Text = text } }),
                Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key",
            RequireKey(_options.AzureTranslatorKey, "Azure Translator"));
        if (!string.IsNullOrWhiteSpace(_options.AzureTranslatorRegion))
            request.Headers.Add("Ocp-Apim-Subscription-Region", _options.AzureTranslatorRegion);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        using var doc = await ReadJsonAsync(response, "Azure Translator", ct);
        return doc.RootElement[0].GetProperty("translations")[0].GetProperty("text").GetString() ?? "";
    }

    private async Task<string> GoogleCloudAsync(string text, string source, CancellationToken ct)
    {
        // Google Cloud Translation Basic v2; no API key is appended to URL or logged.
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://translation.googleapis.com/language/translate/v2")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                q = text, source, target = "ru", format = "text"
            }), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-goog-api-key", RequireKey(_options.GoogleCloudApiKey, "Google Cloud"));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        using var doc = await ReadJsonAsync(response, "Google Cloud", ct);
        return System.Net.WebUtility.HtmlDecode(
            doc.RootElement.GetProperty("data").GetProperty("translations")[0]
                .GetProperty("translatedText").GetString() ?? "");
    }

    private async Task<string> GeminiAsync(string text, string source, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                contents = new[] { new { parts = new[] { new { text = Prompt(source, text) } } } },
                generationConfig = new { temperature = 0, maxOutputTokens = 384 }
            }), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", RequireKey(_options.GeminiApiKey, "Gemini"));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        using var doc = await ReadJsonAsync(response, "Gemini", ct);
        return doc.RootElement.GetProperty("candidates")[0].GetProperty("content")
            .GetProperty("parts")[0].GetProperty("text").GetString()?.Trim() ?? "";
    }

    private async Task<string> GroqAsync(string text, string source, CancellationToken ct)
        => await OpenAiCompatibleAsync("https://api.groq.com/openai/v1/chat/completions",
            RequireKey(_options.GroqApiKey, "Groq"), "llama-3.1-8b-instant",
            "Groq", text, source, ct);

    private async Task<string> OpenRouterAsync(string text, string source, CancellationToken ct)
        => await OpenAiCompatibleAsync("https://openrouter.ai/api/v1/chat/completions",
            RequireKey(_options.OpenRouterApiKey, "OpenRouter"), "openrouter/free",
            "OpenRouter", text, source, ct);

    private async Task<string> CloudflareAsync(string text, string source, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(_options.CloudflareAccountId, "^[a-fA-F0-9]{32}$"))
            throw new TranslationException("Cloudflare: неверный идентификатор аккаунта.");
        return await OpenAiCompatibleAsync(
            "https://api.cloudflare.com/client/v4/accounts/" + _options.CloudflareAccountId +
            "/ai/v1/chat/completions",
            RequireKey(_options.CloudflareApiToken, "Cloudflare"),
            "@cf/meta/llama-3.1-8b-instruct", "Cloudflare", text, source, ct);
    }

    private async Task<string> OpenAiCompatibleAsync(string endpoint, string apiKey,
        string model, string name, string text, string source, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                model,
                messages = new[] {
                    new { role = "system",
                        content = "You translate song lyrics faithfully into Russian. Output only the translated line." },
                    new { role = "user", content = Prompt(source, text) }
                },
                temperature = 0,
                max_tokens = 384
            }), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        using var doc = await ReadJsonAsync(response, name, ct);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message")
            .GetProperty("content").GetString()?.Trim() ?? "";
    }
}
