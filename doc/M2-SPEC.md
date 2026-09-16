# EveLocalMon — M2 implementation spec: region picker & live preview

**Status: implemented (2026-09-15).** 54 automated tests pass
(`dotnet test EveLocalMon.sln --filter "Category!=Perf"`). Two sections were rewritten against
reality while building and now describe as-built behaviour rather than the original plan: the
icon-run filter in §4b and the `ListRect.Y` walk in §4c — the planned rules for both were wrong.
The manual acceptance checklist in §9 is still outstanding: it needs a real EVE window, a second
monitor and a scaled display, none of which can be verified from here.

Scope: the first real UI. Drag-select the Local pane on screen, let the app work out the rest of
the calibration, and watch the M1 reader classify it live. Plus the `capture` CLI fixes described
in §6. Alerts (M3), tray/monitoring shell (M4) and macOS (M6) stay out of scope.

Background: `doc/PLAN.md` §4 (detection design) and `doc/M1-SPEC.md` (the reader this drives).

Decided with the user before writing this:

| Question | Answer |
| --- | --- |
| Milestone scope | Picker **+ live preview with per-row classification** — not picker alone |
| App shell | Plain window; tray icon/autostart wait for M4 |
| Calibration beyond the region | **Auto-detect, with manual override** |

---

## 0. Prerequisites (verified 2026-09-15)

- **Avalonia 12.1.2** restores and compiles against `net10.0` — checked in a throwaway project,
  not assumed.
- API facts confirmed by compiling/reflecting against that package, because v12 moved things:

  | Need | Avalonia 12 API |
  | --- | --- |
  | Monitor list | `window.Screens.All` → `IReadOnlyList<Screen>` |
  | Monitor geometry | `Screen.Bounds` / `Screen.WorkingArea` (`PixelRect`, **physical px**), `Screen.Scaling` (double), `Screen.IsPrimary`, `Screen.DisplayName` |
  | Borderless window | `Window.WindowDecorations = WindowDecorations.None` — **`SystemDecorations` is obsolete in v12** |
  | Transparent window | `Window.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }` — v12 makes `WindowTransparencyLevel` a **struct with statics** (`None, Transparent, Blur, AcrylicBlur, Mica`), not an enum |
  | Window placement | `Window.Position` (`PixelPoint`, physical px); `Width`/`Height` are **DIPs** |
  | Preview surface | `new WriteableBitmap(PixelSize, Vector dpi, PixelFormat.Bgra8888, AlphaFormat.Opaque)`, `Lock()` → `ILockedFramebuffer.Address` / `.RowBytes` |
  | Marshalling to UI | `Dispatcher.UIThread.Post(...)` |

---

## 1. Projects

```text
src/EveLocalMon.App/            net10.0-windows10.0.19041.0, refs Core + Platform.Windows
  Program.cs                    AppBuilder entry point
  App.axaml(.cs)                Fluent theme, main window bootstrap
  Views/
    MainWindow.axaml(.cs)       preview + calibration panel + Start/Stop
    RegionPickerWindow.axaml(.cs)  full-screen drag-select overlay (one per monitor)
  ViewModels/MainViewModel.cs   plain INotifyPropertyChanged — no MVVM framework
  Services/MonitorLoop.cs       capture → read → publish, on a background task
  Rendering/PreviewRenderer.cs  Frame → WriteableBitmap, Snapshot → row overlays
assets/templates/               canonical default templates (moved out of tests, see §5)
```

The App takes the **Windows** TFM because it references `Platform.Windows` directly. M6 (macOS)
replaces that with multi-targeting plus a runtime-selected `IScreenCapture`; don't build that
indirection now, but keep every OS call inside `Platform.*` so it stays a small change.

New Core code (platform-neutral, unit tested):

```text
src/EveLocalMon.Core/
  Settings/AppSettings.cs, SettingsStore.cs, SettingsPaths.cs
  Reading/CalibrationDetector.cs
  Capture/CaptureSession.cs     shared dedupe/counting used by both the CLI and the app
```

