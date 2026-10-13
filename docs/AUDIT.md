# Code Audit - iRacing Automated Replay Director

Scope: full static review of the `ReplayTimeline` project (`iRacingSequenceDirector`),
a WPF / .NET Framework 4.7.2 desktop application.

Severity legend: **High** = correctness/data-loss or blocks build/CI; **Medium** =
robustness or maintainability with real user impact; **Low** = hygiene, style, or
environment-specific risk.

> This document records findings only. The corresponding fixes are tracked
> separately and are not applied here.

---

## 1. Build and repository hygiene

| # | Severity | Finding | Location |
|---|----------|---------|----------|
| B1 | High | Four assemblies were referenced from outside the repo and only `Newtonsoft.Json` is NuGet-restorable, so a clean build was impossible without first building `NickThissen/iRacingSdkWrapper`. **Resolved** by adding the wrapper as a git submodule (`external/iRacingSdkWrapper`) built via `ProjectReference`. | [`ReplayTimeline.csproj`](../ReplayTimeline/ReplayTimeline.csproj:48) |
| B2 | Medium | `YamlDotNet.dll` `HintPath` pointed at a `Debug` output folder while the other three wrapper DLLs used `Release` - inconsistent and fragile. **Resolved**: the wrapper DLLs are now `ProjectReference`s and `YamlDotNet.dll` is a transitive dependency copied to the output automatically. | [`ReplayTimeline.csproj`](../ReplayTimeline/ReplayTimeline.csproj:48) |
| B3 | Medium | Dead duplicate sources at the project root declared namespace `ReplayTimeline`, were not included in the `.csproj`, and duplicated the live `iRacingReplayDirector` types. **Resolved** by deletion. | `ReplayTimeline/TimelineProject.cs`, `ReplayTimeline/SaveLoadHelper.cs` |
| B4 | Low | Version drift: UI reports `1.41`, assembly is `1.0.0.0`, copyright `2021`. | [`ReplayDirectorVM.Properties.cs`](../ReplayTimeline/ViewModel/ReplayDirectorVM.Properties.cs:17), [`AssemblyInfo.cs`](../ReplayTimeline/Properties/AssemblyInfo.cs:54) |
| B5 | Low | No `.editorconfig`, no analyzers, no `nuget.config`, no CI prior to this work. | repo root |
| B6 | Low | Two near-identical save/load helpers existed (`iRacingReplayDirector.SaveLoadHelper` vs the deleted `ReplayTimeline.SaveLoadHelper`) with different file layouts (`Saves\<id>.timeline` vs `<id>_timeline.timeline`). Only the former is used. | [`ViewModel/Helpers/SaveLoadHelper.cs`](../ReplayTimeline/ViewModel/Helpers/SaveLoadHelper.cs:45) |

---

## 2. Correctness and data integrity

| # | Severity | Finding | Location |
|---|----------|---------|----------|
| C1 | High | `Execute()` calls `App.Current.Shutdown()` and then `Environment.Exit(0)`, with `SaveLoadHelper.SaveSettings`, stopping replay speed and stopping capture *after* the shutdown call. Because `Environment.Exit(0)` terminates the process immediately, settings may not be persisted and an active recording may not be stopped cleanly. Ordering must be: stop playback -> stop capture -> save settings -> exit. | [`ApplicationQuitCommand.cs`](../ReplayTimeline/Commands/Menu%20Items/ApplicationQuitCommand.cs:29) |
| C2 | High | Unguarded SDK access inside `ProcessTelemetry`: `Sim.Instance.Sdk.GetTelemetryValue<bool>("VidCapEnabled").Value` and `"VidCapActive"` are read without null checks, unlike the surrounding defensive code. These can throw during SDK teardown. | [`ReplayDirectorVM.cs`](../ReplayTimeline/ViewModel/ReplayDirectorVM.cs:447) |
| C3 | Medium | Two competing node-application mechanisms: exact-frame match when paused (`GetNodeOnCurrentFrame` -> `CurrentNode` -> `ApplyNode`) versus frame-threshold selection during playback (`PlaybackCameraSwitching`). `_lastAppliedNode` is not reset on node edits or rewinds, so a node can be skipped on re-application. | [`NodeCollection.cs`](../ReplayTimeline/Model/NodeCollection.cs:107), [`ReplayDirectorVM.cs`](../ReplayTimeline/ViewModel/ReplayDirectorVM.cs:582) |
| C4 | Medium | Side effects inside a collection-view predicate: `DriverOnTrackFilter` mutates `_currentDriver` and raises `OnPropertyChanged("CurrentDriver")` every time it runs. Filters can be evaluated frequently and re-entrantly. | [`ReplayDirectorVM.cs`](../ReplayTimeline/ViewModel/ReplayDirectorVM.cs:149) |
| C5 | Medium | `FrameSkipNode.UpdateLabel` mutates `Enabled` as a side effect of refreshing the label, coupling display to behavior; `Enabled` changes then trigger a save through `NodeCollection`. | [`FrameSkipNode.cs`](../ReplayTimeline/Model/FrameSkipNode.cs:18), [`NodeCollection.cs`](../ReplayTimeline/Model/NodeCollection.cs:42) |
| C6 | Medium | Two independent scan implementations exist: a synchronous UI-blocking path used by the command and an async path in `AIDirector` that appears unused by the command, each with its own `CaptureSnapshot`. Divergence risk. | [`ScanReplayCommand.cs`](../ReplayTimeline/Commands/AI/ScanReplayCommand.cs:115), [`AIDirector.cs`](../ReplayTimeline/AI/Director/AIDirector.cs:167) |
| C7 | Low | Hardcoded 60 fps assumptions in anticipation/gap math, while the scan also derives `DurationSeconds = totalFrames / 60.0`. | [`AIDirector.cs`](../ReplayTimeline/AI/Director/AIDirector.cs:593) |
| C8 | Low | `StopRecording` is `async void` and awaits a fixed 500 ms delay, then re-enables the sim UI unconditionally. Exceptions cannot be observed and the delay is a timing guess. | [`ReplayDirectorVM.cs`](../ReplayTimeline/ViewModel/ReplayDirectorVM.cs:712) |

