using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using System.ComponentModel;
using KaraokeStudio.Services;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace KaraokeStudio.Models;

public sealed class LyricLineViewModel : INotifyPropertyChanged
{
    public int StartMs { get; init; }
    public int EndMs { get; init; }
    private string _text = "";
    public string Text
    {
        get => _text;
        init { _text = value ?? ""; _remainingText = _text; }
    }
    public IReadOnlyList<WordTimingDto> Words { get; init; } = [];

    private Brush _highlightForeground = Brushes.White;
    public Brush HighlightForeground { get => _highlightForeground; set { if (_highlightForeground != value) { _highlightForeground = value; Changed(); } } }
    private string _highlightedText = "";
    public string HighlightedText { get => _highlightedText; private set { if (_highlightedText != value) { _highlightedText = value; Changed(); } } }
    private string _remainingText = "";
    public string RemainingText { get => _remainingText; private set { if (_remainingText != value) { _remainingText = value; Changed(); } } }
    private bool _karaokeFillEnabled;
    public bool KaraokeFillEnabled { get => _karaokeFillEnabled; set { if (_karaokeFillEnabled != value) { _karaokeFillEnabled = value; Changed(); UpdateKaraokeSlices(); } } }
    private Visibility _nextCueVisibility = Visibility.Collapsed;
    public Visibility NextCueVisibility { get => _nextCueVisibility; set { if (_nextCueVisibility != value) { _nextCueVisibility = value; Changed(); } } }
    public bool HasPrecision => Words.Count > 0;


    private Brush _foreground = Brushes.Gray;
    public Brush Foreground { get => _foreground; set { if (_foreground != value) { _foreground = value; Changed(); } } }
    private double _opacity = 0.5;
    public double Opacity { get => _opacity; set { if (_opacity != value) { _opacity = value; Changed(); } } }
    private double _fontSize = 42;
    public double FontSize { get => _fontSize; set { if (_fontSize != value) { _fontSize = value; Changed(); } } }
    private FontWeight _weight = FontWeights.Normal;
    public FontWeight Weight { get => _weight; set { if (_weight != value) { _weight = value; Changed(); } } }
    private Visibility _visibility = Visibility.Visible;
    public Visibility Visibility { get => _visibility; set { if (_visibility != value) { _visibility = value; Changed(); } } }
    private Visibility _progressVisibility = Visibility.Collapsed;
    public Visibility ProgressVisibility { get => _progressVisibility; set { if (_progressVisibility != value) { _progressVisibility = value; Changed(); } } }
    private double _progress;
    private KaraokeHighlightEngine.PreparedLine? _preparedHighlight;
    private int _lastHighlightCut = -1;
    public double Progress { get => _progress; set { if (Math.Abs(_progress-value)>0.1) { _progress = value; UpdateKaraokeSlices(); } } }
    private Thickness _padding = new(8,12,8,12);
    public Thickness Padding { get => _padding; set { if (_padding != value) { _padding = value; Changed(); } } }
    private double _lineHeight = 60;
    public double LineHeight { get => _lineHeight; set { if (Math.Abs(_lineHeight-value)>0.05) { _lineHeight=value; Changed(); } } }

    // Recompute slices only when the highlighted grapheme boundary advances.
    // Hundreds of timer ticks can occur between two visual character changes.
    private void UpdateKaraokeSlices()
    {
        if (!KaraokeFillEnabled || Text.Length == 0)
        {
            if (_lastHighlightCut == 0) return;
            _lastHighlightCut = 0;
            HighlightedText = "";
            RemainingText = Text;
            return;
        }
        _preparedHighlight ??= KaraokeHighlightEngine.Prepare(Text, Words);
        int cut = _preparedHighlight.GetHighlightLength(StartMs, EndMs, Progress);
        if (_lastHighlightCut == cut) return;
        _lastHighlightCut = cut;
        HighlightedText = cut == 0 ? "" : Text[..cut];
        RemainingText = cut == Text.Length ? "" : Text[cut..];
    }

    // The translation is keyed to this original lyric line, so both columns
    // remain time-aligned even when Russian text wraps to more visual lines.
    private string _translationText = "";
    public string TranslationText
    {
        get => _translationText;
        set { if (_translationText != value) { _translationText = value; Changed(); } }
    }
    private Visibility _translationVisibility = Visibility.Collapsed;
    public Visibility TranslationVisibility
    {
        get => _translationVisibility;
        set { if (_translationVisibility != value) { _translationVisibility = value; Changed(); } }
    }
    private double _translationPaneWidth;
    public double TranslationPaneWidth
    {
        get => _translationPaneWidth;
        set { if (Math.Abs(_translationPaneWidth - value) > 0.5) { _translationPaneWidth = value; Changed(); } }
    }
    private double _translationFontSize = 35;
    public double TranslationFontSize
    {
        get => _translationFontSize;
        set { if (Math.Abs(_translationFontSize - value) > 0.05) { _translationFontSize = value; Changed(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
