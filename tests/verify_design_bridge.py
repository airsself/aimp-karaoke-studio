"""Portable static integration check; does not claim a Windows/WPF runtime test."""
from pathlib import Path
from lxml import etree
import re

root=Path(__file__).resolve().parents[1]
xaml=(root/'app/MainWindow.xaml').read_text('utf-8')
app=(root/'app/App.xaml').read_text('utf-8')
resources='\n'.join((root/p).read_text('utf-8') for p in
    ('app/Styles/Tokens.xaml', 'app/Styles/Components.xaml'))
code=(root/'app/MainWindow.xaml.cs').read_text('utf-8')
plugin=(root/'plugin/Plugin.cpp').read_text('utf-8')
dto=(root/'app/Models/PipeMessage.cs').read_text('utf-8')
for name in ('app/App.xaml','app/MainWindow.xaml', 'app/Styles/Tokens.xaml',
             'app/Styles/Components.xaml', 'app/KaraokeStudio.csproj'):
    etree.parse(str(root/name))
keys=set(re.findall(r'x:Key="([\w-]+)"',app+xaml))
refs=set(re.findall(r'\{(?:StaticResource|DynamicResource)\s+([^}]+)\}',app+xaml+resources))
assert not (refs-keys), f'Missing XAML resources: {refs-keys}'
events=set(re.findall(r'\b(?:Click|Loaded|Closing|KeyDown|PreviewMouseWheel|ValueChanged|SizeChanged|MouseLeftButtonDown|MouseLeftButtonUp|PreviewMouseLeftButtonUp)="(\w+)"',xaml))
assert all(re.search(r'\b'+re.escape(name)+r'\s*\(',code) for name in events)
names=re.findall(r'x:Name="([^"]+)"',xaml)
assert len(names)==len(set(names))
for piece in ('SettingsDrawer','FontSlider','SpacingSlider','ContrastSlider','OffsetSlider','AmbienceSlider','SmoothCheck','ProgressCheck','TopCheck','CoverImage','LyricsItems','LyricLine_MouseLeftButtonUp'):
    assert piece in xaml and piece in code
assert 'filePath' in plugin and 'album' in plugin
assert 'public string FilePath' in dto and 'public string Album' in dto
assert (root/'app/Assets/demo-cover.png').is_file()
assert (root/'docs/design-reference/index.html').is_file()
assert '<Resource Include="Assets/demo-cover.png" />' in (root/'app/KaraokeStudio.csproj').read_text()
print(f'Static design integration checks OK: {len(keys)} resources, {len(events)} handlers, {len(names)} named elements.')
