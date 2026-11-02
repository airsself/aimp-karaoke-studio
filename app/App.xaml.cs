using Application = System.Windows.Application;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using KaraokeStudio.Services;

namespace KaraokeStudio;

public partial class App : Application
{
    private Mutex? _singleInstance;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? caption);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => StudioLog.Error("Unhandled UI exception", args.Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => StudioLog.Error("Unobserved task exception", args.Exception);
        string? pipe = null;
        bool demo = false;
        for (int i=0;i<e.Args.Length;i++)
        {
            if (e.Args[i] == "--pipe" && i+1<e.Args.Length) pipe=e.Args[++i];
            else if (e.Args[i] == "--demo") demo=true;
        }
        if (!demo && !string.IsNullOrWhiteSpace(pipe))
        {
            // Only accept the native bridge's PID-scoped pipe name.
            if (!System.Text.RegularExpressions.Regex.IsMatch(pipe, @"^AimpKaraokeStudio\.\d+$"))
            {
                Shutdown(2); return;
            }
            _singleInstance = new Mutex(true, "Local\\KaraokeStudio.UI." + pipe, out var first);
            if (!first)
            {
                var hwnd = FindWindow(null, "AIMP Karaoke Studio - " + pipe);
                if (hwnd != IntPtr.Zero) { ShowWindow(hwnd, 9); SetForegroundWindow(hwnd); }
                Shutdown(); return;
            }
        }
        var main=new MainWindow(demo||string.IsNullOrEmpty(pipe)?null:pipe);
        MainWindow=main;
        main.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
