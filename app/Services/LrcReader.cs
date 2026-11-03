using KaraokeStudio.Models;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace KaraokeStudio.Services;

public static partial class LrcReader
{
    [GeneratedRegex(@"\[([0-9]{1,3}):([0-9]{2})(?:\.([0-9]{1,3}))?\]")]
    private static partial Regex TimestampPattern();
    [GeneratedRegex(@"<([0-9]{1,3}):([0-9]{2})(?:\.([0-9]{1,3}))?>")]
    private static partial Regex WordTimestampPattern();
    [GeneratedRegex(@"^\[offset:([+-]?[0-9]+)\]$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetPattern();

    private static int ReadTime(Match m)
    {
        var mm=int.Parse(m.Groups[1].Value);
        var ss=int.Parse(m.Groups[2].Value);
        if (ss>59)return -1;
        var f=m.Groups[3].Value;
        return ((mm*60)+ss)*1000+(f.Length>0?int.Parse(f.PadRight(3,'0')):0);
    }
    private static LyricsDto ParseEnhanced(int time,string content)
    {
        var line=new LyricsDto{StartMs=time};
        var matches=WordTimestampPattern().Matches(content);
        if (matches.Count == 0){line.Text=content.Trim();return line;}
        var text=new StringBuilder();
        int cursor=0;int pendingTime=-1;
        foreach(Match m in matches)
        {
            int stamp=ReadTime(m);
            if(stamp<0)continue;
            string chunk=content[cursor..m.Index];
            text.Append(chunk);
            if(pendingTime>=0 && chunk.Length>0)
                line.Words.Add(new WordTimingDto{StartMs=pendingTime,EndMs=stamp,Text=chunk});
            else if(pendingTime<0 && !string.IsNullOrWhiteSpace(chunk))
                line.Words.Clear(); // un-timed prefix cannot be aligned
            pendingTime=stamp;
            cursor=m.Index+m.Length;
        }
        var tail=content[cursor..];text.Append(tail);
        if(pendingTime>=0 && tail.Length>0)
            line.Words.Add(new WordTimingDto{StartMs=pendingTime,Text=tail});
        else if(pendingTime>=0 && tail.Length==0 && line.Words.Count>0)
            line.Words[^1].EndMs=pendingTime; // explicit ending stamp
        line.Text=text.ToString().Trim();
        if(line.Words.Count>0)
        {
            line.Words[0].Text=line.Words[0].Text.TrimStart();
            line.Words[^1].Text=line.Words[^1].Text.TrimEnd();
            if(line.Words.Count>256 || line.Words[0].StartMs<time ||
               string.Concat(line.Words.Select(w=>w.Text))!=line.Text)
                line.Words.Clear();
        }
        return line;
    }

    public static (List<LyricsDto> Lines, bool Synced) Parse(string content, int durationMs = 0)
    {
        var timed = new List<LyricsDto>(); var plain = new List<LyricsDto>();
        int offset=0;
        foreach(var row in content.Split('\n'))
        {
            var line=row.Trim().TrimStart('\uFEFF');
            if(line.Length==0)continue;
            var offsetMatch=OffsetPattern().Match(line);
            if(offsetMatch.Success)
            {
                if(int.TryParse(offsetMatch.Groups[1].Value,out int parsed))offset=Math.Clamp(parsed,-600000,600000);
                continue;
            }
            var prefix=TimestampPattern().Matches(line);
            var times=new List<int>();int cursor=0;
            foreach(Match m in prefix)
            {
                if(m.Index!=cursor)break;
                var t=ReadTime(m);
                if(t<0){times.Clear();break;}
                times.Add(t);cursor+=m.Length;
            }
            if(times.Count>0)
                foreach(var t in times)timed.Add(ParseEnhanced(t,line[cursor..].Trim()));
            else if(!line.StartsWith('['))plain.Add(new LyricsDto{Text=line});
        }
        if(timed.Count==0)return (plain,false);
        foreach(var line in timed)
        {
            line.StartMs=(int)Math.Clamp((long)line.StartMs+offset,0,86400000);
            foreach(var w in line.Words)
            {
                w.StartMs=(int)Math.Clamp((long)w.StartMs+offset,0,86400000);
                if(w.EndMs>0)w.EndMs=(int)Math.Clamp((long)w.EndMs+offset,0,86400000);
            }
        }
        timed=timed.OrderBy(l=>l.StartMs).ToList();
        var merged=new List<LyricsDto>();
        foreach(var line in timed)
        {
            if(merged.Count>0 && merged[^1].StartMs==line.StartMs)
            {
                merged[^1].Text+=(merged[^1].Text.Length>0?"\n":"")+line.Text;
                merged[^1].Words.Clear(); // bilingual simultaneous phrases: ambiguous word timing
            }
            else merged.Add(line);
        }
        for(int i=0;i<merged.Count;i++)
        {
            var line=merged[i];
            int wordEnd=line.Words.Count>0?line.Words[^1].EndMs:0;
            line.EndMs=i+1<merged.Count?Math.Max(line.StartMs+1,merged[i+1].StartMs):Math.Max(Math.Max(line.StartMs+4000,durationMs),wordEnd);
            for(int j=0;j<line.Words.Count;j++)
            {
                var w=line.Words[j];
                w.StartMs=Math.Clamp(w.StartMs,line.StartMs,line.EndMs);
                int next=j+1<line.Words.Count?line.Words[j+1].StartMs:line.EndMs;
                w.EndMs=Math.Clamp(w.EndMs>w.StartMs?w.EndMs:next,w.StartMs+1,Math.Max(w.StartMs+1,line.EndMs));
            }
        }
        return (merged,true);
    }

    public static (List<LyricsDto> Lines, bool Synced) Read(string path, int durationMs)
    {
        if (new FileInfo(path).Length > 8_000_000) throw new InvalidDataException("LRC is larger than 8 MB.");
        byte[] raw = File.ReadAllBytes(path);
        string content;
        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
            content = Encoding.Unicode.GetString(raw, 2, raw.Length - 2);
        else if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
            content = Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2);
        else
        {
            int start = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF ? 3 : 0;
            try { content = new UTF8Encoding(false, true).GetString(raw, start, raw.Length - start); }
            catch (DecoderFallbackException)
            {
                // Match native plugin's system ANSI fallback for legacy files.
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                content = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(raw);
            }
        }
        return Parse(content, durationMs);
    }
}
