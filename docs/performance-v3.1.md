# v3.1 Performance Optimization / QA

## Measured defects in v3
These are code-level performance sources, rather than percentages observed
on a specific user machine:
1. A 16 ms DispatcherTimer executes continuously, even when player is paused.
2. The active karaoke line re-concatenates all timed word strings and parses
   Unicode grapheme boundaries on EVERY progress refresh. WPF receives many
   expensive text reflow invalidations for unchanged character boundaries.
3. Each new translated line re-serializes the full translation dictionary to
   disk; for N new lines this grows toward quadratic serialization work on the
   UI synchronization context.
4. All lyric rows (up to 5,000) have right-pane widths and visibility walked
   during nearly every SizeChanged event, even when width changed by <1 DIP.
5. A very large WPF image blur radius (72 DIPs) increases GPU/compositor work
   over a full-window ambient cover.

## Implementation
- KaraokeHighlightEngine.Prepare precomputes whole-line / per-word Unicode
  element boundaries and verifies whether word chunks align once.
- LyricLineViewModel memoizes its PreparedLine lazily and checks the cut index
  before creating/submitting two new run text strings. On non-visible frame
  progress no text property changes are raised.
- Active frame cadence 33ms (30.3 Hz); paused 150ms, minimized 300ms.
  PlaybackClock interpolates monotonic AIMP position, unchanged.
- Background ambient cover cached at render scale 0.35, blur radius 18.
- Bilingual pane width changes <12 DIP don't touch 5000 ViewModels.
  A version counter invalidates the cache on new tracks even if line counts
  coincide. Explicit open/close always reapplies.
- Translation cache checkpoints every 20 newly received lines and at the
  completion/cancellation boundary; JSON snapshot/serialize on ThreadPool,
  serialized file writes protected by SemaphoreSlim. Warm-cache bursts yield
  every 32 lines, preventing dispatcher starvation.

## Regression gates
- Cross-check original vs PreparedLine outputs for 6 different Unicode
  categories, exact Enhanced LRC and invalid-timing fallback.
- Measure allocated bytes and elapsed time across 25,000 highlight refreshes;
  print side-by-side reference/optimized values, assert >=100x allocation
  reduction. Time ratios are informational (CI hardware varies).
- WPF ViewModel test 30k updates and maximum 2*line length property changes.
- Translation mock HTTP test 42 unique lines, <=3 cache checkpoints; check
  warm cache makes no disk writes; cancellation still persists completed work.
- AIMP 5.40 smoke: native DLL, Named Pipe, seek, pause/resume, bilingual UI,
  lyrics search WPF dialog, process CPU and memory samples.

## Known limitations
- Overall CPU/RAM usage depends on compositor, WPF/GPU driver and display DPI.
  CI performance numbers are not comparable to user Windows machines
  without an identical controlled workload.
- Network translation/lyric-provider latency is separate from rendering and
  may still depend on rate limits or connectivity.
- The 0.35 background cache produces a deliberately softer image, but the
  sharp original album cover uses its previous image source.
