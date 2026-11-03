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
// Enhanced LRC timestamps are absolute song positions. Offset applies to all of them.
void Normalize(ParsedLyrics& data,int offset) {
    for(auto& line:data.lines){
        line.startMs=static_cast<int>(std::clamp(static_cast<long long>(line.startMs)+offset,0LL,86400000LL));
        for(auto& word:line.words){
            word.startMs=static_cast<int>(std::clamp(static_cast<long long>(word.startMs)+offset,0LL,86400000LL));
            if(word.endMs>0)word.endMs=static_cast<int>(std::clamp(static_cast<long long>(word.endMs)+offset,0LL,86400000LL));
        }
    }
    std::stable_sort(data.lines.begin(),data.lines.end(),[](const auto& a,const auto& b){return a.startMs<b.startMs;});
    std::vector<LyricLine> merged;
    for(const auto& line:data.lines){
        if(!merged.empty() && merged.back().startMs==line.startMs){
            if(!line.text.empty()){
                if(!merged.back().text.empty())merged.back().text+=L"\n";
                merged.back().text+=line.text;
            }
            // Two simultaneous vocal parts have no unambiguous per-word alignment.
            merged.back().words.clear();
        } else merged.push_back(line);
    }
    data.lines.swap(merged);
    for(size_t i=0;i<data.lines.size();++i){
        auto& line=data.lines[i];
        const int end=i+1<data.lines.size()?data.lines[i+1].startMs:line.startMs+4000;
        line.endMs=std::max(line.startMs+1,end);
        for(size_t j=0;j<line.words.size();++j){
            auto& word=line.words[j];
            word.startMs=std::clamp(word.startMs,line.startMs,line.endMs);
            const int next=j+1<line.words.size()?line.words[j+1].startMs:line.endMs;
            word.endMs=std::clamp(word.endMs>word.startMs?word.endMs:next,word.startMs+1,std::max(word.startMs+1,line.endMs));
        }
    }
}
// Parse <mm:ss.xx>word segments; do not manufacture word timings for plain LRC.
LyricLine ParseEnhancedLine(int startMs,const std::wstring& body){
    LyricLine result;result.startMs=startMs;
    std::wstring plain;size_t p=0;int current=-1;std::wstring prefix;
    bool sawTag=false;
    while(p<body.size()){
        if(body[p]!=L'<'){prefix.push_back(body[p++]);continue;}
        const size_t end=body.find(L'>',p+1);
        int stamp=0;
        if(end==std::wstring::npos||!Timestamp(body.substr(p+1,end-p-1),stamp)){
            prefix.push_back(body[p++]);continue;
        }
        sawTag=true;
        if(current>=0 && !prefix.empty()){
            result.words.push_back({current,stamp,prefix});
        } else if(current<0 && !Trim(prefix).empty()){
            // The prefix has no timestamp: preserve content, drop precision.
            result.words.clear();
            sawTag=false;
        }
        plain+=prefix;prefix.clear();
        current=stamp;p=end+1;
    }
    if(current>=0 && !prefix.empty())result.words.push_back({current,0,prefix});
    if(current>=0 && prefix.empty()&&!result.words.empty())
        result.words.back().endMs=current; // optional closing tag
    plain+=prefix;
    result.text=Trim(plain);
    if(sawTag && !result.words.empty()){
        // Trim only the boundaries; keep spaces between words verbatim.
        auto& first=result.words.front().text;
        first.erase(0,first.find_first_not_of(L" \t"));
        auto& last=result.words.back().text;
        const auto lastNonSpace=last.find_last_not_of(L" \t");
        if(lastNonSpace!=std::wstring::npos)last.erase(lastNonSpace+1);
        if(result.words.size()>256||result.words.front().startMs<startMs)
            result.words.clear();
        std::wstring rebuilt;
        for(const auto& w:result.words)rebuilt+=w.text;
        if(rebuilt!=result.text)result.words.clear();
    } else result.words.clear();
    return result;
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
            for(int t:times)out.lines.push_back(ParseEnhancedLine(t,Trim(row.substr(p))));
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
