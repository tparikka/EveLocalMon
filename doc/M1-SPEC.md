# EveLocalMon — M0 + M1 implementation spec

**Status: implemented (2026-09-12).** All thresholds and coordinates below matched the two
real screenshots on the first test run — no tuning was needed. `src/EveLocalMon.Core/Reading`
and `src/EveLocalMon.Core/Alerts` are the reader and alert state machine; `EveLocalMon.sln`
builds all four projects; `dotnet test EveLocalMon.sln --filter "Category!=Perf"` passes
(28 tests). Next up is M2 (alert dispatch — sound/toast/ntfy).

Scope: **M0** (solution skeleton + capture CLI) and **M1** (Local list reader, header count
reader, alert state machine, fixture tests). Alerts (M2) and GUI (M3) are out of scope.
Background and rationale: `doc/PLAN.md` §4.

All pixel coordinates are **frame coordinates** (physical pixels, origin top-left of the
captured image). Rectangles are `x, y, width, height`; ranges are half-open `[start, end)`
unless written as `a … b` (inclusive).

---

## 0. Prerequisites

- **.NET 10 SDK** (not installed on the dev machine as of 2026-09-12; only 6 and 7 are):
  `winget install Microsoft.DotNet.SDK.10`
- Use the latest stable NuGet versions of: `SkiaSharp`, `xunit`, `xunit.runner.visualstudio`,
  `Microsoft.NET.Test.Sdk`, `System.CommandLine` (CLI parsing; optional — hand-rolled parsing is fine).

---

## 1. M0 — Skeleton and capture CLI

### 1.1 Solution layout

```text
EveLocalMon.sln
Directory.Build.props             Nullable=enable, ImplicitUsings=enable, LangVersion=latest
src/
  EveLocalMon.Core/               net10.0, refs SkiaSharp. TreatWarningsAsErrors=true
  EveLocalMon.Platform.Windows/   net10.0-windows10.0.19041.0, refs Core
  EveLocalMon.Capture.Cli/        net10.0-windows10.0.19041.0, refs Core + Platform.Windows
tests/
  EveLocalMon.Core.Tests/         net10.0, xUnit, refs Core
.gitignore                        standard VisualStudio/dotnet ignore
```

`EveLocalMon.App` (Avalonia) is created in M3, not now.

### 1.2 Core: frame and image I/O

```csharp
namespace EveLocalMon.Core.Imaging;

// BGRA8888, top-down. Alpha is ignored by all detection code.
public sealed class Frame
{
    public Frame(int width, int height, int stride, byte[] pixels);
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public byte[] Pixels { get; }
    public Bgr GetPixel(int x, int y);            // bounds-checked in DEBUG only
    public Frame Crop(PixelRect rect);            // copies
}

public readonly record struct Bgr(byte B, byte G, byte R)
{
    public int Luma => (R * 299 + G * 587 + B * 114) / 1000;
    public int Chroma => Math.Max(R, Math.Max(G, B)) - Math.Min(R, Math.Min(G, B));
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;    // exclusive
    public int Bottom => Y + Height;  // exclusive
}

public static class FrameIo
{
    public static Frame LoadPng(string path);          // SkiaSharp decode → Bgra8888
    public static void SavePng(Frame frame, string path);
}
```

### 1.3 Core: capture abstraction

```csharp
namespace EveLocalMon.Core.Capture;

public interface IScreenCapture
{
    // Region in physical virtual-desktop pixels (can be negative on multi-monitor setups).
    Frame Capture(PixelRect region);
}
```

### 1.4 Platform.Windows: `GdiScreenCapture : IScreenCapture`

- P/Invoke: `GetDC(IntPtr.Zero)`, `CreateCompatibleDC`, `CreateDIBSection` (32-bpp top-down,
  negative `biHeight`), `BitBlt(..., SRCCOPY | CAPTUREBLT)`, copy bits into `Frame`,
  release everything in `finally`.
- The process must be **per-monitor DPI aware (V2)** so coordinates are physical pixels:
  CLI gets an `app.manifest` with `<dpiAwareness>PerMonitorV2</dpiAwareness>`.
