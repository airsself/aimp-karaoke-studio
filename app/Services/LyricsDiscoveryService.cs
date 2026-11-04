using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KaraokeStudio.Services;

public sealed record LyricsTrack(string Artist, string Title, string Album, int DurationMs);
public sealed record LyricsCandidate(
    string Artist, string Title, string Source, string Content,
    bool Synced, double Confidence)
{
    public string DisplayTitle => $"{Title} — {Artist}";
    public string DisplayDetail => $"{Source}  ·  {(Synced ? "LRC · синхронизировано" : "Текст без таймкодов")}  ·  {Confidence:P0}";
}
public sealed record LyricsLookupResult(IReadOnlyList<LyricsCandidate> Candidates, string Diagnostic)
{
    public LyricsCandidate? BestAutomatic => Candidates.FirstOrDefault(c => c.Confidence >= 0.93);
}

/// <summary>
/// Source-based successor to AIMP Lyrics Bridge 2.5: attempts to use the
/// installed localhost bridge's documented marker protocol, then LRCLIB,
/// then lyrics.ovh for text-only fallback. No bundled binary code.
/// This class never writes audio files or edits AIMP metadata.
/// </summary>
public sealed class LyricsDiscoveryService
{
    private const int MaxResponseBytes = 1_000_000;
    private const int MaxLyricsChars = 150_000;
    private static readonly HttpClient WebClient = CreateWebClient();
    private static readonly HttpClient LocalClient = CreateLocalClient();
    private readonly HttpClient _web;
    private readonly HttpClient _local;
    private readonly Uri _lrclib;
    private readonly Uri _lyricsOvh;
    private readonly int _portStart, _portEnd;
    private readonly string _cacheFile;
    private readonly ConcurrentDictionary<string, CacheRecord> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _cacheGate = new(1, 1);

