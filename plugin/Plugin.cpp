#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <filesystem>
#include <atomic>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <vector>
#include <algorithm>
#include <new>
#include <cwchar>
#include <utility>
#include <condition_variable>
#include <cstdint>
#include "apiPlugin.h"
#include "apiPlayer.h"
#include "apiLyrics.h"
#include "apiAlbumArt.h"
#include "apiFileManager.h"
#include "apiMessages.h"
#include "apiMenu.h"
#include "PipeServer.h"
#include "JsonUtil.h"
#include "Lrc.h"

namespace {
HMODULE g_dll=nullptr;
void Diagnostic(const wchar_t* message){OutputDebugStringW(message);OutputDebugStringW(L"\n");}
std::mutex g_requestsMutex;
std::condition_variable g_callbacksDone;
size_t g_callbacksActive=0;
std::atomic<uintptr_t> g_requestSequence{1};
struct Request {
    std::weak_ptr<PipeServer> bridge;
    std::shared_ptr<std::atomic<int>> epoch;
    int expectedEpoch=0;
    ParsedLyrics fallback;
    TTaskHandle handle=0;
};
std::map<uintptr_t,Request> g_requests;
struct ArtworkRequest {
    std::weak_ptr<PipeServer> bridge;
    std::shared_ptr<std::atomic<int>> epoch;
    int expectedEpoch=0;
    TTaskHandle handle=0;
};
std::map<uintptr_t,ArtworkRequest> g_artRequests;


std::wstring Property(IAIMPFileInfo* file,int id){
    IAIMPString* s=nullptr;
    if(!file||FAILED(file->GetValueAsObject(id,IID_IAIMPString,(void**)&s))||!s)return {};
    std::wstring value(s->GetData(),s->GetLength());
    s->Release();return value;
}
std::wstring FromAimp(IAIMPString* s){
    return s?std::wstring(s->GetData(),s->GetLength()):std::wstring{};
}
// Cap total UTF-8 payload instead of just limiting the number of lines.
// A rejected payload becomes an explicit error rather than a disconnect loop.
std::string AsLyricsJson(const ParsedLyrics& data,int generation,const char* source){
    std::string out="{\"type\":\"lyrics\",\"generation\":"+std::to_string(generation)
       +",\"source\":\""+source+"\",\"synced\":"+(data.synced?"true":"false")+",\"lines\":[";
    unsigned count=0;
    for(const auto& ln:data.lines){
        if(count>=5000)break;
        std::wstring capped=ln.text.substr(0,8192);
        std::string entry="{\"startMs\":"+std::to_string(ln.startMs)+",\"endMs\":"+std::to_string(ln.endMs)
            +",\"text\":"+JsonString(capped);
        if(!ln.words.empty()){
            entry+=",\"words\":[";
            for(size_t wi=0;wi<ln.words.size();++wi){
                const auto& w=ln.words[wi];
                if(wi)entry+=",";
                entry+="{\"startMs\":"+std::to_string(w.startMs)+",\"endMs\":"+std::to_string(w.endMs)
                    +",\"text\":"+JsonString(w.text)+"}";
            }
            entry+="]";
        }
        entry+="}";
        if(out.size()+entry.size()+2>800000){
            return "{\"type\":\"lyrics\",\"generation\":"+std::to_string(generation)
                +",\"source\":\"error\",\"synced\":false,\"error\":\"lyrics_too_large\",\"lines\":[]}";
        }
        if(count++)out+=",";
        out+=entry;
    }
    return out+"]}";
}
ParsedLyrics DecodeLyrics(IAIMPLyrics* lyrics){
    ParsedLyrics data;
    if(!lyrics)return data;
    int type=AIMP_LYRICS_TYPE_UNKNOWN;
    lyrics->GetValueAsInt32(AIMP_LYRICS_PROPID_TYPE,&type);
    // AIMP supplies start/end in milliseconds. OFFSET is metadata; its
    // treatment must be verified against the actual player's lyrics panel.
    // Do not add it blindly here, which could double-shift the timeline.
    int aimpOffset=0;
    lyrics->GetValueAsInt32(AIMP_LYRICS_PROPID_OFFSET,&aimpOffset);
    (void)aimpOffset;
    if(type==AIMP_LYRICS_TYPE_SYNCED){
        int count=0;
        if(SUCCEEDED(lyrics->GetCount(&count))){
            for(int i=0;i<std::min(count,5000);++i){
                int start=0,end=0;
                IAIMPString* text=nullptr;
                if(SUCCEEDED(lyrics->Get(i,&start,&end,&text))&&text){
                    data.lines.push_back({std::max(0,start),std::max(0,end),FromAimp(text)});
                    text->Release();
                }
            }
            if(!data.lines.empty()){
                data.synced=true;
                std::stable_sort(data.lines.begin(),data.lines.end(),[](auto& a,auto& b){return a.startMs<b.startMs;});
                // Merge equal timestamp entries (e.g. bilingual lyric lines).
                std::vector<LyricLine> merged;
                for(const auto& ln:data.lines){
                    if(!merged.empty() && ln.startMs==merged.back().startMs){
                        if(!ln.text.empty()){
                            if(!merged.back().text.empty())merged.back().text+=L"\n";
                            merged.back().text+=ln.text;
                        }
                        merged.back().endMs=std::max(merged.back().endMs,ln.endMs);
                    }else merged.push_back(ln);
                }
                data.lines.swap(merged);
                for(size_t i=0;i<data.lines.size();++i){
                    if(data.lines[i].endMs<=data.lines[i].startMs){
                        data.lines[i].endMs=i+1<data.lines.size()?data.lines[i+1].startMs:data.lines[i].startMs+4000;
                    }
                }
                return data;
            }
        }
    }
    IAIMPString* content=nullptr;
    if(SUCCEEDED(lyrics->GetValueAsObject(AIMP_LYRICS_PROPID_TEXT,IID_IAIMPString,(void**)&content))&&content){
        data=ParseLrc(FromAimp(content));
        content->Release();
    }
    return data;
}
void CALLBACK OnAimpLyrics(IAIMPLyrics* lyrics,void* data){
    const auto id=reinterpret_cast<uintptr_t>(data);
    Request req;
    {
        std::lock_guard lock(g_requestsMutex);
        ++g_callbacksActive;
        auto it=g_requests.find(id);
        if(it!=g_requests.end()){
            req=it->second;
            g_requests.erase(it);
        }
    }
    // All callback exits decrement under the same lock; Finalize waits for
    // already-entered callbacks after canceling SDK tasks.
    struct ExitGuard {
        ~ExitGuard(){
            std::lock_guard lock(g_requestsMutex);
            --g_callbacksActive;
            g_callbacksDone.notify_all();
        }
    } guard;
    auto bridge=req.bridge.lock();
    if(!bridge || !req.epoch || req.epoch->load()!=req.expectedEpoch)return;
    auto decoded=DecodeLyrics(lyrics);
    if(decoded.lines.empty()||(!decoded.synced && req.fallback.synced))decoded=std::move(req.fallback);
    if(req.epoch->load()==req.expectedEpoch)
        bridge->Publish("lyrics",AsLyricsJson(decoded,req.expectedEpoch,"aimp-or-fallback"));
}
std::vector<TTaskHandle> TakeRequests(const std::shared_ptr<std::atomic<int>>& epoch){
    std::vector<TTaskHandle> pending;
    std::lock_guard lock(g_requestsMutex);
    for(auto it=g_requests.begin();it!=g_requests.end();){
        if(it->second.epoch==epoch){
            if(it->second.handle)pending.push_back(it->second.handle);
            it=g_requests.erase(it);
        }else ++it;
    }
    return pending;
}
// Native AIMP cover service: receive offline embedded/local cover artwork.
// Do not assume IAIMPImageContainer contains encoded image bytes unless its
// signature is a recognizable PNG/JPEG. Reject other formats gracefully.
std::string Base64(const BYTE* bytes,size_t size){
    static constexpr char table[]="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string out;out.reserve(((size+2)/3)*4);
    for(size_t i=0;i<size;i+=3){
        const uint32_t value=(static_cast<uint32_t>(bytes[i])<<16)
            |(i+1<size?static_cast<uint32_t>(bytes[i+1])<<8:0)
            |(i+2<size?static_cast<uint32_t>(bytes[i+2]):0);
        out+=table[(value>>18)&63];out+=table[(value>>12)&63];
        out+=i+1<size?table[(value>>6)&63]:'=';
        out+=i+2<size?table[value&63]:'=';
    }
    return out;
}
void CALLBACK OnAimpArtwork(IAIMPImage*,IAIMPImageContainer* container,void* user){
    const auto id=reinterpret_cast<uintptr_t>(user);
    ArtworkRequest request;
    {
        std::lock_guard lock(g_requestsMutex);
        ++g_callbacksActive;
        auto it=g_artRequests.find(id);
        if(it!=g_artRequests.end()){request=it->second;g_artRequests.erase(it);}
    }
    struct ExitGuard{~ExitGuard(){std::lock_guard lock(g_requestsMutex);--g_callbacksActive;g_callbacksDone.notify_all();}} guard;
    auto bridge=request.bridge.lock();
    if(!bridge||!request.epoch||request.epoch->load()!=request.expectedEpoch||!container)return;
    const auto bytes=container->GetData();const auto n=container->GetDataSize();
    if(!bytes || n<8 || n>450000)return;
    const auto* b=reinterpret_cast<const BYTE*>(bytes);
    const bool png=n>=8&&b[0]==0x89&&b[1]=='P'&&b[2]=='N'&&b[3]=='G';
    const bool jpeg=n>=3&&b[0]==0xFF&&b[1]==0xD8&&b[2]==0xFF;
    if(!png&&!jpeg)return;
    const auto payload=Base64(b,n);
    if(request.epoch->load()==request.expectedEpoch)
        bridge->Publish("artwork","{\"type\":\"artwork\",\"generation\":"+std::to_string(request.expectedEpoch)
            +",\"artworkData\":\""+payload+"\"}");
}
std::vector<TTaskHandle> TakeArtRequests(const std::shared_ptr<std::atomic<int>>& epoch){
    std::vector<TTaskHandle> ids;
    std::lock_guard lock(g_requestsMutex);
    for(auto it=g_artRequests.begin();it!=g_artRequests.end();){
        if(it->second.epoch==epoch){if(it->second.handle)ids.push_back(it->second.handle);it=g_artRequests.erase(it);}
        else ++it;
    }
    return ids;
}
LRESULT CALLBACK CommandsWndProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp);
} // namespace

