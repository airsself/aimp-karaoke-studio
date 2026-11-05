"""Static WPF design-system contract: token parity, resource resolution and
accessible control states. Windows runtime tests remain authoritative for BAML.
"""
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parents[1]
app = (root / "app/App.xaml").read_text(encoding="utf-8")
main = (root / "app/MainWindow.xaml").read_text(encoding="utf-8")
dialog = (root / "app/LyricsSearchWindow.xaml").read_text(encoding="utf-8")
tokens = (root / "app/Styles/Tokens.xaml").read_text(encoding="utf-8")
components = (root / "app/Styles/Components.xaml").read_text(encoding="utf-8")
metrics = (root / "app/Design/DesignMetrics.cs").read_text(encoding="utf-8")
spec = json.loads((root / "docs/design-system/tokens.json").read_text(encoding="utf-8"))

ns_key = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"
for filename in ["app/App.xaml", "app/MainWindow.xaml",
                 "app/LyricsSearchWindow.xaml", "app/Styles/Tokens.xaml",
                 "app/Styles/Components.xaml"]:
    ET.parse(root / filename)

def extract(path):
    parsed = ET.parse(root / path)
    result = {}
    for el in parsed.iter():
        key = el.attrib.get(ns_key)
        if key is not None:
            assert key not in result, f"Duplicate key {key} in {path}"
            result[key] = el
    return result

palette = extract("app/Styles/Tokens.xaml")
styles = extract("app/Styles/Components.xaml")
keys = set(palette) | set(styles)
assert not set(palette).intersection(styles), "Duplicate keys across dictionaries"
refs = set(re.findall(r"\{(?:StaticResource|DynamicResource)\s+([\w.]+)\}",
                      app + main + dialog + tokens + components))
assert not (refs - keys), f"Unknown resource keys: {refs-keys}"
assert 'Source="Styles/Tokens.xaml"' in app
assert 'Source="Styles/Components.xaml"' in app
assert app.index('Source="Styles/Tokens.xaml"') < app.index(
    'Source="Styles/Components.xaml"'), "Token dictionary must load first"

for name, expected in spec["colors"].items():
    assert name in palette, f"Missing color {name}"
    assert palette[name].attrib.get("Color", "").lower() == expected.lower(), (
        f"Color drift: {name}")
assert spec["baseline"] == 4
assert spec["spacing"] == [4, 8, 12, 16, 24, 32]

for name, expected in spec["geometry"].items():
    found = re.search(r"public const double " + re.escape(name)
                      + r"\s*=\s*([0-9.]+)\s*;", metrics)
    assert found and float(found.group(1)) == expected, (
        f"Geometry drift: {name}")

def luminance(rgb):
    xs = [int(rgb[i:i+2], 16) / 255 for i in (1, 3, 5)]
    xs = [x / 12.92 if x <= .04045 else ((x + .055) / 1.055) ** 2.4
          for x in xs]
    return sum(x * w for x, w in zip(xs, (.2126, .7152, .0722)))

def contrast(a, b):
    x, y = luminance(spec["colors"][a]), luminance(spec["colors"][b])
    return (max(x, y) + .05) / (min(x, y) + .05)

for foreground, background in [
    ("TextBrush", "WindowBrush"),
    ("SecondaryBrush", "DrawerBrush"),
    ("DimBrush", "StageBrush"),
    ("FocusRingBrush", "ControlBrush"),
]:
    assert contrast(foreground, background) >= 4.5, (
        f"Poor text/focus contrast: {foreground}/{background}")

required = [
    "ChromeButton", "OutlineButton", "PrimaryButton", "ModeButton",
    "WindowControl", "SettingSlider", "SettingCheck",
    "StudioTextBox", "StudioComboBox", "StudioComboBoxItem",
    "StudioCard", "DiscoveryResultItem", "StudioVerticalScrollBar", "PanelTitle", "PanelLabel",
]
assert set(required).issubset(styles)
assert "IsKeyboardFocused" in components
assert "IsKeyboardFocusWithin" in components
assert "IsSelected" in components
assert "IsMouseOver" in components
assert 'Property="IsEnabled"' in components
assert 'x:Name="PART_ContentHost"' in components
assert 'x:Name="PART_Popup"' in components

for expected in [
    'Style="{StaticResource StudioComboBox}"',
    'Style="{StaticResource StudioTextBox}"',
    'x:Name="SettingsScrim"',
    "UseLayoutRounding",
    "DesignMetrics.HeaderGridHeight",
]:
    assert expected in main
for expected in [
    'Style="{StaticResource StudioCard}"',
    'Style="{StaticResource StudioTextBox}"',
    'ItemContainerStyle="{StaticResource DiscoveryResultItem}"',
    'Style="{StaticResource PrimaryButton}"',
]:
    assert expected in dialog

code = (root / "app/MainWindow.xaml.cs").read_text(encoding="utf-8")
# Compact button captions must not break Windows UI Automation or smoke tests.
assert 'AutomationProperties.Name="Найти"' in main
assert 'AutomationProperties.Name="RU · Перевод"' in main
assert "AutomationProperties.SetName(TranslationButton" in code

assert 'KeyboardNavigation.TabNavigation="Cycle"' in main
assert "SettingsCloseButton.Focus()" in code
assert 'x:Name="ResultsPlaceholder"' in dialog
assert 'VerticalContentAlignment="Top"' in dialog
assert "ResultsPlaceholder.Visibility" in (root / "app/LyricsSearchWindow.xaml.cs").read_text(encoding="utf-8")
assert 'WindowStyle="None" ResizeMode="CanResize"' in dialog
assert 'DialogTitlebar_MouseLeftButtonDown' in dialog
assert 'BasedOn="{StaticResource StudioVerticalScrollBar}"' in main
assert dialog.count('BasedOn="{StaticResource StudioVerticalScrollBar}"') >= 2
assert "SettingsScrim_MouseLeftButtonDown" in code
assert "UpdateResponsiveChrome()" in code
assert "DesignMetrics.SettingsWidth" in code
assert "DesignBrush(" in code

print(f"Design-system static contract OK: {len(palette)} tokens, "
      f"{len(styles)} component styles, {len(refs)} resource references, "
      "AA contrast and responsive geometry verified")
