# EveLocalMon — Plan (draft v0.5)

Implementation detail for M0/M1 lives in `doc/M1-SPEC.md`.

A desktop app that watches the EVE Online Local chat member list on screen and alerts
when a hostile, neutral or unrecognised pilot appears: plays a sound, shows a toast
notification, and optionally POSTs to an ntfy topic.

Target: Windows first, macOS second. Passive only — the app reads screen pixels and never
reads client memory or sends input to the EVE client.

## Decisions so far

| Topic | Decision |
| --- | --- |
| Stack | C# / .NET 10 (LTS) + Avalonia UI |
| Alert on | Red/orange standings, criminal/suspect, war targets, **neutrals** (no icon, or the gray neutral-standing icon), **unknown icons** |
| Alert behaviour | Alert on first appearance **and** whenever the threat count goes up; silent while unchanged |
| List overflow | **Separate alert condition** when Local is too long to read in full; text says the list can't be verified, *not* that a threat was seen (§4.6) |
| Sorting | EVE can't sort Local by standing (it's alphabetical), so overflow can't be avoided by sorting |
| Clients | One Local window (single region) for v1; keep the design open to more later |
| Local layout | Compact member list (small icon left of the name) |
| Own character | The pilot in the active client is shown **without an icon**. One no-icon row is allowed for it; each additional no-icon row is a threat |
| Display | EVE Windowed mode, EVE UI scale 100%, Windows scaling 100% |
| ntfy | `https://ntfy.sh/<topic>`, no auth token, no screenshot attachment |

---

## 1. Requirements

### Must have (v1, Windows)

- Select the Local member list region with a drag-to-select overlay; save it.
- Calibrate the list layout once (row pitch, icon slot, name area, header count area).
- Classify each visible row: empty / friendly / enemy / neutral / unknown.
- Read the Local member count from the header (see §4.6).
- Alert when the threat count increases: sound + toast + optional ntfy.
- Alert when the list overflows (can't be fully read), with distinct wording (§4.6).
- Debounce so scrolling, hover highlights, blinking flags or redraws don't cause false alerts.
- Tray icon: Start/Stop, Mute (15 min / until re-enabled), Settings, Quit.
- Live preview: captured region with per-row classification overlay and match scores.
- "Test alert" button per channel.
- Settings persisted to JSON.

### Should have

- Global hotkey to mute/acknowledge.
- Detection history log.
- Start with Windows.
- Per-category enable/disable (e.g. ignore neutrals in highsec).
- "Add unknown icon as friendly" action from the preview/history, to quickly teach new icons.

### Later

- macOS build (signed + notarized).
- Multiple regions (multiboxing).
- Quiet hours, per-category sounds.
- ntfy auth token (Windows DPAPI / macOS Keychain) and screenshot attachment.

---

## 2. Stack

- **.NET 10**, **Avalonia** (latest stable) — cross-platform UI, built-in `TrayIcon`,
  transparent topmost windows for the region picker.
- **SkiaSharp** (already shipped with Avalonia) for PNG load/save of templates and fixtures.
  No extra imaging dependency.
- **Detection in pure managed C#** on raw BGRA buffers (`Span<byte>`, optionally
  `System.Numerics.Vector`). This avoids native OpenCV runtimes (OpenCvSharp's macOS support
  is weak; Emgu CV is GPL/commercial). The row-based approach below only searches
  a few small slots per frame, so we don't need a general-purpose matching library.
- **Microsoft.Extensions.Hosting / DI** to wire platform services.
- **xUnit** for tests.
- **Velopack** for the installer + auto-update (supports Windows and macOS).

### Platform services (interfaces in Core, implementations per OS)

| Interface | Windows | macOS (later) |
| --- | --- | --- |
| `IScreenCapture` | GDI `BitBlt` from screen DC via P/Invoke (simple, fine at 2 Hz). Upgrade path: Windows.Graphics.Capture to capture the EVE window even when covered. | ScreenCaptureKit (`SCScreenshotManager`, macOS 14+) via a small Swift helper or .NET-macOS bindings. `CGWindowListCreateImage` is obsolete. |
| `IToastNotifier` | `Microsoft.Toolkit.Uwp.Notifications` `ToastContentBuilder` (works for unpackaged Win32, needs TFM `net10.0-windows10.0.19041.0`). Alternative: Windows App SDK `AppNotificationManager`. | `UNUserNotificationCenter` (requires bundled, signed `.app`). |
| `ISoundPlayer` | `System.Media.SoundPlayer` (WAV) or NAudio for volume control | `AVAudioPlayer` / `afplay` |

`INtfyClient` (HttpClient), detector, state machine and config are all platform-neutral.

---

## 3. Architecture

