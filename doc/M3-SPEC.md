# EveLocalMon — M3 implementation spec: alerts

**Status: implemented (2026-09-15).** 91 automated tests pass
(`dotnet test EveLocalMon.sln --filter "Category!=Perf"`), the app builds and starts clean, and the
built-in chime was verified end to end through winmm with `test-alert --channel sound`.

Outstanding: the manual list in §8 — it needs a real EVE window, a phone subscribed to ntfy, and a
second monitor. **ntfy has never actually been contacted**: every test uses a stub handler, so the
first real push will be from your own Test button, to your own topic.

Scope: turn the `AlertEvent`s that M1's `AlertState` already produces into things you can notice —
a sound, an on-screen popup, and an ntfy push — wired into M2's monitoring loop. Tray icon,
autostart, template teaching and history stay in M4.

Background: `doc/PLAN.md` §5 (alert channels) and §4.5 (when alerts fire), `doc/M1-SPEC.md` §3
(`AlertState`, already implemented and tested), `doc/M2-SPEC.md` (the loop this hangs off).

Decided with the user before writing this:

| Question | Answer | Consequence |
| --- | --- | --- |
| On-screen alert | **Custom always-on-top popup**, not a native Windows toast | Windows Focus Assist / Do Not Disturb routinely suppresses toast banners during fullscreen games — precisely when this alert matters. Also means **no notification packages at all** (no WindowsAppSDK, no CommunityToolkit) and the same code works on macOS in M6. |
| Channels when Local is unreadable | **All three, ntfy included** | "I cannot confirm Local is clear" is treated as seriously as a confirmed hostile. |
| Sound | **Single chime** per increase | No repeat-until-acknowledged; the count rising again is what re-alerts. |

---

## 1. Architecture

```text
MonitorLoop (M2) → Snapshot → AlertState (M1) → AlertEvent[] → AlertDispatcher ─┬─ ISoundPlayer    (Platform.Windows: winmm)
                                                                                ├─ IAlertPresenter (App: popup window)
                                                                                └─ INtfyClient     (Core: HttpClient)
```

`AlertState` is finished and tested — do not change its debouncing. M3 adds only the part that
turns its events into output.

### New in `EveLocalMon.Core`

```text
Alerts/AlertKind.cs            ThreatIncreased | LocalClear | Unreadable | Readable | MemberCountWhileUnreadable
Alerts/AlertMessage.cs         record (Kind, Title, Body, AlertPriority, DateTimeOffset At)
Alerts/AlertMessageFactory.cs  pure AlertEvent -> AlertMessage; owns all wording (§3)
Alerts/IAlertChannels.cs       ISoundPlayer, IAlertPresenter, INtfyClient
Alerts/AlertDispatcher.cs      routing, per-channel rate limiting, mute, failure isolation
Alerts/AlertSettings.cs        per-kind channel matrix, ntfy config, volume, rate limits
Audio/AlertTone.cs             synthesises the built-in chime as WAV bytes
Net/NtfyClient.cs              INtfyClient over HttpClient
```

### New elsewhere

```text
src/EveLocalMon.Platform.Windows/WinmmSoundPlayer.cs   PlaySound(SND_MEMORY | SND_ASYNC) via P/Invoke
src/EveLocalMon.App/Views/AlertPopupWindow.cs          the popup itself
src/EveLocalMon.App/Services/AlertPresenter.cs         popup queue + placement, marshals to UI thread
src/EveLocalMon.App/Views/MainWindow.cs                alerts panel: enables, ntfy fields, volume, Test buttons, Mute
src/EveLocalMon.Capture.Cli/TestAlertCommand.cs        `test-alert --channel sound|ntfy|all`
```

No new NuGet packages anywhere.

---

## 2. Sound without a dependency or a binary asset

`AlertTone.BuildWav(volumePercent, ...)` generates a 16-bit mono PCM WAV **in memory**: two tones
(880 Hz then 1175 Hz, ~120 ms each) with a few ms of fade in/out so it doesn't click. Volume is
applied as sample amplitude, which is what makes a volume control possible at all here.

