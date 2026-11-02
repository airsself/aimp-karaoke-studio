# Design transfer map — approved v1 → WPF v0.2

| Reference component | Native WPF component | Behavior |
| --- | --- | --- |
| `.titlebar` | `Grid.Row=0` in `MainWindow.xaml`, `WindowChrome` | Drag, maximize, minimize, close, resize |
| `.toolbar` | `Grid.Row=1` | Track, artist, source, local cover, mode switch, settings button |
| `.lyrics-surface` | `Grid.Row=2`, `ScrollArea` | AIMP song text, 46% focus, low-contrast neighbors |
| `.settings` | `SettingsDrawer`, `SettingsColumn` | 298-DIP right drawer, hidden by default |
| `.footer` | `Grid.Row=3` | Transport, song seek, A-/A+, fullscreen, LRC import |
| CSS `--accent` | WPF app resource `AccentBrush` | One of 4 sober color variants |
| CSS `--lyric-size` | `StudioSettings.FontSize` / `FontSlider` | 24–100 DIP |
| CSS `--leading` | `StudioSettings.LineSpacing` / VM Padding | 1.1–2.0 |
| CSS `--quiet-opacity` | `StudioSettings.NeighborContrast` | 15–95% |
| CSS `--ambient-opacity` | `AmbientImage.Opacity` | 0–40%, only when art exists |
| CSS `--fill` | `LyricLineViewModel.Progress` | Line timeline progress bar |
| `syncIndicator` | `SyncIndicator` | Sync / unsynced / not found |

**Deliberate differences:** The HTML design shows an artistic rendition of AIMP desktop behind the mockup. The native renderer uses a normal top-level Windows window, not a background screenshot. The setting “фон плеера за окном” from the mockup is omitted because a real native window cannot reliably render AIMP's interface behind itself; `Topmost` and a subtle album-art ambience provide useful alternatives.

The approved design's native defaults and layout structure are preserved as closely as practical in WPF. Real pixel fidelity remains subject to Windows visual testing.