- Reuse the DIB between calls when the region size is unchanged (it's called at 2 Hz for hours).

### 1.5 Capture CLI

Executable name `evelocalmon-capture`. Subcommands:

| Command | Behaviour |
| --- | --- |
| `capture --region x,y,w,h --out <dir> [--interval <sec>=2] [--count <n>=∞]` | Capture region every interval, save `yyyyMMdd-HHmmss-fff.png`. Skip saving if bytes identical to the previous frame. Ctrl+C stops. |
| `crop <src.png> x,y,w,h <out.png>` | Crop a PNG (used to extract templates from samples). |
| `read <src.png> <fixture.json> <templatesDir>` | *(M1)* Run the reader and print a per-row table: slot y, category, template, best score, second-best score, text pixel count, icon pixel count. Then header count, readability, threat count. |

M0 acceptance: `capture` produces valid PNGs of the requested region on a 100%-scaled display;
`crop` output of `doc/samples/sample-local-mixed-overflow.png` at `303,81,12,12` is the green
star icon.

---

## 2. M1 — Reader

### 2.1 Measured facts (100% EVE UI scale, 100% Windows scaling, compact Local list)

| Fact | Value | Source |
| --- | --- | --- |
| Icon size | 12 × 12 px, rounded square, opaque | both samples |
| Row pitch | 17 px (12 px icon + 5 px gap) | both samples |
| Name text rows | slot top + 2 … slot top + 11 | sample 2 |
| Name text start | icon x + 17 | sample 2 (x 331 vs icon 314) |
| Name text | light gray, core luma ≈ 150–195 (anti-aliased edges lower); background luma ≤ 30 typ., ≤ 60 max (window is translucent — stars/grid show through) | both |
| Same icon, different samples/backgrounds | mean abs diff ≤ 2.2 | measured |
| Different icons, closest pair (blue star vs blue plus) | mean abs diff 17.3 | measured |
| Different icons, other pairs | ≥ 48 | measured |
| **Own pilot** (active client) | **has no icon** | sample 2, "Echo Menoptra" |

### 2.2 Types

```csharp
namespace EveLocalMon.Core.Reading;

public enum RowCategory { Empty, Friendly, Enemy, Neutral, Unknown, NoIcon }
// Friendly/Enemy/Neutral come from a matched template.
// Unknown = icon present, no template within threshold.
// NoIcon  = name text present, no icon (a true neutral, or the own pilot).

public enum Readability { Full, Overflow, NotVisible }

public sealed record Calibration(
    PixelRect ListRect,          // member list viewport (below the header), frame coords
    int IconX,                   // left x of the 12 px icon column
    int NameX,                   // left x of the name area (usually IconX + 15)
    int NameRight,               // exclusive right edge of the name area (stop before scrollbar)
    PixelRect? HeaderCountRect,  // digits only — must exclude the person icon
    int RowPitch = 17,
    int IconSize = 12);

public enum TemplateCategory { Friendly, Enemy, Neutral }

public sealed record IconTemplate(string Name, TemplateCategory Category, Frame Image, bool[] Mask);
// Mask: IconSize*IconSize, row-major; true = compared. Default mask = all except the 4 corner pixels.

public sealed record TemplateSet(IReadOnlyList<IconTemplate> Icons, IReadOnlyDictionary<char, bool[,]> Digits);

public sealed record RowResult(
    int SlotTop, bool IsPartial, RowCategory Category, string? TemplateName,
    double BestScore, double SecondBestScore, int TextPixels, int IconPixels);

public sealed record ThreatBreakdown(int Enemy, int Neutral, int Unknown, int ExtraNoIcon)
{
    public int Total => Enemy + Neutral + Unknown + ExtraNoIcon;
}

public sealed record Snapshot(
    IReadOnlyList<RowResult> Rows,        // full and partial rows, top to bottom
    int OccupiedFullRows,                 // full rows with Category != Empty
    bool HasPartialRowWithContent,
    int? HeaderMemberCount,               // null if unreadable
    Readability Readability,
    ThreatBreakdown Threats);

public sealed record ReaderOptions
{
    public int TextLumaMin { get; init; } = 120;
    public int TextMinPixels { get; init; } = 15;      // per row, in name area
    public int IconPixelLumaMin { get; init; } = 70;   // pixel counts as "icon" if luma ≥ this…
    public int IconPixelChromaMin { get; init; } = 40; // …or chroma ≥ this
    public double IconPresentFraction { get; init; } = 0.5;  // of masked pixels
    public double MatchMaxMeanDiff { get; init; } = 8.0;     // see §2.1: 2.2 vs 17.3
    public int MatchSearchRadius { get; init; } = 1;         // ±px in x and y
    public int PartialRowMinPixels { get; init; } = 4;
    public int OwnPilotNoIconAllowance { get; init; } = 1;
    public int DigitLumaMin { get; init; } = 120;
    public double DigitMaxMismatchFraction { get; init; } = 0.15;
}

public static class LocalListReader
{
    public static Snapshot Read(Frame frame, Calibration cal, TemplateSet templates, ReaderOptions? options = null);
}

public static class HeaderCountReader
{
    public static int? Read(Frame frame, PixelRect rect, IReadOnlyDictionary<char, bool[,]> digits, ReaderOptions options);
}

public static class TemplateStore
{
    // Loads templates.json + PNGs, and digits/<d>.png (binarised on load with DigitLumaMin).
    public static TemplateSet Load(string directory);
}
```

### 2.3 Algorithm

**a) Row phase.** For each `y` in `ListRect`, let `content[y]` = number of pixels in
`[IconX, NameRight)` that are *text-like* (luma ≥ `TextLumaMin`) or *icon-like*
(luma ≥ `IconPixelLumaMin` or chroma ≥ `IconPixelChromaMin`). For each phase `p` in
`0 … RowPitch−1`:

```text
score(p) = Σ_y  ( (y − ListRect.Y − p) mod RowPitch < IconSize ? +content[y] : −content[y] )
```

Pick the max (ties → smallest `p`). If all `content` is 0, `p = 0`.

**b) Slots.** Slot tops `t = ListRect.Y + p + k·RowPitch` for every integer `k` (including
`k = −1`) where `[t, t+IconSize)` intersects `ListRect`. A slot is **full** if it lies entirely
inside `ListRect`, otherwise **partial**.

**c) Per full slot** (icon rect `IconX, t, 12, 12`; name rect `NameX, t, NameRight−NameX, 12`):

1. `TextPixels` = pixels in name rect with luma ≥ `TextLumaMin`. Occupied if ≥ `TextMinPixels`.
2. `IconPixels` = masked pixels in icon rect that are icon-like. Icon present if
   `IconPixels ≥ IconPresentFraction × maskedCount`.
3. If icon present: for every template and every offset `dx, dy ∈ [−r, r]`, score =
   mean over masked pixels of `(|ΔR| + |ΔG| + |ΔB|) / 3`. Keep best and second-best
   (second-best = best score of a *different* template). Best ≤ `MatchMaxMeanDiff` →
   template's category; else `Unknown`.
4. No icon: occupied → `NoIcon`; else `Empty`.

**d) Partial slots**: not classified (`Category = Empty`, `IsPartial = true`); they only set
`HasPartialRowWithContent` if the visible part has ≥ `PartialRowMinPixels` content pixels.

**e) Header count**: binarise `HeaderCountRect` with `DigitLumaMin`, split into glyphs at
all-dark columns (if a glyph is wider than 1.6 × the widest digit template, split it into
equal cells of that width). Crop each glyph to its bounding box, and compare with each
digit template of the same size (±1 px of padding allowed). Fraction of mismatched pixels ≤
`DigitMaxMismatchFraction` → that digit. Any glyph unmatched, or no glyphs → `null`.

**f) Readability**:

- `NotVisible` if `OccupiedFullRows == 0`. The own pilot is always in Local, so an empty
  list means the window is covered, minimised, moved, or the calibration is wrong.
- else `Overflow` if `HasPartialRowWithContent` or `HeaderMemberCount > OccupiedFullRows`.
- else `Full`.

**g) Threats**: `Enemy`, `Neutral`, `Unknown` = row counts of those categories;
`ExtraNoIcon = max(0, NoIconRows − OwnPilotNoIconAllowance)`.
Known caveat: during overflow the own pilot may be off-screen, so one real no-icon neutral
can be hidden by the allowance. That's acceptable because overflow alerts anyway.

**Performance**: `Read` on sample 1 must average < 5 ms after warm-up (Release build).
Iterate the pixel buffer directly (`Span<byte>`), not through `GetPixel`, in the hot loops.

---

## 3. M1 — Alert state

