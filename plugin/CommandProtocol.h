#pragma once
#include <charconv>
#include <string_view>
#include <system_error>

// The pipe uses newline-delimited JSON. Windows StreamWriter writes CRLF,
// while older clients may send LF: normalize the frame terminator before
// comparing the deliberately small, fixed transport command vocabulary.
enum class KaraokeCommand : int { Invalid=0, Toggle=1, Previous=2, Next=3, SeekMs=4, Status=5 };
struct KaraokeCommandFrame {
    KaraokeCommand kind=KaraokeCommand::Invalid;
    int positionMs=0;
};
inline KaraokeCommandFrame DecodeKaraokeCommand(std::string_view line) {
    while(!line.empty() && (line.back()=='\r'||line.back()=='\n'))line.remove_suffix(1);
    if(line==R"({"command":"toggle"})")return {KaraokeCommand::Toggle,0};
    if(line==R"({"command":"status"})")return {KaraokeCommand::Status,0};
    if(line==R"({"command":"previous"})")return {KaraokeCommand::Previous,0};
    if(line==R"({"command":"next"})")return {KaraokeCommand::Next,0};
    constexpr std::string_view prefix=R"({"command":"seek","positionMs":)";
    if(line.size()<=prefix.size()+1 || line.substr(0,prefix.size())!=prefix ||
       line.back()!='}')return {};
    auto text=line.substr(prefix.size(),line.size()-prefix.size()-1);
    if(text.empty()||text.size()>9)return {};
    int position=0;
    auto result=std::from_chars(text.data(),text.data()+text.size(),position);
    if(result.ec!=std::errc()||result.ptr!=text.data()+text.size()||
       position<0||position>24*60*60*1000)return {};
    return {KaraokeCommand::SeekMs,position};
}