---

## 3. Robustness and error handling

| # | Severity | Finding | Location |
|---|----------|---------|----------|
| R1 | Medium | UI-thread blocking scan uses `DoEvents()` + `Thread.Sleep(50)` per frame, freezing the window for the entire scan and risking re-entrancy from `DoEvents`. | [`ScanReplayCommand.cs`](../ReplayTimeline/Commands/AI/ScanReplayCommand.cs:159) |
| R2 | Low | Pervasive empty `catch { }` / swallow blocks in detectors, scan, and apply paths hide failures despite a usable `crash.log` facility. | [`IncidentDetector.cs`](../ReplayTimeline/AI/EventDetection/IncidentDetector.cs:115), [`AIDirector.cs`](../ReplayTimeline/AI/Director/AIDirector.cs:328) |
| R3 | Low | Process automation via `SetForegroundWindow` + `SendKeys` with a hardcoded hotkey is brittle and can misdirect input if focus changes. | [`ExternalProcessHelper.cs`](../ReplayTimeline/ViewModel/Helpers/ExternalProcessHelper.cs:20), [`CaptureMode_OBS.cs`](../ReplayTimeline/Model/CaptureModes/CaptureMode_OBS.cs:9) |
| R4 | Low | `CaptureMode_Iracing.IsAvailable()` has a double semicolon and reads telemetry without guarding the SDK. | [`CaptureMode_Iracing.cs`](../ReplayTimeline/Model/CaptureModes/CaptureMode_Iracing.cs:15) |
| R5 | Low | Position ordering treats non-Race sessions as position `999` for everyone, and the pace car (`NumberRaw == 0`) is pushed to `999`; downstream selection relies on this. | [`ReplayDirectorVM.cs`](../ReplayTimeline/ViewModel/ReplayDirectorVM.cs:523) |

---

## 4. Security and privacy

| # | Severity | Finding | Location |
|---|----------|---------|----------|
| S1 | Medium | LLM provider API keys are held in `AIDirectorSettings` strings; confirm they are not written to disk in plaintext or logged. | [`AIDirectorSettings.cs`](../ReplayTimeline/AI/Director/AIDirectorSettings.cs:16) |
| S2 | Low | `LLMProviderBase` uses a single static `HttpClient` shared by all providers with a 180 s timeout - acceptable, but note there is no per-provider base-address pinning beyond the configured endpoint. | [`LLMProviderBase.cs`](../ReplayTimeline/AI/LLM/LLMProviderBase.cs:14) |
| S3 | Low | The `OpenAICompatibleProvider` accepts an arbitrary user-supplied base URL; the API key is then sent to that host. This is intended for local/self-hosted models but should be clearly surfaced to the user. | [`OpenAICompatibleProvider.cs`](../ReplayTimeline/AI/LLM/OpenAICompatibleProvider.cs:40) |

---

## 5. Performance

| # | Severity | Finding | Location |
|---|----------|---------|----------|
| P1 | Medium | `ScanReplayAsync` issues one dispatcher `InvokeAsync` per frame plus a fixed 100 ms delay per frame; scanning a full replay is dominated by this fixed wait. | [`AIDirector.cs`](../ReplayTimeline/AI/Director/AIDirector.cs:241) |
| P2 | Low | `DriversView.Refresh()` runs every 90 telemetry updates and inside `UpdateDriverTelemetry` on surface transitions; frequent full refreshes can cause UI churn. | [`ReplayDirectorVM.cs`](../ReplayTimeline/ViewModel/ReplayDirectorVM.cs:570) |
| P3 | Low | `FindMostExcitingDriver` recomputes scoring per camera action against in-memory histories; fine at current scales but quadratic-ish growth as events/cuts increase. | [`AIDirector.cs`](../ReplayTimeline/AI/Director/AIDirector.cs:1242) |

