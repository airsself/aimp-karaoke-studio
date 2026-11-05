using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KaraokeStudio.Services;

namespace KaraokeStudio;

/// <summary>
/// End-to-end regression for the exact "Найти -> select -> Применить" path.
/// Uses real WPF dialog and deterministic fake LRCLIB HTTP responses.
/// This failed with ObjectDisposedException on v4.1.0 when ShowDialog closed.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Exercise the real production design dictionaries, not placeholder
        // colors/styles: catches broken template resources and runtime XAML errors.
        app.Resources.MergedDictionaries.Add(new ResourceDictionary {
            Source = new Uri("pack://application:,,,/Styles/Tokens.xaml", UriKind.Absolute)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary {
            Source = new Uri("pack://application:,,,/Styles/Components.xaml", UriKind.Absolute)
        });
        foreach (var key in new[] { "StudioTextBox", "StudioCard",
            "DiscoveryResultItem", "PrimaryButton", "SettingCheck" })
            if (app.TryFindResource(key) is not Style)
                throw new Exception("Missing production design resource: " + key);
        try
        {
            ApplyAfterCompletedSearchAndRetry();
            CloseDuringPendingSearch();
            Console.WriteLine("PASS: real WPF completed-search -> retry -> Apply, and pending-search close");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL Lyrics Apply WPF regression: " + error);
            return 1;
        }
        finally { app.Shutdown(); }
    }

    private static void ApplyAfterCompletedSearchAndRetry()
    {
        var handler = new FakeLyricsApi();
        var service = NewService(handler);
        var window = new LyricsSearchWindow(service,
            new LyricsTrack("Demo Singer", "Ocean of Silence", "", 180000),
            "", false);
        Exception? failure = null;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await UntilAsync(() =>
                    Find<ListBox>(window, "ResultsList").Items.Count > 0 &&
                    Find<Button>(window, "SearchButton").IsEnabled);
                // Previous version retained a disposed CTS in the shared field.
                // A second search then touched that disposed instance.
                int requests = handler.Calls;
                Find<Button>(window, "SearchButton")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await UntilAsync(() =>
                    handler.Calls > requests &&
                    Find<Button>(window, "SearchButton").IsEnabled &&
                    Find<ListBox>(window, "ResultsList").Items.Count > 0);
                Find<ListBox>(window, "ResultsList").SelectedIndex = 0;
                var apply = Find<Button>(window, "ApplyButton");
                if (!apply.IsEnabled) throw new Exception("Apply not enabled");
                apply.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            catch (Exception error)
            {
                failure = error;
                try { window.Close(); } catch { }
            }
        };
        bool? accepted = window.ShowDialog();
        if (failure is not null) throw failure;
        if (accepted != true || window.SelectedLyrics is null ||
            !window.SelectedLyrics.Content.Contains("[00:01.00]"))
            throw new Exception("Apply did not return the selected synchronized lyrics");
    }

    private static void CloseDuringPendingSearch()
    {
        var service = NewService(new FakeLyricsApi { Delay = true });
        var window = new LyricsSearchWindow(service,
            new LyricsTrack("Demo Singer", "Ocean of Silence", "", 180000),
            "", false);
        Exception? failure = null;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(80);
                window.Close();
            }
            catch (Exception error) { failure = error; }
        };
        window.ShowDialog();
        if (failure is not null) throw failure;
    }

    private static LyricsDiscoveryService NewService(FakeLyricsApi handler)
        => new(new HttpClient(handler), new HttpClient(handler),
            Path.Combine(Path.GetTempPath(),
                "lyrics-apply-regression-" + Guid.NewGuid().ToString("N") + ".json"),
            new Uri("https://lrclib.invalid/"), new Uri("https://ovh.invalid/"));

    private static T Find<T>(Window window, string name) where T : FrameworkElement
        => (T)(window.FindName(name)
            ?? throw new Exception("Missing WPF control: " + name));

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100; i++)
        {
            if (condition()) return;
            await Task.Delay(40);
        }
        throw new TimeoutException("WPF search did not reach the expected state");
    }

    private sealed class FakeLyricsApi : HttpMessageHandler
    {
        public int Calls;
        public bool Delay;
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Calls);
            if (Delay) await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
            string path = request.RequestUri?.AbsolutePath ?? "";
            string? body = path switch
            {
                "/api/get" => """{"artistName":"Demo Singer","trackName":"Ocean of Silence","duration":180,"syncedLyrics":"[00:01.00]One\n[00:02.00]Two"}""",
                "/api/search" => "[]",
                _ => null
            };
            return new HttpResponseMessage(body is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? "", Encoding.UTF8, "application/json")
            };
        }
    }
}
