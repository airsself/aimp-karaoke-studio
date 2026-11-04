#pragma once
#include <functional>
#include <memory>
class IAIMPCore;
class IAIMPServicePlayer;
class IAIMPServiceThreads;
class LyricsBatchFeature;
// One AIMP plugin DLL: RC2 hotkeys and selected-track batch integrated here.
std::shared_ptr<LyricsBatchFeature> CreateLyricsBatchFeature(
    IAIMPCore*, IAIMPServicePlayer*, IAIMPServiceThreads*,
    std::function<void()> openStudio, std::function<void()> refreshTrack);
void StopLyricsBatchFeature(const std::shared_ptr<LyricsBatchFeature>&);
