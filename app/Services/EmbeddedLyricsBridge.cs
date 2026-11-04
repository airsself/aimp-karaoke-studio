using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KaraokeStudio.Services;

/// <summary>
/// Built-in, loopback-only successor to AIMP Lyrics Bridge /lyrics.
/// The RC2 AIMP hotkey worker talks to this listener through exactly the
/// existing marker protocol. No separate Bridge EXE / WebLyrics DLL needed.
/// Only runs while the Karaoke Studio window is open.
/// </summary>
public sealed class EmbeddedLyricsBridge : IDisposable
{
    private const int MaxHeaderBytes = 8192;
    private const int MaxLyricsCharacters = 140_000;
    private readonly LyricsDiscoveryService _discovery;
    private readonly CancellationTokenSource _cancel = new();
    private readonly SemaphoreSlim _singleLookup = new(1, 1);
    private TcpListener? _listener;
    private Task? _run;
    public int Port { get; private set; }

    public EmbeddedLyricsBridge(LyricsDiscoveryService discovery)
        => _discovery = discovery;

    public bool Start(int firstPort = 17321, int lastPort = 17340)
    {
        if (_run is not null) return true;
        for (int port = firstPort; port <= lastPort; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Server.ExclusiveAddressUse = true;
                listener.Start(4);
                _listener = listener;
                Port = port;
                _run = Task.Run(AcceptLoopAsync);
                return true;
            }
            catch (SocketException) { /* The user's old Bridge may own this port. */ }
        }
        return false;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cancel.IsCancellationRequested && _listener is not null)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cancel.Token);
                var accepted = client;
                _ = Task.Run(async () =>
                {
                    using (accepted)
                    {
                        try { await ServeAsync(accepted, _cancel.Token); }
                        catch (OperationCanceledException) { }
                        catch (Exception error) { StudioLog.Error("Embedded Lyrics Bridge", error); }
                    }
                });
            }
            catch (OperationCanceledException) { client?.Dispose(); break; }
            catch (ObjectDisposedException) { client?.Dispose(); break; }
            catch (SocketException) when (_cancel.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                client?.Dispose();
                StudioLog.Error("Embedded Lyrics Bridge accept", error);
                try { await Task.Delay(150, _cancel.Token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        client.NoDelay = true;
        await using var stream = client.GetStream();
        var buffer = new byte[MaxHeaderBytes];
        int used = 0;
        while (used < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(used), ct);
            if (read == 0) return;
            used += read;
            var data = Encoding.ASCII.GetString(buffer, 0, used);
            if (data.Contains("\r\n\r\n", StringComparison.Ordinal)) break;
        }
        string request = Encoding.ASCII.GetString(buffer, 0, used).Split("\r\n")[0];
        if (request.Length > 6000 || !request.StartsWith("GET /lyrics?", StringComparison.Ordinal))
        {
            await ReplyAsync(stream, 400, "AIMPLB-STATUS: BAD_REQUEST", ct);
            return;
        }
        string[] parts = request.Split(' ', 3);
        if (parts.Length < 3 || !parts[1].StartsWith("/lyrics?", StringComparison.Ordinal))
        {
            await ReplyAsync(stream, 400, "AIMPLB-STATUS: BAD_REQUEST", ct);
            return;
        }
        string? artist = null, title = null, album = null;
        foreach (string part in parts[1][8..].Split('&'))
        {
            int equal = part.IndexOf('=');
            if (equal < 0) continue;
            string key = part[..equal];
            string value = WebUtility.UrlDecode(part[(equal + 1)..]);
            if (value.Length > 2048) continue;
            if (key == "artist") artist = value;
            if (key == "title") title = value;
            if (key == "album") album = value;
        }
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
        {
            await ReplyAsync(stream, 400, "AIMPLB-STATUS: MISSING_METADATA", ct);
            return;
        }

        await _singleLookup.WaitAsync(ct);
        try
        {
            // Do not query this same localhost listener recursively.
            var results = await _discovery.SearchAsync(
                new LyricsTrack(artist, title, album ?? "", 0),
                useLegacyBridge: false, forceRefresh: true, cancellation: ct);
            var best = results.BestAutomatic;
            if (best is not null && best.Content.Length <= MaxLyricsCharacters &&
                !best.Content.Contains("AIMPLB-LYRICS-END", StringComparison.Ordinal) &&
                !best.Content.Contains("AIMPLB-LINEBREAK-7F3C", StringComparison.Ordinal))
            {
                var body = "AIMPLB-STATUS: OK\nAIMPLB-LYRICS-BEGIN" +
                    best.Content.Replace("\r\n", "\n", StringComparison.Ordinal)
                        .Replace('\r', '\n')
                        .Replace("\n", "AIMPLB-LINEBREAK-7F3C", StringComparison.Ordinal) +
                    "AIMPLB-LYRICS-END";
                await ReplyAsync(stream, 200, body, ct);
            }
            else await ReplyAsync(stream, 200, "AIMPLB-STATUS: NOT_FOUND", ct);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error)
        {
            StudioLog.Error("Embedded Lyrics Bridge lookup", error);
            await ReplyAsync(stream, 503, "AIMPLB-STATUS: PROVIDER_ERROR", ct);
        }
        finally { _singleLookup.Release(); }
    }

    private static async Task ReplyAsync(NetworkStream stream, int status, string body, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        string reason = status switch { 200 => "OK", 400 => "Bad Request", _ => "Service Unavailable" };
        string header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/plain; charset=utf-8\r\n" +
            $"Content-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _listener?.Stop();
        _listener = null;
        Port = 0;
        // Background accept/lookup tasks observe cancellation; no blocking UI wait.
    }
}
