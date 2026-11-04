using KaraokeStudio.Models;
using System;
using System.ComponentModel;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        var lyric = new LyricLineViewModel {
            StartMs = 1000, EndMs = 11000,
            Text = "We keep moving toward the midnight sun 👩‍🎤"
        };
        int visualChanges=0, progressChanges=0;
        lyric.PropertyChanged += (_, e) => {
            if (e.PropertyName is nameof(lyric.HighlightedText) or nameof(lyric.RemainingText))
                visualChanges++;
            if (e.PropertyName == nameof(lyric.Progress)) progressChanges++;
        };
        lyric.KaraokeFillEnabled = true;
        int width=lyric.Text.Length;
        for(int i=0;i<30000;i++) lyric.Progress = i/300.0;
        if (visualChanges > width*2+4 || visualChanges==0)
            throw new Exception($"Visual invalidations {visualChanges} exceeded 2*graphemes");
        if (progressChanges > 0)
            throw new Exception("Unbound Progress raised redundant WPF PropertyChanged");
        if (lyric.HighlightedText+lyric.RemainingText!=lyric.Text)
            throw new Exception("Karaoke text changed on slice rendering");
        lyric.KaraokeFillEnabled = false;
        if(lyric.HighlightedText!=""||lyric.RemainingText!=lyric.Text)
            throw new Exception("Disabled karaoke left highlighted fragments");
        Console.WriteLine($"WPF ViewModel PASS: 30,000 frame ticks, only {visualChanges} " +
                          $"text property updates; no progress invalidations");
        return 0;
    }
}