Why synthesise: no binary blob in the repo, no sound-file licensing question, and volume works
without pulling in NAudio. A user-supplied `.wav` path overrides it (played as-is; the volume
slider then only applies to the built-in tone, and the UI must say so).

`WinmmSoundPlayer` pins the byte array and calls `PlaySound(pointer, IntPtr.Zero, SND_MEMORY | SND_ASYNC)`
from `winmm.dll` — no package, same P/Invoke style as `GdiScreenCapture`. Playback failure is
reported, never thrown: a missing audio device must not stop the popup and ntfy from firing.

---

## 3. Wording (the safety-critical part)

`AlertMessageFactory` is pure and fully unit tested. The invariant, carried over from
`doc/PLAN.md` §4.6: **an unreadable Local must never be phrased as a confirmed threat, and must
never be phrased as all-clear.**

| Event | Title | Body | Priority |
| --- | --- | --- | --- |
| `ThreatIncreased` from 0 | `EVE: hostile in Local` | `1 red, 1 neutral — 2 threats visible` | Urgent |
| `ThreatIncreased` from >0 | `EVE: more hostiles in Local` | `+1 (2 red, 1 neutral) — now 3 threats` | Urgent |
| `LocalClear` | `EVE: Local clear` | `No threats visible.` | Low |
| `ReadabilityChanged` → `Overflow` | `EVE: Local unreadable` | `Local too long to read: 100 pilots, 56 visible. Cannot confirm Local is clear.` | High |
| `ReadabilityChanged` → `NotVisible` | `EVE: Local unreadable` | `Local window not visible — covered, minimised or mis-calibrated. Cannot confirm Local is clear.` | High |
| `ReadabilityChanged` → `Full` | `EVE: Local readable again` | `41 pilots, all visible.` | Low |
| `MemberCountIncreasedWhileUnreadable` | `EVE: pilots joined, Local unreadable` | `Local 100 → 103 while unreadable. New pilots not checked.` | High |

Counts come from the event's `ThreatBreakdown`; zero-valued categories are omitted, so it reads
`1 red` rather than `1 red, 0 neutral, 0 unknown`. "red" is the user-facing word for the Enemy
category; unknown icons read as `1 unknown icon` to make clear it is *unidentified*, not confirmed
hostile.

---

## 4. Dispatch

```csharp
public sealed record AlertChannelSet(bool Sound, bool Popup, bool Ntfy);

public sealed record AlertSettings
{
    public AlertChannelSet Threats { get; init; } = new(true, true, true);
    public AlertChannelSet Unreadable { get; init; } = new(true, true, true);   // user's choice
    public AlertChannelSet AllClear { get; init; } = new(false, true, false);
    public int VolumePercent { get; init; } = 70;
    public string? SoundFilePath { get; init; }
    public string NtfyServer { get; init; } = "https://ntfy.sh";
    public string? NtfyTopic { get; init; }
    public int SoundMinIntervalMs { get; init; } = 3000;
    public int PopupMinIntervalMs { get; init; } = 1000;
    public int NtfyMinIntervalMs { get; init; } = 15000;
}
```

- **Rate limiting is per channel**, not per alert: a burst collapses to one sound while the popup
  still updates. A suppressed alert is dropped, not queued — stale threat counts are worse than none.
- **Mute** suppresses sound and ntfy for N minutes; the popup still appears (muting should not make
  the app silently lie about a clear Local). `MuteUntil` lives in memory, not settings — a restart
  should not leave you muted without knowing.
- **Failure isolation**: each channel runs in its own try/catch; one throwing channel must not stop
  the others, and the failure surfaces in the UI status line rather than a dialog.
- Dispatch is `async` and never blocks the capture loop; ntfy runs on the thread pool.

### ntfy

