# Code Flow Map - iRacing Automated Replay Director

This document maps the runtime flow of the application. It complements
[`AUDIT.md`](AUDIT.md) (findings) and the architecture notes in
[`CLAUDE.md`](../CLAUDE.md).

Companion documents:
- [`plans/build-ci-and-code-audit-plan.md`](../plans/build-ci-and-code-audit-plan.md) - build/CI plan
- [`AUDIT.md`](AUDIT.md) - code audit findings

---

## 1. High-level component map

```mermaid
flowchart LR
    subgraph View
        MW[MainWindow.xaml]
        OV[Driver / Ahead / Behind / Leaderboard overlays]
        SET[AIDirectorSettingsWindow.xaml]
    end

    subgraph ViewModel
        VM[ReplayDirectorVM]
        VMP[ReplayDirectorVM.Properties]
    end

    subgraph Commands
        CMD[MENU / NODES / PLAYBACK / SESSION / AI]
    end

    subgraph Model
        NODE[Node / CamChangeNode / FrameSkipNode]
        NC[NodeCollection]
        CAP[CaptureModeBase + modes]
        DOM[Driver / Camera / TrackSurfaces]
    end

    subgraph AI
        DIR[AIDirector]
        DET[Incident / Overtake / Battle detectors]
        LLM[OpenAI / Compatible / Local providers]
        MOD[AI models]
    end

    subgraph Persistence
        SL[SaveLoadHelper]
        SETTINGS[Properties.Settings]
    end

    SDK[Sim.Instance - iRacing SDK wrapper]

    MW --> VM
    OV --> VM
    SET --> DIR
    VM --> VMP
    VM --> CMD
    VM --> NC
    VM --> CAP
    VM --> DIR
    VM --> SL
    VM --> SETTINGS
    NC --> NODE
    NODE --> SDK
    CAP --> SDK
    CMD --> SDK
    DIR --> DET
    DIR --> LLM
    DIR --> MOD
    VM --> SDK
```

---

## 2. Application startup and SDK connection

```mermaid
flowchart TD
    A[App.OnStartup] --> B[Register Dispatcher / AppDomain / TaskScheduler handlers]
    B --> C[StartupUri loads MainWindow.xaml]
    C --> D[MainWindow ctor: DataContext = ReplayDirectorVM, subscribe PropertyChanged]
    D --> E[ReplayDirectorVM ctor]
    E --> E1[InitSDK]
    E --> E2[InitVariables]
    E --> E3[SaveLoadHelper.LoadSettings]
    E --> E4[ToggleInactiveDriverDisplay]
    E1 --> E1a[Subscribe Connected / Disconnected / SessionInfoUpdated / TelemetryUpdated]
    E1 --> E1b[Sim.Instance.Start]
    E2 --> E2a[Build NodeCollection, Drivers, Cameras, CaptureModes]
    E2 --> E2b[Instantiate all commands incl. AIDirector + AI commands]
```

---

## 3. Session info and telemetry pipeline

```mermaid
flowchart TD
    A[SDK Connected] --> A1[SdkConnectedCallback: status bar text]

    B[SessionInfoUpdated] --> B1[ProcessSessionInfo]
    B1 --> B2[GetSessionDrivers]
    B1 --> B3[GetSessionCameras]
    B1 --> B4[VerifyExistingNodeCameras]
    B1 --> B5[Read SessionID from WeekendInfo]
    B1 --> B6{SessionInfoLoaded?}
    B6 -- no --> B7[InitSession: live-session warning, load project file, enable UI]
    B1 --> B8[LogReadinessState + RefreshCommandStates]

    C[TelemetryUpdated] --> C1[ProcessTelemetry]
    C1 --> C2[Read frame, end, time, play speed, capture flags]
    C1 --> C3[UpdateDriverTelemetry: laps, distance, surfaces, positions]
    C1 --> C4[UpdateSessionInformation]
    C1 --> C5{PlaybackEnabled?}
    C5 -- no --> C6[Set CurrentNode from GetNodeOnCurrentFrame, UpdateUILabels]
    C5 -- no --> C7{IsCaptureActive?}
    C7 -- yes --> C8[StopRecording]
    C1 --> C9[PlaybackCameraSwitching]
    C1 --> C10[RateLimitedChecks: periodic DriversView.Refresh]

    D[Wrapper throws mid-update] --> D1[App.OnDispatcherUnhandledException]
    D1 --> D2[RecoverFromWrapperError -> ProcessSessionInfo + ProcessTelemetry]
```

---

## 4. Timeline node editing and playback application

