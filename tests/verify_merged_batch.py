#!/usr/bin/env python3
from pathlib import Path
r=Path(__file__).resolve().parents[1]
b=(r/"plugin/BatchLyrics.cpp").read_text(encoding="utf-8")
p=(r/"plugin/Plugin.cpp").read_text(encoding="utf-8")
m=(r/"plugin/CMakeLists.txt").read_text(encoding="utf-8")
s=(r/"app/Services/EmbeddedLyricsBridge.cs").read_text(encoding="utf-8")
tests={
"single AIMP plugin entry point":p.count("AIMPPluginGetHeader(")==1 and "AIMPPluginGetHeader(" not in b,
"one linked DLL": "BatchLyrics.cpp" in m and m.count("add_library(AimpKaraokeStudio SHARED")==1,
"prior native action identifiers": all(x in b for x in ["RefreshCurrentLyrics","RefreshSelectedLyrics","CancelLyricsUpdate"]),
"original default hotkeys": "AIMP_ACTION_HOTKEY_MODIFIER_SHIFT" in b and "AIMP_ACTION_HOTKEY_MODIFIER_ALT" in b,
"selected-item snapshot": "AIMP_PLAYLISTITEM_PROPID_SELECTED" in b,
"dedup and batch limit": "seen_.insert(" in b and "std::min(5000" in b,
"serialized worker tasks": "ExecuteInThread(" in b and "Next();" in b,
"safe metadata writer": "GetTag(" in b and "AIMP_FILEINFO_PROPID_LYRICS" in b and "AIMP_FILETAG_ID_ID3v1" in b,
"no tag overwrite": "->SetToAll(" not in b,
"safe separate LRC": "SaveSeparateLrc(" in b and "MoveFileExW(" in b,
"loopback only": "IPAddress.Loopback" in s and "TcpListener" in s and "0.0.0.0" not in s,
"marker protocol": all(x in s for x in ["AIMPLB-LYRICS-BEGIN","AIMPLB-LYRICS-END","AIMPLB-LINEBREAK-7F3C"]),
"no legacy binary packaging": "AIMPLyricsBridgeHotkey.dll" not in m and "aimp_webLyrics.dll" not in m,
}
for name,ok in tests.items():print(("PASS" if ok else "FAIL")+": "+name)
if not all(tests.values()):raise SystemExit(1)
print(f"{len(tests)} merged plugin safety guards passed")
