"""Static regression guard for native-to-WPF contract; NOT a runtime smoke test."""
from pathlib import Path
r=Path(__file__).resolve().parents[1]
plugin=(r/'plugin/Plugin.cpp').read_text('utf-8')
pipe=(r/'plugin/PipeServer.cpp').read_text('utf-8')
ui=(r/'app/MainWindow.xaml.cs').read_text('utf-8')
xaml=(r/'app/MainWindow.xaml').read_text('utf-8')
build=(r/'build.ps1').read_text('utf-8')
checks={
'lyrics cancellation': 'AIMP_SERVICE_LYRICS_FLAGS_WAITFOR' in plugin and 'lyrics_->Cancel' in plugin,
'art cancellation': 'AIMP_SERVICE_ALBUMART_FLAGS_WAITFOR' in plugin,
'async task handle': 'TTaskHandle task=0' in plugin and '&task)' in plugin,
'callback drain': 'g_callbacksDone.wait' in plugin,
'bounded IPC payload': 'lyrics_too_large' in plugin and 'json.size()>900000' in pipe,
'IPC reconnect snapshot': 'latest_[kind]' in pipe and '"artwork","state","position"' in pipe,
'AIMP main-thread dispatch': 'ExecuteInMainThread' in plugin and 'dispatchCommand_' in pipe,
'generation on position/state': '"generation\\\":"+std::to_string(epoch_->load())' in plugin,
'virtualized UI': 'VirtualizingPanel.IsVirtualizing="True"' in xaml and 'VirtualizingPanel.VirtualizationMode="Recycling"' in xaml,
'actual line height': 'LineHeight="{Binding LineHeight}"' in xaml,
'stale update filter': 'if (message.Generation != _generation) break;' in ui,
'fullscreen monitor': 'Screen.FromHandle' in ui and 'TransformFromDevice' in ui,
'album service': 'IAIMPServiceAlbumArt' in plugin and 'OnAimpArtwork' in plugin,
'package 64-bit layout': "Join-Path $targetFolder 'x64'" in build,
'per instance mutex': 'new Mutex(true' in (r/'app/App.xaml.cs').read_text(),
'enhanced lrc parser': 'ParseEnhancedLine' in (r/'plugin/Lrc.cpp').read_text(),
'precision ipc word payload': 'words' in plugin,
'inline karaoke two run': 'HighlightedText' in xaml and 'RemainingText' in xaml,
'no lyric progress bar': 'LyricProgress' not in xaml,
'vocal focus anchor': 'UpdateViewportPadding' in ui,
'precision highlight engine': (r/'app/Services/KaraokeHighlightEngine.cs').exists(),
'only active phrase grows': 'item.FontSize = _settings.FontSize * 0.70;' in ui,
'next phrase label removed': 'СЛЕДУЮЩАЯ СТРОКА' not in xaml,
'player command acknowledgement': 'commandResult' in plugin,
'handles Windows CRLF command frames': 'DecodeKaraokeCommand' in pipe and (r/'plugin/CommandProtocol.h').exists(),
'seek uses click coordinate': 'e.GetPosition(SeekSlider)' in ui,
'bilingual UI toggle': 'TranslationButton' in xaml and 'SetTranslationVisible' in ui,
'aligned bilingual row template': 'TranslationPaneWidth' in xaml and 'TranslationText' in xaml,
'automatic translator with cache': (r/'app/Services/TranslationService.cs').exists() and 'BeginTranslation' in ui,
'v3 lyrics search button': 'FindLyricsButton' in xaml and 'FindLyrics_Click' in ui,
'v3 source-only discovery': (r/'app/Services/LyricsDiscoveryService.cs').exists(),
'v3 manual sidecar save': (r/'app/Services/LyricsSidecarStore.cs').exists(),
'v3 autosearch cancellation': 'CancelLyricsLookup' in ui and 'BeginAutomaticLyricsLookup' in ui,
'v3 metadata safety': 'SetToAll(' not in (r/'app/Services/LyricsSidecarStore.cs').read_text('utf-8'),
'prepared karaoke graphemes': 'KaraokeHighlightEngine.Prepare' in (r/'app/Models/LyricLineViewModel.cs').read_text('utf-8'),
'adaptive rendering cadence': 'TimeSpan.FromMilliseconds(interval)' in ui,
'cached background rendering': 'BitmapCache RenderAtScale' in xaml,
'batch translation cache': 'unsaved >= 20' in (r/'app/Services/TranslationService.cs').read_text('utf-8'),
'background cache serialization': 'Task.Run(() =>' in (r/'app/Services/TranslationService.cs').read_text('utf-8'),
'bilingual resize invalidation guard': '_appliedTranslationLayoutVersion' in ui,
}
for k,v in checks.items():
    if not v: raise AssertionError(k)
print(f'{len(checks)} source contract checks passed (static only)')
