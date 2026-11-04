using System;
using System.Diagnostics;
using System.Globalization;
using KaraokeStudio.Models;
using KaraokeStudio.Services;

static void Check(bool result, string label) { if (!result) throw new Exception("HOT PATH: "+label); }
var samples = new [] {
    "One word at a time",
    "Тихая ночь, музыка и солнце",
    "君がいない夜にも歌う",
    "Мы поём 👩‍🎤 не спеша ☀️",
    "Cafe\u0301 — e\u0301lite — ZWJ 👩‍❤️‍💋‍👩",
    new string('あ', 240),
};
foreach(var text in samples)
{
    var plan=KaraokeHighlightEngine.Prepare(text,null);
    for(int p=0;p<=100;p++)
        Check(plan.GetHighlightLength(0,10000,p) == Reference(text,null,0,10000,p),
            "plain Unicode parity: "+p);
}
var words = new List<WordTimingDto>();
string piece = "Breathe slowly ";
for(int i=0;i<20;i++)
    words.Add(new WordTimingDto{Text=piece,StartMs=i*500,EndMs=(i+1)*500});
string enhanced=string.Concat(words.Select(w=>w.Text));
var prepared=KaraokeHighlightEngine.Prepare(enhanced,words);
for(int p=0;p<=100;p++)
    Check(prepared.GetHighlightLength(0,10000,p)==Reference(enhanced,words,0,10000,p),
          "enhanced word timestamp parity: "+p);
var invalid=KaraokeHighlightEngine.Prepare(enhanced,[new WordTimingDto {
    Text="non-matching",StartMs=0,EndMs=1000}]);
for(int p=0;p<=100;p++)
    Check(invalid.GetHighlightLength(0,10000,p)==Reference(enhanced,null,0,10000,p),
          "invalid word timings fallback");

const int iterations=25000;
for(int i=0;i<1500;i++) { _ = Reference(enhanced,words,0,10000,i%101);
    _ = prepared.GetHighlightLength(0,10000,i%101); }
GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
long alloc0=GC.GetAllocatedBytesForCurrentThread();
var sw=Stopwatch.StartNew();
long legacySum=0;
for(int i=0;i<iterations;i++)legacySum+=Reference(enhanced,words,0,10000,i%101);
sw.Stop();double referenceMs=sw.Elapsed.TotalMilliseconds;
long referenceBytes=GC.GetAllocatedBytesForCurrentThread()-alloc0;
alloc0=GC.GetAllocatedBytesForCurrentThread();
sw.Restart();
long optimizedSum=0;
for(int i=0;i<iterations;i++)optimizedSum+=prepared.GetHighlightLength(0,10000,i%101);
sw.Stop();double preparedMs=sw.Elapsed.TotalMilliseconds;
long preparedBytes=GC.GetAllocatedBytesForCurrentThread()-alloc0;
Check(legacySum==optimizedSum,"timed output unchanged");
Check(preparedBytes < Math.Max(4096,referenceBytes/100),
      "prepared highlight has at least 100x lower per-tick allocation");
Console.WriteLine($"HIGHLIGHT BENCH (25k updates): old={referenceMs:F1}ms/{referenceBytes:N0}B; " +
                  $"prepared={preparedMs:F1}ms/{preparedBytes:N0}B; " +
                  $"allocated reduction={(referenceBytes>0 ? 100.0*(1.0-(double)preparedBytes/referenceBytes) : 0):F1}%");
Console.WriteLine("Unicode/Enhanced-LRC prepared highlight parity and allocation tests passed");

static int Reference(string text, IReadOnlyList<WordTimingDto>? words,
    int start,int end,double percentage)
{
    if (string.IsNullOrEmpty(text))return 0;
    double normalized=double.IsFinite(percentage)?Math.Clamp(percentage/100,0,1):0;
    if (words is {Count:>0} && words.Count<=256 &&
        string.Concat(words.Select(w=>w.Text)) == text)
    {
        double time=start+normalized*Math.Max(1,end-start);
        int prefix=0;
        foreach(var word in words) {
            if(time >= word.EndMs)prefix+=word.Text.Length;
            else if(time > word.StartMs) {
                double fraction=(time-word.StartMs)/Math.Max(1,word.EndMs-word.StartMs);
                prefix+=OldCut(word.Text,fraction);break;
            } else break;
        }
        return Math.Clamp(prefix,0,text.Length);
    }
    return OldCut(text,normalized);
}
static int OldCut(string text,double progress)
{
    if(progress<=0||text.Length==0)return 0;
    if(progress>=1)return text.Length;
    int[] starts=StringInfo.ParseCombiningCharacters(text);
    int count=Math.Clamp((int)Math.Floor(starts.Length*progress),0,starts.Length);
    return count>=starts.Length?text.Length:starts[count];
}