```text
┌──────────────┐ frame ┌────────────────┐ Snapshot ┌──────────────┐ AlertEvent ┌─────────────────┐
│ CaptureLoop  │──────▶│ LocalListReader│─────────▶│ AlertState   │───────────▶│ AlertDispatcher │
│ (timer, 2 Hz)│       │ (rows, icons,  │          │ (debounce,   │            │ ├ Sound         │
└──────────────┘       │  header count) │          │  count delta)│            │ ├ Toast         │
       ▲               └────────────────┘          └──────────────┘            │ └ Ntfy          │
       │                                                                        └─────────────────┘
   AppSettings ◀── Avalonia UI (tray, settings, region picker, calibration, templates, live preview)
```

- **CaptureLoop**: `PeriodicTimer` on a background task. Skips processing when the frame
  hash is unchanged. Publishes frames to the preview (throttled) and the reader.
- **LocalListReader**: pure function `Read(Frame, Calibration, TemplateSet) -> Snapshot`
  (per-row results, counts per category, header member count). No OS/UI dependencies,
  so it's fully unit testable.
- **AlertState**: threat count (§4.5) and overflow state (§4.6).
- **AlertDispatcher**: fans out to enabled channels via a `Channel<AlertEvent>` so network or
  audio never blocks capture; per-channel rate limit.

### Solution layout

```text
EveLocalMon.sln
src/
  EveLocalMon.Core/               net10.0      — detection, state, settings, ntfy, interfaces
  EveLocalMon.Platform.Windows/   net10.0-windows10.0.19041.0 — capture, toast, sound
  EveLocalMon.Platform.MacOS/     (later)
  EveLocalMon.App/                Avalonia app — tray, views, DI composition root
  EveLocalMon.Capture.Cli/        M0 spike: dumps region captures as PNG fixtures
tests/
  EveLocalMon.Core.Tests/         xUnit
    Fixtures/                     real EVE screenshots + expected-result JSON
doc/
  samples/                        raw screenshots supplied for design/calibration
```

Settings: `%APPDATA%\EveLocalMon\settings.json` (macOS:
`~/Library/Application Support/EveLocalMon/`). Templates are stored beside it as PNG files.

---

## 4. Detection (the main risk)

### 4.1 Row-based classification

Neutrals have **no icon**, so plain "find the icon anywhere" matching can't see them.
Instead, treat the Local member list as a grid of rows and classify each one:

| Row state | How it's determined | Alerts? |
| --- | --- | --- |
| Empty | Name area has no text pixels | — |
| Friendly | Icon slot matches a **friendly** template | No |
| Enemy (red, orange, criminal, suspect, war target) | Icon slot matches an **enemy** template | Yes |
| Neutral | Name area has text **and** icon slot is plain background | Yes |
| Unknown | Icon slot has an icon that matches no template | Yes |

Because unknown icons alert, **every friendly icon needs a template too**. The gray
**neutral-standing** icon is a template in the **Neutral** category, so it alerts the same
way as a pilot with no icon.

At EVE UI scale 100% and Windows scaling 100%, icons are rendered pixel-identically, so exact
templates cropped once should match with very low error.

### 4.2 Measured from `doc/samples/sample-local-mixed-overflow.png`

436×1035 px raw capture, 100% / 100% scaling, compact list.

| Property | Value |
| --- | --- |
| Icon column | x = 303 … 314 (**12 px** wide) |
| Icon size | **12 × 12 px**, coloured rounded square with a white glyph |
| Row pitch | **17 px** (12 px icon + 5 px gap), perfectly regular |
| Name text | starts at x ≈ 318; bright gray on background ≈ rgb(19,23,26) |
| Header count | "100" beside the person icon, approx. x 320–345, y 45–60 |
| Visible rows | **56 full + 1 partial** (top row cut to 8 px) of **100** pilots |

Icons in the sample (mean colour over the 12×12 slot):

| Icon | Mean RGB | Count in sample | Category |
| --- | --- | --- | --- |
| Dark-blue square, star | (40, 62, 123) | 22 | Friendly |
| Purple square, person | (120, 60, 163) | 21 | Friendly |
| Green square, star | (54, 122, 56) | 11 | Friendly |
| Gray square | (139, 141, 142) | 1 (Orion Siegeteck) | Neutral → alert |
| Red square, minus | (134, 29, 30) | 1 (kriller 643) | Enemy → alert |

The mean colours are well separated, so hue alone already classifies these five. Masked
template matching is still used, to tell apart icons that share a colour but have
different glyphs (e.g. red standing vs war target) and to reject unknowns.

**First fixture expectation:** 56 classified rows, threats = 2 (1 enemy, 1 neutral),
overflow = true (100 members, 57 rows visible).

