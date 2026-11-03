#include "PipeServer.h"
#include "CommandProtocol.h"
#include <algorithm>
#include <cstdlib>
#include <chrono>
#include <sddl.h>
#include <vector>

namespace {
PSECURITY_DESCRIPTOR CurrentUserPipeAcl() {
    HANDLE token=nullptr;
    if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token))return nullptr;
    DWORD size=0;
    GetTokenInformation(token,TokenUser,nullptr,0,&size);
    std::vector<unsigned char> buffer(size);
    bool ok=size && GetTokenInformation(token,TokenUser,buffer.data(),size,&size);
    CloseHandle(token);
    if(!ok)return nullptr;
    auto* user=reinterpret_cast<TOKEN_USER*>(buffer.data());
    LPWSTR sid=nullptr;
    if(!ConvertSidToStringSidW(user->User.Sid,&sid))return nullptr;
    const std::wstring sddl=L"D:P(A;;GA;;;SY)(A;;GA;;;"+std::wstring(sid)+L")";
    LocalFree(sid);
    PSECURITY_DESCRIPTOR descriptor=nullptr;
    if(!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(),SDDL_REVISION_1,&descriptor,nullptr))return nullptr;
    return descriptor;
}
}
PipeServer::PipeServer(std::function<void(WPARAM,LPARAM)> dispatchCommand):dispatchCommand_(std::move(dispatchCommand)) {
    name_=L"AimpKaraokeStudio."+std::to_wstring(GetCurrentProcessId());
}
PipeServer::~PipeServer(){Stop();}
bool PipeServer::Start(){
    stop_=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    if(!stop_) return false;
    try { worker_=std::thread([this](){Run();}); }
    catch (...) {CloseHandle(stop_);stop_=nullptr;return false;}
    return true;
}
void PipeServer::Stop(){
    if(!stop_)return;
    SetEvent(stop_);
    if(worker_.joinable())worker_.join();
    CloseHandle(stop_);stop_=nullptr;
}
void PipeServer::Publish(const std::string& kind,const std::string& json){
    // Never let an unexpectedly large metadata payload poison reconnect.
    const std::string safe=json.size()>900000 ?
        "{\"type\":\"protocol-error\",\"error\":\"oversize\"}" : json;
    std::lock_guard lock(mu_);
    latest_[kind]=safe;
    if(!connected_)return;
    if(kind=="position") {
        // Drop stale position messages; never let updates accumulate.
        for(auto it=queue_.begin();it!=queue_.end();) {
            if(it->rfind("{\"type\":\"position\"",0)==0)it=queue_.erase(it);
            else ++it;
        }
    }
    if(queue_.size()>96)queue_.pop_front();
    queue_.push_back(safe);
}

