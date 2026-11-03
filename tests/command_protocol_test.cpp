#include "../plugin/CommandProtocol.h"
#include <iostream>
#include <stdexcept>
#include <string>

void check(bool ok,const char* msg){if(!ok)throw std::runtime_error(msg);}
int main(){
    try {
        using K=KaraokeCommand;
        check(DecodeKaraokeCommand("{\"command\":\"toggle\"}\n").kind==K::Toggle,"LF toggle");
        check(DecodeKaraokeCommand("{\"command\":\"toggle\"}\r\n").kind==K::Toggle,"CRLF toggle");
        check(DecodeKaraokeCommand("{\"command\":\"previous\"}\r\n").kind==K::Previous,"CRLF previous");
        check(DecodeKaraokeCommand("{\"command\":\"status\"}\r\n").kind==K::Status,"CRLF status");
        check(DecodeKaraokeCommand("{\"command\":\"next\"}\r\n").kind==K::Next,"CRLF next");
        auto seek=DecodeKaraokeCommand("{\"command\":\"seek\",\"positionMs\":12000}\r\n");
        check(seek.kind==K::SeekMs&&seek.positionMs==12000,"CRLF seek");
        auto first=DecodeKaraokeCommand("{\"command\":\"seek\",\"positionMs\":0}\n");
        check(first.kind==K::SeekMs && first.positionMs==0,"seek zero");
        check(DecodeKaraokeCommand("{\"command\":\"seek\",\"positionMs\":86400000}").kind==K::SeekMs,"seek max");
        check(DecodeKaraokeCommand("{\"command\":\"seek\",\"positionMs\":86400001}").kind==K::Invalid,"seek over max");
        check(DecodeKaraokeCommand("{\"command\":\"seek\",\"positionMs\":1.5}").kind==K::Invalid,"seek fractional");
        check(DecodeKaraokeCommand("{\"command\":\"seek\",\"positionMs\":-1}").kind==K::Invalid,"seek negative");
        check(DecodeKaraokeCommand("{\"command\":\"toggle\",\"extra\":1}").kind==K::Invalid,"reject extra fields");
        check(DecodeKaraokeCommand("{\"command\":\"invalid\"}").kind==K::Invalid,"unknown command");
        std::cout<<"13 transport command scenarios passed\n";
    }catch(const std::exception& error){
        std::cerr<<"FAIL: "<<error.what()<<'\n';return 1;
    }
}
