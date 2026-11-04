using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KaraokeStudio.Services;

/// <summary>
/// Per-search snapshot. API keys are read from environment variables and are
/// never persisted in StudioSettings, the translation cache, or diagnostic logs.
/// </summary>
public sealed record TranslationProviderOptions(
    string Mode = "auto",
    string LibreTranslateUrl = "",
    string LibreTranslateApiKey = "",
    string DeepLApiKey = "")
{
    public static TranslationProviderOptions FromEnvironment(string mode, string libreUrl)
        => new(mode, libreUrl,
            Environment.GetEnvironmentVariable("LIBRETRANSLATE_API_KEY") ?? "",
            Environment.GetEnvironmentVariable("DEEPL_AUTH_KEY") ?? "");
}

/// <summary>Request-scoped provider routing with circuit breaking and cancellation.</summary>
internal sealed class TranslationProviderRouter
{
    private readonly HttpClient _http;
    private readonly TranslationProviderOptions _options;
    private readonly Action<string>? _onProviderChanged;
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private readonly List<string> _order;
    private string? _lastProvider;

    public string CacheNamespace { get; }

    public TranslationProviderRouter(HttpClient http, TranslationProviderOptions options,
        Action<string>? onProviderChanged = null)
    {
        _http = http;
        _options = options;
        _onProviderChanged = onProviderChanged;
        _order = BuildOrder(options);
        // Preserve v4.1's cache for the legacy anonymous-only configuration.
        CacheNamespace = _order.SequenceEqual(["mymemory"]) ? "" :
            string.Join(",", _order);
    }

    private static List<string> BuildOrder(TranslationProviderOptions options)
    {
        if (options.Mode is not ("auto" or "mymemory" or "libre" or "deepl"))
            throw new TranslationException("Неизвестный провайдер перевода.");
        if (options.Mode == "libre" && string.IsNullOrWhiteSpace(options.LibreTranslateUrl))
            throw new TranslationException("Укажите URL сервера LibreTranslate в настройках.");
        if (options.Mode == "deepl" && string.IsNullOrWhiteSpace(options.DeepLApiKey))
            throw new TranslationException("Для DeepL задайте переменную среды DEEPL_AUTH_KEY и перезапустите приложение.");
        if (options.Mode == "mymemory") return ["mymemory"];
        if (options.Mode == "libre") return ["libre"];
        if (options.Mode == "deepl") return ["deepl"];

        var order = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.LibreTranslateUrl)) order.Add("libre");
        if (!string.IsNullOrWhiteSpace(options.DeepLApiKey)) order.Add("deepl");
        order.Add("mymemory");
        return order;
    }

    public async Task<string> TranslateAsync(string text, string source, CancellationToken ct)
    {
        var failures = new List<string>();
        foreach (var id in _order)
        {
            ct.ThrowIfCancellationRequested();
            if (_failed.Contains(id)) continue;
            try
            {
                string translated = id switch
                {
                    "libre" => await LibreTranslateAsync(text, source, ct),
                    "deepl" => await DeepLAsync(text, source, ct),
                    _ => await MyMemoryAsync(text, source, ct)
                };
                if (string.IsNullOrWhiteSpace(translated))
                    throw new TranslationException("Пустой перевод.");
                if (_lastProvider != id)
                {
                    _lastProvider = id;
                    _onProviderChanged?.Invoke(DisplayName(id));
                }
                return translated;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is TranslationException or HttpRequestException
                or TaskCanceledException or JsonException or InvalidOperationException
                or UriFormatException or NotSupportedException)
            {
                _failed.Add(id);
                // Do not leak provider responses, request text, or API secrets in UI.
                failures.Add(DisplayName(id) + ": " +
                    (error is TranslationException te ? te.Message : "ошибка соединения или ответа"));
            }
        }
        string reason = failures.Count > 0 ? string.Join("; ", failures) :
            "все настроенные источники недоступны после предыдущих ошибок";
        throw new TranslationException("Нет доступных источников перевода. " + reason);
    }

    public static string DisplayName(string id) => id switch
    {
        "mymemory" => "MyMemory",
        "libre" => "LibreTranslate",
        "deepl" => "DeepL",
        _ => id
    };

    public static Uri ValidateLibreUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 300 ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new TranslationException("LibreTranslate: используйте HTTPS URL или HTTP на localhost.");
        // A root installation and installations hosted under a subpath are supported.
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/translate", StringComparison.OrdinalIgnoreCase))
            return uri;
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + path + "/translate");
    }

    private static string NormalizeLanguage(string source)
        => source == "zh-CN" ? "zh" : source;

    private async Task<string> LibreTranslateAsync(string text, string source, CancellationToken ct)
    {
        var endpoint = ValidateLibreUrl(_options.LibreTranslateUrl);
        var json = JsonSerializer.Serialize(new
        {
            q = text,
            source = NormalizeLanguage(source),
            target = "ru",
            format = "text",
            api_key = _options.LibreTranslateApiKey
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        EnsureHttpSuccess(response, "LibreTranslate");
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return document.RootElement.GetProperty("translatedText").GetString() ?? "";
    }

    private async Task<string> DeepLAsync(string text, string source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.DeepLApiKey))
            throw new TranslationException("Отсутствует DEEPL_AUTH_KEY.");
        // DeepL Free keys end in :fx. Pro keys use the separate api.deepl.com endpoint.
        string host = _options.DeepLApiKey.EndsWith(":fx", StringComparison.Ordinal)
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate";
        string lang = source == "zh-CN" ? "ZH" : source.ToUpperInvariant();
        var json = JsonSerializer.Serialize(new
        {
            text = new[] { text }, source_lang = lang, target_lang = "RU"
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, host)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", _options.DeepLApiKey);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        EnsureHttpSuccess(response, "DeepL");
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return document.RootElement.GetProperty("translations")[0].GetProperty("text").GetString() ?? "";
    }

    private async Task<string> MyMemoryAsync(string text, string source, CancellationToken ct)
    {
        var uri = new Uri("https://api.mymemory.translated.net/get?q=" +
            Uri.EscapeDataString(text) + "&langpair=" + Uri.EscapeDataString(source + "|ru"));
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        EnsureHttpSuccess(response, "MyMemory");
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        if (root.TryGetProperty("responseStatus", out var status) &&
            status.ValueKind == JsonValueKind.Number && status.GetInt32() != 200)
            throw new TranslationException("MyMemory сообщил о лимите или ошибке API.");
        string result = WebUtility.HtmlDecode(
            root.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "");
        // MyMemory sometimes sends quota messages as HTTP 200 and translatedText.
        if (result.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("YOU USED ALL AVAILABLE FREE TRANSLATIONS", StringComparison.OrdinalIgnoreCase))
            throw new TranslationException("Квота MyMemory исчерпана.");
        return result;
    }

    private static void EnsureHttpSuccess(HttpResponseMessage response, string provider)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new TranslationException(provider + ": превышена квота запросов (429).");
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TranslationException(provider + ": нет доступа или неверный API-ключ.");
        if (!response.IsSuccessStatusCode)
            throw new TranslationException(provider + ": HTTP " + (int)response.StatusCode + ".");
    }
}
