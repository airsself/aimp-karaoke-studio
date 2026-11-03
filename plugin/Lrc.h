#pragma once
#include <string>
#include <vector>
#include <cstdint>
struct LyricWord { int startMs = 0; int endMs = 0; std::wstring text; };
struct LyricLine {
    int startMs = 0;
    int endMs = 0;
    std::wstring text;
    std::vector<LyricWord> words;
};
struct ParsedLyrics {
    bool synced = false;
    std::vector<LyricLine> lines;
};
ParsedLyrics ParseLrc(const std::wstring& raw);
std::wstring ReadLyricsFile(const std::wstring& path);