```mermaid
flowchart TD
    A[Add Cam Change / Add Frame Skip] --> B[NodeCollection.AddNode]
    B --> C[Find current active node at frame]
    C --> D[Link PreviousNode / NextNode chain]
    D --> E[Insert into Nodes]
    E --> F[SaveNodeChanges -> SaveLoadHelper.SaveProject]

    G[Toggle node Enabled] --> H[NodeCollection.TimelineNodesPropertyChanged]
    H --> F

    I[Playback at 1x] --> J[PlaybackCameraSwitching]
    J --> K[Select last node with Frame <= CurrentFrame]
    K --> L{StopRecordingOnFinalNode and capture active?}
    L -- yes --> M[StopRecording and return]
    K --> N[Set CurrentNode]
    N --> O[Node.ApplyNode]
    O --> O1[CamChangeNode: SwitchToCar]
    O --> O2[FrameSkipNode: SetPosition to NextNode.Frame, speed 1]
```

---

## 5. Auto Director pipeline

```mermaid
flowchart TD
    A[Auto Director - Scan Replay for Events] --> B[ScanReplayCommand.Execute]
    B --> C[RunScanSynchronously: step frames, SetPosition, CaptureSnapshot]
    C --> D[Run detectors: Incident, Overtake, Battle]
    D --> E[ReplayScanResult with Events]

    F[Generate Camera Plan] --> G{UseAIForCameraPlan?}
    G -- no --> H[GenerateEventDrivenCameraPlan]
    H --> H1[Schedule opening cut]
    H --> H2[Schedule cuts before each significant event with anticipation + jitter]
    H --> H3[FillGapsWithCuts for quiet periods]
    G -- yes --> I[BuildEventSummary]
    I --> J{Duration > MaxSegmentFrames?}
    J -- no --> K[provider.GenerateCameraPlanAsync]
    J -- yes --> L[GenerateChunkedPlanAsync per segment]

    M[Apply Camera Plan] --> N[ApplyPlanToNodeCollection]
    N --> O[RemoveAllNodes if clearing]
    N --> P[Per action: FindMostExcitingDriver]
    P --> Q[Resolve camera, create CamChangeNode, AddNode]
    H --> M
    K --> M
    L --> M
```

### 5.1 Driver selection scoring (summary)

When applying a plan, `FindMostExcitingDriver` scores each active driver per cut:

```
ActionScore  = EventProximity + Momentum + ActiveBattle + PackProximity + FreshAction
PositionScore = baseline interest from running position
VarietyPenalty = penalty for recently shown drivers, dampened by action level
FinalScore = ActionScore + (PositionScore * (1 - ActionLevel * 0.5))
             - (VarietyPenalty * VarietyDampener) - OverexposurePenalty + DiversityBonus
```

Weights are configurable in `AIDirectorSettings` (see the README tuning guide).

---

## 6. Recording / capture flow

```mermaid
flowchart TD
    A[Toggle Recording] --> B[ReplayDirectorVM.StartRecording]
    B --> C[Set playback speed 1]
    B --> D[InSimUIEnabled = not DisableUIWhenRecording]
    B --> E[SelectedCaptureMode.StartRecording]
    E --> E1[In-Sim: broadcast VideoCapture start]
    E --> E2[OBS: SendKeys hotkey to obs64]
    E --> E3[ShadowPlay: SendKeys hotkey to nvsphelper64]

    F[Playback stops / final node / user stops] --> G[StopRecording]
    G --> H[Set playback speed 0]
    G --> I[SelectedCaptureMode.StopRecording]
    G --> J[After 500ms delay, re-enable sim UI]
```

---

## 7. Persistence

```mermaid
flowchart LR
    A[NodeCollection.SaveNodeChanges] --> B[SaveLoadHelper.SaveProject]
    B --> C[Saves\<SessionID>.timeline JSON]

    D[InitSession] --> E[LoadExistingProjectFile]
    E --> F[SaveLoadHelper.LoadProject]
    F --> G[Rebuild nodes and re-link chain]

    H[App settings] --> I[Properties.Settings / App.config]
```

---

## 8. GitHub Actions auto-build flow

The iRacing SDK wrapper is a git submodule at `external/iRacingSdkWrapper`
(source `rockavoldy/iRacingSdkWrapper`) and is built as part of the solution via
`ProjectReference`, so there is no separate wrapper checkout or build step. Its projects
are SDK-style (`net48`), so the workflow restores both `packages.config` and
`PackageReference` before building.

```mermaid
flowchart TD
    A[push / PR / v* tag] --> B[Checkout app repo with submodules: recursive]
    B --> C[setup-msbuild + setup-nuget]
    C --> D[nuget restore ReplayTimeline.sln packages.config]
    D --> D1[msbuild ReplayTimeline.sln -t:Restore for PackageReference]
    D1 --> E[msbuild ReplayTimeline.sln Release Any CPU]
    E --> E1[Builds wrapper projects: iRacingSdkWrapper, iRSDKSharp, iRacingSimulator]
    E1 --> E2[Copies wrapper DLLs + transitive YamlDotNet.dll to app output]
    E2 --> F[iRacingSequenceDirector.exe]
    F --> G[Upload build artifact]
    G --> H{Tag v*?}
    H -- yes --> I[Zip and attach to GitHub Release]
```
