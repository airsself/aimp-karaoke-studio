using System.Net;
using System.Text;
using System.Text.Json;
using KaraokeStudio.Services;

static void Check(bool value, string name)
{
    if (!value) throw new Exception("Translation test failed: " + name);
}

Check(TranslationService.DetectSourceLanguage("I love this night") == "en", "English default");
Check(TranslationService.DetectSourceLanguage("君はいつも ありがとう") == "ja", "Japanese Kana");
Check(TranslationService.DetectSourceLanguage("안녕하세요") == "ko", "Korean Hangul");
Check(TranslationService.DetectSourceLanguage("我喜欢音乐") == "zh-CN", "Chinese Han");
Check(TranslationService.DetectSourceLanguage("Привет, как дела?") == "ru", "Russian Cyrillic");

var chunks = TranslationService.ChunkByUtf8Bytes(new string('あ', 270));
Check(chunks.Count >= 2 && chunks.All(x => Encoding.UTF8.GetByteCount(x) <= 480),
      "Unicode request chunks under 500-byte API limit");
Check(string.Concat(chunks) == new string('あ', 270), "No Unicode content loss");

var temp = Path.Combine(Path.GetTempPath(), "karaoke-translation-tests-" + Guid.NewGuid() + ".json");
try
{
    var handler = new FakeTranslationHandler();
    var service = new TranslationService(new HttpClient(handler), temp);
    var results = new Dictionary<int,string>();
    var progress = new List<int>();
    await service.TranslateLinesAsync(["Hello", "Good night", "Hello"], "en", 1,
        (i,t,cached) => results[i] = t, (done,total) => progress.Add(done), CancellationToken.None);
    Check(results.Count==3 && results[0].StartsWith("RU:") &&
          results[1].StartsWith("RU:") && progress[^1]==3, "Translated each aligned line");
    Check(handler.Calls==2, "Deduplicated repeated lyric line requests");
    Check(handler.Queries.All(x=>Encoding.UTF8.GetByteCount(x) <= 480),
          "All API requests under 500-byte limit");

    var fromDisk = new TranslationService(new HttpClient(new FakeTranslationHandler()), temp);
    Check(fromDisk.TryGetCached("Hello","en",out var cached) && cached=="RU:Hello",
          "Persistent translation cache");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try {
        await service.TranslateLinesAsync(["New"],"en",0,(i,t,c)=>{},(n,total)=>{},cancelled.Token);
        throw new Exception("Expected cancellation");
    } catch (OperationCanceledException) { }
    var russian = new Dictionary<int,string>();
    await service.TranslateLinesAsync(["Привет"],"auto",0,(i,t,c)=>russian[i]=t,(n,total)=>{},CancellationToken.None);
    Check(russian[0]=="Привет","Russian text doesn't incur network request");

    var longPhrase = new string('あ',270);
    await service.TranslateLinesAsync([longPhrase],"ja",0,(i,t,c)=>{},(n,total)=>{},CancellationToken.None);
    Check(handler.Queries.All(x=>Encoding.UTF8.GetByteCount(x)<=480), "Long phrase remains chunked");
    var many = Enumerable.Range(0, 42).Select(i => "Unique cached lyric number " + i).ToArray();
    int beforeSaves = service.CacheSaveCount;
    await service.TranslateLinesAsync(many,"en",0,(i,t,cached)=>{},(n,total)=>{},CancellationToken.None);
    int addedSaves = service.CacheSaveCount - beforeSaves;
    Check(addedSaves is >= 1 and <= 3, "Batched cache checkpoints for 42 new lines");
    var warm = new TranslationService(new HttpClient(new FakeTranslationHandler()), temp);
    Check(many.All(line => warm.TryGetCached(line,"en",out var value) && value.StartsWith("RU:")),
          "All 42 new translations survive batch persistence");
    beforeSaves = service.CacheSaveCount;
    await service.TranslateLinesAsync(many,"en",0,(i,t,cached)=>{},(n,total)=>{},CancellationToken.None);
    Check(service.CacheSaveCount == beforeSaves, "Warm cache never rewrites disk");
    // Provider fallback: LibreTranslate quota failure must switch to MyMemory.
    var fallback = new ProviderFakeHandler { LibreStatus = HttpStatusCode.TooManyRequests };
    var providerSource = new TranslationService(new HttpClient(fallback), temp + ".fallback");
    string? providerName = null;
    var autoResults = new Dictionary<int, string>();
    await providerSource.TranslateLinesAsync(["Provider fallback test", "Second line"], "en", 0,
        (i, t, cached) => autoResults[i] = t, (_, _) => { }, CancellationToken.None,
        new TranslationProviderOptions("auto", "https://libre.example.test"),
        name => providerName = name);
    Check(autoResults.Count == 2 && autoResults.Values.All(x => x.StartsWith("MM:")),
        "Fallback produces translations for every lyric line");
    Check(fallback.LibreCalls == 1 && fallback.MyMemoryCalls == 2,
        "Failed provider disabled for remaining session");
    Check(providerName == "MyMemory", "Active fallback provider reported to UI");

    var libre = new ProviderFakeHandler();
    var libreService = new TranslationService(new HttpClient(libre), temp + ".libre");
    var libreLines = new Dictionary<int, string>();
    await libreService.TranslateLinesAsync(["Good morning"], "ja", 0,
        (i, t, cached) => libreLines[i] = t, (_, _) => { }, CancellationToken.None,
        new TranslationProviderOptions("libre", "https://libre.example.test"),
        _ => { });
    Check(libre.LibreCalls == 1 && libre.MyMemoryCalls == 0 &&
        libreLines[0] == "LT:Good morning" && libre.LibreSource == "ja",
        "LibreTranslate JSON response and Japanese source language");

    var deepL = new ProviderFakeHandler();
    var deepService = new TranslationService(new HttpClient(deepL), temp + ".deepl");
    var deepLines = new Dictionary<int, string>();
    await deepService.TranslateLinesAsync(["Good evening"], "zh-CN", 0,
        (i, t, cached) => deepLines[i] = t, (_, _) => { }, CancellationToken.None,
        new TranslationProviderOptions("deepl", DeepLApiKey: "test-key:fx"));
    Check(deepL.DeepLCalls == 1 && deepL.DeepLSource == "ZH" &&
        deepL.AuthHeader == "DeepL-Auth-Key test-key:fx" &&
        deepLines[0] == "DL:Good evening",
        "DeepL endpoint, authentication, language mapping and JSON response");

    Check(TranslationProviderRouter.ValidateLibreUrl("https://libre.example.test/api").AbsolutePath
        == "/api/translate", "LibreTranslate subpath endpoint construction");
    try {
        TranslationProviderRouter.ValidateLibreUrl("http://evil.example.test/translate");
        throw new Exception("Expected insecure remote URL rejection");
    } catch (TranslationException) { }

    var failAll = new ProviderFakeHandler { LibreStatus = HttpStatusCode.TooManyRequests,
        MyMemoryStatus = HttpStatusCode.TooManyRequests };
    try {
        await new TranslationService(new HttpClient(failAll), temp + ".failed")
            .TranslateLinesAsync(["No provider available"], "en", 0,
                (_, _, _) => { }, (_, _) => { }, CancellationToken.None,
                new TranslationProviderOptions("auto", "https://libre.example.test"));
        throw new Exception("Expected provider exhaustion");
    } catch (TranslationException e) {
        Check(e.Message.Contains("Нет доступных источников"), "Provider exhaustion diagnostic");
    }

    using (var abort = new CancellationTokenSource()) {
        abort.Cancel();
        var cancelledHandler = new ProviderFakeHandler();
        try {
            await new TranslationService(new HttpClient(cancelledHandler), temp + ".cancelled")
                .TranslateLinesAsync(["Cancelled work"], "en", 0,
                    (_, _, _) => { }, (_, _) => { }, abort.Token,
                    new TranslationProviderOptions("auto", "https://libre.example.test"));
            throw new Exception("Expected abort");
        } catch (OperationCanceledException) { }
        Check(cancelledHandler.LibreCalls == 0 && cancelledHandler.MyMemoryCalls == 0,
            "Cancellation does not start fallback request");
    }
    Console.WriteLine("Translation/cache tests and provider routing regressions passed");

}
finally {
    if (File.Exists(temp)) File.Delete(temp);
}