Packages: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` (12.1.2). No MVVM toolkit.

---

## 2. Coordinate spaces (get this wrong and nothing lines up)

Three spaces, converted in exactly two places:

1. **Screen space** — physical pixels on the virtual desktop, possibly negative. What the picker
   produces and what `IScreenCapture.Capture(PixelRect)` consumes.
2. **Region/frame space** — origin at the captured region's top-left. **Everything in
   `Calibration` is stored in this space**, so a saved calibration survives the EVE window being
   moved: the user re-picks the region and the inner offsets still hold.
3. **DIPs** — Avalonia's layout units. Only the picker overlay and the preview `Image` deal in
   these; convert with `Screen.Scaling`.

```csharp
// Screen -> region: frameX = screenX - region.X
// Region -> screen: screenX = region.X + frameX
// DIP (window-local, inside an overlay placed at screen.Bounds) -> screen:
//   screenX = screen.Bounds.X + (int)Math.Round(pointerX * screen.Scaling)
```

The existing fixtures stay valid unchanged: their "region" is the whole PNG, so region space and
image space coincide there.

Put these conversions in one static class (`Core/Imaging/CoordinateSpaces.cs`) with unit tests —
they are the single most likely source of silent, hard-to-see error in this milestone.

---

## 3. Region picker

Trigger: **"Select region…"** on the main window.

Behaviour:

1. Open one borderless, topmost, transparent window per `Screens.All` entry, each positioned at
   `screen.Bounds.X/Y` (physical) with `Width = screen.Bounds.Width / screen.Scaling` and
   `Height = screen.Bounds.Height / screen.Scaling` (DIPs). Getting this pair wrong is the classic
   multi-monitor/mixed-DPI bug — an overlay that covers only part of a 150% monitor.
2. Dim each overlay (~35% black), crosshair cursor, and a centred hint: *"Drag around the Local
   member list — Esc to cancel"*.
3. Press → drag → release draws a live rectangle with a `W × H` readout in physical pixels, plus
   the origin. The selection is confined to the monitor where the drag **started**; a drag that
   leaves that monitor clamps to its bounds. (EVE's Local pane lives on one monitor; supporting
   cross-monitor rectangles buys nothing and complicates capture.)
4. Esc, right-click, or a release smaller than 20×20 px cancels and leaves settings untouched.
5. On confirm: close all overlays, capture the region once, run auto-detect (§4), and populate the
   calibration panel.

Notes:

- Hide the main window while picking, and wait ~150 ms after hiding before capturing, so it isn't
  in the shot.
- The overlays are closed before any capture, so they never contaminate a frame.

---

## 4. Calibration auto-detect

`CalibrationDetector.Detect(Frame region, DetectorOptions? options = null) -> DetectionResult`

```csharp
public sealed record DetectionResult(
    Calibration? Calibration,       // null when Confidence == Failed
    DetectionConfidence Confidence, // Good | Partial | Failed
    IReadOnlyList<string> Notes);   // human-readable findings/warnings for the panel
