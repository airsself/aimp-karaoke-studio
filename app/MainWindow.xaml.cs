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
    private readonly DispatcherTimer _frames = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly PipeClient? _pipe;
    private readonly StudioSettings _settings = SettingsStore.Load();
    private readonly bool _demo;
    private int _generation;
    private ScrollViewer? _scrollViewer;
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
    }

    private static SolidColorBrush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindChild<ScrollViewer>(LyricsItems);
        if (_demo) StartDemo(); else _pipe?.Start();
        _frames.Start();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _frames.Stop();
        _pipe?.Dispose();
        _artworkCancel?.Cancel();
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
                _lines.Clear(); _synced = false; _currentIndex = -2;
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
                EndMs = Math.Max(0, item.EndMs), Text = item.Text ?? "" });
        _synced = synced && _lines.Count > 0;
        SourceText.Text = string.IsNullOrWhiteSpace(source) ? "AIMP Lyrics" : source.Replace('-', ' ');
        SyncIndicator.Text = _lines.Count == 0 ? "ТЕКСТ НЕ НАЙДЕН" :
            (_synced ? "ПОСТРОЧНАЯ СИНХРОНИЗАЦИЯ" : "ТЕКСТ БЕЗ ВРЕМЕННЫХ МЕТОК");
        EmptyPanel.Visibility = _lines.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyHeadline.Text = "Текст песни не найден";
        EmptyDescription.Text = "Откройте LRC кнопкой внизу или добавьте файл с тем же названием, что и аудиотрек.";
        _currentIndex = -2;
        ApplyLineStyles();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateTargetScroll));
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
        bool reading = _mode == "Reading";
        int focus = _currentIndex >= 0 ? _currentIndex : _lastScrollIndex;
        int count = _lines.Count;
        int accentIndex = Array.FindIndex(Accents,
            x => x.Equals(_settings.AccentColor, StringComparison.OrdinalIgnoreCase));
        Brush activeText = Solid(Highlights[Math.Max(0, accentIndex)]);
        double padding = _settings.VerseGap / 2;
        // Playback updates affect only rows near the old/new focus.
        // Setting/mode changes pass -2 and update the full collection.
        IEnumerable<int> affected = previous == -2 ? Enumerable.Range(0, count) :
            Enumerable.Range(Math.Max(0, previous - 3), Math.Min(count, Math.Max(0, previous + 4)) - Math.Max(0, previous - 3))
            .Concat(Enumerable.Range(Math.Max(0, _currentIndex - 3), Math.Min(count, Math.Max(0, _currentIndex + 4)) - Math.Max(0, _currentIndex - 3))).Distinct();
        foreach (int i in affected)
        {
            var item = _lines[i];
            int dist = Math.Abs(i - focus);
            bool active = _synced && i == _currentIndex;
            bool next = _synced && i == focus + 1;
            bool near = dist == 1;
            // Preserve layout positions while hiding far-away rows in Karaoke/Stage.
            bool visible = reading || !_synced || (_mode == "Stage" ? active || next : dist <= 2);
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            item.Foreground = active ? activeText : near ? NearBrush : QuietBrush;
            item.Opacity = active ? 1 : !_synced ? 0.9 :
                near ? Math.Min(0.96, _settings.NeighborContrast / 100.0 * 1.8) :
                i < focus ? Math.Max(0.15, _settings.NeighborContrast / 100.0 * 0.6) : _settings.NeighborContrast / 100.0;
            item.FontSize = _settings.FontSize * (!_synced ? _settings.PlainTextScale : active ? (_mode == "Stage" ? 1.24 : 1.0) : 0.70);
            item.LineHeight = Math.Max(item.FontSize * 1.1, item.FontSize * _settings.LineSpacing);
            item.Weight = active ? FontWeights.SemiBold : FontWeights.Normal;
            item.Padding = new Thickness(8, padding, 8, padding);
            item.ProgressVisibility = active && _settings.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
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
            var top = container.TranslatePoint(new Point(0, 0), LyricsItems);
            var target = _scrollViewer.VerticalOffset + top.Y + container.ActualHeight/2 - _scrollViewer.ViewportHeight*0.46;
            _targetScroll = Math.Clamp(target, 0, _scrollViewer.ScrollableHeight);
        }
        catch (InvalidOperationException) { }
    }

    private void UpdateModeButtons()
    {
        if (!_ready) return;
        foreach (var (button, name) in new[] { (ReadingButton, "Reading"), (KaraokeButton, "Karaoke"), (StageButton, "Stage") })
        {
            button.Background = _mode == name ? Solid("#4A415F") : Brushes.Transparent;
            button.Foreground = _mode == name ? Solid("#EEE9FC") : Solid("#9C9AA5");
        }
    }

    private void SetMode(string value)
    {
        _mode = value;
        _settings.Mode = value;
        ApplyLineStyles();
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

    private void ToggleSettings(bool? show = null)
    {
        if (_fullscreen) return;
        _settingsOpen = show ?? !_settingsOpen;
        SettingsColumn.Width = _settingsOpen ? new GridLength(298) : new GridLength(0);
        SettingsDrawer.Visibility = _settingsOpen ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Background = _settingsOpen ? Solid("#342B44") : Solid("#1D1B21");
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateTargetScroll));
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
            HeaderRow.Height = new GridLength(38); ToolbarRow.Height = new GridLength(70); FooterRow.Height = new GridLength(64);
            WindowState = WindowState.Normal;
            WindowStyle = _restoreStyle;
            ResizeMode = ResizeMode.CanResize;
            Left = _restoreBounds.Left; Top = _restoreBounds.Top;
            Width = _restoreBounds.Width; Height = _restoreBounds.Height;
            WindowState = _stateBeforeFullscreen;
            Topmost = _settings.AlwaysOnTop;
            if (_settingsBeforeFullscreen) ToggleSettings(true);
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateTargetScroll));
    }

    private void AdjustFont(int delta)
    {
        _settings.FontSize = Math.Clamp(_settings.FontSize + delta, 24, 100);
        FontSlider.Value = _settings.FontSize;
        UpdateSettingsValues();
        ApplyLineStyles();
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
        if (_durationMs > 0) SeekTo((int)Math.Round(SeekSlider.Value / 1000 * _durationMs));
    }
    private void LyricLine_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_synced || sender is not FrameworkElement { DataContext: LyricLineViewModel line }) return;
        SeekTo(line.StartMs);
    }
    private void ScrollArea_SizeChanged(object sender, SizeChangedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateTargetScroll));
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
