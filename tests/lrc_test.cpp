#include "../plugin/Lrc.h"
#include <iostream>
#include <stdexcept>
void check(bool ok,const char* why){if(!ok)throw std::runtime_error(why);}
int main(){
 try{
    auto x=ParseLrc(L"[ar:Example]\n[offset:-100]\n[00:01.20][00:02.500]Hello\n[00:04.00]World\n");
    check(x.synced&&x.lines.size()==3,"basic size");
    check(x.lines[0].startMs==1100&&x.lines[1].startMs==2400&&x.lines[2].startMs==3900,"basic time");
    check(x.lines[0].endMs==2400,"end");
    auto plain=ParseLrc(L"One\nTwo\n");check(!plain.synced&&plain.lines.size()==2,"plain");
    auto bad=ParseLrc(L"[01:99]bad\n[00:12.03]good\n");check(bad.lines.size()==1&&bad.lines[0].startMs==12030,"invalid stamp");
    auto equal=ParseLrc(L"[00:01.00]A\n[00:01.00]B\n");
    check(equal.lines.size()==1&&equal.lines[0].text==L"A\nB"&&equal.lines[0].endMs>equal.lines[0].startMs,"bilingual timestamp");
    auto late=ParseLrc(L"[00:10]First\n[offset:1000]\n[00:12]Second\n");
    check(late.lines.size()==2&&late.lines[0].startMs==11000&&late.lines[1].startMs==13000,"late offset");
    auto clipped=ParseLrc(L"[offset:-5000]\n[00:01]early\n");check(clipped.lines[0].startMs==0,"clamped offset");
    auto enhanced=ParseLrc(L"[00:01.00]<00:01.00>Снова <00:01.50>споём <00:02.00>дальше\n");
    check(enhanced.synced && enhanced.lines.size()==1 && enhanced.lines[0].words.size()==3,"enhanced word count");
    check(enhanced.lines[0].words[0].startMs==1000 && enhanced.lines[0].words[0].endMs==1500,"word timing 1");
    check(enhanced.lines[0].words[1].startMs==1500 && enhanced.lines[0].words[1].endMs==2000,"word timing 2");
    check(enhanced.lines[0].text==L"Снова споём дальше","enhanced plain text");
    auto shifted=ParseLrc(L"[00:01.00]<00:01.00>Hello <00:02.00>world\n[offset:500]\n");
    check(shifted.lines[0].words[1].startMs==2500,"enhanced offset");
    std::cout<<"9 LRC parser scenarios passed\n";
 }catch(const std::exception& e){std::cerr<<"FAIL: "<<e.what()<<'\n';return 1;}
}