```

All steps work on the region frame. Defaults reused from `ReaderOptions` (luma 120 / 70,
chroma 40) so the detector and the reader agree on what a pixel is.

**a) Icon column.** For each x, count pixels with `Chroma >= 40` (saturated). Text is gray and the
neutral icon is gray, so this isolates colour icons. Measured on `sample-local-mixed-overflow.png`:
columns 303–314 scored 392–668, every other column scored 0 — an unambiguous run. Take the longest
run of consecutive non-zero columns; require width 8–20 → `IconX` = run start, `IconSize` = run width.

*Fallback (a Local showing only gray/neutral icons):* repeat with "icon-like" (`Luma >= 70 ||
Chroma >= 40`) restricted to the leftmost 40 px of the region. If that also fails → `Failed`, and
the panel asks the user to click an icon in the preview instead.

**b) Row pitch and icon size.** Within the icon column, find vertical runs of icon-like pixels.
The modal run height is `IconSize` (measured: 12). **Discard runs more than 1 px off that height
before measuring the pitch** — a row clipped by the top of the viewport leaves a short run whose
distance to the next icon is *smaller* than the true pitch, silently poisoning the estimate. (As
built, this cost a cycle: including the clipped run gave pitch 13 on sample 1, and 74 "occupied"
rows.) `RowPitch` = the **smallest** gap between surviving run starts, because gaps are always
whole multiples of the pitch — rows without icons just skip one. Needs ≥2 runs; with only one,
assume `IconSize + 5` and mark `Partial`.

The header's person glyph sits in the same columns as the icons, so runs not sharing the list's
modal row phase are dropped before anything else uses them.

**c) `ListRect.Y`.** This must be the viewport's clipping edge, and neither obvious rule finds it:
setting it just below the header makes a clipped first row look complete (destroying the overflow
signal of `doc/PLAN.md` §4.6), while setting it to the first content row turns an *unclipped* first
row into a phantom partial one and falsely reports overflow. Instead, walk upward from the topmost
detected icon in pitch-sized steps and classify the band above:

- content starting at the slot top +0 or +2 (an icon, or text on a row with no icon) → a whole row:
  adopt it and keep walking up;
- content starting lower than +2 → that row is cut off, so the viewport edge is exactly where its
  content begins: stop there;
- no content, or content continuing above the slot (that's the header block) → stop; the current
  top is the first row.

Verified both ways: sample 1 (scrolled) gives y=68, keeping the clipped row partial so Overflow is
still detected; sample 2 (not scrolled) gives y=78, keeping all three rows whole.
`ListRect.X`/`Width` = the region's, `ListRect.Bottom` = region bottom.

**d) `NameX` / `NameRight`.** Count, per column, how many *slots* carry text there — slot-based
counting keeps the header digits (above the list) and stray bright pixels from reading as name
text. `NameX` = the first column right of the icon column with text in ≥¼ of the occupied rows,
minus 1; it falls back to `IconX + IconSize + 3` when the list is too sparse to measure, which is
what happens on sample 2 and gives exactly the right answer. `NameRight` = the last column with
text in ≥⅛ of them, +2 — a laxer bar, since only the longest names reach that far. Columns lit on
≥80% of *all* rows are excluded as borders/dividers, since real text has gaps between rows.

**No scrollbar detection is needed.** Measured on sample 1, EVE's scrollbar peaks at luma 88
against a text threshold of 120, so it contributes no text pixels and cannot inflate `TextPixels`.

**e) `HeaderCountRect`.** Inside the header band, find runs of lit (`Luma >= 120`) columns. The
leftmost run is the person glyph; the digits are the run(s) to its right. Rect = first digit column
− 2 … last digit column + 2, header band ± 2. If no digits are found, leave it null: overflow then
rests on the partial-row signal alone, which `SyntheticReaderTests` already covers.

Every value lands in editable numeric fields; `Notes` explains what was found ("Icon column x=5
w=12, row pitch 17px, name area x=20..116, list starts at y=28, 56 icons seen"). The preview draws
`ListRect`, the icon column and the name area as outlines so mistakes are visible rather than
inferred.

---

## 5. Default templates

Move `tests/EveLocalMon.Core.Tests/Fixtures/templates/**` → `assets/templates/**` (6 icons +
`templates.json` + `digits/`). Then:

- The test project **links** them, exactly as it already links `doc/samples/*.png`:
  `<None Include="..\..\assets\templates\**\*" Link="Fixtures\templates\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />`.
  All 29 tests must still pass untouched — if a test needs editing, the move was done wrong.
- The App copies `assets/templates` into `%APPDATA%\EveLocalMon\templates` on first run (never
  overwriting), and loads templates from there, so M4's "add this unknown icon as friendly" has a
  writable home.

---

## 6. `capture` CLI fixes — why 5 intervals produced 4 files

Observed: `--count 5` wrote files at 20:14:33.476, :35.725, :37.761, :41.818. The 4-second gap is
two intervals — the 4th frame was byte-identical to the 3rd and was dropped **silently**, and the
run then ended because `--count` had been reached. Both behaviours were intended; neither was
visible. `CaptureCommand` currently counts *attempts*, not files, and prints nothing on a skip.

Move the logic into `Core/Capture/CaptureSession` (unit-testable, no console, no Win32) and change:

1. **`--count` counts saved files**, which is what "give me 5 screenshots" means. Unbounded by default.
2. **Skips are visible**: keep a run of consecutive duplicates and report it with the next save —
   `Saved 20260915-201441-818.png (3 identical frames skipped)` — so gaps in timestamps explain
   themselves without spamming a line every 500 ms at an idle screen.
3. **Say why it stopped**: `Reached --count 5, stopping.` or `Ctrl+C, stopping.`
4. **Exit summary**: `5 frames captured, 4 saved, 1 identical skipped, 8.3 s elapsed.`
5. **`--no-dedupe`** saves every frame (for timing-sensitive collection).

Already applied while diagnosing: `.vscode/launch.json` no longer passes `--count 5` (it now runs
until stopped, with the dedupe behaviour noted in a comment), and `captures/` is git-ignored.

---

## 7. Main window

Single window, three parts:

- **Toolbar** — `Select region…`, `Start` / `Stop`, poll interval (ms), `Re-detect calibration`,
  `Save settings`.
- **Preview** (left, scrollable) — the captured region at 1:1 physical pixels via `WriteableBitmap`.
  Set `RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None)` and size the
  `Image` to `pixelSize / screen.Scaling` DIPs so it isn't resampled to mush on a scaled display.
  Overlays per row: Friendly green, Enemy red, Neutral amber, Unknown magenta, NoIcon gray, partial
  rows a dashed outline. Calibration rects drawn as thin outlines.
- **Status panel** (right) — readability (`Full` / `Overflow` / `NotVisible`) with the reason,
  header member count vs. visible rows, threat total and breakdown, and a per-row table matching
  the CLI's `read` output (slot y, category, template, best/second-best score).

`MonitorLoop`: `PeriodicTimer` (default 500 ms) on a background task → `IScreenCapture.Capture` →
`LocalListReader.Read` → post `(Frame, Snapshot)` to the UI thread. Skip the reader when the frame
hash is unchanged, but keep the UI responsive. Never touch Avalonia objects off the UI thread.

`NotVisible` must read as a warning in the panel ("Local window not visible — covered, minimised or
mis-calibrated"), never as "all clear" — the same distinction alerts will rely on in M3.

---

## 8. Settings

`%APPDATA%\EveLocalMon\settings.json` (macOS `~/Library/Application Support/EveLocalMon/`), written
atomically (temp file + replace):

```json
{
  "schemaVersion": 1,
  "region": { "x": 2200, "y": 300, "width": 140, "height": 900 },
  "calibration": {
    "listRect": { "x": 0, "y": 24, "width": 140, "height": 876 },
    "iconX": 3, "nameX": 18, "nameRight": 130,
    "headerCountRect": { "x": 20, "y": 0, "width": 30, "height": 16 },
    "rowPitch": 17, "iconSize": 12
  },
  "pollIntervalMs": 500,
  "templatesDirectory": null,
  "readerOptions": null
}
```

`null` means "use the built-in default". Unknown/missing fields must not throw — a settings file
from a future version should degrade, not crash. Calibration is region-relative (§2).

---

## 9. Tests

Automated (`dotnet test EveLocalMon.sln --filter "Category!=Perf"` stays green, 29 existing + new):

| Area | Test |
| --- | --- |
| Auto-detect | On both samples, `Detect` returns `Good` and yields `IconX`/`RowPitch`/`IconSize`/`NameX` within ±2 px of the known-good fixture calibration, and `HeaderCountRect` that reads the right count |
| Auto-detect → reader | Feeding the **auto-detected** calibration into `LocalListReader` reproduces each fixture's expected counts, threats and readability — the detector is only correct if the reader agrees |
| Auto-detect failure | A blanked frame returns `Failed` with a note, and never a bogus calibration |
| Coordinate spaces | Screen↔region↔DIP round-trips at scaling 1.0, 1.25, 1.5, including negative screen origins |
| `CaptureSession` | `--count` stops after N **saved** frames; duplicates are skipped and counted; `--no-dedupe` saves every frame; summary numbers add up |
| Settings | Round-trip; unknown field ignored; missing optional field falls back to default; atomic write leaves no temp file |

Manual acceptance (no UI automation this milestone):

1. Drag a region around EVE's Local pane; preview shows it live and rows are classified correctly.
2. Counts in the status panel match what you see in game, including a neutral and the own-pilot row.
3. Overflow: shrink the Local window until it scrolls → panel flips to `Overflow` with the reason.
4. Cover the Local window → `NotVisible`, not "clear".
5. Settings survive an app restart; `Re-detect calibration` reproduces the same numbers.
6. Works on a second monitor, and on a 125%/150%-scaled monitor (region lands where you dragged).
7. Esc during picking changes nothing.

---

## 10. Risks

- **Mixed-DPI multi-monitor** is the most likely thing to be subtly wrong; the manual checklist
  exists mainly for it.
- **Auto-detect on an all-gray Local** (no coloured icons visible) falls back and may need the
  manual path — acceptable, but it should say so rather than guess.
- **Scrollbar inside the name area** inflates text pixels; detected and excluded, worth re-checking
  against a real scrolled window.
- **Preview CPU**: at 500 ms with a ~140×900 region this is negligible, but don't re-run the reader
  on unchanged frames.

---

## 11. Out of scope

Alerts of any kind, tray icon, autostart, template cropping/teaching, multi-region/multiboxing,
macOS, installer. Don't start them here.
