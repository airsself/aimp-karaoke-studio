// Integrated AIMP Lyrics Bridge 2.5.0 Batch RC2, ported to the native Karaoke Studio plugin.
// Actions, selected-track snapshot, cancel, safe per-tag updates, LRC sidecar fallback.
// No SetToAll(partial FileInfo), no third-party WebLyrics DLL, no separate AIMP plugin.
#define NOMINMAX
#include <windows.h>
#include <winhttp.h>
#include <algorithm>
#include <atomic>
#include <functional>
#include <memory>
#include <set>
#include <string>
#include <vector>
#include <filesystem>
#include "apiPlugin.h"
#include "apiActions.h"
#include "apiPlaylists.h"
#include "apiFileManager.h"
#include "apiTagEditor.h"
#include "apiMenu.h"
#include "apiMessages.h"
#include "apiPlayer.h"
#include "apiThreading.h"
#include "BatchLyrics.h"

namespace {
template<typename T> void Release(T*& p) { if(p){p->Release();p=nullptr;} }
IAIMPString* MakeAimpString(IAIMPCore* core,const std::wstring& str){
    IAIMPString* v=nullptr;
    if(!core||FAILED(core->CreateObject(IID_IAIMPString,(void**)&v))||!v)return nullptr;
    if(FAILED(v->SetData((TChar*)str.data(),(int)str.size()))) {Release(v);return nullptr;}
    return v;
}
std::wstring Property(IAIMPPropertyList* p,int id){
    IAIMPString* v=nullptr;
    if(!p||FAILED(p->GetValueAsObject(id,IID_IAIMPString,(void**)&v))||!v)return {};
    std::wstring s(v->GetData(),v->GetLength());Release(v);return s;
}
bool IsLocalFile(const std::wstring& path){
    if(path.size()<3||path[1]!=L':'||path[2]!=L'\\'||path.rfind(L"\\\\",0)==0)return false;
    DWORD a=GetFileAttributesW(path.c_str());
    return a!=INVALID_FILE_ATTRIBUTES&&!(a&FILE_ATTRIBUTE_DIRECTORY);
}
std::wstring UrlEncode(const std::wstring& s){
    if(s.size()>2048)return {};
    int n=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,s.data(),(int)s.size(),nullptr,0,nullptr,nullptr);
    if(n<=0||n>8192)return {};
    std::string bytes(n,'\0');
    WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,s.data(),(int)s.size(),bytes.data(),n,nullptr,nullptr);
    const wchar_t* hex=L"0123456789ABCDEF";
    std::wstring out;out.reserve(bytes.size()*3);
    for(unsigned char c:bytes){
        if((c>='A'&&c<='Z')||(c>='a'&&c<='z')||(c>='0'&&c<='9')||c=='-'||c=='_'||c=='.'||c=='~')
            out.push_back((wchar_t)c);
        else {out+=L'%';out+=hex[c>>4];out+=hex[c&15];}
    }
    return out;
}
std::wstring DecodeUtf8(const std::string& str) {
    if(str.empty()||str.size()>300000)return {};
    int n=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,str.data(),(int)str.size(),nullptr,0);
    if(n<=0||n>150000)return {};
    std::wstring out(n,L'\0');
    if(MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,str.data(),(int)str.size(),out.data(),n)!=n)return {};
    return out;
}
std::string EncodeUtf8(const std::wstring& str) {
    if(str.empty()||str.size()>150000)return {};
    int n=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,str.data(),(int)str.size(),nullptr,0,nullptr,nullptr);
    if(n<=0||n>300000)return {};
    std::string out(n,'\0');
    if(WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,str.data(),(int)str.size(),out.data(),n,nullptr,nullptr)!=n)return {};
    return out;
}
struct Track {std::wstring artist,title,album,file;};
enum class Status {Found,Missing,Offline,Bad,Cancelled};
struct Fetch {Status status;std::wstring lyrics;};
Fetch ParseMarkers(const std::string& body){
    const std::string beginMarker="AIMPLB-LYRICS-BEGIN",endMarker="AIMPLB-LYRICS-END";
    auto a=body.find(beginMarker);
    if(a==std::string::npos)return {body.find("AIMPLB-STATUS:")!=std::string::npos?Status::Missing:Status::Bad,{}};
    a+=beginMarker.size();
    auto b=body.find(endMarker,a);
    if(b==std::string::npos||b<=a||b-a>280000)return {Status::Bad,{}};
    std::string raw=body.substr(a,b-a);
    const std::string nl="AIMPLB-LINEBREAK-7F3C";
    for(size_t p=0;(p=raw.find(nl,p))!=std::string::npos;){raw.replace(p,nl.size(),"\n");p++;}
    auto text=DecodeUtf8(raw);
    while(!text.empty()&&(text.back()==L'\r'||text.back()==L'\n'))text.pop_back();
    return text.empty()?Fetch{Status::Missing,{}}:Fetch{Status::Found,std::move(text)};
}
bool IsCancelled(const std::atomic<bool>& stopped,IAIMPTaskOwner* owner){
    return stopped.load()||(owner&&owner->IsCanceled());
}
Fetch FetchLegacy(const Track& t,const std::atomic<bool>& stopped,IAIMPTaskOwner* owner){
    auto artist=UrlEncode(t.artist),title=UrlEncode(t.title),album=UrlEncode(t.album);
    if(artist.empty()||title.empty())return {Status::Bad,{}};
    std::wstring url=L"/lyrics?artist="+artist+L"&title="+title+L"&album="+album+L"&mode=best&force=1&nocache=1";
    HINTERNET session=WinHttpOpen(L"AimpKaraokeStudio/4.0",WINHTTP_ACCESS_TYPE_NO_PROXY,nullptr,nullptr,0);
    if(!session)return {Status::Offline,{}};
    WinHttpSetTimeouts(session,500,500,700,6000);
    Fetch result{Status::Offline,{}};
    for(int pass=0;pass<3 && result.status==Status::Offline;pass++){
        for(int port=17321;port<=17340;port++){
            if(IsCancelled(stopped,owner)){result.status=Status::Cancelled;break;}
            HINTERNET connection=WinHttpConnect(session,L"127.0.0.1",(INTERNET_PORT)port,0);
            if(!connection)continue;
            HINTERNET request=WinHttpOpenRequest(connection,L"GET",url.c_str(),nullptr,nullptr,nullptr,0);
            if(!request){WinHttpCloseHandle(connection);continue;}
            bool ok=WinHttpSendRequest(request,L"Cache-Control: no-cache\r\n",DWORD(-1),nullptr,0,0,0)!=0
                 &&WinHttpReceiveResponse(request,nullptr)!=0;
            std::string response;
            if(ok){
                DWORD available=0,got=0;
                while(!IsCancelled(stopped,owner)&&WinHttpQueryDataAvailable(request,&available)&&available){
                    if(response.size()+available>300000){ok=false;break;}
                    std::vector<char> buffer(available);
                    if(!WinHttpReadData(request,buffer.data(),available,&got)||got==0){ok=false;break;}
                    response.append(buffer.data(),got);
                }
            }
            WinHttpCloseHandle(request);WinHttpCloseHandle(connection);
            if(IsCancelled(stopped,owner)){result.status=Status::Cancelled;break;}
            if(!ok)continue;
            auto found=ParseMarkers(response);
            if(found.status==Status::Found||found.status==Status::Missing){
                result=std::move(found);break;
            }
        }
        if(result.status==Status::Offline && pass<2)
            for(int i=0;i<8&&!IsCancelled(stopped,owner);i++)Sleep(125);
    }
    if(IsCancelled(stopped,owner))result.status=Status::Cancelled;
    WinHttpCloseHandle(session);
    return result;
}
HRESULT SaveToExistingTags(IAIMPCore* core,const Track& t,const std::wstring& lyrics) {
    // Metadata-safety invariant from RC2: edit ONLY Lyrics on existing supported tags.
    // NEVER call SetToAll: blank artist/album/cover fields can be irreversibly wiped.
    IAIMPServiceFileTagEditor* service=nullptr;
    IAIMPFileTagEditor* editor=nullptr;
    IAIMPString* file=MakeAimpString(core,t.file),*value=MakeAimpString(core,lyrics);
    HRESULT hr=(file&&value)?core->QueryInterface(IID_IAIMPServiceFileTagEditor,(void**)&service):E_FAIL;
    if(SUCCEEDED(hr)&&service)hr=service->EditFile(file,IID_IAIMPFileTagEditor,(void**)&editor);
    int updated=0;
    if(SUCCEEDED(hr)&&editor){
        int count=editor->GetTagCount();
        for(int i=0;i<count;i++){
            IAIMPFileTag* tag=nullptr;
            if(SUCCEEDED(editor->GetTag(i,IID_IAIMPFileTag,(void**)&tag))&&tag){
                int tagId=-1;
                if(SUCCEEDED(tag->GetValueAsInt32(AIMP_FILETAG_PROPID_TAG_ID,&tagId))&&
                   tagId!=AIMP_FILETAG_ID_ID3v1&&
                   SUCCEEDED(tag->SetValueAsObject(AIMP_FILEINFO_PROPID_LYRICS,value)))updated++;
            }
            Release(tag);
        }
        hr=updated?editor->Save():E_NOTIMPL;
    }
    Release(file);Release(value);Release(editor);Release(service);
    return hr;
}
bool SaveSeparateLrc(const Track& t,const std::wstring& lyrics){
    if(!IsLocalFile(t.file))return false;
    namespace fs=std::filesystem;
    fs::path dest(t.file);dest.replace_extension(L".lrc");
    const std::string data=EncodeUtf8(lyrics);
    if(data.empty())return false;
    fs::path tmp=dest;
    tmp+=L".karaoke-"+std::to_wstring(GetCurrentProcessId())+L".tmp";
    HANDLE f=CreateFileW(tmp.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(f==INVALID_HANDLE_VALUE)return false;
    DWORD written=0;
    bool ok=WriteFile(f,data.data(),(DWORD)data.size(),&written,nullptr)!=0&&written==data.size();
    if(ok)ok=FlushFileBuffers(f)!=0;
    CloseHandle(f);
    // Never overwrite an existing sidecar that might contain hand-edited lyrics.
    if(ok)ok=MoveFileExW(tmp.c_str(),dest.c_str(),MOVEFILE_WRITE_THROUGH)!=0;
    if(!ok)DeleteFileW(tmp.c_str());
    return ok;
}
} // namespace

