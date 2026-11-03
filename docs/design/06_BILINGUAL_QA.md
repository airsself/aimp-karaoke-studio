# Bilingual Lyrics v2.0 — design, implementation and QA

## Layout
- Off: full-width Vocal Focus with original lyrics.
- On: one virtualized ListBox row contains a DockPanel with two equal
  visual columns. A quiet vertical border separates original and translation.
- No independently scrolling list: a Russian phrase that wraps to three lines
  enlarges the shared row height; both sides retain the same timestamp.
- An informational header displays translation progress and provider errors.
- Existing subtitle modes, shortcuts, word-timed LRC and player controls stay.
- Source-language override and retry control are in Settings.
- Open/closed state persists via SettingsStore v4.

## Translation
Provider: anonymous MyMemory API via HTTPS. Each GET query is at most
480 UTF-8 bytes. The documented anonymous allowance is 5000 chars/day,
and no key is required. Text is sent only after enabling the RU pane.

Source detection uses script heuristics: kana=ja, hangul=ko, han=zh-CN,
Cyrillic=ru (no-op), other Latin=en. Languages es/fr/de/it/pt are
selectable manually. This is NOT a general language classifier.

Persistent cache: %LOCALAPPDATA%/AimpKaraokeStudio/translations.v1.json
A hashed key includes source language, target ru and the original text.
Requests are canceled on track changes, pane closure and settings changes.

## Limitations
- MyMemory can reject or limit requests.
- Rhymes, idioms and non-English Latin songs need user review.
- Enhanced LRC precise word highlighting is on the original ONLY.
- Translation highlights the corresponding active row; it does not
  invent per-word timing for Russian words.

## Release QA gates
- [ ] C# deterministic translation, cache, language and cancellation tests
- [ ] Windows .NET WPF and native C++ compile
- [ ] AIMP 5.40 Named Pipe, player commands and native window smoke test
- [ ] WPF UI Automation toggles the bilingual pane
- [ ] Inspect actual WPF split-pane screenshot for overlap
- [ ] Manual review of real-song translation quality on a user installation

Automated tests do not claim quality of real-world translations.
