using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using KaraokeStudio.Services;

static void Check(bool result,string name){if(!result)throw new Exception("BRIDGE "+name);}
var fake = new FakeProvider();
string path = Path.Combine(Path.GetTempPath(),"AimpBridgeTest-"+Guid.NewGuid().ToString("N")+".json");
try {
  var lookup = new LyricsDiscoveryService(new HttpClient(fake), cacheFile:path);
  using var probe = new TcpListener(IPAddress.Loopback,0);
  probe.Start();
  int port = ((IPEndPoint)probe.LocalEndpoint).Port;
  probe.Stop();
  using var server = new EmbeddedLyricsBridge(lookup);
  Check(server.Start(port,port)&&server.Port==port,"loopback bind");
  using var client=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(8)};
  var url=$"http://127.0.0.1:{port}/lyrics?artist=Demo%20Singer&title=Night%20Sky&album=&force=1&nocache=1&mode=best";
  string data=await client.GetStringAsync(url);
  Check(data.Contains("AIMPLB-LYRICS-BEGIN")&&data.Contains("AIMPLB-LYRICS-END"),
        "Legacy protocol start and end");
  Check(data.Contains("AIMPLB-LINEBREAK-7F3C"),"newlines encoded in marker protocol");
  Check(data.Contains("Тишина"),"Unicode preserved through native-compatible API");
  var missing=await client.GetStringAsync($"http://127.0.0.1:{port}/lyrics?artist=Unknown&title=Missing");
  Check(missing.Contains("AIMPLB-STATUS: NOT_FOUND"),"not found no false positive");
  var bad=await client.GetAsync($"http://127.0.0.1:{port}/lyrics?artist=");
  Check(bad.StatusCode==HttpStatusCode.BadRequest,"missing metadata rejected");
  Check(fake.Calls>=2,"provider called without recursive loopback");
  server.Dispose();
  Console.WriteLine("6 embedded native-compatible Lyrics Bridge HTTP tests passed");
} finally { if(File.Exists(path))File.Delete(path); }

sealed class FakeProvider:HttpMessageHandler
{
   public int Calls;
   protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
      ct.ThrowIfCancellationRequested();
      Interlocked.Increment(ref Calls);
      var uri=request.RequestUri!.AbsoluteUri;
      string json=uri.Contains("Night%20Sky") && uri.Contains("/api/get")
        ? """{"artistName":"Demo Singer","trackName":"Night Sky","duration":200,"syncedLyrics":"[00:01.00]Тишина\n[00:06.00]The world sings","plainLyrics":"Тишина\nThe world sings"}"""
        : uri.Contains("Night%20Sky") && uri.Contains("/api/search")
          ? "[]" : "[]";
      HttpStatusCode code=uri.Contains("/api/get") && !uri.Contains("Night%20Sky")
        ? HttpStatusCode.NotFound : HttpStatusCode.OK;
      return Task.FromResult(new HttpResponseMessage(code) {
        Content=new StringContent(json,Encoding.UTF8,"application/json")});
   }
}
