using KaraokeStudio.Services;
using System.Text;

static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
var first = LrcReader.Parse("[00:10]First\n[offset:1000]\n[00:12]Second\n");
Check(first.Synced && first.Lines.Count == 2 && first.Lines[0].StartMs == 11000 && first.Lines[1].StartMs == 13000, "late offset");
var equal = LrcReader.Parse("[00:01.00]A\n[00:01.00]B\n");
Check(equal.Lines.Count == 1 && equal.Lines[0].Text == "A\nB" && equal.Lines[0].EndMs > equal.Lines[0].StartMs, "equal timestamp");
var repeated = LrcReader.Parse("[00:01.20][00:02.500]Hello\n");
Check(repeated.Lines.Count == 2 && repeated.Lines[0].StartMs == 1200 && repeated.Lines[1].StartMs == 2500, "repeated stamps");
var plain = LrcReader.Parse("First\nSecond");
Check(!plain.Synced && plain.Lines.Count == 2, "plain text");
var invalid = LrcReader.Parse("[00:99.000]No\n[00:10]Yes");
Check(invalid.Lines.Count == 1 && invalid.Lines[0].StartMs == 10000, "bad seconds");
var negative = LrcReader.Parse("[00:01.00]a\n[offset:-5000]");
Check(negative.Lines[0].StartMs == 0, "negative offset clamping");
var temp = Path.GetTempFileName();
try {
    File.WriteAllBytes(temp, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("[00:02]Тест")).ToArray());
    var decoded = LrcReader.Read(temp, 5000);
    Check(decoded.Lines[0].Text == "Тест", "UTF-16 decode");
}
finally { File.Delete(temp); }
var enhanced = LrcReader.Parse("[00:01.00]<00:01.00>Снова <00:01.50>споём <00:02.00>дальше");
Check(enhanced.Synced && enhanced.Lines.Count==1 && enhanced.Lines[0].Words.Count==3,"enhanced words");
Check(enhanced.Lines[0].Words[1].StartMs==1500 && enhanced.Lines[0].Words[1].EndMs==2000,"precision word timing");
Check(enhanced.Lines[0].Text=="Снова споём дальше","enhanced text reconstruction");
var shifted = LrcReader.Parse("[00:01.00]<00:01.00>hello <00:02.00>world\n[offset:500]");
Check(shifted.Lines[0].Words[1].StartMs==2500,"word timing global offset");
Check(KaraokeHighlightEngine.GetHighlightLength("Снова споём дальше", enhanced.Lines[0].Words, 1000, 5000, 0)==0,"initial word sweep");
Check(KaraokeHighlightEngine.GetHighlightLength("Снова споём дальше", enhanced.Lines[0].Words, 1000, 5000, 25)>=6,"timed word sweep");
Check(KaraokeHighlightEngine.GetHighlightLength("Снова споём дальше", enhanced.Lines[0].Words, 1000, 5000, 100)=="Снова споём дальше".Length,"final word sweep");
var emoji="Тест 👩‍🎤!";
int emojiCut=KaraokeHighlightEngine.GetHighlightLength(emoji,null,0,2000,60);
Check(emojiCut >= 0 && emojiCut <= emoji.Length && !(emojiCut>0&&char.IsHighSurrogate(emoji[emojiCut-1])),"unicode boundary");
Console.WriteLine("13 managed LRC/highlighting scenarios passed");
