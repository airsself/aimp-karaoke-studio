#pragma once
#include <string>
#include <cstdio>
#define NOMINMAX
#include <windows.h>
inline std::string Utf8(const std::wstring& s) {
    if(s.empty()) return {};
    int count=WideCharToMultiByte(CP_UTF8,0,s.data(),static_cast<int>(s.size()),nullptr,0,nullptr,nullptr);
    std::string result(count,'\0');
    if(count) WideCharToMultiByte(CP_UTF8,0,s.data(),static_cast<int>(s.size()),result.data(),count,nullptr,nullptr);
    return result;
}
inline std::string JsonString(const std::wstring& s) {
    std::string in=Utf8(s),out="\"";
    for(unsigned char c:in) {
        switch(c) {
        case '"':out+="\\\"";break;
        case '\\':out+="\\\\";break;
        case '\n':out+="\\n";break;
        case '\r':out+="\\r";break;
        case '\t':out+="\\t";break;
        default:
            if(c<32) {char buf[8];std::snprintf(buf,sizeof(buf),"\\u%04x",c);out+=buf;}
            else out+=static_cast<char>(c);
        }
    }
    return out+="\"";
}
