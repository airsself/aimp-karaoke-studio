#pragma once
#define NOMINMAX
#include <windows.h>
#include <atomic>
#include <deque>
#include <map>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include <utility>

// All AIMP SDK interaction remains on AIMP's message thread.
// Worker owns the pipe and never dereferences AIMP COM objects.
class PipeServer {
public:
    explicit PipeServer(HWND commandsWindow);
    ~PipeServer();
    bool Start();
    void Stop();
    void Publish(const std::string& kind,const std::string& json);
    std::vector<std::pair<WPARAM,LPARAM>> TakeCommands();
    std::wstring ShortName() const {return name_;}
private:
    void Run();
    bool WriteLine(HANDLE pipe,HANDLE stopping,const std::string& line);
    bool ReadCommands(HANDLE pipe,std::string& accumulator);
    void ParseCommand(const std::string& line);
    void QueueCommand(WPARAM command, LPARAM argument);
    std::wstring name_;
    HWND commandWindow_{};
    HANDLE stop_{};
    std::thread worker_;
    std::mutex mu_;
    std::deque<std::string> queue_;
    std::deque<std::pair<WPARAM,LPARAM>> commands_;
    std::map<std::string,std::string> latest_;
    std::atomic<bool> connected_{false};
};
inline constexpr UINT WM_KARAOKE_COMMAND=WM_APP+327;
inline constexpr WPARAM KAR_CMD_TOGGLE=1;
inline constexpr WPARAM KAR_CMD_PREVIOUS=2;
inline constexpr WPARAM KAR_CMD_NEXT=3;
inline constexpr WPARAM KAR_CMD_SEEK_MS=4;