bool PipeServer::WriteLine(HANDLE pipe,HANDLE stopping,const std::string& line){
    if(line.size()>1024*1024) return false;
    std::string data=line+"\n";
    HANDLE done=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    if(!done)return false;
    OVERLAPPED ov{};ov.hEvent=done;
    DWORD transferred=0;
    bool ok=WriteFile(pipe,data.data(),static_cast<DWORD>(data.size()),nullptr,&ov)!=0;
    if(!ok && GetLastError()==ERROR_IO_PENDING){
        HANDLE waits[]={stopping,done};
        DWORD result=WaitForMultipleObjects(2,waits,FALSE,500);
        if(result==WAIT_OBJECT_0+1)ok=GetOverlappedResult(pipe,&ov,&transferred,FALSE)!=0;
        else {CancelIoEx(pipe,&ov);WaitForSingleObject(done,INFINITE);ok=false;}
    } else if(ok)ok=GetOverlappedResult(pipe,&ov,&transferred,TRUE)!=0;
    CloseHandle(done);
    return ok&&transferred==data.size();
}
bool PipeServer::ReadCommands(HANDLE pipe,std::string& buffer){
    DWORD available=0;
    if(!PeekNamedPipe(pipe,nullptr,0,nullptr,&available,nullptr))return false;
    if(!available)return true;
    if(available>8192 || buffer.size()>8192)return false;
    char data[4096];
    HANDLE done=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    if(!done)return false;
    OVERLAPPED ov{};ov.hEvent=done;
    DWORD count=0;
    bool ok=ReadFile(pipe,data,std::min<DWORD>(available,sizeof(data)),nullptr,&ov)!=0;
    if(!ok && GetLastError()==ERROR_IO_PENDING) {
        HANDLE waits[]={stop_,done};
        DWORD result=WaitForMultipleObjects(2,waits,FALSE,200);
        if(result==WAIT_OBJECT_0+1)ok=GetOverlappedResult(pipe,&ov,&count,FALSE)!=0;
        else {CancelIoEx(pipe,&ov);WaitForSingleObject(done,INFINITE);ok=false;}
    } else if(ok)ok=GetOverlappedResult(pipe,&ov,&count,TRUE)!=0;
    CloseHandle(done);
    if(!ok||!count)return false;
    buffer.append(data,data+count);
    size_t p=0;
    while((p=buffer.find('\n'))!=std::string::npos) {
        ParseCommand(buffer.substr(0,p));
        buffer.erase(0,p+1);
    }
    return buffer.size()<8192;
}
// The worker parses IPC only. It never calls AIMP player methods directly;
// the callback schedules work via IAIMPServiceThreads::ExecuteInMainThread.
void PipeServer::QueueCommand(WPARAM command,LPARAM argument){
    if(dispatchCommand_)dispatchCommand_(command,argument);
}
void PipeServer::ParseCommand(const std::string& input){
    const auto frame=DecodeKaraokeCommand(input);
    switch(frame.kind) {
        case KaraokeCommand::Toggle:QueueCommand(KAR_CMD_TOGGLE,0);break;
        case KaraokeCommand::Next:QueueCommand(KAR_CMD_NEXT,0);break;
        case KaraokeCommand::Previous:QueueCommand(KAR_CMD_PREVIOUS,0);break;
        case KaraokeCommand::SeekMs:QueueCommand(KAR_CMD_SEEK_MS,static_cast<LPARAM>(frame.positionMs));break;
        default:break;
    }
}

void PipeServer::Run(){
    const auto full=L"\\\\.\\pipe\\"+name_;
    while(WaitForSingleObject(stop_,0)!=WAIT_OBJECT_0){
        PSECURITY_DESCRIPTOR dacl=CurrentUserPipeAcl();
        if(!dacl)return; // Never fall back to an unrestricted pipe.
        SECURITY_ATTRIBUTES sa{sizeof(SECURITY_ATTRIBUTES),dacl,FALSE};
        HANDLE pipe=CreateNamedPipeW(full.c_str(),PIPE_ACCESS_DUPLEX|FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_BYTE|PIPE_READMODE_BYTE|PIPE_WAIT|PIPE_REJECT_REMOTE_CLIENTS,1,262144,262144,0,&sa);
        LocalFree(dacl);
        if(pipe==INVALID_HANDLE_VALUE)return;
        HANDLE done=CreateEventW(nullptr,TRUE,FALSE,nullptr);
        if(!done){CloseHandle(pipe);return;}
        OVERLAPPED ov{};ov.hEvent=done;
        BOOL result=ConnectNamedPipe(pipe,&ov);
        bool attached=result!=0;
        if(!attached){
            DWORD err=GetLastError();
            if(err==ERROR_PIPE_CONNECTED)attached=true;
            else if(err==ERROR_IO_PENDING){
                HANDLE waits[]={stop_,done};
                DWORD wr=WaitForMultipleObjects(2,waits,FALSE,INFINITE);
                DWORD connectedBytes=0;
                attached=(wr==WAIT_OBJECT_0+1&&GetOverlappedResult(pipe,&ov,&connectedBytes,FALSE));
                if(!attached){CancelIoEx(pipe,&ov);WaitForSingleObject(done,INFINITE);}
            }
        }
        CloseHandle(done);
        if(attached){
            {
                std::lock_guard lock(mu_);
                connected_=true;
                queue_.clear();
                for(const auto* key:{"track","lyrics","artwork","state","position"}){
                    auto it=latest_.find(key);
                    if(it!=latest_.end())queue_.push_back(it->second);
                }
            }
            std::string incoming;
            while(WaitForSingleObject(stop_,0)!=WAIT_OBJECT_0){
                std::string message;
                {
                    std::lock_guard lock(mu_);
                    if(!queue_.empty()){message=std::move(queue_.front());queue_.pop_front();}
                }
                if(!message.empty()&&!WriteLine(pipe,stop_,message))break;
                if(!ReadCommands(pipe,incoming))break;
                if(message.empty())Sleep(16);
            }
        }
        {
           std::lock_guard lock(mu_);
           connected_=false;
           queue_.clear();
        }
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }
}
