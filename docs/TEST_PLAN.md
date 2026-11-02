# План тестирования

## Автоматически исполнено в Linux

`cmake -S tests -B build/ctest && cmake --build build/ctest && ctest --test-dir build/ctest --output-on-failure`

Проверяется парсер: duplicate timestamp, fractional timestamp, metadata, offset, unsynchronized content.

## Требует Windows и установленного AIMP

1. DLL обнаруживается AIMP 5.40 x64 (или x86 соответствующей разрядности), появляется пункт меню.
2. Открытие окна не запускает вторую копию при повторном вызове из меню.
3. Синхронизированный `.lrc` рядом с MP3/FLAC появляется автоматически.
4. При отсутствии LRC проверяется текст из встроенных тегов и службы AIMP Lyrics.
5. При смене трека во время получения онлайн-текста старый текст не заменяет новый.
6. Seek вперёд и назад немедленно пересчитывает строку.
7. Пауза останавливает прогресс, возобновление продолжает с правильной отметки.
8. Независимое окно можно перенести на второй монитор и переключить в fullscreen.
9. Ctrl+колесо меняет масштаб; настройки восстанавливаются при новом запуске.
10. Закрытие окна не останавливает AIMP, повторное открытие восстановит snapshot.
11. Закрытие AIMP не приводит к падению UI; в интерфейсе видно RECONNECTING.
12. Unicode: русский, японский, корейский, китайский; длинные строки, переносы.
13. Варианты 125%, 150%, 200% DPI и маленькое окно 700x420.
14. Пакет .aimppack устанавливается через встроенный инсталлятор.
15. Под нагрузкой 2 часа: нет зависаний и неограниченного роста памяти.

## Открытые критерии проверки

- Точность `IAIMPLyrics.Get()` для временных границ: подтвердить миллисекунды на нескольких провайдерах.
- Количество актуальных callback'ов после отмены и повторного запроса.
- Пропуск нестандартных `CUE://` путей при поиске локального .lrc.
- Наличие доступного меню в конкретной установленной оболочке AIMP.

## v0.3 audit-critical acceptance tests (Windows + AIMP 5.40)

- Open AIMP with no song, play, pause, seek and close repeatedly; check toolbar commands always arrive on an AIMP-owned message-loop thread.
- While Lyrics/AlbumArt callbacks are pending, rapidly skip 30 songs and terminate AIMP. Use Application Verifier/WinDbg for UAF/deadlocks.
- Compare raw AIMP `IAIMPLyrics::Get` start/end and `AIMP_LYRICS_PROPID_OFFSET` with the player's built-in highlighted line to avoid double-offset.
- Embedded PNG/JPEG album art in MP3/FLAC, local folder.jpg, missing artwork, malformed image, huge image; test async cancellation.
- Large 5,000-line LRC, bilingual duplicate timestamps, long UTF-8 lines and over-limit JSON: no reconnect storm or UI freeze.
- Launch two AIMP processes and standalone `--demo`, then open each lyric window: separate instances and accurate live song selection.
- Two monitors with 100%/150% DPI, disconnect secondary monitor with karaoke window saved there, F11 exit and re-entry.
- Verify on 32-bit and 64-bit AIMP using architecture-specific package layout, then inspect Windows log and native debugger output.
