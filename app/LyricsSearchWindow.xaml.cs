using KaraokeStudio.Services;
using Microsoft.Win32;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace KaraokeStudio;

/// <summary>Separate native WPF lookup dialog. No changes to AIMP metadata.</summary>
public partial class LyricsSearchWindow : Window
{
    private readonly LyricsDiscoveryService _discovery;
    private readonly LyricsTrack _initial;
    private readonly string _audioFile;
    private CancellationTokenSource? _cancel;
    private bool _closed;
    private bool _searched;
    public LyricsCandidate? SelectedLyrics { get; private set; }
    public LyricsTrack SelectedTrack { get; private set; }
    public bool SavedSidecar { get; private set; }
    public bool UseLegacyBridge => LegacyBridgeCheck.IsChecked == true;

    public LyricsSearchWindow(LyricsDiscoveryService discovery, LyricsTrack initial,
        string audioFile, bool useLegacyBridge)
    {
        InitializeComponent();
        _discovery = discovery;
        _initial = initial;
        _audioFile = audioFile;
        SelectedTrack = initial;
        ArtistInput.Text = initial.Artist;
        TitleInput.Text = initial.Title;
        LegacyBridgeCheck.IsChecked = useLegacyBridge;
    }

    private void SearchWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_searched) return;
        _searched = true;
        if (!string.IsNullOrWhiteSpace(ArtistInput.Text) &&
            !string.IsNullOrWhiteSpace(TitleInput.Text))
            _ = SearchNowAsync();
    }

    private void SearchWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closed = true;
        _cancel?.Cancel();
    }

    private async Task SearchNowAsync()
    {
        _cancel?.Cancel();
        var tokenSource = new CancellationTokenSource();
        _cancel = tokenSource;
        var token = tokenSource.Token;
        var signature = new LyricsTrack(ArtistInput.Text.Trim(), TitleInput.Text.Trim(),
            _initial.Album, _initial.DurationMs);
        SelectedTrack = signature;
        SearchButton.IsEnabled = false;
        SearchStatus.Text = "Ищем по нескольким источникам…";
        ResultsList.ItemsSource = null;
        PreviewText.Text = "Получаем результаты…";
        ApplyButton.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        try
        {
            var result = await _discovery.SearchAsync(signature,
                LegacyBridgeCheck.IsChecked == true, true, token);
            if (token.IsCancellationRequested || _closed) return;
            ResultsList.ItemsSource = result.Candidates;
            SearchStatus.Text = result.Diagnostic;
            if (result.Candidates.Count > 0) ResultsList.SelectedIndex = 0;
            else PreviewText.Text = "Текст не найден. Проверьте исполнителя и название.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_closed && !token.IsCancellationRequested)
            {
                SearchStatus.Text = "Ошибка поиска: " + error.Message;
                StudioLog.Error("Lyrics lookup", error);
            }
        }
        finally
        {
            if (!_closed && ReferenceEquals(_cancel, tokenSource)) SearchButton.IsEnabled = true;
            tokenSource.Dispose();
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => _ = SearchNowAsync();

    private void SearchField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; _ = SearchNowAsync(); }
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ResultsList.SelectedItem as LyricsCandidate;
        ApplyButton.IsEnabled = selected is not null;
        DownloadButton.IsEnabled = selected is not null;
        PreviewText.Text = selected?.Content ?? "Выберите найденный текст.";
        PreviewText.ScrollToHome();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not LyricsCandidate selected) return;
        SelectedLyrics = selected;
        DialogResult = true;
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not LyricsCandidate selected) return;
        string file = !string.IsNullOrWhiteSpace(_audioFile)
            ? System.IO.Path.GetFileNameWithoutExtension(_audioFile) + ".lrc"
            : selected.Artist + " - " + selected.Title + ".lrc";
        var dialog = new SaveFileDialog {
            Title = "Сохранить текст в отдельный LRC-файл",
            Filter = "Синхронизированный/обычный текст LRC (*.lrc)|*.lrc",
            DefaultExt = ".lrc",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = file
        };
        if (LyricsSidecarStore.TrySuggestPath(_audioFile, out var suggested))
        {
            dialog.InitialDirectory = System.IO.Path.GetDirectoryName(suggested);
            dialog.FileName = System.IO.Path.GetFileName(suggested);
        }
        if (dialog.ShowDialog(this) != true) return;
        DownloadButton.IsEnabled = false;
        try
        {
            await LyricsSidecarStore.SaveAsync(dialog.FileName, selected.Content, true,
                CancellationToken.None);
            SelectedLyrics = selected;
            SavedSidecar = true;
            DialogResult = true;
        }
        catch (Exception error)
        {
            SearchStatus.Text = "Ошибка сохранения: " + error.Message;
            StudioLog.Error("Save lyrics LRC", error);
        }
        finally { if (!_closed) DownloadButton.IsEnabled = true; }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