class KaraokePlugin final:public IAIMPPlugin,public IAIMPMessageHook,public IAIMPActionEvent {
public:
    HRESULT WINAPI QueryInterface(REFIID id,void** value) override {
        if(!value)return E_POINTER;
        *value=nullptr;
        if(id==IID_IUnknown) *value=static_cast<IAIMPPlugin*>(this);
        else if(id==IID_IAIMPMessageHook)*value=static_cast<IAIMPMessageHook*>(this);
        else if(id==IID_IAIMPActionEvent)*value=static_cast<IAIMPActionEvent*>(this);
        if(!*value)return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    ULONG WINAPI AddRef() override{return static_cast<ULONG>(InterlockedIncrement(&refs_));}
    ULONG WINAPI Release() override {
        LONG left=InterlockedDecrement(&refs_);
        if(!left)delete this;
        return static_cast<ULONG>(left);
    }
    TChar* WINAPI InfoGet(int id) override {
        switch(id){
            case AIMP_PLUGIN_INFO_NAME:return (TChar*)L"Karaoke Studio";
            case AIMP_PLUGIN_INFO_AUTHOR:return (TChar*)L"Karaoke Studio project";
            case AIMP_PLUGIN_INFO_SHORT_DESCRIPTION:return (TChar*)L"Independent synchronized lyrics window for AIMP 5.40";
            default:return nullptr;
        }
    }
    LongWord WINAPI InfoGetCategories() override{return AIMP_PLUGIN_CATEGORY_ADDONS;}
    HRESULT WINAPI Initialize(IAIMPCore* core) override {
        if(!core)return E_POINTER;
        core_=core;core_->AddRef();
        core_->QueryInterface(IID_IAIMPServicePlayer,(void**)&player_);
        core_->QueryInterface(IID_IAIMPServiceLyrics,(void**)&lyrics_);
        core_->QueryInterface(IID_IAIMPServiceAlbumArt,(void**)&art_);
        core_->QueryInterface(IID_IAIMPServiceMessageDispatcher,(void**)&dispatcher_);
        if(!player_||!dispatcher_){Diagnostic(L"[KaraokeStudio] AIMP Player/Dispatcher service missing");Finalize();return E_NOINTERFACE;}
        WNDCLASSW wc{};wc.lpfnWndProc=CommandsWndProc;wc.hInstance=g_dll;wc.lpszClassName=L"AimpKaraokeStudioCommandWindow";
        RegisterClassW(&wc);
        commandsWindow_=CreateWindowExW(0,wc.lpszClassName,L"",0,0,0,0,0,HWND_MESSAGE,nullptr,g_dll,nullptr);
        if(!commandsWindow_){Diagnostic(L"[KaraokeStudio] Failed to create command window");Finalize();return E_FAIL;}
        SetWindowLongPtrW(commandsWindow_,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(this));
        pipe_=std::make_shared<PipeServer>(commandsWindow_);
        if(!pipe_->Start()){Diagnostic(L"[KaraokeStudio] Pipe server failed to start");Finalize();return E_FAIL;}
        if(SUCCEEDED(dispatcher_->Hook(this)))hooked_=true;
        IAIMPServiceMenuManager* menus=nullptr;
        if(SUCCEEDED(core_->QueryInterface(IID_IAIMPServiceMenuManager,(void**)&menus))&&menus){
            IAIMPMenuItem* parent=nullptr;
            if(SUCCEEDED(menus->GetBuiltIn(AIMP_MENUID_PLAYER_MAIN_FUNCTIONS,&parent))&&parent){
                if(SUCCEEDED(core_->CreateObject(IID_IAIMPMenuItem,(void**)&menu_))&&menu_){
                    auto id=MakeString(L"karaoke.studio.open");
                    auto caption=MakeString(L"Karaoke Studio — open lyrics window");
                    if(id){menu_->SetValueAsObject(AIMP_MENUITEM_PROPID_ID,id);id->Release();}
                    if(caption){menu_->SetValueAsObject(AIMP_MENUITEM_PROPID_CAPTION,caption);caption->Release();}
                    menu_->SetValueAsObject(AIMP_MENUITEM_PROPID_EVENT,static_cast<IAIMPActionEvent*>(this));
                    menu_->SetValueAsObject(AIMP_MENUITEM_PROPID_PARENT,parent);
                    core_->RegisterExtension(IID_IAIMPServiceMenuManager,menu_);
                }
                parent->Release();
            }
            menus->Release();
        }
        RefreshTrack(true);
        Diagnostic(L"[KaraokeStudio] Plugin Initialized");
        return S_OK;
    }
    HRESULT WINAPI Finalize() override {
        if(epoch_)++(*epoch_);
        CancelRequests();
        if(hooked_&&dispatcher_){dispatcher_->Unhook(this);hooked_=false;}
        if(menu_){if(core_)core_->UnregisterExtension(menu_);menu_->Release();menu_=nullptr;}
        if(pipe_){pipe_->Stop();pipe_.reset();}
        if(commandsWindow_){DestroyWindow(commandsWindow_);commandsWindow_=nullptr;}
        if(lyrics_){lyrics_->Release();lyrics_=nullptr;}
        if(art_){art_->Release();art_=nullptr;}
        if(player_){player_->Release();player_=nullptr;}
        if(dispatcher_){dispatcher_->Release();dispatcher_=nullptr;}
        if(core_){core_->Release();core_=nullptr;}
        return S_OK;
    }
    void WINAPI SystemNotification(int, IUnknown*) override{}
    void WINAPI OnExecute(IUnknown*) override{LaunchWindow();}
    void WINAPI CoreMessage(LongWord message,int a,void* p,HRESULT*) override {
        // Secondary drain path when AIMP dispatches events. Commands are
        // executed exactly once even if a Windows wake message also arrives.
        DrainCommands();
        switch(message){
            case AIMP_MSG_EVENT_STREAM_START:
            case AIMP_MSG_EVENT_STREAM_START_SUBTRACK:RefreshTrack(true);break;
            case AIMP_MSG_EVENT_PLAYING_FILE_INFO:RefreshTrack(false);break;
            case AIMP_MSG_EVENT_PLAYER_STATE:SendState(a);SendPosition();break;
            case AIMP_MSG_EVENT_PLAYER_UPDATE_POSITION_HR:SendPosition();break;
            case AIMP_MSG_EVENT_PROPERTY_VALUE:
                if(a==AIMP_MSG_PROPERTY_PLAYER_POSITION)SendPosition();
                break;
            case AIMP_MSG_EVENT_STREAM_END:SendState(AIMP_PLAYER_STATE_STOPPED);break;
            default:break;
        }
    }
    void DrainCommands(){
        if(!pipe_)return;
        for(auto [id,value]:pipe_->TakeCommands())ExecuteCommand(id,value);
    }
    void ExecuteCommand(WPARAM command,LPARAM argument){
        if(!player_)return;
        switch(command){
            case KAR_CMD_TOGGLE:
                if(player_->GetState()==AIMP_PLAYER_STATE_PLAYING)player_->Pause();
                else if(player_->GetState()==AIMP_PLAYER_STATE_PAUSED)player_->Resume();
                break;
            case KAR_CMD_NEXT:player_->GoToNext();break;
            case KAR_CMD_PREVIOUS:player_->GoToPrev();break;
            case KAR_CMD_SEEK_MS:player_->SetPosition(static_cast<double>(argument)/1000.0);break;
        }
    }
private:
    void CancelRequests(){
        const auto ids=TakeRequests(epoch_);
        if(lyrics_){
            // WAITFOR prevents returning while an SDK task is still running.
            for(auto id:ids)if(FAILED(lyrics_->Cancel(id,AIMP_SERVICE_LYRICS_FLAGS_WAITFOR)))
                Diagnostic(L"[KaraokeStudio] WARN lyrics cancellation failed; verify shutdown on Windows");
        }
        const auto artworkIds=TakeArtRequests(epoch_);
        if(art_)for(auto id:artworkIds)
            if(FAILED(art_->Cancel(id,AIMP_SERVICE_ALBUMART_FLAGS_WAITFOR)))
                Diagnostic(L"[KaraokeStudio] WARN artwork cancellation failed");
        std::unique_lock lock(g_requestsMutex);
        g_callbacksDone.wait(lock,[]{return g_callbacksActive==0;});
    }
    ~KaraokePlugin(){Finalize();}
    IAIMPString* MakeString(const wchar_t* text){
        IAIMPString* result=nullptr;
        if(core_&&SUCCEEDED(core_->CreateObject(IID_IAIMPString,(void**)&result))&&result)
            result->SetData((TChar*)text,static_cast<int>(wcslen(text)));
        return result;
    }
    void SendState(int state){
        if(pipe_)pipe_->Publish("state","{\"type\":\"state\",\"generation\":"+std::to_string(epoch_->load())+
            ",\"state\":"+std::to_string(state)+"}");
    }
    void SendPosition(){
        if(!pipe_||!player_)return;
        double seconds=0;
        if(SUCCEEDED(player_->GetPosition(&seconds)) && seconds>=0 && seconds<86400)
            pipe_->Publish("position","{\"type\":\"position\",\"generation\":"+std::to_string(epoch_->load())+
                ",\"positionMs\":"+std::to_string(static_cast<long long>(seconds*1000.0))+"}");
    }
    void RefreshTrack(bool force){
        if(!player_||!pipe_)return;
        IAIMPFileInfo* fi=nullptr;
        if(FAILED(player_->GetInfo(&fi))||!fi)return;
        std::wstring file=Property(fi,AIMP_FILEINFO_PROPID_FILENAME),
            artist=Property(fi,AIMP_FILEINFO_PROPID_ARTIST),title=Property(fi,AIMP_FILEINFO_PROPID_TITLE),
            album=Property(fi,AIMP_FILEINFO_PROPID_ALBUM);
        std::wstring identity=file+L"\n"+artist+L"\n"+title;
        if(!force&&identity==currentIdentity_){fi->Release();return;}
        currentIdentity_=identity;
        int generation=++(*epoch_);
        CancelRequests();
        double duration=0;
        fi->GetValueAsFloat(AIMP_FILEINFO_PROPID_DURATION,&duration);
        // Restrict metadata size independently of the lyrics JSON cap.
        std::string info="{\"type\":\"track\",\"generation\":"+std::to_string(generation)
            +",\"title\":"+JsonString(title.substr(0,4096))+",\"artist\":"+JsonString(artist.substr(0,4096))
            +",\"filePath\":"+JsonString(file.substr(0,4096))+",\"album\":"+JsonString(album.substr(0,4096))
            +",\"durationMs\":"+std::to_string(static_cast<long long>(std::max(0.0,duration)*1000.0))+"}";
        pipe_->Publish("track",info);
        pipe_->Publish("lyrics",AsLyricsJson({},generation,"loading"));
        pipe_->Publish("artwork","{\"type\":\"artwork\",\"generation\":"+std::to_string(generation)+",\"artworkData\":\"\"}");
        if(art_){
            const auto id=g_requestSequence.fetch_add(1);
            {std::lock_guard lock(g_requestsMutex);g_artRequests.emplace(id,ArtworkRequest{pipe_,epoch_,generation,0});}
            TTaskHandle handle=0;
            HRESULT artResult=art_->Get2(fi,AIMP_SERVICE_ALBUMART_FLAGS_OFFLINE,OnAimpArtwork,reinterpret_cast<void*>(id),&handle);
            std::lock_guard lock(g_requestsMutex);
            auto it=g_artRequests.find(id);
            if(it!=g_artRequests.end()){
                if(FAILED(artResult))g_artRequests.erase(it);
                else it->second.handle=handle;
            }
        }
        SendState(player_->GetState());SendPosition();
        ParsedLyrics local;
        if(!file.empty()){
            try {
                std::filesystem::path path(file);
                if(path.has_extension()){
                    path.replace_extension(L".lrc");
                    local=ParseLrc(ReadLyricsFile(path.wstring()));
                }
            }catch(...){}
        }
        if(local.synced){pipe_->Publish("lyrics",AsLyricsJson(local,generation,"local-lrc"));fi->Release();return;}
        ParsedLyrics fallback=local;
        auto embedded=ParseLrc(Property(fi,AIMP_FILEINFO_PROPID_LYRICS));
        if(fallback.lines.empty() || embedded.synced)fallback=std::move(embedded);
        if(!fallback.lines.empty())pipe_->Publish("lyrics",AsLyricsJson(fallback,generation,"embedded-or-local"));
        // Prefer the existing AIMP Lyrics service (embedded tags, cached and online providers).
        if(lyrics_){
            uintptr_t id=g_requestSequence.fetch_add(1);
            {
                std::lock_guard lock(g_requestsMutex);
                g_requests.emplace(id,Request{pipe_,epoch_,generation,fallback});
            }
            TTaskHandle task=0;
            HRESULT hr=lyrics_->Get(fi,0,OnAimpLyrics,reinterpret_cast<void*>(id),&task);
            if(FAILED(hr)){
                std::lock_guard lock(g_requestsMutex);g_requests.erase(id);
            } else {
                // Inline completion is permitted: the callback might have
                // removed the request before Get() returns.
                std::lock_guard lock(g_requestsMutex);
                auto it=g_requests.find(id);
                if(it!=g_requests.end())it->second.handle=task;
                fi->Release();return;
            }
        }
        // Fallback for versions/configurations without the lyrics service.
        pipe_->Publish("lyrics",AsLyricsJson(fallback,generation,fallback.lines.empty()?"missing":"embedded-or-local"));
        fi->Release();
    }
    void LaunchWindow(){
        if(!pipe_)return;
        const auto caption=L"AIMP Karaoke Studio - "+pipe_->ShortName();
        if(HWND existing=FindWindowW(nullptr,caption.c_str())){
            ShowWindow(existing,SW_RESTORE);SetForegroundWindow(existing);return;
        }
        wchar_t modulePath[MAX_PATH]{};
        if(!GetModuleFileNameW(g_dll,modulePath,MAX_PATH))return;
        std::filesystem::path exe=std::filesystem::path(modulePath).parent_path()/L"KaraokeStudio.exe";
        if(!std::filesystem::exists(exe)){
            MessageBoxW(nullptr,L"KaraokeStudio.exe was not found next to the plugin DLL.\nRun build.ps1 to publish the WPF application.",L"Karaoke Studio",MB_OK|MB_ICONERROR);
            return;
        }
        std::wstring args=L"--pipe "+pipe_->ShortName();
        ShellExecuteW(nullptr,L"open",exe.c_str(),args.c_str(),exe.parent_path().c_str(),SW_SHOWNORMAL);
    }
    LONG refs_=1;
    IAIMPCore* core_=nullptr;
    IAIMPServicePlayer* player_=nullptr;
    IAIMPServiceLyrics* lyrics_=nullptr;
    IAIMPServiceAlbumArt* art_=nullptr;
    IAIMPServiceMessageDispatcher* dispatcher_=nullptr;
    IAIMPMenuItem* menu_=nullptr;
    HWND commandsWindow_=nullptr;
    bool hooked_=false;
    std::wstring currentIdentity_;
    std::shared_ptr<PipeServer> pipe_;
    std::shared_ptr<std::atomic<int>> epoch_=std::make_shared<std::atomic<int>>(0);
};
namespace {
LRESULT CALLBACK CommandsWndProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp){
    if(msg==WM_KARAOKE_COMMAND){
        auto* plugin=reinterpret_cast<KaraokePlugin*>(GetWindowLongPtrW(hwnd,GWLP_USERDATA));
        if(plugin)plugin->DrainCommands();
        return 0;
    }
    return DefWindowProcW(hwnd,msg,wp,lp);
}
}
extern "C" __declspec(dllexport) HRESULT WINAPI AIMPPluginGetHeader(IAIMPPlugin** out){
    if(!out)return E_POINTER;
    *out=new(std::nothrow) KaraokePlugin();
    return *out?S_OK:E_OUTOFMEMORY;
}
BOOL APIENTRY DllMain(HMODULE module,DWORD reason,LPVOID){
    if(reason==DLL_PROCESS_ATTACH){g_dll=module;DisableThreadLibraryCalls(module);}
    return TRUE;
}
