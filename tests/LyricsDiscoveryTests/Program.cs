using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using KaraokeStudio.Services;

static void Check(bool ok, string name) { if(!ok) throw new Exception("FAILED: " + name); }
var track = new LyricsTrack("Demo Singer","Ocean of Silence","An Album",180000);
var handler = new FakeHttp();
string root = Path.Combine(Path.GetTempPath(), "karaoke-lyrics-v3-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string cache = Path.Combine(root,"cache.json");
    var service = new LyricsDiscoveryService(new HttpClient(handler), new HttpClient(handler),
        cache, new Uri("https://lrclib.net/"), new Uri("https://api.lyrics.ovh/"));
    Check(LyricsDiscoveryService.ParseLegacyMarkerResponse(
        "AIMPLB-STATUS: OK\nAIMPLB-LYRICS-BEGIN[00:10.00]OneAIMPLB-LINEBREAK-7F3C[00:15.00]TwoAIMPLB-LYRICS-END")
        == "[00:10.00]One\n[00:15.00]Two", "Legacy Bridge marker + LRC preserved");
    Check(LyricsDiscoveryService.ParseLegacyMarkerResponse("AIMPLB-STATUS:NOT_FOUND") == null,
          "Legacy Bridge no result");
    Check(LyricsDiscoveryService.IsTimed("[00:12.00]Song\n[00:14.00]Next"), "LRC timestamp recognition");
    Check(!LyricsDiscoveryService.IsTimed("Plain song\nNext verse"), "Plain-text recognition");

    var found = await service.SearchAsync(track,false,false,CancellationToken.None);
    Check(found.Candidates.Count >= 1, "LRCLIB search found candidates");
    Check(found.BestAutomatic is { Synced: true, Source: "LRCLIB" }, "Prefer confidence-matched timed LRCLIB");
    Check(found.BestAutomatic!.Content.StartsWith("[00:01.00]"), "Preserve LRC source text");
    Check(found.Candidates.All(x=>x.Confidence >= 0.5), "Reject unrelated tracks");
    Check(LyricsDiscoveryService.MatchConfidence(track,"Unrelated","Wrong",180) < .50,
          "Reject different metadata");
    Check(LyricsDiscoveryService.MatchConfidence(track,track.Artist,track.Title,240) < .93,
          "Duration mismatch prevents silent auto-selection");
    int count = handler.Calls;
    await service.RememberAsync(track,found.BestAutomatic!,CancellationToken.None);
    var cached = await service.SearchAsync(track,false,false,CancellationToken.None);
    Check(cached.Candidates.Count == 1 && handler.Calls == count, "Cache avoids HTTP");
    var reopened = new LyricsDiscoveryService(new HttpClient(handler), null, cache);
    Check(reopened.TryCached(track,out var disk) && disk!.Synced, "Persistent cache load");

    var plainTrack = new LyricsTrack("Plain Artist","Plain Song","",123000);
    var plain = await service.SearchAsync(plainTrack,false,true,CancellationToken.None);
    Check(plain.Candidates.Any(x=>x.Source=="lyrics.ovh"&&!x.Synced), "Plain provider fallback");

    // The only allowed write target is a separate .lrc; byte-for-byte
    // metadata integrity is therefore independent of AIMP TagEditor behavior.
    string audio = Path.Combine(root,"song.mp3");
    byte[] bytes = Encoding.UTF8.GetBytes("FAKE_ID3:Artist=DoNotModify;Artwork=Original;Audio=123");
    await File.WriteAllBytesAsync(audio,bytes);
    Check(LyricsSidecarStore.TrySuggestPath(audio,out var suggested) &&
          suggested.EndsWith(".lrc",StringComparison.OrdinalIgnoreCase), "Adjacent sidecar suggestion");
    await LyricsSidecarStore.SaveAsync(suggested,found.BestAutomatic!.Content,false,CancellationToken.None);
    Check(File.ReadAllBytes(audio).SequenceEqual(bytes), "NO AUDIO OR METADATA MODIFICATIONS");
    Check((await File.ReadAllTextAsync(suggested)).Contains("[00:01.00]"), "LRC UTF-8 saved");
    try { await LyricsSidecarStore.SaveAsync(suggested,"new",false,CancellationToken.None);
          throw new Exception("Overwrite without permission unexpectedly succeeded"); }
    catch(IOException) { }
    Check((await File.ReadAllTextAsync(suggested)).StartsWith("[00:01.00]"),
          "Existing sidecar preserved unless explicitly overwritten");
    await LyricsSidecarStore.SaveAsync(suggested,"[00:04.00]Updated",true,CancellationToken.None);
    Check((await File.ReadAllTextAsync(suggested)).Contains("Updated"), "Explicit overwrite allowed");

    var empty=await service.SearchAsync(new LyricsTrack("","", "",0),false,false,CancellationToken.None);
    Check(empty.Candidates.Count == 0 && empty.Diagnostic.Contains("исполнитель"), "Missing metadata");
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    try { await service.SearchAsync(track,false,true,cts.Token); throw new Exception("Cancellation missing"); }
    catch (OperationCanceledException) { }
    Check(handler.Requests.All(x=>!x.Contains("127.0.0.1")), "Bridge opt-out respected");
    Console.WriteLine("17 lyrics provider/bridge/cache/sidecar cases passed");
}
finally { Directory.Delete(root,true); }

sealed class FakeHttp: HttpMessageHandler
{
    public int Calls;
    public List<string> Requests = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        var url = request.RequestUri!.AbsoluteUri;
        Requests.Add(url);
        string? body = null;
        if (url.Contains("api/get") && url.Contains("Ocean%20of%20Silence"))
            body = """{"artistName":"Demo Singer","trackName":"Ocean of Silence","duration":180,"syncedLyrics":"[00:01.00]One\n[00:05.00]Two","plainLyrics":"One\nTwo"}""";
        if (url.Contains("api/search") && url.Contains("Ocean%20of%20Silence"))
            body = """[{"artistName":"Demo Singer","trackName":"Ocean of Silence","duration":180,"syncedLyrics":"[00:01.00]One\n[00:05.00]Two"},{"artistName":"Someone Else","trackName":"Wrong Song","plainLyrics":"NO"}]""";
        if (url.Contains("v1/Plain%20Artist/Plain%20Song"))
            body = """{"lyrics":"Plain lyrics here\nSecond line"}""";
        if (url.Contains("api/search") && url.Contains("Plain%20Song")) body = "[]";
        return Task.FromResult(body is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(body,Encoding.UTF8,"application/json")});
    }
}
