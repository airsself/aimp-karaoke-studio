using System.Windows;

namespace KaraokeStudio.Design;

/// <summary>
/// Structural design metrics shared by fullscreen, responsive chrome, and the
/// authored WPF layout. Values are in WPF device-independent pixels (DIP).
/// </summary>
public static class DesignMetrics
{
    public const double HeaderHeight = 40;
    public const double ToolbarHeight = 64;
    public const double FooterHeight = 64;
    public const double SettingsWidth = 320;
    public const double OverlayBreakpoint = 980;
    public const double CompactChromeBreakpoint = 1100;
    public const double CompactFooterBreakpoint = 880;
    public const double OverlayInset = 12;

    public static GridLength HeaderGridHeight => new(HeaderHeight);
    public static GridLength ToolbarGridHeight => new(ToolbarHeight);
    public static GridLength FooterGridHeight => new(FooterHeight);
}