Second sample, `sample-local-nonscrolled-one-neut-one-friendly.png` (461×696, Local [3], not
scrolled): the own pilot "Echo Menoptra" has **no icon**, plus one blue "+" (excellent
standing) and one gray (neutral). Same 12 px icons and 17 px pitch; icon column at x = 314.

Findings from comparing the two:

- The Local window is **translucent**, with stars and grid lines behind the list. Empty
  background stays dark (99th-percentile luma 27, max 60), and name text is > 120, so a
  luma threshold still separates text from background.
- Icons are **opaque and pixel-identical** across samples. The same icon differs by ≤ 2.2
  (mean abs diff, 0–255), while the closest different icons (blue star vs blue plus) differ by
  17.3. So exact template matching with a threshold of ~8 is reliable.
- The own pilot always being present means **zero occupied rows = Local not visible**
  (window covered/minimised/moved) → treated as unreadable (§4.6).

### 4.3 Calibration (one-time, in the UI)

1. Drag a rectangle around the member list pane (physical pixels), including the header
   with the member count.
2. The preview shows the capture; the user clicks one icon to set the **icon slot**
   (x offset, size) and the **name area** x-range, and drags a box around the header count.
3. **Row pitch** is auto-detected from the vertical brightness profile
   (autocorrelation of text lines), with a manual override. Measured value: 17 px (§4.2).
4. Templates: crop from the live preview (click a row → "Save icon as… Friendly / Red / …")
   or import PNGs. Digit templates (0–9) for the header count are cropped the same way.

### 4.4 Per-row classification

- **Row phase**: the list scrolls, so the first visible row can be cut off at any
  height. Each frame, find the row phase (0 … pitch−1 px) that best lines up the icon
  column, then ignore partly visible rows at the top and bottom.
- **Text present**: count pixels in the name area whose luminance exceeds background by a
  margin; `count > K` → occupied.
- **Icon present**: colour saturation / variance in the icon slot vs. the row's own
  background (sampled beside the slot) → handles hover/selection highlight.
- **Template match**: masked sum of squared differences in **colour** (BGR, not grayscale,
  since red vs orange and light vs dark blue differ mainly by hue) over a ±1 px window.
  Template alpha is the mask so the changing background is ignored. Best score under
  threshold → that category; icon present but no match → Unknown.
- Cost: ~60 visible rows × ~10 templates × 9 offsets × ~120 px ≈ 0.65 M ops/frame, i.e.
  about 1 ms in managed code.

### 4.5 Alert state (appearance + count increase)

The alert trigger is a single **threat count** = enemy + neutral + unknown. A single total
is used, not per category, because criminal/suspect/war-target flags **blink**. A blinking
icon can flip between "enemy" and "neutral" frame to frame, and separate counts would
produce spurious "increases". The per-category breakdown still goes into the alert text.

- A raw count must be seen for **N consecutive frames** (default 2 at 2 Hz ≈ 1 s) to become
  the new stable count. This filters scroll/redraw glitches.
- If the stable threat count rises above the previous stable count, alert with
  "`+delta` (threats now `n`: 1 red, 1 neutral)". 0 → 1 is the "first appearance" case.
- Decreases are recorded silently; optional "Local clear" message when it reaches 0.
- Rapid increases are merged: at most one alert per channel per X seconds (configurable;
  ntfy default 15 s). A merged alert reports the latest total.
- Scrolling the list can bring a hostile into view; that counts as an increase and
  alerts, which is fine because it's a real threat.
- Overflow has its own state and alert, independent of the threat count (§4.6).

### 4.6 List overflow (separate alert condition)

The sample shows **Local [100]** with only 56 full rows visible, and EVE can't sort by
standing, so hidden pilots can't be classified. When that happens the app raises an
**overflow alert**. Its wording makes clear the list couldn't be verified, not that a
threat was seen.

**Detecting overflow** (either signal is enough):

1. **Header count > visible row count.** Read the member count next to the person icon
   using digit templates (0–9, fixed font at 100% scale, cropped once during calibration).
   Compare it with the number of rows that are fully visible.
2. **Partial row at the top or bottom edge.** A partly visible row means the list is scrolled,
   which only happens when it doesn't fit. This works even if reading the header count fails.

**State machine** (debounced like the threat count, N consecutive frames):

| Transition | Alert |
| --- | --- |
| Readable → Overflow | **Overflow alert** (sound + toast + ntfy, each channel configurable). E.g. *"Local too long to read: 100 pilots, 56 visible. Cannot confirm Local is clear."* |
| Overflow, member count rises | Optional, **default off**: *"Local 100 → 103 while list is unreadable. New pilots not verified."* Off because in busy friendly systems this would fire constantly. |
| Overflow → Readable | Toast only: *"Local readable again (41 pilots)"*. The visible-row threat count then applies as normal. |

