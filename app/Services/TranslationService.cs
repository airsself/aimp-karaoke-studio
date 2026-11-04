using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KaraokeStudio.Services;

/// <summary>
/// Opt-in, line-by-line English/Japanese/etc to Russian translation.
/// Only lyric text is sent to MyMemory when the translation pane is opened.
/// The hashed on-disk cache prevents repeat requests when a track is replayed.
/// </summary>
public sealed class TranslationService
{
    private const int RequestLimitBytes = 480; // documented MyMemory maximum is 500
    private readonly HttpClient _http;
    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _diskLock = new(1, 1);

    public TranslationService(HttpClient? client = null, string? cachePath = null)
    {
        _http = client ?? SharedHttpClient;
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AimpKaraokeStudio", "translations.v1.json");
        try
        {
            if (File.Exists(_cachePath))
            {
                var values = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(_cachePath));
                if (values is not null)
                    foreach (var kv in values.Take(15000))
                        if (!string.IsNullOrEmpty(kv.Value)) _cache[kv.Key] = kv.Value;
            }
        }
        catch (Exception) { /* An unusable cache must not prevent playback. */ }
    }

    private static readonly HttpClient SharedHttpClient = NewClient();
    private static HttpClient NewClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(18) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AimpKaraokeStudio/2.0");
        return c;
    }

    public static string DetectSourceLanguage(string lyrics)
    {
        bool hiragana = false, hangul = false, han = false, cyrillic = false;
        foreach (char c in lyrics)
        {
            hiragana |= c is >= '\u3040' and <= '\u30ff';
            hangul |= c is >= '\uac00' and <= '\ud7a3';
            han |= c is >= '\u4e00' and <= '\u9fff';
            cyrillic |= c is >= '\u0400' and <= '\u052f';
        }
        return hiragana ? "ja" : hangul ? "ko" : han ? "zh-CN" : cyrillic ? "ru" : "en";
    }

    private static string CacheKey(string sourceLang, string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceLang + "\nru\n" + text));
        return Convert.ToHexString(bytes);
    }

    public bool TryGetCached(string text, string sourceLang, out string translated)
        => _cache.TryGetValue(CacheKey(sourceLang, text), out translated!);

    /// <summary>Reports each completed line on the calling synchronization context.</summary>
    public async Task TranslateLinesAsync(
        IReadOnlyList<string> lines, string configuredLanguage, int focusIndex,
        Action<int, string, bool> onLine,
        Action<int, int> onProgress,
        CancellationToken ct)
    {
        if (lines.Count == 0) return;
        string source = configuredLanguage == "auto"
            ? DetectSourceLanguage(string.Join(" ", lines.Take(80)))
            : configuredLanguage;
        if (!AllowedLanguages.Contains(source))
            throw new TranslationException("Неизвестный исходный язык перевода.");
        int total = lines.Count, done = 0;
        int unsaved = 0;
        // Give the current lyric and following phrases priority, then fill the rest.
        int pivot = Math.Clamp(focusIndex, 0, total - 1);
        var indices = Enumerable.Range(pivot, total - pivot)
            .Concat(Enumerable.Range(0, pivot));
        try
        {
            foreach (int index in indices)
            {
                ct.ThrowIfCancellationRequested();
                string original = lines[index];
                if (string.IsNullOrWhiteSpace(original) || source == "ru")
                {
                    onLine(index, source == "ru" ? original : "", true);
                }
                else
                {
                    string key = CacheKey(source, original);
                    if (_cache.TryGetValue(key, out var cached))
                    {
                        onLine(index, cached, true);
                    }
                    else
                    {
                        var chunks = ChunkByUtf8Bytes(original);
                        var translated = new StringBuilder();
                        foreach (var chunk in chunks)
                        {
                            ct.ThrowIfCancellationRequested();
                            translated.Append(await TranslateChunkAsync(chunk, source, ct));
                        }
                        string result = translated.ToString();
                        ct.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(result))
                            throw new TranslationException("Сервис перевода вернул пустой результат.");
                        _cache[key] = result;
                        onLine(index, result, false);
                        if (++unsaved >= 20)
                        {
                            // Checkpoint every 20 NEW lines, not for every line.
                            // The actual serialization occurs on a pool thread.
                            await SaveCacheAsync(CancellationToken.None);
                            unsaved = 0;
                        }
                        // Avoid API bursts under anonymous provider limits.
                        await Task.Delay(150, ct);
                    }
                }
                done++;
                if ((done & 7) == 0 || done == total)
                    onProgress(done, total);
                // A warm cache can contain thousands of lines with no I/O await.
                // Periodic yielding keeps input, window dragging and playback live.
                if ((done & 31) == 0)
                    await Task.Delay(4, ct);
            }
        }
        finally
        {
            // Preserve completed translations on cancellation or API quota errors.
            // Never block the WPF message pump with whole-cache JSON serialization.
            if (unsaved > 0) await SaveCacheAsync(CancellationToken.None);
        }
    }

    private static readonly HashSet<string> AllowedLanguages = new(StringComparer.Ordinal) {
        "en", "ja", "ko", "zh-CN", "es", "fr", "de", "it", "pt", "ru"
    };

    public static IReadOnlyList<string> ChunkByUtf8Bytes(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) <= RequestLimitBytes) return [text];
        var parts = new List<string>();
        var buffer = new StringBuilder();
        int bytes = 0;
        var chars = StringInfo.GetTextElementEnumerator(text);
        while (chars.MoveNext())
        {
            string element = chars.GetTextElement();
            int count = Encoding.UTF8.GetByteCount(element);
            if (bytes > 0 && bytes + count > RequestLimitBytes)
            {
                parts.Add(buffer.ToString());
                buffer.Clear(); bytes = 0;
            }
            buffer.Append(element); bytes += count;
        }
        if (buffer.Length > 0) parts.Add(buffer.ToString());
        return parts;
    }

    private async Task<string> TranslateChunkAsync(string chunk, string source, CancellationToken ct)
    {
        var query = Uri.EscapeDataString(chunk);
        var pair = Uri.EscapeDataString(source + "|ru");
        var uri = new Uri("https://api.mymemory.translated.net/get?q=" + query + "&langpair=" + pair);
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
                throw new TranslationException("Лимит онлайн-перевода исчерпан. Попробуйте позже.");
            if (!response.IsSuccessStatusCode)
                throw new TranslationException($"Сервис перевода недоступен: HTTP {(int)response.StatusCode}.");
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = document.RootElement;
            if (root.TryGetProperty("responseStatus", out var status) &&
                status.ValueKind == JsonValueKind.Number && status.GetInt32() != 200)
                throw new TranslationException("MyMemory вернул ошибку или ограничение запросов.");
            string? value = root.GetProperty("responseData").GetProperty("translatedText").GetString();
            return WebUtility.HtmlDecode(value ?? "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TranslationException) { throw; }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new TranslationException("Не удалось подключиться к сервису перевода.", error);
        }
    }

    private async Task SaveCacheAsync(CancellationToken ct)
    {
        await _diskLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache.Count > 15000) return; // bounded disk footprint
            // JSON serialization and large dictionary snapshots must never
            // run on WPF's UI thread.
            string json = await Task.Run(() =>
                JsonSerializer.Serialize(_cache.ToArray().ToDictionary(
                    pair => pair.Key, pair => pair.Value,
                    StringComparer.Ordinal)), ct).ConfigureAwait(false);
            if (Encoding.UTF8.GetByteCount(json) > 8_000_000) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            string temp = _cachePath + ".tmp";
            await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
            File.Move(temp, _cachePath, overwrite: true);
            Interlocked.Increment(ref _cacheSaveCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (IOException) { /* cache is optional */ }
        catch (UnauthorizedAccessException) { /* read-only profile */ }
        finally { _diskLock.Release(); }
    }
    private int _cacheSaveCount;
    // Observable for profiling and deterministic regression checks.
    public int CacheSaveCount => Volatile.Read(ref _cacheSaveCount);
}

public sealed class TranslationException : Exception
{
    public TranslationException(string message) : base(message) { }
    public TranslationException(string message, Exception inner) : base(message, inner) { }
}
