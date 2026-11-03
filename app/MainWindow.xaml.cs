using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using Button = System.Windows.Controls.Button;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MessageBox = System.Windows.MessageBox;
using Brush = System.Windows.Media.Brush;
using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using KaraokeStudio.Models;
using KaraokeStudio.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace KaraokeStudio;

/// <summary>Independent WPF renderer; AIMP SDK interaction lives exclusively in the native bridge.</summary>
public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] Accents = ["#8A7BB7", "#6C99A6", "#90A593", "#C0ADA0"];
    private static readonly string[] Highlights = ["#AC9ADC", "#8FC0CB", "#B4C8B7", "#E2CDBB"];
    private static readonly Brush QuietBrush = Solid("#9997A0");
    private static readonly Brush NearBrush = Solid("#C0BDC8");
    private readonly ObservableCollection<LyricLineViewModel> _lines = [];
    private readonly PlaybackClock _clock = new();
    private readonly TranslationService _translationService = new();
    private CancellationTokenSource? _translationCancel;
    private int _translationSession;
    private bool _translationEnabled;
    private readonly DispatcherTimer _frames = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly PipeClient? _pipe;
    private readonly StudioSettings _settings = SettingsStore.Load();
    private readonly bool _demo;
    private int _generation;
    private ScrollViewer? _scrollViewer;
    private VirtualizingStackPanel? _lyricsPanel;
    private double FocusAnchor => _mode == "Stage" ? 0.47 : _mode == "Karaoke" ? 0.44 : 0.46;
    private bool _followLyrics = true;
    private CancellationTokenSource? _artworkCancel;
    private bool _embeddedArtwork;
    private Rect _restoreBounds;
    private WindowStyle _restoreStyle;
    private int _durationMs;
    private int _currentIndex = -2;
    private int _lastScrollIndex;
    private bool _synced;
    private bool _fullscreen;
    private bool _settingsOpen;
    private bool _settingsBeforeFullscreen;
    private bool _manualOverride;
    private bool _ready;
    private bool _closing;
    private double _targetScroll;
    private int _lastUiTime = -1;
    private string _mode;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;

    public MainWindow(string? pipeName)
    {
        InitializeComponent();
        _mode = _settings.Mode is "Reading" or "Karaoke" or "Stage" ? _settings.Mode : "Reading";
        _translationEnabled = _settings.TranslationEnabled;
        _settings.FontSize = Math.Clamp(_settings.FontSize, 24, 100);
        _settings.LineSpacing = Math.Clamp((_settings.LineSpacing < 1.05 || _settings.LineSpacing > 2.1) ? 1.48 : _settings.LineSpacing, 1.1, 2.0);
        _settings.NeighborContrast = Math.Clamp(_settings.NeighborContrast, 15, 95);
        _settings.AmbientPercent = Math.Clamp(_settings.AmbientPercent, 0, 40);
        _settings.VerseGap = Math.Clamp(_settings.VerseGap, 0, 48);
        _settings.PlainTextScale = Math.Clamp(_settings.PlainTextScale, 0.8, 1.4);
        _settings.OffsetMs = Math.Clamp(_settings.OffsetMs, -2000, 2000);
        if (!Accents.Contains(_settings.AccentColor, StringComparer.OrdinalIgnoreCase)) _settings.AccentColor = Accents[0];
        Topmost = _settings.AlwaysOnTop;
        if (_settings.WindowWidth is >= 710 and <= 9000) Width = _settings.WindowWidth;
        if (_settings.WindowHeight is >= 480 and <= 9000) Height = _settings.WindowHeight;
        if (_settings.WindowLeft is double left && _settings.WindowTop is double top &&
            double.IsFinite(left) && double.IsFinite(top))
        {
            // Restore only when a useful portion remains visible on a screen.
            var target = new Rect(left, top, Width, Height);
            if (System.Windows.Forms.Screen.AllScreens.Any(m =>
                Rect.Intersect(target, new Rect(m.WorkingArea.X, m.WorkingArea.Y, m.WorkingArea.Width, m.WorkingArea.Height)).Width >= 120 &&
                Rect.Intersect(target, new Rect(m.WorkingArea.X, m.WorkingArea.Y, m.WorkingArea.Width, m.WorkingArea.Height)).Height >= 80))
            {
                Left = left;
                Top = top;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }
        LyricsItems.ItemsSource = _lines;
        TranslationSourceCombo.SelectedItem = TranslationSourceCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(x => (x.Tag as string) == _settings.TranslationSourceLanguage)
            ?? TranslationSourceCombo.Items[0];
        _demo = string.IsNullOrWhiteSpace(pipeName);
        if (!_demo) Title = "AIMP Karaoke Studio - " + pipeName;
        if (!_demo)
        {
            _pipe = new PipeClient(pipeName!);
            _pipe.Message += text =>
            {
                if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => ReceiveMessage(text)));
            };
            _pipe.ConnectionChanged += connected =>
            {
                if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => SetConnection(connected)));
            };
        }
        _frames.Tick += Frame;
        FontSlider.Value = _settings.FontSize;
        SpacingSlider.Value = _settings.LineSpacing;
        ContrastSlider.Value = _settings.NeighborContrast;
        OffsetSlider.Value = _settings.OffsetMs;
        AmbienceSlider.Value = _settings.AmbientPercent;
        SmoothCheck.IsChecked = _settings.SmoothScroll;
        ProgressCheck.IsChecked = _settings.ShowProgress;
        TopCheck.IsChecked = _settings.AlwaysOnTop;
        _ready = true;
        ApplyAccent();
        UpdateSettingsValues();
        ApplyLineStyles();
        UpdateTranslationLayout();
    }

    private static SolidColorBrush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Initial WPF size must fit the active desktop working area even on
        // 1024px or smaller displays. The authored 1194x760 is only a default.
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var tl = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var br = transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        Width = Math.Min(Width, Math.Max(MinWidth, br.X - tl.X - 20));
        Height = Math.Min(Height, Math.Max(MinHeight, br.Y - tl.Y - 20));
        Left = Math.Max(tl.X, Math.Min(Left, br.X - Width));
        Top = Math.Max(tl.Y, Math.Min(Top, br.Y - Height));
        _scrollViewer = FindChild<ScrollViewer>(LyricsItems);
        UpdateViewportPadding();
        UpdateTranslationLayout();
        if (_demo) StartDemo(); else _pipe?.Start();
        _frames.Start();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _frames.Stop();
        _pipe?.Dispose();
        _artworkCancel?.Cancel();
        _translationCancel?.Cancel();
        if (!_fullscreen && WindowState == WindowState.Normal)
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
        _settings.Mode = _mode;
        SettingsStore.Save(_settings);
    }

    private static string Stamp(int ms)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
    }

    private void SetConnection(bool connected)
    {
        if (_closing) return;
        StatusText.Text = connected ? "Подключено к AIMP" : "Переподключение…";
        StatusDot.Fill = Solid(connected ? "#7D9C83" : "#967F72");
    }

    private void StartDemo()
    {
        TrackTitle.Text = "Independent lyrics window";
        TrackArtist.Text = "Karaoke Studio · демонстрация";
        StatusText.Text = "DEMO · без подключения к AIMP";
        StatusDot.Fill = Solid("#8A7BB7");
        _ = SetArtworkAsync(null, true);
        _generation = 1;
        _durationMs = 63000;
        const string lyrics = """
            [ar:Karaoke Studio]
            [ti:Independent lyrics window]
            [00:00.00]A quiet breath before the sound
            [00:06.00]The night unfolds beyond the glass
            [00:12.00]We watch the fading city lights
            [00:18.00]And let another moment pass
            [00:24.00]There is no need to rush the dawn
            [00:30.00]The rhythm leads us through the dark
            [00:36.00]A single voice becomes a wave
            [00:42.00]And every pulse becomes a spark
            [00:49.00]The silence slowly comes around
            [00:55.00]And leaves us standing with the sound
            """;
        var (lines, synced) = LrcReader.Parse(lyrics, _durationMs);
        SetLyrics(lines, synced, "Демонстрационный LRC");
        _clock.Seek(29000);
        _clock.SetPlaying(false);
        PlayButton.Content = "▶";
    }

    private void ReceiveMessage(string line)
    {
        if (_closing) return;
        PipeMessage? message;
        try { message = JsonSerializer.Deserialize<PipeMessage>(line, Json); }
        catch (JsonException) { return; }
        if (message == null) return;
        switch (message.Type)
        {
            case "track":
                if (message.Generation < _generation) break;
                _generation = message.Generation;
                _manualOverride = false;
                TrackTitle.Text = string.IsNullOrWhiteSpace(message.Title) ? "Неизвестная композиция" : message.Title;
                TrackArtist.Text = string.IsNullOrWhiteSpace(message.Album) ? message.Artist : $"{message.Artist} · {message.Album}";
                _durationMs = Math.Max(0, message.DurationMs);
                CancelTranslation();
                _lines.Clear(); _synced = false; _currentIndex = -2;
                if (_translationEnabled) TranslationStatus.Text = "Ожидаем текст песни";
                _lastScrollIndex = 0;
                _clock.Seek(0);
                CoverImage.Source = null;
                AmbientImage.Source = null;
                CoverPlaceholder.Visibility = Visibility.Visible;
                _embeddedArtwork = false;
                _ = SetArtworkAsync(message.FilePath, false);
                EmptyPanel.Visibility = Visibility.Visible;
                EmptyHeadline.Text = "Поиск текста…";
                EmptyDescription.Text = "Проверяем тексты AIMP и локальные LRC-файлы.";
                SourceText.Text = "Загрузка текста";
                SyncIndicator.Text = "ОЖИДАНИЕ ТЕКСТА";
                break;
            case "lyrics":
                if (message.Generation != _generation || _manualOverride) break;
                if (message.Source == "loading") break;
                if (!string.IsNullOrEmpty(message.Error))
                {
                    EmptyHeadline.Text = "Ошибка загрузки текста";
                    EmptyDescription.Text = message.Error;
                    EmptyPanel.Visibility = Visibility.Visible;
                    break;
                }
                SetLyrics(message.Lines, message.Synced, message.Source);
                break;
            case "artwork":
                if (message.Generation != _generation || string.IsNullOrWhiteSpace(message.ArtworkData)) break;
                try
                {
                    var bytes = Convert.FromBase64String(message.ArtworkData);
                    if (bytes.Length < 8 || bytes.Length > 450_000) break;
                    using var stream = new MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = stream; bmp.DecodePixelWidth = 650; bmp.EndInit(); bmp.Freeze();
                    _embeddedArtwork = true;
                    _artworkCancel?.Cancel();
                    CoverImage.Source = bmp; AmbientImage.Source = bmp;
                    CoverPlaceholder.Visibility = Visibility.Collapsed;
                    UpdateSettingsValues();
                }
                catch (Exception error) { StudioLog.Error("Embedded artwork", error); }
                break;
            case "position":
                if (message.Generation != _generation) break;
                _clock.SetPosition(Math.Max(0, message.PositionMs));
                break;
            case "state":
                if (message.Generation != _generation) break;
                _clock.SetPlaying(message.State == 2);
                PlayButton.Content = message.State == 2 ? "Ⅱ" : "▶";
                break;
            case "commandResult":
                if (!message.Ok)
                {
                    StatusText.Text = "AIMP отклонил команду";
                    StudioLog.Error("AIMP command " + message.Command,
                        new InvalidOperationException("AIMP SDK returned error: " + message.Result));
                }
                else
                {
                    if (message.Command == "seek")
                    {
                        _clock.SetPosition(Math.Max(0, message.PositionMs));
                        _lastUiTime = -1;
                    }
                    // Player state changes asynchronously in AIMP. A command
                    // acknowledgement may contain the OLD state immediately
                    // after Pause/Resume; only the "state" event updates Play.
                }
                break;
        }
    }

    private void SetLyrics(IEnumerable<LyricsDto> items, bool synced, string source)
    {
        _lines.Clear();
        _lastScrollIndex = 0;
        _followLyrics = true;
        ResumeFollowButton.Visibility = Visibility.Collapsed;
        foreach (var item in items.OrderBy(x => x.StartMs).Take(5000))
            _lines.Add(new LyricLineViewModel { StartMs = Math.Max(0, item.StartMs),
                EndMs = Math.Max(0, item.EndMs), Text = item.Text ?? "", Words = item.Words ?? [] });
        _synced = synced && _lines.Count > 0;
        SourceText.Text = string.IsNullOrWhiteSpace(source) ? "AIMP Lyrics" : source.Replace('-', ' ');
        SyncIndicator.Text = _lines.Count == 0 ? "ТЕКСТ НЕ НАЙДЕН" :
            !_synced ? "ТЕКСТ БЕЗ ВРЕМЕННЫХ МЕТОК" :
            _lines.Any(l => l.HasPrecision) ? "ПОСЛОВНАЯ СИНХРОНИЗАЦИЯ · ENHANCED LRC" :
            "ПОСТРОЧНЫЙ LRC · ПРИБЛИЖЁННАЯ ПОДСВЕТКА";
        EmptyPanel.Visibility = _lines.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyHeadline.Text = "Текст песни не найден";
        EmptyDescription.Text = "Откройте LRC кнопкой внизу или добавьте файл с тем же названием, что и аудиотрек.";
        _currentIndex = -2;
        ApplyLineStyles();
        UpdateTranslationLayout();
        BeginTranslation();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            UpdateViewportPadding();
            UpdateTargetScroll();
        }));
    }

    private void Frame(object? sender, EventArgs e)
    {
        if (_demo && _durationMs > 0 && _clock.PositionMs > _durationMs)
        {
            _clock.Seek(_durationMs);
            _clock.SetPlaying(false);
            PlayButton.Content = "▶";
        }
        int position = Math.Min(_durationMs > 0 ? _durationMs : int.MaxValue, _clock.PositionMs);
        if (_synced && _lines.Count > 0)
        {
            int index = FindCurrentLine(position + _settings.OffsetMs);
            if (index != _currentIndex) {
                var previous = _currentIndex;
                _currentIndex = index;
                ApplyLineStyles(previous);
            }
            if (_currentIndex >= 0 && _currentIndex < _lines.Count)
            {
                var line = _lines[_currentIndex];
                int end = line.EndMs > line.StartMs ? line.EndMs :
                    (_currentIndex + 1 < _lines.Count ? _lines[_currentIndex + 1].StartMs : line.StartMs + 4000);
                line.Progress = 100.0 * Math.Clamp((double)(position + _settings.OffsetMs - line.StartMs) /
                                  Math.Max(1, end - line.StartMs), 0, 1);
            }
            if (_followLyrics && _scrollViewer != null && Math.Abs(_targetScroll - _scrollViewer.VerticalOffset) > 0.4)
            {
                double remaining = _targetScroll - _scrollViewer.VerticalOffset;
                _scrollViewer.ScrollToVerticalOffset(_settings.SmoothScroll ?
                    _scrollViewer.VerticalOffset + remaining * 0.15 : _targetScroll);
            }
        }
        if (position / 250 != _lastUiTime)
        {
            _lastUiTime = position / 250;
            ElapsedText.Text = Stamp(position);
            DurationText.Text = Stamp(_durationMs);
            if (_durationMs > 0 && !SeekSlider.IsMouseCaptureWithin)
                SeekSlider.Value = (double)position / _durationMs * 1000;
        }
    }

    private int FindCurrentLine(int ms)
    {
        int lo = 0, hi = _lines.Count - 1, answer = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_lines[mid].StartMs <= ms) { answer = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        _lastScrollIndex = Math.Max(0, answer);
        if (answer < 0 || ms >= _lines[answer].EndMs) return -1;
        return answer;
    }

    private void ApplyLineStyles(int previous = -2)
    {
        if (!_ready) return;
        int focus = _currentIndex >= 0 ? _currentIndex : _lastScrollIndex;
        int count = _lines.Count;
        var r = Application.Current.Resources;
        Brush sung = (Brush)r["LyricsSungBrush"], activeColor = (Brush)r["LyricsActiveBrush"];
        Brush far = (Brush)r["LyricsFarBrush"];
        IEnumerable<int> affected = previous == -2 ? Enumerable.Range(0, count) :
            Enumerable.Range(Math.Max(0, previous - 3), Math.Min(count, Math.Max(0, previous + 4)) - Math.Max(0, previous - 3))
            .Concat(Enumerable.Range(Math.Max(0, _currentIndex - 3), Math.Min(count, Math.Max(0, _currentIndex + 4)) - Math.Max(0, _currentIndex - 3))).Distinct();
        foreach (int i in affected)
        {
            var item = _lines[i];
            int dist = Math.Abs(i - focus);
            bool active = _synced && i == _currentIndex;
            bool next = _synced && i == focus + 1;
            bool reading = _mode == "Reading";
            item.Visibility = reading || !_synced || (_mode == "Stage" ? active || next : dist <= 2)
                ? Visibility.Visible : Visibility.Collapsed;
            // Only the current phrase receives a larger font and accent color.
            // Every non-current phrase has the same size, weight and palette,
            // regardless of whether it is previous, next or farther away.
            item.NextCueVisibility = Visibility.Collapsed;
            item.HighlightForeground = sung;
            item.KaraokeFillEnabled = active && _settings.ShowProgress;
            if (active)
            {
                item.Foreground = activeColor;
                item.Opacity = 1.0;
                item.FontSize = _settings.FontSize * (_mode == "Stage" ? 1.18 : 1.10);
                item.Weight = FontWeights.SemiBold;
            }
            else
            {
                item.Foreground = far;
                item.Opacity = _mode == "Stage" ? 0.74 : 0.84;
                item.FontSize = _settings.FontSize * 0.70;
                item.Weight = FontWeights.Normal;
                item.KaraokeFillEnabled = false;
            }
            if (!_synced)
            {
                item.FontSize = _settings.FontSize * 0.70 * _settings.PlainTextScale;
                item.Opacity = 0.84;
            }
            // Russian and original phrases share a timeline and row height.
            item.TranslationFontSize = active ? item.FontSize * 0.94 : item.FontSize;
            item.LineHeight = Math.Max(item.FontSize * 1.14, item.FontSize * _settings.LineSpacing);
            item.Padding = new Thickness(8, _settings.VerseGap * 0.6, 8, _settings.VerseGap * 0.6);
            item.ProgressVisibility = Visibility.Collapsed; // no separate progress bar
            if (!active) item.Progress = 0;
        }
        UpdateModeButtons();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateTargetScroll));
    }

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (int i=0; i<VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindChild<T>(VisualTreeHelper.GetChild(root,i)) is { } child) return child;
        return null;
    }

    private void UpdateViewportPadding()
    {
        if (_closing) return;
        _scrollViewer ??= FindChild<ScrollViewer>(LyricsItems);
        _lyricsPanel ??= FindChild<VirtualizingStackPanel>(LyricsItems);
        if (_scrollViewer == null || _lyricsPanel == null || _scrollViewer.ViewportHeight <= 0) return;
        double height = _scrollViewer.ViewportHeight;
        var pad = new Thickness(0, Math.Max(24, height * FocusAnchor - _settings.FontSize * 0.6),
                                0, Math.Max(24, height * (1 - FocusAnchor) - _settings.FontSize * 0.6));
        if (_lyricsPanel.Margin != pad) _lyricsPanel.Margin = pad;
    }

    private void ApplyResponsiveSettingsLayout()
    {
        if (!_ready) return;
        bool overlay = ActualWidth < 900;
        if (_settingsOpen && overlay)
        {
            SettingsColumn.Width = new GridLength(0);
            Grid.SetColumn(SettingsDrawer, 0);
            Grid.SetColumnSpan(SettingsDrawer, 2);
            SettingsDrawer.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            SettingsDrawer.Width = Math.Min(298, Math.Max(265, ActualWidth - 22));
            System.Windows.Controls.Panel.SetZIndex(SettingsDrawer, 10);
        }
        else
        {
            Grid.SetColumn(SettingsDrawer, 1);
            Grid.SetColumnSpan(SettingsDrawer, 1);
            SettingsDrawer.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            SettingsDrawer.Width = double.NaN;
            SettingsColumn.Width = _settingsOpen ? new GridLength(298) : new GridLength(0);
        }
    }

    private void UpdateTargetScroll()
    {
        if (_closing || !_followLyrics || _lines.Count == 0) return;
        _scrollViewer ??= FindChild<ScrollViewer>(LyricsItems);
        if (_scrollViewer is null || _scrollViewer.ViewportHeight <= 0) return;
        var index = Math.Clamp(_currentIndex < 0 ? _lastScrollIndex : _currentIndex, 0, _lines.Count - 1);
        if (LyricsItems.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container)
        {
            LyricsItems.ScrollIntoView(_lines[index]); // Realize only target row.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!_closing && LyricsItems.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement)
                    UpdateTargetScroll();
            }));
            return;
        }
        try
        {
            var top = container.TranslatePoint(new Point(0, 0), _scrollViewer);
            var target = _scrollViewer.VerticalOffset + top.Y + container.ActualHeight/2 - _scrollViewer.ViewportHeight*FocusAnchor;
            _targetScroll = Math.Clamp(target, 0, _scrollViewer.ScrollableHeight);
        }
        catch (InvalidOperationException) { }
    }

    private void UpdateModeButtons()
    {
        if (!_ready) return;
        foreach (var (button, name) in new[] { (ReadingButton, "Reading"), (KaraokeButton, "Karaoke"), (StageButton, "Stage") })
        {
            button.Background = _mode == name ? Solid("#3B3151") : Brushes.Transparent;
            button.Foreground = _mode == name ? Solid("#F0EBFF") : Solid("#AAA5B7");
            button.BorderBrush = _mode == name ? Solid("#695991") : Brushes.Transparent;
        }
    }

    private void SetMode(string value)
    {
        _mode = value;
        _settings.Mode = value;
        ApplyLineStyles();
        UpdateViewportPadding();
    }

    private void UpdateSettingsValues()
    {
        if (!_ready) return;
        FontValue.Text = $"{_settings.FontSize:0} px";
        SpacingValue.Text = $"{_settings.LineSpacing:0.00}";
        ContrastValue.Text = $"{_settings.NeighborContrast:0}%";
        OffsetValue.Text = $"{_settings.OffsetMs:+0;-0;+0} мс";
        AmbienceValue.Text = $"{_settings.AmbientPercent:0}%";
        AmbientImage.Opacity = CoverImage.Source == null ? 0 : _settings.AmbientPercent / 100.0;
    }

    private void ApplyAccent()
    {
        int id = Array.FindIndex(Accents, x => x.Equals(_settings.AccentColor, StringComparison.OrdinalIgnoreCase));
        if (id < 0) id = 0;
        Application.Current.Resources["AccentBrush"] = Solid(Accents[id]);
        Application.Current.Resources["HighlightBrush"] = Solid(Highlights[id]);
        foreach (var b in new[] { AccentAimp, AccentBlue, AccentSage, AccentWarm })
            b.BorderBrush = (string)b.Tag == Accents[id] ? Solid("#EAE5F6") : Brushes.Transparent;
        ApplyLineStyles();
    }

    private async Task SetArtworkAsync(string? audioFilePath, bool demo)
    {
        _artworkCancel?.Cancel();
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        _artworkCancel = cts;
        try
        {
            BitmapSource? image;
            if (demo)
            {
                image = new BitmapImage(new Uri("pack://application:,,,/Assets/demo-cover.png"));
                image.Freeze();
            }
            else
            {
                image = await Task.Run(() => LoadSidecarArtwork(audioFilePath, token), token);
            }
            if (token.IsCancellationRequested || _closing || _embeddedArtwork) return;
            CoverImage.Source = image;
            AmbientImage.Source = image;
            CoverPlaceholder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
            UpdateSettingsValues();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { StudioLog.Error("Artwork", error); }
        finally { cts.Dispose(); }
    }
    private static BitmapSource? LoadSidecarArtwork(string? audioFilePath, CancellationToken cancel)
    {
        if (string.IsNullOrEmpty(audioFilePath) || !Path.IsPathFullyQualified(audioFilePath)) return null;
        // No UNC/network reads from track changes.
        if (audioFilePath.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        var folder = Path.GetDirectoryName(audioFilePath);
        if (folder == null) return null;
        string[] candidates = [Path.ChangeExtension(audioFilePath, ".jpg"), Path.Combine(folder, "cover.jpg"),
            Path.Combine(folder, "folder.jpg"), Path.Combine(folder, "front.jpg"), Path.Combine(folder,"cover.png"), Path.Combine(folder,"folder.png")];
        foreach (var candidate in candidates)
        {
            cancel.ThrowIfCancellationRequested();
            if (!File.Exists(candidate) || new FileInfo(candidate).Length > 15_000_000) continue;
            using var input = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.StreamSource = input; bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 650; bitmap.EndInit(); bitmap.Freeze();
            return bitmap;
        }
        return null;
    }

    // One shared virtualized row per timestamp; translations never lose
    // alignment if they occupy more lines than the original text.
    private void UpdateTranslationLayout()
    {
        if (!_ready) return;
        TranslationHeader.Visibility = _translationEnabled ? Visibility.Visible : Visibility.Collapsed;
        TranslationDivider.Visibility = _translationEnabled ? Visibility.Visible : Visibility.Collapsed;
        SyncIndicator.Visibility = _translationEnabled ? Visibility.Collapsed : Visibility.Visible;
        TranslationButton.Content = _translationEnabled ? "RU · Закрыть" : "RU · Перевод";
        TranslationButton.Background = _translationEnabled ? Solid("#3B3151") : Solid("#1D1B21");
        TranslationButton.BorderBrush = _translationEnabled ? Solid("#7A659C") : Solid("#34313B");
        double width = LyricsItems.ActualWidth > 0 ? LyricsItems.ActualWidth : Math.Max(400, ActualWidth - 48);
        // Take half the available lyric canvas; leave room for both halves to wrap.
        double paneWidth = Math.Max(140, (width - 24) * 0.50);
        foreach (var line in _lines)
        {
            line.TranslationVisibility = _translationEnabled ? Visibility.Visible : Visibility.Collapsed;
            line.TranslationPaneWidth = _translationEnabled ? paneWidth : 0;
        }
    }

    private void CancelTranslation()
    {
        ++_translationSession;
        _translationCancel?.Cancel();
        _translationCancel = null;
    }

    private void SetTranslationVisible(bool visible)
    {
        if (_translationEnabled == visible) return;
        _translationEnabled = visible;
        _settings.TranslationEnabled = visible;
        UpdateTranslationLayout();
        ApplyLineStyles();
        if (visible) BeginTranslation();
        else CancelTranslation();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(() => { UpdateViewportPadding(); UpdateTargetScroll(); }));
    }

    private void Translation_Click(object sender, RoutedEventArgs e)
        => SetTranslationVisible(!_translationEnabled);

    private void TranslationRetry_Click(object sender, RoutedEventArgs e)
    {
        if (!_translationEnabled) SetTranslationVisible(true);
        else BeginTranslation();
    }

    private void TranslationSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || TranslationSourceCombo.SelectedItem is not ComboBoxItem selected
            || selected.Tag is not string code) return;
        if (_settings.TranslationSourceLanguage == code) return;
        _settings.TranslationSourceLanguage = code;
        if (_translationEnabled) BeginTranslation();
    }

    private void BeginTranslation()
    {
        CancelTranslation();
        if (!_translationEnabled || _closing) return;
        if (_lines.Count == 0)
        {
            TranslationStatus.Text = "Ожидаем текст песни";
            return;
        }
        var tokenSource = new CancellationTokenSource();
        _translationCancel = tokenSource;
        int session = _translationSession;
        string[] lines = _lines.Select(x => x.Text).ToArray();
        foreach (var line in _lines)
            line.TranslationText = string.IsNullOrWhiteSpace(line.Text) ? "" : "…";
        TranslationStatus.Text = "Переводим строки…";
        int focus = Math.Max(0, _currentIndex);
        _ = TranslateCurrentTrackAsync(lines, _settings.TranslationSourceLanguage, focus, session, tokenSource.Token);
    }

    private async Task TranslateCurrentTrackAsync(string[] lyrics, string language,
        int focus, int session, CancellationToken cancellation)
    {
        bool Current() => !cancellation.IsCancellationRequested && !_closing &&
                          _translationEnabled && session == _translationSession;
        try
        {
            await _translationService.TranslateLinesAsync(
                lyrics, language, focus,
                (index, translated, cached) =>
                {
                    if (!Current() || index >= _lines.Count) return;
                    _lines[index].TranslationText = translated;
                    if (Math.Abs(index - _currentIndex) <= 2)
                        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateTargetScroll));
                },
                (done, total) =>
                {
                    if (Current()) TranslationStatus.Text = $"Переведено {done}/{total}";
                },
                cancellation);
            if (Current()) TranslationStatus.Text = "Перевод завершён · MyMemory";
        }
        catch (OperationCanceledException) { }
        catch (TranslationException error)
        {
            if (Current()) TranslationStatus.Text = error.Message + " · Повтор: Настройки";
            StudioLog.Error("Translation", error);
        }
        catch (Exception error)
        {
            if (Current()) TranslationStatus.Text = "Ошибка перевода. Повторите в настройках.";
            StudioLog.Error("Translation", error);
        }
    }

    private void ToggleSettings(bool? show = null)
    {
        if (_fullscreen) return;
        _settingsOpen = show ?? !_settingsOpen;
        SettingsDrawer.Visibility = _settingsOpen ? Visibility.Visible : Visibility.Collapsed;
        ApplyResponsiveSettingsLayout();
        SettingsButton.Background = _settingsOpen ? Solid("#342B44") : Solid("#1D1B21");
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { UpdateTranslationLayout(); UpdateTargetScroll(); }));
    }

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            _restoreStyle = WindowStyle;
            _stateBeforeFullscreen = WindowState;
            _settingsBeforeFullscreen = _settingsOpen;
            ToggleSettings(false);
            _fullscreen = true;
            HeaderRow.Height = new GridLength(0); ToolbarRow.Height = new GridLength(0); FooterRow.Height = new GridLength(0);
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize; WindowStyle = WindowStyle.None;
            // WPF coordinates are DIPs; Screen.Bounds is in physical pixels.
            var window = new WindowInteropHelper(this);
            var monitor = System.Windows.Forms.Screen.FromHandle(window.Handle);
            var source = PresentationSource.FromVisual(this);
            var dpi = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var bounds = monitor.Bounds;
            var a = dpi.Transform(new Point(bounds.Left,bounds.Top));
            var b = dpi.Transform(new Point(bounds.Right,bounds.Bottom));
            Left = a.X; Top = a.Y; Width = b.X-a.X; Height = b.Y-a.Y;
            Topmost = true;
        }
        else
        {
            _fullscreen = false;
            HeaderRow.Height = new GridLength(38); ToolbarRow.Height = new GridLength(62); FooterRow.Height = new GridLength(60);
            WindowState = WindowState.Normal;
            WindowStyle = _restoreStyle;
            ResizeMode = ResizeMode.CanResize;
            Left = _restoreBounds.Left; Top = _restoreBounds.Top;
            Width = _restoreBounds.Width; Height = _restoreBounds.Height;
            WindowState = _stateBeforeFullscreen;
            Topmost = _settings.AlwaysOnTop;
            if (_settingsBeforeFullscreen) ToggleSettings(true);
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { UpdateTranslationLayout(); UpdateTargetScroll(); }));
    }

    private void AdjustFont(int delta)
    {
        _settings.FontSize = Math.Clamp(_settings.FontSize + delta, 24, 100);
        FontSlider.Value = _settings.FontSize;
        UpdateSettingsValues();
        ApplyLineStyles();
        UpdateViewportPadding();
    }

    private void AdjustOffset(int delta)
    {
        _settings.OffsetMs = Math.Clamp(_settings.OffsetMs + delta, -2000, 2000);
        OffsetSlider.Value = _settings.OffsetMs;
        UpdateSettingsValues();
        _currentIndex = -2;
    }

    private async Task Send(string cmd)
    {
        if (_pipe != null) await _pipe.SendAsync("{\"command\":\"" + cmd + "\"}");
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { _clock.SetPlaying(!_clock.Playing); PlayButton.Content = _clock.Playing ? "Ⅱ" : "▶"; }
        else _ = Send("toggle");
    }
    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) _clock.Seek(0); else _ = Send("previous");
    }
    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) _clock.Seek(0); else _ = Send("next");
    }
    private void Reading_Click(object sender, RoutedEventArgs e) => SetMode("Reading");
    private void Karaoke_Click(object sender, RoutedEventArgs e) => SetMode("Karaoke");
    private void Stage_Click(object sender, RoutedEventArgs e) => SetMode("Stage");
    private void Settings_Click(object sender, RoutedEventArgs e) => ToggleSettings();
    private void FontMinus_Click(object sender, RoutedEventArgs e) => AdjustFont(-2);
    private void FontPlus_Click(object sender, RoutedEventArgs e) => AdjustFont(2);
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            for (DependencyObject? current = source; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is Button) return;
                if (ReferenceEquals(current, sender)) break;
            }
        }
        if (e.ClickCount == 2) Maximize_Click(sender, new RoutedEventArgs());
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void SettingSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _settings.FontSize = Math.Round(FontSlider.Value / 2) * 2;
        _settings.LineSpacing = SpacingSlider.Value;
        _settings.NeighborContrast = Math.Round(ContrastSlider.Value);
        _settings.OffsetMs = (int)(Math.Round(OffsetSlider.Value / 50) * 50);
        _settings.AmbientPercent = Math.Round(AmbienceSlider.Value);
        UpdateSettingsValues();
        ApplyLineStyles();
        UpdateViewportPadding();
    }
    private void SettingCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.SmoothScroll = SmoothCheck.IsChecked == true;
        _settings.ShowProgress = ProgressCheck.IsChecked == true;
        _settings.AlwaysOnTop = TopCheck.IsChecked == true;
        Topmost = _settings.AlwaysOnTop;
        ApplyLineStyles();
    }
    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string hex)
        {
            _settings.AccentColor = hex;
            ApplyAccent();
        }
    }

    private void SeekTo(int ms)
    {
        int position = Math.Clamp(ms, 0, _durationMs > 0 ? _durationMs : 24 * 60 * 60 * 1000);
        _clock.Seek(position);
        _followLyrics = true;
        ResumeFollowButton.Visibility = Visibility.Collapsed;
        if (!_demo && _pipe != null) _ = _pipe.SendAsync("{\"command\":\"seek\",\"positionMs\":" + position + "}");
        _currentIndex = -2;
    }
    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_durationMs <= 0) return;
        // PreviewMouseLeftButtonUp fires before the Slider template updates
        // its value, so using SeekSlider.Value here can seek to the OLD point.
        // The release coordinate is valid for both track clicks and thumb drags.
        var point = e.GetPosition(SeekSlider);
        double width = Math.Max(1, SeekSlider.ActualWidth - 12);
        double fraction = Math.Clamp((point.X - 6) / width, 0, 1);
        int requestedMs = (int)Math.Round(fraction * _durationMs);
        SeekSlider.Value = fraction * 1000;
        SeekTo(requestedMs);
    }

    private void SeekSlider_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_durationMs > 0 && e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown)
            SeekTo((int)Math.Round(SeekSlider.Value / 1000 * _durationMs));
    }
    private void LyricLine_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_synced || sender is not FrameworkElement { DataContext: LyricLineViewModel line }) return;
        SeekTo(line.StartMs);
    }
    private void ScrollArea_SizeChanged(object sender, SizeChangedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            { UpdateViewportPadding(); UpdateTranslationLayout(); UpdateTargetScroll(); }));

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_ready) return;
        ApplyResponsiveSettingsLayout();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            { UpdateViewportPadding(); UpdateTranslationLayout(); UpdateTargetScroll(); }));
    }
    private void LyricsItems_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
        _followLyrics = false;
        ResumeFollowButton.Visibility = Visibility.Visible;
    }
    private void ResumeFollow_Click(object sender, RoutedEventArgs e)
    {
        _followLyrics = true;
        ResumeFollowButton.Visibility = Visibility.Collapsed;
        UpdateTargetScroll();
    }

    private void OpenLrc_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Открыть LRC", Filter = "LRC subtitles (*.lrc)|*.lrc|Text (*.txt)|*.txt", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var (lines, synced) = LrcReader.Read(dialog.FileName, _durationMs);
            if (lines.Count == 0) { MessageBox.Show(this, "В файле нет строк текста.", "LRC", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            _manualOverride = true;
            if (_demo)
            {
                TrackTitle.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                TrackArtist.Text = "Открыт локальный LRC · демо-таймер";
                _durationMs = Math.Max(5000, lines.Max(x => x.EndMs > 0 ? x.EndMs : x.StartMs + 5000));
                _clock.Seek(0);
                _clock.SetPlaying(false);
                PlayButton.Content = "▶";
            }
            SetLyrics(lines, synced, "Локальный LRC (вручную)");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось прочитать LRC: " + ex.Message, "LRC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
        else if (e.Key == Key.Escape && _fullscreen) { ToggleFullscreen(); e.Handled = true; }
        else if (e.Key == Key.Escape && _settingsOpen) { ToggleSettings(false); e.Handled = true; }
        else if (e.Key == Key.Space) { Play_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { AdjustFont(2); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { AdjustFont(-2); e.Handled = true; }
        else if (alt && e.Key == Key.Left) { AdjustOffset(-100); e.Handled = true; }
        else if (alt && e.Key == Key.Right) { AdjustOffset(100); e.Handled = true; }
    }
    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        AdjustFont(e.Delta > 0 ? 2 : -2);
        e.Handled = true;
    }
}