class LyricsBatchFeature:public std::enable_shared_from_this<LyricsBatchFeature>{
public:
    LyricsBatchFeature(IAIMPCore* core,IAIMPServicePlayer* player,IAIMPServiceThreads* threads,
                       std::function<void()> launch,std::function<void()> refresh)
       :core_(core),player_(player),threads_(threads),launch_(std::move(launch)),refresh_(std::move(refresh)){
        core_->AddRef();player_->AddRef();threads_->AddRef();
    }
    ~LyricsBatchFeature(){Stop();}
    bool Init();
    void Stop();
    void Execute(int type);
    void Complete(unsigned generation,const Track&,Fetch);
    IAIMPServiceThreads* Threads() const{return threads_;}
    std::atomic<bool> cancelled{false},stopping{false};
private:
    struct Event;
    bool AddAction(int type,const wchar_t* id,const wchar_t* caption,WORD modifiers,WORD key,
                   IAIMPServiceActionManager* manager);
    void Start(bool batch);
    void Next();
    void Finish();
    Track FromInfo(IAIMPFileInfo* info)const{
        if(!info)return {};
        return {Property(info,AIMP_FILEINFO_PROPID_ARTIST),Property(info,AIMP_FILEINFO_PROPID_TITLE),
                Property(info,AIMP_FILEINFO_PROPID_ALBUM),Property(info,AIMP_FILEINFO_PROPID_FILENAME)};
    }
    void Notice(const std::wstring& txt){
        if(!core_)return;
        IAIMPServiceMessageDispatcher* messages=nullptr;
        if(SUCCEEDED(core_->QueryInterface(IID_IAIMPServiceMessageDispatcher,(void**)&messages))&&messages){
            messages->Send(AIMP_MSG_CMD_SHOW_NOTIFICATION,0,(void*)txt.c_str());
        }
        Release(messages);
        OutputDebugStringW((L"[Karaoke Batch] "+txt+L"\n").c_str());
    }
    IAIMPCore* core_;
    IAIMPServicePlayer* player_;
    IAIMPServiceThreads* threads_;
    std::function<void()> launch_,refresh_;
    std::vector<IAIMPAction*> actions_;
    IAIMPMenuItem* menu_=nullptr;
    std::vector<Track> queue_;
    std::set<std::wstring> seen_;
    std::atomic<unsigned> generation_{0};
    std::atomic<bool> busy_{false};
    TTaskHandle handle_=0;
    size_t index_=0;int saved_=0,sidecar_=0,missing_=0,skip_=0,errors_=0;
    bool batch_=false;
};
namespace {
class TaskBase:public IAIMPTask{
    std::atomic<ULONG> refs_{1};
protected:virtual ~TaskBase()=default;
public:
    HRESULT WINAPI QueryInterface(REFIID id,void** out)override{
        if(!out)return E_POINTER;*out=nullptr;
        if(id!=IID_IUnknown&&id!=IID_IAIMPTask)return E_NOINTERFACE;
        *out=static_cast<IAIMPTask*>(this);AddRef();return S_OK;
    }
    ULONG WINAPI AddRef()override{return ++refs_;}
    ULONG WINAPI Release()override{ULONG n=--refs_;if(!n)delete this;return n;}
};
class CompleteTask:public TaskBase {
    std::shared_ptr<LyricsBatchFeature> host_;Track track_;Fetch outcome_;unsigned generation_;
public:
    CompleteTask(std::shared_ptr<LyricsBatchFeature> h,Track t,Fetch f,unsigned gen)
        :host_(std::move(h)),track_(std::move(t)),outcome_(std::move(f)),generation_(gen){}
    void WINAPI Execute(IAIMPTaskOwner*) override{
        if(!host_->stopping.load())host_->Complete(generation_,track_,std::move(outcome_));
    }
};
class WorkerTask:public TaskBase {
    std::shared_ptr<LyricsBatchFeature> host_;Track track_;unsigned generation_;
public:
    WorkerTask(std::shared_ptr<LyricsBatchFeature> h,Track t,unsigned gen)
        :host_(std::move(h)),track_(std::move(t)),generation_(gen){}
    void WINAPI Execute(IAIMPTaskOwner* owner) override{
        auto result=FetchLegacy(track_,host_->cancelled,owner);
        if(host_->stopping.load())return;
        auto* complete=new(std::nothrow) CompleteTask(host_,track_,std::move(result),generation_);
        if(!complete)return;
        host_->Threads()->ExecuteInMainThread(complete,0);
        complete->Release();
    }
};
}
struct LyricsBatchFeature::Event:IAIMPActionEvent{
    std::atomic<ULONG> refs{1};std::weak_ptr<LyricsBatchFeature> host;int type;
    Event(std::shared_ptr<LyricsBatchFeature> h,int t):host(h),type(t){}
    HRESULT WINAPI QueryInterface(REFIID id,void** out)override{
        if(!out)return E_POINTER;*out=nullptr;
        if(id!=IID_IUnknown&&id!=IID_IAIMPActionEvent)return E_NOINTERFACE;
        *out=static_cast<IAIMPActionEvent*>(this);AddRef();return S_OK;
    }
    ULONG WINAPI AddRef()override{return ++refs;}
    ULONG WINAPI Release()override{ULONG n=--refs;if(!n)delete this;return n;}
    void WINAPI OnExecute(IUnknown*) override{if(auto h=host.lock())h->Execute(type);}
};
bool LyricsBatchFeature::AddAction(int type,const wchar_t* id,const wchar_t* caption,
                 WORD mods,WORD key,IAIMPServiceActionManager* manager){
    IAIMPAction* action=nullptr;
    if(FAILED(core_->CreateObject(IID_IAIMPAction,(void**)&action))||!action)return false;
    auto* event=new(std::nothrow) Event(shared_from_this(),type);
    IAIMPString* name=MakeAimpString(core_,caption),*identity=MakeAimpString(core_,id),
               *group=MakeAimpString(core_,L"Karaoke Studio · Lyrics");
    HRESULT hr=name&&identity&&group&&event?S_OK:E_FAIL;
    if(SUCCEEDED(hr))hr=action->SetValueAsObject(AIMP_ACTION_PROPID_ID,identity);
    if(SUCCEEDED(hr))hr=action->SetValueAsObject(AIMP_ACTION_PROPID_NAME,name);
    if(SUCCEEDED(hr))hr=action->SetValueAsObject(AIMP_ACTION_PROPID_GROUPNAME,group);
    if(SUCCEEDED(hr))hr=action->SetValueAsInt32(AIMP_ACTION_PROPID_ENABLED,1);
    if(SUCCEEDED(hr)&&key)hr=action->SetValueAsInt32(AIMP_ACTION_PROPID_DEFAULTGLOBALHOTKEY,
        manager->MakeHotkey(mods,key));
    if(SUCCEEDED(hr))hr=action->SetValueAsObject(AIMP_ACTION_PROPID_EVENT,event);
    if(SUCCEEDED(hr))hr=core_->RegisterExtension(IID_IAIMPServiceActionManager,action);
    Release(name);Release(identity);Release(group);
    if(event)event->Release();
    if(FAILED(hr)){Release(action);return false;}
    actions_.push_back(action);
    return true;
}
bool LyricsBatchFeature::Init(){
    IAIMPServiceActionManager* manager=nullptr;
    if(FAILED(core_->QueryInterface(IID_IAIMPServiceActionManager,(void**)&manager))||!manager)return false;
    WORD mod=(WORD)(AIMP_ACTION_HOTKEY_MODIFIER_CTRL|AIMP_ACTION_HOTKEY_MODIFIER_ALT);
    bool ok=AddAction(1,L"aimp.AIMPLyricsBridge.RefreshCurrentLyrics",
         L"Обновить и сохранить текст текущего трека",mod,L'L',manager);
    if(ok)ok=AddAction(2,L"aimp.AIMPLyricsBridge.RefreshSelectedLyrics",
         L"Обновить тексты выделенных треков",
         (WORD)(mod|AIMP_ACTION_HOTKEY_MODIFIER_SHIFT),L'L',manager);
    if(ok)ok=AddAction(3,L"aimp.AIMPLyricsBridge.CancelLyricsUpdate",
         L"Отменить обновление текстов",0,0,manager);
    Release(manager);
    if(!ok)return false;
    IAIMPServiceMenuManager* menus=nullptr;IAIMPMenuItem* parent=nullptr;
    if(SUCCEEDED(core_->QueryInterface(IID_IAIMPServiceMenuManager,(void**)&menus))&&menus&&
       SUCCEEDED(menus->GetBuiltIn(AIMP_MENUID_PLAYER_PLAYLIST_CONTEXT_FUNCTIONS,&parent))&&parent&&
       SUCCEEDED(core_->CreateObject(IID_IAIMPMenuItem,(void**)&menu_))&&menu_){
        IAIMPString* id=MakeAimpString(core_,L"karaoke.studio.menu.batch"),
                   *caption=MakeAimpString(core_,L"Загрузить тексты выделенных треков");
        if(id&&caption){
            menu_->SetValueAsObject(AIMP_MENUITEM_PROPID_ID,id);
            menu_->SetValueAsObject(AIMP_MENUITEM_PROPID_CAPTION,caption);
            menu_->SetValueAsInt32(AIMP_MENUITEM_PROPID_ENABLED,1);
            menu_->SetValueAsObject(AIMP_MENUITEM_PROPID_ACTION,actions_[1]);
            menu_->SetValueAsObject(AIMP_MENUITEM_PROPID_PARENT,parent);
            if(FAILED(core_->RegisterExtension(IID_IAIMPServiceMenuManager,menu_)))Release(menu_);
        }else Release(menu_);
        Release(id);Release(caption);
    }
    Release(parent);Release(menus);
    return true;
}
void LyricsBatchFeature::Start(bool batch){
    if(stopping.load()||busy_.exchange(true)){Notice(L"Другая загрузка текстов ещё выполняется");return;}
    cancelled.store(false);++generation_;
    queue_.clear();seen_.clear();index_=0;saved_=sidecar_=missing_=skip_=errors_=0;batch_=batch;
    if(batch) {
        IAIMPServicePlaylistManager* manager=nullptr;IAIMPPlaylist* playlist=nullptr;
        if(SUCCEEDED(core_->QueryInterface(IID_IAIMPServicePlaylistManager,(void**)&manager))&&manager&&
           SUCCEEDED(manager->GetActivePlaylist(&playlist))&&playlist){
            for(int i=0;i<std::min(5000,playlist->GetItemCount());i++){
                IAIMPPlaylistItem* item=nullptr;
                if(SUCCEEDED(playlist->GetItem(i,IID_IAIMPPlaylistItem,(void**)&item))&&item){
                    int selected=0;
                    if(SUCCEEDED(item->GetValueAsInt32(AIMP_PLAYLISTITEM_PROPID_SELECTED,&selected))&&selected){
                        IAIMPFileInfo* info=nullptr;
                        if(SUCCEEDED(item->GetValueAsObject(AIMP_PLAYLISTITEM_PROPID_FILEINFO,
                           IID_IAIMPFileInfo,(void**)&info))&&info){
                            auto t=FromInfo(info);
                            if(t.file.empty())t.file=Property(item,AIMP_PLAYLISTITEM_PROPID_FILENAME);
                            if(!t.file.empty()&&seen_.insert(t.file).second)queue_.push_back(std::move(t));
                        }
                        Release(info);
                    }
                }
                Release(item);
            }
        }
        Release(playlist);Release(manager);
    }else{
        IAIMPFileInfo* info=nullptr;
        if(player_->GetState()!=AIMP_PLAYER_STATE_STOPPED&&
           SUCCEEDED(player_->GetInfo(&info))&&info)queue_.push_back(FromInfo(info));
        Release(info);
    }
    if(queue_.empty()){Notice(L"Нет подходящих треков для обработки");Finish();return;}
    if(batch){
        std::wstring prompt=L"Обновить тексты "+std::to_wstring(queue_.size())+
            L" выбранных треков?\nЗапись только Lyrics в существующих тегах, "
            L"иначе отдельный LRC; остальные теги сохраняются.";
        if(MessageBoxW(nullptr,prompt.c_str(),L"Karaoke Studio",
            MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2)!=IDYES){Finish();return;}
    }
    launch_(); // Opens the existing WPF window hosting the built-in Bridge.
    Notice(L"Идёт загрузка текстов…");
    Next();
}
void LyricsBatchFeature::Next(){
    if(!busy_.load()||cancelled.load()||stopping.load()){Finish();return;}
    while(index_<queue_.size()){
        Track t=queue_[index_];
        if(t.artist.empty()||t.title.empty()||!IsLocalFile(t.file)){skip_++;index_++;continue;}
        auto* task=new(std::nothrow) WorkerTask(shared_from_this(),t,generation_.load());
        if(!task){errors_++;index_++;continue;}
        HRESULT hr=threads_->ExecuteInThread(task,&handle_);
        task->Release();
        if(SUCCEEDED(hr))return;
        errors_++;index_++;
    }
    Finish();
}
void LyricsBatchFeature::Complete(unsigned gen,const Track& t,Fetch outcome){
    if(stopping.load()||!busy_.load()||gen!=generation_.load())return;
    handle_=0;
    if(cancelled.load()){Finish();return;}
    if(outcome.status==Status::Found){
        HRESULT tag=SaveToExistingTags(core_,t,outcome.lyrics);
        if(SUCCEEDED(tag)){saved_++;if(!batch_)refresh_();}
        else if(SaveSeparateLrc(t,outcome.lyrics)){sidecar_++;if(!batch_)refresh_();}
        else errors_++;
    }else if(outcome.status==Status::Missing)missing_++;
    else if(outcome.status==Status::Cancelled){Finish();return;}
    else if(outcome.status==Status::Offline){
        errors_++;Notice(L"Bridge недоступен — проверьте подключение к интернету");
        Finish();return;
    }else errors_++;
    index_++;
    if(batch_&&(index_%5==0||index_==queue_.size()))
        Notice(L"Тексты: "+std::to_wstring(index_)+L"/"+std::to_wstring(queue_.size())+
               L", теги "+std::to_wstring(saved_)+L", LRC "+std::to_wstring(sidecar_));
    Next();
}
void LyricsBatchFeature::Finish(){
    if(!busy_.exchange(false))return;
    ++generation_;handle_=0;queue_.clear();seen_.clear();
    if(!stopping.load())Notice(L"Готово: теги "+std::to_wstring(saved_)+L", LRC "+
        std::to_wstring(sidecar_)+L", не найдено "+std::to_wstring(missing_)+
        L", пропущено "+std::to_wstring(skip_)+L", ошибки "+std::to_wstring(errors_));
}
void LyricsBatchFeature::Execute(int type){
    if(type==3){
        if(!busy_.load()){Notice(L"Активной операции нет");return;}
        cancelled.store(true);++generation_;
        if(handle_)threads_->Cancel(handle_,0);
        Finish();Notice(L"Обновление текстов отменено");return;
    }
    Start(type==2);
}
void LyricsBatchFeature::Stop(){
    if(stopping.exchange(true))return;
    cancelled.store(true);++generation_;
    if(threads_&&handle_)threads_->Cancel(handle_,AIMP_SERVICE_THREADS_FLAGS_WAITFOR);
    handle_=0;busy_.store(false);queue_.clear();
    if(core_){
        if(menu_){core_->UnregisterExtension(menu_);Release(menu_);}
        for(auto i=actions_.rbegin();i!=actions_.rend();i++)
            if(*i){core_->UnregisterExtension(*i);Release(*i);}
    }
    actions_.clear();Release(threads_);Release(player_);Release(core_);
}
std::shared_ptr<LyricsBatchFeature> CreateLyricsBatchFeature(IAIMPCore* core,
    IAIMPServicePlayer* player,IAIMPServiceThreads* threads,
    std::function<void()> launch,std::function<void()> refresh){
    if(!core||!player||!threads)return {};
    auto ptr=std::make_shared<LyricsBatchFeature>(
        core,player,threads,std::move(launch),std::move(refresh));
    if(!ptr->Init()){ptr->Stop();return {};}
    return ptr;
}
void StopLyricsBatchFeature(const std::shared_ptr<LyricsBatchFeature>& ptr){if(ptr)ptr->Stop();}