---

## 6. Architecture observations (not defects)

- MVVM separation is generally respected; overlays are created and driven by
  [`MainWindow.xaml.cs`](../ReplayTimeline/View/MainWindow.xaml.cs:17) reacting to
  ViewModel `PropertyChanged`.
- Capture modes correctly implement the Strategy pattern via `CaptureModeBase`.
- The Auto Director is well decomposed: detectors, providers, and models are separate
  from the orchestrator.
- The crash-handling design in [`App.xaml.cs`](../ReplayTimeline/App.xaml.cs:24) is a
  deliberate workaround for a closed-source wrapper that intermittently throws; it is
  reasonable but should be revisited if the wrapper is ever updated.

---

## 7. Recommended follow-up (priority order)

1. C1 - reorder shutdown so settings save and recording stop reliably.
2. C2 / R4 - guard all `Sim.Instance.Sdk` telemetry reads.
3. C3 - unify node application and reset `_lastAppliedNode` on edits/rewinds.
4. C4 / C5 - remove side effects from the filter predicate and node label refresh.
5. C6 / R1 - consolidate on the async scan path and remove UI blocking.
6. S1 - confirm and, if needed, protect API key persistence.
7. B4 - unify the version number across UI and assembly (and consider stamping from CI).

---

## 8. Resolution status (follow-up change set)

The following findings were addressed:

| # | Status | What changed |
|---|--------|--------------|
| B1 | Resolved | `NickThissen/iRacingSdkWrapper` added as a git submodule at `external/iRacingSdkWrapper` and built as part of `ReplayTimeline.sln`; the app references the three wrapper projects via `ProjectReference`, so a clean build no longer needs pre-built DLLs. CI checks out submodules recursively. |
| B2 | Resolved | The four wrapper `HintPath`s are gone; `iRacingSdkWrapper.dll`, `iRacingSimulator.dll` and `iRSDKSharp.dll` come from `ProjectReference`, and `YamlDotNet.dll` is a transitive dependency copied to the output automatically. |
| B3 | Resolved | Dead duplicate `ReplayTimeline/TimelineProject.cs` and `ReplayTimeline/SaveLoadHelper.cs` deleted. |
| B4 | Resolved | `AssemblyInfo.cs` aligned to `1.4.1.0` (informational `1.41`); product/title set to `iRacingSequenceDirector` / `iRacing Sequence Director`. |
| C1 | Resolved | [`ApplicationQuitCommand.Execute`](../ReplayTimeline/Commands/Menu%20Items/ApplicationQuitCommand.cs:29) stops playback, stops capture and saves settings *before* `App.Current.Shutdown()`/`Environment.Exit(0)`, each step guarded; added a re-entrancy guard because the command runs from both the menu and window close. |
| C2 | Resolved | `VidCapEnabled`/`VidCapActive` reads in `ProcessTelemetry` wrapped in try/catch. |
| C3 | Resolved | `_lastAppliedNode` reset whenever playback is paused, so nodes re-apply after a rewind or node edit. |
| C4 | Resolved | [`DriverOnTrackFilter`](../ReplayTimeline/ViewModel/ReplayDirectorVM.cs:149) is now a pure predicate; it no longer mutates `CurrentDriver`. |
| C5 | Resolved | [`Node.Enabled`/`Node.Frame`](../ReplayTimeline/Model/Node.cs:9) only raise change notifications on an actual value change, eliminating spurious saves from node re-linking. |
| C6 / R1 | Resolved | [`ScanReplayCommand`](../ReplayTimeline/Commands/AI/ScanReplayCommand.cs:1) runs the scan via `AIDirector.ScanReplayAsync`, removing the UI-blocking `DoEvents`/`Thread.Sleep` loop and the duplicate scan/snapshot code. The now-unused synchronous helpers (`SetScanning`, `UpdateProgress`, `RunDetectors`, `SetScanComplete`, `SetError`, `DoEvents`) were removed. |
| R4 | Resolved | `CaptureMode_Iracing.IsAvailable()` guards the SDK read; the stray double semicolon is gone. |
| S1 | Resolved (verified, no change needed) | API keys live only in the in-memory `AIDirectorSettings`; they are not part of `Properties.Settings.Default`, so nothing persists them to disk. |

Intentionally left as-is (need design decisions or live testing): C7, C8, R2, R3, R5, P1, P2, P3.
