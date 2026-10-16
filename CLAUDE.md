# EveLocalMon

Desktop app that watches the EVE Online Local chat member list on screen and alerts (sound,
toast, ntfy) when a non-friendly pilot appears or the list is too long to read.
C# / .NET 10 + Avalonia. Windows first, macOS later.

## Read first

- `doc/PLAN.md` — overall design, decisions, milestones.
- `doc/M1-SPEC.md` — detailed spec for M0 (skeleton + capture CLI) and M1 (list reader +
  alert state). It is the source of truth for types, algorithms, thresholds and fixtures.
  If implementation forces a change (e.g. a threshold), update the spec in the same change.
- `doc/M2-SPEC.md` — spec for the current milestone: Avalonia region picker, calibration
  auto-detect, live preview, settings, and the `capture` CLI count/dedupe fixes.

## Hard rules

- **Passive only.** Read screen pixels. Never read EVE client memory, never inject
  input (keys, mouse, window messages) into the client, never automate gameplay.
- Network access only for the ntfy alert channel.
- `EveLocalMon.Core` must stay platform-neutral: no Windows/macOS APIs, no UI. OS-specific
  code lives in `EveLocalMon.Platform.*`, UI in `EveLocalMon.App`.
- `doc/samples/*.png` are raw captures at 100% EVE UI scale / 100% Windows scaling. Never
  modify, re-save or rescale them — pixel coordinates in the spec refer to them exactly.

## Build and test

```sh
dotnet build EveLocalMon.sln
dotnet test EveLocalMon.sln --filter "Category!=Perf"
```

The performance test (`PerformanceTests`, `[Trait("Category", "Perf")]`) is excluded from the
standard run because timing assertions are noisy on shared/CI hardware. Run it explicitly,
in Release, when touching the hot path in `LocalListReader`:

```sh
dotnet test EveLocalMon.sln --filter "Category=Perf" -c Release
```

Fixture-driven reader tests live in `tests/EveLocalMon.Core.Tests`. `FixtureReaderTests` prints
the same per-row table as the CLI's `read` command on failure (slot y, category, best/second-best
template score, via `ITestOutputHelper`) — tuning a threshold depends on seeing the scores.

## Conventions

- `net10.0` for Core and tests; `net10.0-windows10.0.19041.0` for `Platform.Windows`,
  `Capture.Cli` and `App`, which reach Win32 capture. macOS (M6) is what splits `App` into
  multi-targeting plus a runtime-selected `IScreenCapture`.
- `EveLocalMon.App` builds its UI in C#, not XAML: compiler-checked, no designer to keep in sync,
  and preview overlays are composited into the bitmap so what you see is the pixels the reader saw.
- **No notification or audio packages.** Alerts use the app's own always-on-top popup, because
  Windows Focus Assist suppresses native toasts during fullscreen games — the one moment this tool
  exists for. The chime is a WAV synthesised at runtime and played through winmm `PlaySound`, which
  also gives a volume control the OS API doesn't. Don't "improve" either into a dependency.
- Nullable enabled, implicit usings, file-scoped namespaces, `TreatWarningsAsErrors` in Core.
- Immutable data as `record` / `readonly record struct`; pure functions for detection.
- Pixel buffers are BGRA8888, top-down, `Stride` bytes per row. Coordinates are physical
  pixels in frame space unless stated otherwise.
- Image I/O via SkiaSharp (already an Avalonia dependency). No OpenCV / Emgu / System.Drawing
  in Core.
- xUnit for tests; no mocking framework needed for Core — use real fixture images.
- Keep comments sparse; explain *why* (EVE-specific quirks, threshold choices), not *what*.