`POST {server}/{topic}`, headers `Title`, `Priority` (`urgent`/`high`/`low`), `Tags`
(`rotating_light` for threats, `warning` for unreadable, `white_check_mark` for clear), body = the
message. 5 s timeout, **one** retry on failure, then give up and report. No auth (M7 adds tokens).

If `NtfyTopic` is empty, the app generates a 20-character random topic on first enable and shows
it — on ntfy.sh anyone who knows the topic can read it, so it must not be guessable, and the UI
says so plainly next to the field.

---

## 5. The popup

`AlertPopupWindow`: borderless, `Topmost`, `ShowInTaskbar = false`, and **`ShowActivated = false`**
so it never steals focus from the game — a popup that pulls focus mid-fight would be worse than no
popup at all. Coloured by priority (red urgent / amber high / gray low), showing title, body and
time. Auto-dismisses after 8 s (configurable), click dismisses immediately.

Placement: bottom-right of the monitor **containing the watched region**, not the primary monitor —
the point is to appear next to the EVE window you're watching. Stacked upward when several are live,
capped at 3 with older ones dropped.

---

## 6. Wiring and settings

`MainWindow` owns `AlertState` + `AlertDispatcher`, feeding every `Snapshot` from `MonitorLoop`
through them. The alerts panel gets: per-kind channel checkboxes, volume, ntfy server/topic,
**Test** buttons per channel, a Mute control, and the last-alert / last-error line.

`AppSettings` gains `Alerts` and bumps `CurrentSchemaVersion` to **2**. A v1 file must load without
complaint, getting defaults for the new block — `SettingsStoreTests` gets a case for exactly that.

CLI: `evelocalmon-capture test-alert --channel sound|ntfy|all [--topic x] [--server y]`, for
checking a channel without opening the GUI. The popup channel is GUI-only.

---

## 7. Tests

| Area | Test |
| --- | --- |
| Wording | Every event kind maps to the expected title/body/priority; zero categories omitted |
| Wording invariant | No `Unreadable`/`MemberCountWhileUnreadable` message contains threat-confirming words, and none reads as all-clear |
| Dispatch routing | Each kind reaches exactly the channels its `AlertChannelSet` enables |
| Rate limiting | A burst yields one sound but several popups; suppressed alerts are dropped, not queued |
| Mute | Suppresses sound + ntfy, never the popup; expires |
| Failure isolation | A channel that throws doesn't prevent the others and is reported |
| `AlertTone` | Valid RIFF/WAVE header, byte length matches duration × sample rate, amplitude scales with volume, 0% is silence |
| `NtfyClient` | Correct URL, headers and body via a stub `HttpMessageHandler`; retries once; a timeout returns failure without throwing |
| Settings | A v1 settings file loads and gains default alert settings |

**No test may touch the network.** `NtfyClient` is tested with a stub handler only; the real
ntfy.sh is exercised by the user from the Test button, with their own topic.

---

## 8. Manual acceptance

1. Test buttons fire each channel; ntfy arrives on the phone.
2. A hostile entering Local produces chime + popup + push within ~1 s, and the popup does **not**
   steal focus from EVE.
3. Covering the Local window produces the "unreadable" wording, not "clear" and not "hostile".
4. Popup appears on the monitor the watched region is on.
5. Mute silences sound/ntfy but still shows popups; it lapses on its own.
6. Alert settings survive a restart; an existing v1 settings file still loads.

---

## 9. Risks

- **Focus stealing** is the one thing that would make this unusable in a fight; `ShowActivated = false`
  is verified against Avalonia before building.
- **Alert fatigue** from the unreadable channel in busy systems — it fires on every transition, and
  resizing windows causes transitions. Watch it in real use; per-kind rate limits exist for this.
- **ntfy.sh topics are public** to anyone who knows the name; hence the generated random topic.
- **Rate limits dropping the newest alert** would be dangerous; they drop *within* a window only
  after one has gone out, and the count is re-reported by the next increase.
