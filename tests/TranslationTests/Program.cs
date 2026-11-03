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
    Console.WriteLine("9 translation/cache/language tests passed");
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
