#include "Lrc.h"
#include <algorithm>
#include <cwchar>
#include <cwctype>
#include <sstream>
#include <limits>
#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#endif

namespace {
std::wstring Trim(const std::wstring& s) {
    size_t a=s.find_first_not_of(L" \r\n\t");
    if(a==std::wstring::npos) return L"";
    size_t b=s.find_last_not_of(L" \r\n\t");
    return s.substr(a,b-a+1);
}
bool Timestamp(const std::wstring& tag, int& ms) {
    const auto colon=tag.find(L':');
    if(colon==std::wstring::npos||colon<1||colon>3)return false;
    auto mm=tag.substr(0,colon),ss=tag.substr(colon+1);
    auto asciiDigit=[](wchar_t ch){return ch>=L'0'&&ch<=L'9';};
    if(!std::all_of(mm.begin(),mm.end(),asciiDigit))return false;
    auto dot=ss.find(L'.');auto secs=ss.substr(0,dot);
    if(secs.size()!=2||!std::all_of(secs.begin(),secs.end(),asciiDigit))return false;
    int m=std::stoi(mm),sec=std::stoi(secs);
    if(m>999||sec>59)return false;
    int sub=0;
    if(dot!=std::wstring::npos){
        auto frac=ss.substr(dot+1);
        if(frac.empty()||frac.size()>3||!std::all_of(frac.begin(),frac.end(),asciiDigit))return false;
        while(frac.size()<3)frac+=L'0';
        sub=std::stoi(frac);
    }
    ms=(m*60+sec)*1000+sub;
    return true;
}
void Normalize(ParsedLyrics& data,int offset) {
    for(auto& line:data.lines)line.startMs=static_cast<int>(std::clamp(static_cast<long long>(line.startMs)+offset,0LL,86400000LL));
    std::stable_sort(data.lines.begin(),data.lines.end(),[](const auto& a,const auto& b){return a.startMs<b.startMs;});
    // Multiple source rows at the same timestamp are a bilingual/multi-voice phrase,
    // not independent lines with a zero-duration first entry.
    std::vector<LyricLine> merged;
    for(const auto& line:data.lines){
        if(!merged.empty() && merged.back().startMs==line.startMs){
            if(!line.text.empty()) {
                if(!merged.back().text.empty())merged.back().text+=L"\n";
                merged.back().text+=line.text;
            }
        }else merged.push_back(line);
    }
    data.lines.swap(merged);
    for(size_t i=0;i<data.lines.size();++i){
        const int end=i+1<data.lines.size()?data.lines[i+1].startMs:data.lines[i].startMs+4000;
        data.lines[i].endMs=std::max(data.lines[i].startMs+1,end);
    }
}
}
ParsedLyrics ParseLrc(const std::wstring& raw){
    ParsedLyrics out;std::wistringstream input(raw);std::wstring row;
    int offset=0;std::vector<std::wstring> plain;
    while(std::getline(input,row)){
        row=Trim(row);if(row.empty())continue;
        if(row.rfind(L"[offset:",0)==0){
            auto end=row.find(L']');if(end!=std::wstring::npos){
                try {offset=std::clamp(std::stoi(row.substr(8,end-8)),-600000,600000);}catch(...){}
            }continue;
        }
        size_t p=0;std::vector<int> times;
        while(p<row.size() && row[p]==L'['){
            auto end=row.find(L']',p+1);if(end==std::wstring::npos)break;
            int time=0;if(!Timestamp(row.substr(p+1,end-p-1),time))break;
            times.push_back(time);p=end+1;
        }
        if(!times.empty()){
            const auto content=Trim(row.substr(p));
            for(int t:times)out.lines.push_back({t,0,content});
            out.synced=true;
        }else if(row[0]!=L'[')plain.push_back(row);
    }
    if(out.synced)Normalize(out,offset);
    else for(const auto& p:plain)out.lines.push_back({0,0,p});
    return out;
}
std::wstring ReadLyricsFile(const std::wstring& path){
#ifdef _WIN32
    HANDLE f=CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(f==INVALID_HANDLE_VALUE)return L"";
    LARGE_INTEGER length{};
    if(!GetFileSizeEx(f,&length)||length.QuadPart<=0||length.QuadPart>8*1024*1024){CloseHandle(f);return L"";}
    std::string bytes(static_cast<size_t>(length.QuadPart),'\0');DWORD n=0;
    const bool ok=ReadFile(f,bytes.data(),static_cast<DWORD>(bytes.size()),&n,nullptr)!=0;
    CloseHandle(f);if(!ok)return L"";bytes.resize(n);
    if(bytes.size()>=2&&static_cast<unsigned char>(bytes[0])==0xFF&&static_cast<unsigned char>(bytes[1])==0xFE){
        std::wstring out;for(size_t i=2;i+1<bytes.size();i+=2)out.push_back(static_cast<wchar_t>(static_cast<unsigned char>(bytes[i])|(static_cast<unsigned char>(bytes[i+1])<<8)));return out;
    }
    UINT cp=CP_UTF8;size_t start=0;
    if(bytes.size()>=2&&static_cast<unsigned char>(bytes[0])==0xFE&&static_cast<unsigned char>(bytes[1])==0xFF){
        std::wstring out;for(size_t i=2;i+1<bytes.size();i+=2)out.push_back(static_cast<wchar_t>((static_cast<unsigned char>(bytes[i])<<8)|static_cast<unsigned char>(bytes[i+1])));return out;
    }
    if(bytes.size()>=3&&static_cast<unsigned char>(bytes[0])==0xEF&&static_cast<unsigned char>(bytes[1])==0xBB&&static_cast<unsigned char>(bytes[2])==0xBF)start=3;
    const char* ptr=bytes.data()+start;int sz=static_cast<int>(bytes.size()-start);
    if(!sz)return L"";
    int count=MultiByteToWideChar(cp,MB_ERR_INVALID_CHARS,ptr,sz,nullptr,0);
    if(count<=0){cp=CP_ACP;count=MultiByteToWideChar(cp,0,ptr,sz,nullptr,0);}
    if(count<=0)return L"";
    std::wstring result(count,L'\0');MultiByteToWideChar(cp,cp==CP_UTF8?MB_ERR_INVALID_CHARS:0,ptr,sz,result.data(),count);
    return result;
#else
    (void)path;return L"";
#endif
}