Visible-row threat detection keeps running during overflow, so a red that is **visible**
still raises a normal threat alert.

The same "Local unreadable" alert covers **Local not visible**: no occupied rows at all,
even though the own pilot is always listed. That means the window is covered, minimised,
moved or mis-calibrated. Without this, a hidden window would look like an empty, safe Local.

Practical mitigation: make the Local pane as tall as practical. At 17 px per row, a
full-height pane shows ~60 rows at 1080p and ~80 at 1440p.

### 4.7 Other limitations

- Screen capture needs EVE in **Windowed** or **Fixed Window** mode with the Local window
  visible (not covered or minimized); exclusive fullscreen may capture black.

**Validate before building UI:** collect ~20 raw screenshots (see §8) into
`tests/EveLocalMon.Core.Tests/Fixtures/` with expected counts, and get the reader to 100% on
them.

---

## 5. Alert channels

- **Sound**: bundled WAV + user-selectable file, volume, optional "loop until acknowledged".
- **Toast**: title "EVE: threat in Local", body e.g. "+1 (1 red, 1 neutral, 0 unknown)".
  Overflow uses a different title, "EVE: Local unreadable", so it's never confused with a
  confirmed threat. Later: action buttons ("Mute 5 min").
- **ntfy** (ntfy.sh, no auth):

  ```http
  POST https://ntfy.sh/{topic}
  Title: EVE: threat in Local
  Priority: urgent          (enemy) / high (neutral, unknown) — configurable
  Tags: rotating_light

  +1 red — threats now 2 (1 red, 1 neutral) — 21:04:13
  ```

  Overflow: `Title: EVE: Local unreadable`, `Priority: high`, `Tags: warning`,
  body "Local too long to read: 100 pilots, 56 visible. Cannot confirm Local is clear."

  5 s timeout, one retry, failures logged (never block alerts on other channels).
  Note: on ntfy.sh anyone who knows the topic name can read it, so use a long random topic.

---

## 6. Milestones

| # | Milestone | Deliverable |
| --- | --- | --- |
| M0 | ✅ Spike | Solution skeleton + `Capture.Cli`: BitBlt a region every N s and dump PNGs; collect fixtures in game |
| M1 | ✅ Reader + state | `LocalListReader` (rows, templates, header digits), `AlertState`, fixture-driven xUnit tests passing |
| M2 | ✅ Picker & preview UI | Avalonia app: drag-select region overlay, auto-detected calibration with manual override, live preview with per-row classification, settings persistence (`doc/M2-SPEC.md`) |
| M3 | Alerts | Sound, toast, ntfy implementations + console "test alert" command |
| M4 | Monitoring shell | Tray icon, start/stop, mute, template cropper ("add unknown icon as friendly"), detection history |
| M5 | Windows release | Velopack installer, autostart, GitHub Actions build + release |
| M6 | macOS | ScreenCaptureKit capture, Screen Recording permission flow, Retina scaling, notifications, signing/notarization |
| M7 | Extras | Hotkey, quiet hours, multi-region, ntfy auth/attachments |

Reordered after M1: the region picker moved ahead of alerts, because calibrating by hand-editing
JSON is the thing blocking real use, and alerts are only meaningful once the reader is pointed at
a live Local window.

---

## 7. Risks / notes

- **EULA:** passive screen reading only. Tools like this are common, but check CCP's
  current third-party policy and never add anything that interacts with the client.
- **List overflow** in busy systems (§4.6) is the main functional gap; it's surfaced as an
  explicit alert rather than silently missed.
- **Unknown-icon alerts** will fire for any friendly icon not yet templated. Expect some
  teaching during the first sessions; the "add as friendly" action makes this quick.
- **EVE UI changes** in patches can break templates/calibration → keep both user-editable
  and keep the fixture suite current.
- **DPI:** make sure capture coordinates are physical pixels (Avalonia is per-monitor DPI
  aware on Windows; convert from `Screen.Scaling` when drawing the picker overlay).
- **macOS:** Screen Recording permission is per-app and resets for unsigned rebuilds.

---

## 8. Open questions / needed inputs

Resolved: gray icon = neutral standing (alerts as Neutral); Local can't be sorted by
standing; overflow is its own alert (§4.6); own pilot has no icon (one no-icon row allowed).

1. **More raw screenshots** into `doc/samples/` (same method as the existing samples):
   - other icon types you may see: light-blue (+5) standing, orange (−5), suspect,
     criminal, war target (until templated, these read as Unknown and alert);
   - a hovered row and a selected row;
   - a non-own pilot with no icon, to confirm the no-icon neutral case.
2. Should the overflow alert use **all channels** (sound + toast + ntfy) by default, or
   toast only? The plan assumes all channels.
