# Armada TUI performance

This page records how the TUI performs with large data and while idle (TUI_APP_PLAN.md W8.5), what was changed to get
there, and how to reproduce the numbers.

## How to reproduce

The `Tui.Performance` suite in `src/Test.Shared/Suites/Tui/TuiPerformanceSuite.cs` is the harness. It runs headless
(TUIKit `HeadlessBackend` through `TuiTestHost`), prints every measurement with a `[tui-perf]` prefix, and appends them
to the file named by `ARMADA_TUI_PERF_REPORT` when that is set:

```bash
ARMADA_TUI_PERF_REPORT=/tmp/tui-perf.txt ARMADA_TEST_SUITES=Tui.Performance \
  dotnet run --project src/Test.Automated --framework net10.0
```

The assertions use generous limits (several times the numbers below) so the suite catches order-of-magnitude
regressions without being flaky on a loaded machine. Idle CPU is measured from the process's total processor time while
the real loop runs for two seconds with no input, so it includes every thread in the test process.

## Results

Measured on an Apple M5 Max, .NET 10, Debug build, terminal 120x40 (grids) or 140x45 (Ask). "Before" is the code at
the start of W8.5 measured with the same harness; times are means unless noted.

| Measurement | Before | After |
|-------------|--------|-------|
| Idle CPU, Ask with 5,000 messages open | 10.2 % of a core | 1.1 % |
| Idle CPU, Jobs list | 4.1 % of a core | 0.6 % |
| Frames composed while idle | 60 per second | about 4 per second (1 when the terminal is unfocused) |
| Ask, 5,000 messages: streaming chunk plus frame | 51 ms | 4.4 ms |
| Ask, 5,000 messages: relayout plus frame (clock tick, expand or collapse) | about 50 ms | 4.2 ms |
| Ask, 5,000 messages: first frame after open | 68 ms | 68 ms |
| Ask, 5,000 messages: Up plus frame | 0.4 ms | 0.6 ms |
| Grid, 10,000 local rows: load | 1.6 ms | 0.9 ms |
| Grid, 10,000 local rows: sort | 23 ms | 22 ms |
| Grid, 10,000 rows on one page: frame | 0.9 ms | 0.8 ms |
| Grid, 10,000 rows: PgDn plus frame | 0.8 ms | 0.9 ms |
| Grid, 10,000 rows server-paged: first page, jump to the last page | 2.3 ms, 0.2 ms | 2.3 ms, 0.2 ms |

A real `armada tui` in a pseudo-terminal (80x24, login screen) used 1.1 % of a core over 10 idle seconds.

## What changed

- **Frame governor and run loop.** TUIKit's `RunAsync` composes and diffs every frame at 60 per second whether or not
  anything changed, and has no hook to skip frames (TUIKit gap U8). `armada tui` now runs its own loop
  (`Shell/TuiRunLoop.cs`) with the same steps, and `Shell/FrameGovernor.cs` decides per tick whether to compose: after
  input (seen by `Services/TerminalBackendAdapter.cs` when bytes are read), after posted work runs (the UI dispatcher
  calls the governor after every posted action), on a resize, on a theme change, for 150 ms after any of those, and
  otherwise every 250 ms (1 s while the terminal is unfocused). While idle the loop polls input every 25 ms, which is
  the worst-case added key latency. `TuiStartOptions.AdaptiveFrames = false` restores a frame every tick.
- **Ask transcript block cache.** The transcript re-laid out every message on every streaming chunk and once a second
  (relative times), and its Markdown cache was cleared whenever it passed 2,000 entries, so a 5,000-message
  conversation re-rendered all of its Markdown each time. `AskTranscriptBuilder` now keeps each message's laid-out block
  with a signature of its inputs (text, kind, tool calls, relative time, expanded sections, captain name, metrics, width,
  theme, locale) and reuses it when nothing changed. Messages with a confirm card or a live work card are rebuilt every
  time because they show live state.
- **Memory caps.** `AskConversation.MaxMessages` (10,000) drops the oldest messages when live messages push past it and
  offers them again through "Load earlier messages". Log viewers keep the last 20,000 lines (unchanged). Router history
  keeps 100 entries and notification history its existing cap.
- **Grids.** Already virtualized (only visible rows are drawn) and server-paged where the API pages, so no change was
  needed for speed; the 10,000-row numbers above confirm it. Column layout now measures only the visible rows.

## Limits and follow-ups

- The streaming reply re-renders its own Markdown on every chunk (TUIKit gap U7); with very long single replies this is
  the remaining per-chunk cost.
- Screens that load every record to sort locally (where the server cannot sort) hold all matches in memory; they page
  the display but not the fetch.
- Upstream: a TUIKit run-loop hook for frame skipping (U8) would let the TUI drop its own loop.