    public LyricsDiscoveryService(
        HttpClient? webClient = null, HttpClient? localClient = null,
        string? cacheFile = null, Uri? lrclib = null, Uri? lyricsOvh = null,
        int portStart = 17321, int portEnd = 17340)
    {
        _web = webClient ?? WebClient;
        _local = localClient ?? LocalClient;
        _lrclib = lrclib ?? new Uri("https://lrclib.net/");
        _lyricsOvh = lyricsOvh ?? new Uri("https://api.lyrics.ovh/");
        _portStart = portStart; _portEnd = portEnd;
        _cacheFile = cacheFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AimpKaraokeStudio", "lyrics-cache-v3.json");
        try
        {
            if (File.Exists(_cacheFile) && new FileInfo(_cacheFile).Length < 12_000_000)
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, CacheRecord>>(File.ReadAllText(_cacheFile));
                if (saved is not null)
                    foreach (var entry in saved.Take(3000))
                        if (entry.Value.Candidate is { Content.Length: > 0 })
                            _cache[entry.Key] = entry.Value;
            }
        }
        catch (Exception) { /* A corrupted optional cache cannot prevent playback. */ }
    }

    private static HttpClient CreateWebClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "AimpKaraokeStudio/3.0 (+https://github.com/airsself/aimp-karaoke-studio)");
        return client;
    }
    private static HttpClient CreateLocalClient() =>
        new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(12) };

    public bool TryCached(LyricsTrack track, out LyricsCandidate? candidate)
    {
        candidate = null;
        if (_cache.TryGetValue(KeyFor(track), out var cached) &&
            cached.Time > DateTimeOffset.UtcNow.AddDays(-30) &&
            cached.Candidate.Content.Length is > 0 and <= MaxLyricsChars)
        {
            candidate = cached.Candidate; return true;
        }
        return false;
    }

    public async Task RememberAsync(LyricsTrack track, LyricsCandidate chosen, CancellationToken cancellation)
    {
        if (chosen.Content.Length is < 1 or > MaxLyricsChars) return;
        _cache[KeyFor(track)] = new CacheRecord(DateTimeOffset.UtcNow, chosen);
        await _cacheGate.WaitAsync(cancellation);
        try
        {
            if (_cache.Count > 3000) {
                foreach (var key in _cache.OrderBy(x => x.Value.Time)
                         .Take(_cache.Count - 3000).Select(x => x.Key).ToArray())
                    _cache.TryRemove(key, out _);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            var temp = _cacheFile + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(_cache), cancellation);
            File.Move(temp, _cacheFile, true);
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally { _cacheGate.Release(); }
    }

    public async Task<LyricsLookupResult> SearchAsync(
        LyricsTrack track, bool useLegacyBridge, bool forceRefresh, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(track.Artist) || string.IsNullOrWhiteSpace(track.Title))
            return new([], "Для поиска нужны исполнитель и название композиции.");
        if (!forceRefresh && TryCached(track, out var cached) && cached is not null)
            return new([cached], "Найдено в локальном кеше.");
        var candidates = new List<LyricsCandidate>();
        var errors = new List<string>();

        if (useLegacyBridge)
        {
            try
            {
                var old = await TryLegacyBridgeAsync(track, forceRefresh, cancellation);
                if (old is not null) candidates.Add(old);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception error) { errors.Add("Legacy Bridge: " + error.Message); }
        }

        // LRCLIB /api/get selects a duration-matched record; a broader search
        // exposes other versions/remixes for manual choice.
        string query = "api/get?track_name=" + Uri.EscapeDataString(track.Title) +
                       "&artist_name=" + Uri.EscapeDataString(track.Artist);
        if (!string.IsNullOrWhiteSpace(track.Album))
            query += "&album_name=" + Uri.EscapeDataString(track.Album);
        if (track.DurationMs is >= 1000 and <= 3_600_000)
            query += "&duration=" + Math.Round(track.DurationMs / 1000.0).ToString(CultureInfo.InvariantCulture);
        try
        {
            string? exact = await GetTextAsync(_web, new Uri(_lrclib, query), cancellation);
            if (exact is not null) AddLrclibRecords(exact, track, candidates, false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception error) { errors.Add("LRCLIB: " + error.Message); }

        try
        {
            string search = "api/search?track_name=" + Uri.EscapeDataString(track.Title) +
                            "&artist_name=" + Uri.EscapeDataString(track.Artist);
            string? data = await GetTextAsync(_web, new Uri(_lrclib, search), cancellation);
            if (data is not null) AddLrclibRecords(data, track, candidates, true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception error) { errors.Add("LRCLIB search: " + error.Message); }

        // Plain-text provider is only needed when there is no timed version;
        // don't turn a good LRC result into an unsynced one.
        if (!candidates.Any(c => c.Synced && c.Confidence >= 0.90))
        {
            try
            {
                string path = "v1/" + Uri.EscapeDataString(track.Artist) + "/" +
                              Uri.EscapeDataString(track.Title);
                string? data = await GetTextAsync(_web, new Uri(_lyricsOvh, path), cancellation);
                if (data is not null)
                {
                    using var doc = JsonDocument.Parse(data);
                    var lyrics = String(doc.RootElement, "lyrics");
                    if (IsUsable(lyrics))
                        candidates.Add(new(track.Artist, track.Title, "lyrics.ovh", lyrics!.Trim(),
                            IsTimed(lyrics), 0.97));
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception error) { errors.Add("lyrics.ovh: " + error.Message); }
        }

        var result = candidates
            .Where(c => IsUsable(c.Content) && c.Confidence >= 0.50)
            .GroupBy(c => c.Source + "|" + c.Content, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(c => c.Confidence)
            .ThenByDescending(c => c.Synced)
            .ThenBy(c => c.Source == "Legacy Bridge" ? 0 : 1)
            .Take(24).ToArray();
        return new(result, result.Length > 0
            ? $"Найдено вариантов: {result.Length}"
            : (errors.Count > 0 ? string.Join("; ", errors.Take(2)) : "Текст не найден."));
    }

    private async Task<LyricsCandidate?> TryLegacyBridgeAsync(
        LyricsTrack track, bool force, CancellationToken cancellation)
    {
        var query = "/lyrics?artist=" + Uri.EscapeDataString(track.Artist) +
            "&title=" + Uri.EscapeDataString(track.Title) +
            "&album=" + Uri.EscapeDataString(track.Album ?? "") +
            "&mode=best" + (force ? "&force=1&nocache=1" : "");
        for (int port = _portStart; port <= _portEnd && port <= 65535; port++)
        {
            cancellation.ThrowIfCancellationRequested();
            // Detect a listener before doing an expensive provider request.
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, port, cancellation)
                    .AsTask().WaitAsync(TimeSpan.FromMilliseconds(160), cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception) { continue; }

            var local = new Uri("http://127.0.0.1:" + port + query);
            try
            {
                var data = await GetTextAsync(_local, local, cancellation);
                if (data is null) continue;
                var text = ParseLegacyMarkerResponse(data);
                if (IsUsable(text))
                    return new(track.Artist, track.Title, "Legacy Bridge", text!,
                        IsTimed(text!), 0.99);
                if (data.Contains("AIMPLB-STATUS:", StringComparison.Ordinal)) break;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception) { /* Another local process may use this port. */ }
        }
        return null;
    }

    /// <summary>Marker protocol from user-provided AIMP Lyrics Bridge 2.5.0 source handoff.</summary>
    public static string? ParseLegacyMarkerResponse(string data)
    {
        const string begin = "AIMPLB-LYRICS-BEGIN", end = "AIMPLB-LYRICS-END";
        var a = data.IndexOf(begin, StringComparison.Ordinal);
        if (a < 0) return null;
        a += begin.Length;
        var b = data.IndexOf(end, a, StringComparison.Ordinal);
        if (b <= a || b - a > MaxLyricsChars * 3) return null;
        var content = data[a..b].Replace("AIMPLB-LINEBREAK-7F3C", "\n", StringComparison.Ordinal).Trim();
        return IsUsable(content) ? content : null;
    }

    private static void AddLrclibRecords(
        string json, LyricsTrack query, List<LyricsCandidate> items, bool array)
    {
        using var doc = JsonDocument.Parse(json);
        var entries = array && doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().Take(20).ToArray()
            : !array && doc.RootElement.ValueKind == JsonValueKind.Object
                ? [doc.RootElement] : Array.Empty<JsonElement>();
        foreach (var row in entries)
        {
            if (row.ValueKind != JsonValueKind.Object ||
                row.TryGetProperty("instrumental", out var instrumental) &&
                instrumental.ValueKind == JsonValueKind.True) continue;
            string artist = String(row, "artistName") ?? "";
            string title = String(row, "trackName") ?? String(row, "name") ?? "";
            double score = MatchConfidence(query, artist, title, Number(row, "duration"));
            if (score < 0.50) continue;
            string? synced = String(row, "syncedLyrics");
            string? plain = String(row, "plainLyrics");
            string content = IsUsable(synced) ? synced! : IsUsable(plain) ? plain! : "";
            if (!IsUsable(content)) continue;
            items.Add(new(artist, title, "LRCLIB", content.Trim(), IsTimed(content), score));
        }
    }

    public static double MatchConfidence(LyricsTrack query, string candidateArtist,
        string candidateTitle, double durationSeconds)
    {
        string a = Normalize(query.Artist), b = Normalize(candidateArtist);
        string t = Normalize(query.Title), u = Normalize(candidateTitle);
        if (a.Length == 0 || b.Length == 0 || t.Length == 0 || u.Length == 0) return 0;
        double artist = a == b ? 1 : a.Contains(b, StringComparison.Ordinal) ||
            b.Contains(a, StringComparison.Ordinal) ? 0.78 : 0;
        double title = t == u ? 1 : t.Contains(u, StringComparison.Ordinal) ||
            u.Contains(t, StringComparison.Ordinal) ? 0.60 : 0;
        double score = artist * 0.38 + title * 0.62;
        if (durationSeconds > 0 && query.DurationMs > 0 &&
            Math.Abs(query.DurationMs / 1000.0 - durationSeconds) > 15)
            score -= 0.25;
        return Math.Clamp(score, 0, 1);
    }

    private static string Normalize(string s)
    {
        var input = s.Normalize(NormalizationForm.FormD);
        var b = new StringBuilder();
        foreach (var c in input)
            if (char.IsLetterOrDigit(c) &&
                CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                b.Append(char.ToLowerInvariant(c));
        return b.ToString();
    }

    private static string? String(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
    private static double Number(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number : 0;
    }
    private static bool IsUsable(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= MaxLyricsChars &&
        !text.Contains('\0');
    public static bool IsTimed(string text)
    {
        foreach (var row in text.Split('\n').Take(120))
        {
            var line = row.TrimStart();
            if (line.Length >= 7 && line[0] == '[')
            {
                int colon = line.IndexOf(':', 1);
                if (colon > 1 && colon < 5 && char.IsDigit(line[1]) &&
                    colon + 2 < line.Length && char.IsDigit(line[colon + 1]))
                    return true;
            }
        }
        return false;
    }

    private static async Task<string?> GetTextAsync(
        HttpClient client, Uri uri, CancellationToken ct)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new LyricsLookupException("Превышен лимит API (HTTP 429). Повторите позже.");
        if (!response.IsSuccessStatusCode)
            throw new LyricsLookupException("HTTP " + (int)response.StatusCode);
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new LyricsLookupException("Ответ API превышает допустимый размер.");
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (result.Length + count > MaxResponseBytes)
                throw new LyricsLookupException("Ответ API слишком большой.");
            result.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(result.ToArray());
    }

    private static string KeyFor(LyricsTrack t)
    {
        string signature = string.Join("\n", t.Artist.Trim().ToLowerInvariant(),
            t.Title.Trim().ToLowerInvariant(), t.Album.Trim().ToLowerInvariant(),
            t.DurationMs / 1000);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
    }
    private sealed record CacheRecord(DateTimeOffset Time, LyricsCandidate Candidate);
}

public sealed class LyricsLookupException(string message) : Exception(message);