```csharp
namespace EveLocalMon.Core.Alerts;

public sealed record AlertStateOptions
{
    public int StableFrames { get; init; } = 2;
    public bool EmitLocalClear { get; init; } = true;
    public bool AlertOnMemberIncreaseWhileUnreadable { get; init; } = false;
}

public abstract record AlertEvent(DateTimeOffset At);
public sealed record ThreatIncreased(int Previous, int Current, ThreatBreakdown Breakdown, DateTimeOffset At) : AlertEvent(At);
public sealed record LocalClear(DateTimeOffset At) : AlertEvent(At);
public sealed record ReadabilityChanged(Readability Previous, Readability Current, int? MemberCount, int VisibleRows, DateTimeOffset At) : AlertEvent(At);
public sealed record MemberCountIncreasedWhileUnreadable(int Previous, int Current, DateTimeOffset At) : AlertEvent(At);

public sealed class AlertState
{
    public AlertState(AlertStateOptions options);
    public IReadOnlyList<AlertEvent> Update(Snapshot snapshot, DateTimeOffset now);
}
```

Rules:

- Three independently debounced values: `Threats.Total`, `Readability`, `HeaderMemberCount`.
  A raw value becomes **stable** after `StableFrames` consecutive identical readings.
- Initial stable values: threats `0`, readability `Full`, member count `null` (so starting up
  in a system with a neutral alerts immediately after `StableFrames`).
- Stable threats rise → `ThreatIncreased(prev, cur, breakdown of latest snapshot)`.
  Stable threats fall to 0 from > 0 → `LocalClear` (if enabled). Other decreases: no event.
- Stable readability changes → `ReadabilityChanged`. The dispatcher (M2) words
  `Overflow`/`NotVisible` as *"Local unreadable"*, never as a threat.
- Stable member count rises while stable readability ≠ `Full` and the option is on →
  `MemberCountIncreasedWhileUnreadable`. A `null` reading never changes the stable value.
- Blinking flags (criminal/suspect/war target) may flip a row between `Enemy`, `NoIcon`
  and `Unknown` frame to frame; using the combined total keeps this from firing alerts.
- Rate limiting per channel is **not** done here (it belongs to the M2 dispatcher).

---

## 4. Fixtures and templates

### 4.1 Files

```text
tests/EveLocalMon.Core.Tests/
  Fixtures/
    <sample>.json                 calibration + expectations (schema §4.3)
    templates/
      templates.json              [{ "name": "...", "file": "...png", "category": "Friendly" }]
      *.png                       12×12 crops (table §4.2)
      digits/0.png 1.png 3.png    digit glyph crops (bounding box of lit pixels)
```