sealed class FakeTranslationHandler: HttpMessageHandler
{
    public int Calls { get; private set; }
    public List<string> Queries { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        var qs = request.RequestUri!.Query.TrimStart('?').Split('&');
        var q = qs.First(s=>s.StartsWith("q=")).Substring(2);
        string input = Uri.UnescapeDataString(q.Replace("+"," "));
        Queries.Add(input);
        var body=JsonSerializer.Serialize(new { responseStatus=200, responseData=new { translatedText="RU:"+input } });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content=new StringContent(body,Encoding.UTF8,"application/json")});
    }
}

sealed class ProviderFakeHandler : HttpMessageHandler
{
    public int LibreCalls, MyMemoryCalls, DeepLCalls;
    public string LibreSource = "", DeepLSource = "", AuthHeader = "";
    public HttpStatusCode LibreStatus = HttpStatusCode.OK;
    public HttpStatusCode MyMemoryStatus = HttpStatusCode.OK;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.RequestUri!.Host == "libre.example.test")
        {
            LibreCalls++;
            if (LibreStatus != HttpStatusCode.OK)
                return new HttpResponseMessage(LibreStatus);
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            LibreSource = doc.RootElement.GetProperty("source").GetString() ?? "";
            var input = doc.RootElement.GetProperty("q").GetString() ?? "";
            return Ok(new { translatedText = "LT:" + input });
        }
        if (request.RequestUri.Host.Contains("deepl.com"))
        {
            DeepLCalls++;
            AuthHeader = request.Headers.Authorization?.ToString() ?? "";
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            DeepLSource = doc.RootElement.GetProperty("source_lang").GetString() ?? "";
            var input = doc.RootElement.GetProperty("text")[0].GetString() ?? "";
            return Ok(new { translations = new[] { new { text = "DL:" + input } } });
        }
        MyMemoryCalls++;
        if (MyMemoryStatus != HttpStatusCode.OK)
            return new HttpResponseMessage(MyMemoryStatus);
        var text = Uri.UnescapeDataString(request.RequestUri.Query.TrimStart('?')
            .Split('&').Single(x => x.StartsWith("q="))[2..]);
        return Ok(new { responseStatus = 200,
            responseData = new { translatedText = "MM:" + text } });
    }
    private static HttpResponseMessage Ok(object value) =>
        new(HttpStatusCode.OK) {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
}
