using Brush = System.Windows.Media.Brush;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace KaraokeStudio.Models;

public sealed class LyricLineViewModel : INotifyPropertyChanged
{
    public int StartMs { get; init; }
    public int EndMs { get; init; }
    public string Text { get; init; } = "";

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
    public double Progress { get => _progress; set { if (Math.Abs(_progress-value)>0.1) { _progress = value; Changed(); } } }
    private Thickness _padding = new(8,12,8,12);
    public Thickness Padding { get => _padding; set { if (_padding != value) { _padding = value; Changed(); } } }
    private double _lineHeight = 60;
    public double LineHeight { get => _lineHeight; set { if (Math.Abs(_lineHeight-value)>0.05) { _lineHeight=value; Changed(); } } }


    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