Sample PNGs are **linked** from `doc/samples` (don't copy):

```xml
<None Include="..\..\doc\samples\*.png" Link="Fixtures\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
<None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
```

### 4.2 Templates to extract (with `evelocalmon-capture crop`)

S1 = `sample-local-mixed-overflow.png`, S2 = `sample-local-nonscrolled-one-neut-one-friendly.png`.

| Name | Category | Source | Rect | Code |
| --- | --- | --- | --- | --- |
| `green-star` | Friendly | S1 | 303,81,12,12 | G |
| `purple-person` | Friendly | S1 | 303,98,12,12 | P |
| `blue-star` | Friendly | S1 | 303,166,12,12 | B |
| `red-minus` | Enemy | S1 | 303,217,12,12 | R |
| `gray-equals` | Neutral | S1 | 303,472,12,12 | N |
| `blue-plus` | Friendly | S2 | 314,95,12,12 | + |

Not yet available (so they currently read as `Unknown`, which alerts — the safe default):
orange standing, light-blue standing, suspect, criminal, war target. Add them when samples exist.

Digits: `1` and `0` from S1 header rect, `3` from S2 header rect. Crop each glyph to the
bounding box of pixels with luma ≥ 120. Save as PNG; `TemplateStore` binarises on load.

### 4.3 Fixture JSON

`sample-local-mixed-overflow.json`:

```json
{
  "image": "sample-local-mixed-overflow.png",
  "calibration": {
    "listRect": { "x": 300, "y": 68, "width": 116, "height": 967 },
    "iconX": 303, "nameX": 318, "nameRight": 414,
    "headerCountRect": { "x": 320, "y": 44, "width": 30, "height": 16 }
  },
  "expected": {
    "phaseFirstFullSlotTop": 81,
    "occupiedFullRows": 56,
    "hasPartialRowWithContent": true,
    "headerMemberCount": 100,
    "readability": "Overflow",
    "counts": { "Friendly": 54, "Enemy": 1, "Neutral": 1, "Unknown": 0, "NoIcon": 0 },
    "threatTotal": 2,
    "rowCodes": "GPPPPBBBRGGPBBGPPGBGPBPNGBGBBGPPBBBBBBPBGBPPPBPBBPGBPPPP"
  }
}
```

`rowCodes`: one character per full occupied row, top to bottom, using the codes in §4.2
(`-` = NoIcon, `?` = Unknown). Row 9 = "kriller 643" (red), row 24 = "Orion Siegeteck" (gray).

`sample-local-nonscrolled-one-neut-one-friendly.json`:

```json
{
  "image": "sample-local-nonscrolled-one-neut-one-friendly.png",
  "calibration": {
    "listRect": { "x": 310, "y": 72, "width": 132, "height": 546 },
    "iconX": 314, "nameX": 329, "nameRight": 440,
    "headerCountRect": { "x": 330, "y": 53, "width": 30, "height": 16 }
  },
  "expected": {
    "phaseFirstFullSlotTop": 78,
    "occupiedFullRows": 3,
    "hasPartialRowWithContent": false,
    "headerMemberCount": 3,
    "readability": "Full",
    "counts": { "Friendly": 1, "Enemy": 0, "Neutral": 1, "Unknown": 0, "NoIcon": 1 },
    "threatTotal": 1,
    "rowCodes": "-+N"
  }
}
```

Row 1 = "Echo Menoptra" (own pilot, no icon), row 2 = "Jmam Anamamal" (blue plus),
row 3 = "kriller 643" (gray).

`headerCountRect` values are approximate (digits measured at S1 x 323…344 / y 47…56,
S2 x 334…340 / y 56…65). Adjust if needed; they must exclude the person icon (S1 x 305…316,
S2 x 316…327).

---

## 5. Tests (M1 acceptance)

All must pass with `dotnet test`.

**Fixture tests** (theory over every `Fixtures/*.json`): every `expected` field matches.
On failure, output the per-row table (same as CLI `read`).

**Template sanity**: every template matches its own source slot with score ≤ 2.5; `gray-equals`
from S1 matches S2 slot `314,112` with score ≤ 2.5 (cross-sample, different background).

**Synthetic reader tests** (modify a loaded S2 frame in memory):

| Modification | Expected |
| --- | --- |
| Fill `blue-plus` slot (314,95) with solid orange (R255,G140,B0), keeping corners | row 2 `Unknown`; threats 2 |
| Fill same slot with background (19,23,26) | row 2 `NoIcon`; `ExtraNoIcon` 1; threats 2 |
| Paste `red-minus` template into slot (314,78) (own pilot row) | row 1 `Enemy`; threats 2 |
| Blank the whole list rect to background | `NotVisible`; threats 0 |
| S1 with `ListRect` = `300,81,116,952` (starts exactly at a slot top, ends 5 px after the 56th slot) | no partial rows; header 100 → still `Overflow` |
| Same `ListRect`, `HeaderCountRect = null` | `Full` (documents that header count is the only overflow signal when rows align with the viewport) |

**Header tests**: S1 → 100, S2 → 3; a rect containing an unknown glyph → `null`.

**AlertState tests** (hand-built `Snapshot`s):

- 0,0,1,1 threats (StableFrames 2) → one `ThreatIncreased(0,1)` on the 4th update.
- 1,2,1,2,1,2 (flicker) → no event after the initial stable value.
- 1,1,2,2 → `ThreatIncreased(0,1)` then `ThreatIncreased(1,2)`.
- 2,2,0,0 → `LocalClear`; with `EmitLocalClear=false` → nothing.
- Readability Full→Overflow→Overflow → one `ReadabilityChanged(Full, Overflow)`.
- Overflow with member count 100,100,103,103 → event only when option is on.
- Start-up with threats already 1 → alert after `StableFrames`.

**Performance** (trait `Category=Perf`, excluded by default): `Read` on S1 < 5 ms average over
100 runs after 10 warm-up runs.

---

## 6. Out of scope for M1

Alert dispatch/wording, sound/toast/ntfy, GUI, calibration UI, auto-detection of
`ListRect`/`IconX`, macOS, multi-region. Don't add them yet.
