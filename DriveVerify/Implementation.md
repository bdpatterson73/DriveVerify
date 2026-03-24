# FlashDriveTester — Phased Implementation Plan

## Project Overview

**Name:** FlashDriveTester (DriveVerify solution)
**Target:** .NET 10 WPF Desktop (`net10.0-windows`)
**Pattern:** MVVM
**Purpose:** Test removable USB flash drives and SD cards to verify whether the real writable/readable capacity matches the reported capacity. Inspired by H2testw but with a modern WPF UI and original implementation.

---

## Solution Structure (all phases)

```
DriveVerify/
├── DriveVerify.csproj
├── App.xaml / App.xaml.cs
├── Models/
│   ├── DriveItem.cs
│   ├── TestMode.cs
│   ├── TestPlan.cs
│   ├── TestBlockHeader.cs
│   ├── TestResult.cs
│   ├── RegionStatus.cs
│   ├── VerificationIssue.cs
│   └── HtmlReportModel.cs
├── Services/
│   ├── DriveDetectionService.cs
│   ├── TestPatternService.cs
│   ├── ChecksumService.cs
│   ├── FileTestWriterService.cs
│   ├── FileTestVerifierService.cs
│   ├── HtmlReportService.cs
│   └── HeatmapService.cs
├── ViewModels/
│   └── MainViewModel.cs
├── Views/
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   └── HeatmapControl.xaml / HeatmapControl.xaml.cs
└── Helpers/
    ├── RelayCommand.cs
    ├── AsyncRelayCommand.cs
    ├── SizeFormatter.cs
    └── TimeFormatter.cs
```

---

## Phase 1 — Foundation: Project Skeleton, Models, and MVVM Infrastructure ✅ COMPLETED

**Goal:** Establish a compilable, runnable project with all model types, MVVM helpers, and an empty but wired-up MainViewModel bound to MainWindow.

### Files to create/modify

| File | Action | Description |
|---|---|---|
| `Helpers/RelayCommand.cs` | Create | `ICommand` implementation for synchronous commands with `CanExecute` support. |
| `Helpers/AsyncRelayCommand.cs` | Create | `ICommand` implementation that wraps `Func<CancellationToken, Task>`, exposes `IsRunning`, and supports `CancellationToken` via a `Cancel()` method. |
| `Helpers/SizeFormatter.cs` | Create | Static helper: `Format(long bytes)` → human-readable string (B / KB / MB / GB / TB). |
| `Helpers/TimeFormatter.cs` | Create | Static helper: `Format(TimeSpan)` → `"Xh Ym Zs"` or `"Ym Zs"` compact string. |
| `Models/TestMode.cs` | Create | Enum: `FullCapacity`, `QuickSampling`. |
| `Models/DriveItem.cs` | Create | Properties: `DriveLetter`, `VolumeLabel`, `FileSystem`, `TotalSize`, `FreeSpace`, `IsRemovable`, `IsReady`. Computed `DisplayName`. |
| `Models/TestPlan.cs` | Create | Properties: `SessionId` (Guid), `TargetDrive` (DriveItem), `Mode` (TestMode), `TestSizeBytes`, `BlockSizeBytes`, `VerifyThreadCount`, `TestFolderPath`. Method `ComputeTotalBlocks()`. |
| `Models/TestBlockHeader.cs` | Create | Fixed-layout struct/class (128 bytes serialized): `MagicSignature` (8 bytes, `0x4654_4553_5448_4452`), `SessionId` (Guid, 16 bytes), `FileIndex` (int), `BlockIndex` (int), `AbsoluteOffset` (long), `TimestampUtc` (long ticks), `PayloadLength` (int), `PayloadChecksum` (uint CRC32), reserved padding to 128 bytes. Methods: `Serialize(Span<byte>)`, `static Deserialize(ReadOnlySpan<byte>)`. |
| `Models/RegionStatus.cs` | Create | Enum: `Untested`, `Writing`, `Verifying`, `Good`, `Bad`. |
| `Models/VerificationIssue.cs` | Create | Record/class: `BlockIndex`, `AbsoluteOffset`, `IssueKind` (enum: `ChecksumMismatch`, `HeaderMismatch`, `ShortRead`, `Unreadable`, `DuplicateWrap`), `Detail` (string). |
| `Models/TestResult.cs` | Create | Properties: `SessionId`, `DriveInfo` snapshot, `Mode`, `ConfiguredTestSize`, `ActualBytesWritten`, `ActualBytesVerified`, `WriteSpeedBytesPerSec`, `ReadSpeedBytesPerSec`, `Duration`, `FirstFailureOffset` (long?), `VerifiedGoodBytes`, `Issues` (List\<VerificationIssue\>), `RegionMap` (RegionStatus[]), `Verdict` (enum: `Verified`, `Suspect`, `FakeCapacityDetected`, `CorruptionDetected`, `Cancelled`). |
| `Models/HtmlReportModel.cs` | Create | Flattened DTO for the HTML template: all `TestResult` fields plus `AppVersion`, `ReportDateUtc`, `DriveDisplayName`, `HeatmapSvgFragment`. |
| `ViewModels/MainViewModel.cs` | Create | Inherits a base `ObservableObject` (implement `INotifyPropertyChanged` inline or as a nested base class). Exposes placeholder properties for every UI region (drive list, selected drive, test settings, progress, log, result, heatmap data). Wire `RefreshDrivesCommand` as a `RelayCommand` that does nothing yet. |
| `MainWindow.xaml` | Modify | Set `DataContext` to `MainViewModel`. Add a minimal `TextBlock` bound to a `StatusMessage` property so binding is verified at build-time. |
| `MainWindow.xaml.cs` | Modify | Instantiate `MainViewModel` in constructor and assign `DataContext`. No other code-behind. |
| `App.xaml` | Modify | Update `StartupUri` if MainWindow moves to `Views/` folder (defer move to Phase 4 if simpler). |

### Acceptance criteria
- `dotnet build` succeeds with zero errors and zero warnings.
- Running the app shows MainWindow with the bound status text.
- All model classes are complete, non-stub, and serializable where needed.
- `TestBlockHeader.Serialize` / `Deserialize` round-trips correctly (verified by code inspection; unit tests optional).

---

## Phase 2 — Core Services: Checksum, Pattern Generation, Drive Detection ✅ COMPLETED

**Goal:** Implement the stateless computational services and drive enumeration so the write/verify phases have everything they depend on.

### Files to create

| File | Description |
|---|---|
| `Services/ChecksumService.cs` | CRC32 implementation (use `System.IO.Hashing.Crc32` from the BCL if available in .NET 10, otherwise a manual table-based implementation). Single static method: `uint Compute(ReadOnlySpan<byte> data)`. |
| `Services/TestPatternService.cs` | Generates deterministic payload bytes for a given `(Guid sessionId, int blockIndex, long absoluteOffset, int length)`. Use a seeded approach: derive a 32-byte seed from `SHA256(sessionId + blockIndex + offset)`, then expand with a simple PRNG or repeated hashing to fill `length` bytes. Also provides `byte[] GenerateExpectedPayload(...)` for verification. |
| `Services/DriveDetectionService.cs` | `IEnumerable<DriveItem> GetDrives(bool includeFixed = false)` — wraps `DriveInfo.GetDrives()`, filters by `DriveType.Removable` (and optionally `Fixed`), populates `DriveItem` properties, handles `IOException` / `UnauthorizedAccessException` for unready drives. |

### MainViewModel integration
- Wire `RefreshDrivesCommand` → calls `DriveDetectionService.GetDrives()` and populates `ObservableCollection<DriveItem> Drives`.
- Bind `SelectedDrive` property; show a warning string when the selected drive is not removable.
- Add test-settings properties with defaults:
  - `SelectedTestMode` = `FullCapacity`
  - `BlockSizeMB` = 4
  - `CustomTestSizeGB` = null (use free space)
  - `VerifyThreadCount` = `Math.Min(Environment.ProcessorCount, 8)`

### Acceptance criteria
- App lists removable drives on launch and on Refresh click.
- `ChecksumService.Compute` produces consistent output.
- `TestPatternService` round-trips: generate → checksum → regenerate → checksum matches.
- Non-removable drive warning appears when a fixed drive is selected.

---

## Phase 3 — Write Phase Service ✅ COMPLETED

**Goal:** Implement `FileTestWriterService` that performs the full sequential write of test data to the target drive.

### File to create

| File | Description |
|---|---|
| `Services/FileTestWriterService.cs` | Async method: `Task<WritePhaseResult> WriteAsync(TestPlan plan, IProgress<WriteProgress> progress, CancellationToken ct)`. |

### Detailed behavior

1. Create `TestPlan.TestFolderPath` directory (e.g. `E:\FlashDriveTester_Test\`).
2. Determine total blocks from `TestPlan.ComputeTotalBlocks()`.
3. For Quick Sampling Mode: compute evenly-spaced block indices across the logical range; write only those blocks but still create files that span the full range (seek + write at each sample offset).
4. For Full Capacity Mode: iterate sequentially over all blocks.
5. For each block:
   - Build `TestBlockHeader` with current session, indices, timestamp.
   - Generate payload via `TestPatternService`.
   - Compute CRC32 of payload via `ChecksumService`; store in header.
   - Serialize header (128 bytes) + payload into a buffer.
   - Write buffer to the current `FileStream` using async IO.
   - Report progress: blocks written, bytes written, current speed, ETA.
6. Split output across multiple files if a single file would exceed 1 GB (avoids FAT32 4 GB limit; use a 1 GB cap per file for safety on FAT32, configurable up to 4 GB on other file systems).
7. Flush and close each file.
8. Respect `CancellationToken` between block writes.
9. Return `WritePhaseResult` containing total bytes written, elapsed time, average speed, block count, file count, and any IO exceptions encountered.

### Progress model

```
WriteProgress { Phase, FileIndex, BlockIndex, BytesWritten, TotalBytes, SpeedBytesPerSec, Elapsed, EstimatedRemaining }
```

### MainViewModel integration
- Add `StartTestCommand` (`AsyncRelayCommand`) that:
  1. Validates a drive is selected and settings are reasonable.
  2. Shows a confirmation dialog warning about data on the drive.
  3. Builds a `TestPlan`.
  4. Calls `FileTestWriterService.WriteAsync(...)`.
  5. Updates progress properties bound to the UI.
- Add `CancelCommand` that triggers `CancellationTokenSource.Cancel()`.
- Add observable `LogEntries` (`ObservableCollection<string>`) updated with timestamped messages during the write.

### Acceptance criteria
- Running a Full Capacity test on a small drive (or a test folder on a local disk) writes correct test files.
- Files contain valid headers that can be deserialized.
- Progress updates appear in the ViewModel properties.
- Cancellation stops the write within one block boundary.

---

## Phase 4 — Verify Phase Service ✅ COMPLETED

**Goal:** Implement `FileTestVerifierService` that reads back test files and validates every block.

### File to create

| File | Description |
|---|---|
| `Services/FileTestVerifierService.cs` | Async method: `Task<VerifyPhaseResult> VerifyAsync(TestPlan plan, IProgress<VerifyProgress> progress, CancellationToken ct)`. |

### Detailed behavior

1. Enumerate test files in `TestPlan.TestFolderPath` in order.
2. For each file, read blocks sequentially (or in parallel across files).
3. For each block:
   - Read 128-byte header; deserialize `TestBlockHeader`.
   - Validate magic signature.
   - Validate `SessionId` matches the plan.
   - Validate `FileIndex` and `BlockIndex` match expected position.
   - Read `PayloadLength` bytes of payload.
   - Regenerate expected payload via `TestPatternService` using the header's `SessionId`, `BlockIndex`, `AbsoluteOffset`.
   - Compute CRC32 of read payload; compare against `PayloadChecksum` in header.
   - Byte-compare read payload against expected payload to detect wrap/duplicate patterns.
   - If any check fails, create a `VerificationIssue`.
4. Multi-threaded strategy:
   - Partition files across `VerifyThreadCount` workers using `Parallel.ForEachAsync` or a `Channel<T>`-based producer/consumer.
   - Each worker processes one file at a time (sequential reads within a file, parallel across files).
   - Aggregate results in a thread-safe `ConcurrentBag<VerificationIssue>`.
5. Report progress: blocks verified, bytes read, read speed, ETA.
6. Return `VerifyPhaseResult` containing total bytes verified, elapsed time, speed, issues list, first failure offset, region map array.

### Progress model

```
VerifyProgress { Phase, BlockIndex, BytesVerified, TotalBytes, SpeedBytesPerSec, Elapsed, EstimatedRemaining, IssueCount }
```

### MainViewModel integration
- Extend `StartTestCommand` to chain: Write → Verify → Compute `TestResult` → Set `Verdict`.
- Populate `Issues` and `RegionMap` on the ViewModel for later binding.
- Append verify-phase log entries.

### Acceptance criteria
- Verifying files written by Phase 3 on a healthy drive reports zero issues and `Verdict = Verified`.
- Manually corrupting a test file (flip bytes) causes the verifier to detect `ChecksumMismatch` and report the correct offset.
- Truncating a test file causes `ShortRead` detection.
- Multi-threaded verify runs faster than single-threaded on multi-core machines (observable in elapsed time).

---

## Phase 5 — WPF UI Layout and Data Binding ✅ COMPLETED

**Goal:** Build the full MainWindow XAML with all UI regions, bound to MainViewModel.

### Files to modify/create

| File | Action | Description |
|---|---|---|
| `MainWindow.xaml` | Rewrite | Full layout (see below). |
| `MainWindow.xaml.cs` | Minimal | Only `DataContext` assignment. |

### UI Layout Plan

```
┌──────────────────────────────────────────────────┐
│  Title Bar: "FlashDriveTester"                   │
├────────────────────┬─────────────────────────────┤
│  Drive Selection   │   Test Settings             │
│  ┌──────────────┐  │   Mode: [FullCapacity ▼]    │
│  │ ComboBox     │  │   Block size: [4] MB        │
│  │ or ListView  │  │   Test size: [auto] GB      │
│  └──────────────┘  │   Verify threads: [4]       │
│  [Refresh Drives]  │                             │
│  Drive info panel  │   [▶ Start Test] [✖ Cancel] │
│  (label, FS, size) │                             │
├────────────────────┴─────────────────────────────┤
│  Progress Section                                │
│  Phase: Writing   42%  ████████░░░  12.3 MB/s    │
│  Blocks: 1050/2500   Elapsed: 2m 14s  ETA: 3m   │
├──────────────────────────────────────────────────┤
│  Heatmap (HeatmapControl)                        │
│  ┌──────────────────────────────────────────────┐│
│  │ ■■■■■■■■■■■■■■□□□□□□□□□□□□□□□□□□□□□□□□□□□□ ││
│  └──────────────────────────────────────────────┘│
├──────────────────────────────────────────────────┤
│  Verdict Banner: [VERIFIED ✓] / [FAKE ✗] etc.   │
├──────────────────────────────────────────────────┤
│  Log Output (ListBox/TextBox, scrolling)         │
│  2025-07-14 10:03:12  Writing block 1050...      │
│  ...                                             │
├──────────────────────────────────────────────────┤
│  Status Bar: Ready | Drive: E:\ | Free: 14.2 GB │
└──────────────────────────────────────────────────┘
```

### Styling approach
- Use a dark or modern neutral color scheme defined in `App.xaml` resources.
- Define named `SolidColorBrush` resources for backgrounds, foregrounds, accents.
- Style `Button`, `ComboBox`, `ProgressBar`, `TextBox` via implicit styles.
- Verdict banner uses a large `Border` with `Background` bound to verdict-to-color converter.

### Data binding checklist
- Drive list → `ItemsSource="{Binding Drives}"`, `SelectedItem="{Binding SelectedDrive}"`.
- Test settings → two-way bindings to `SelectedTestMode`, `BlockSizeMB`, `CustomTestSizeGB`, `VerifyThreadCount`.
- Start/Cancel → `Command="{Binding StartTestCommand}"`, `Command="{Binding CancelCommand}"`.
- Progress → `ProgressPercent`, `PhaseText`, `SpeedText`, `ElapsedText`, `EtaText`, `BlockProgressText`.
- Log → `ItemsSource="{Binding LogEntries}"` with auto-scroll behavior.
- Verdict → `VerdictText`, `VerdictBrush`.
- Control enabling → `IsEnabled` bound to `IsNotRunning` (inverse of `IsRunning`).

### Acceptance criteria
- All controls are visible and properly laid out.
- Selecting a drive updates the info panel.
- Changing settings updates ViewModel properties.
- Start/Cancel buttons enable/disable based on `IsRunning`.
- Progress bar and speed labels update during a test.

---

## Phase 6 — Heatmap Visualization ✅ COMPLETED

**Goal:** Create a reusable WPF heatmap control that visualizes the region status array.

### Files to create

| File | Description |
|---|---|
| `Views/HeatmapControl.xaml` | UserControl with an `ItemsControl` using a `UniformGrid` panel, or a custom `OnRender` override for performance with large region counts. |
| `Views/HeatmapControl.xaml.cs` | Dependency property: `RegionStatuses` (`RegionStatus[]`). On change, rebuild visual. |
| `Services/HeatmapService.cs` | Converts `RegionStatus[]` into the data shape needed by the control (e.g. list of `HeatmapCell { Index, Status, Color }`). Also generates an SVG string fragment for the HTML report. |

### Color mapping

| Status | Color |
|---|---|
| `Untested` | `#3C3C3C` (dark gray) |
| `Writing` | `#2196F3` (blue) |
| `Verifying` | `#FF9800` (orange) |
| `Good` | `#4CAF50` (green) |
| `Bad` | `#F44336` (red) |

### Behavior
- During the write phase, regions transition `Untested → Writing → Good` (assuming write completes without IO error).
- During the verify phase, regions transition `Good → Verifying → Good` or `Good → Verifying → Bad`.
- The control should handle 1,000+ regions without noticeable lag. If `ItemsControl` + `UniformGrid` is too slow, use `DrawingVisual` or `WriteableBitmap` rendering.
- Live updates via `INotifyPropertyChanged` on the region array or by re-setting the dependency property.

### MainViewModel integration
- Expose `RegionStatuses` property (bound to `HeatmapControl.RegionStatuses`).
- Update region statuses from write/verify progress callbacks.

### Acceptance criteria
- Heatmap renders during a test and updates live.
- Colors match the mapping table.
- Performance is acceptable with 2,000+ regions.

---

## Phase 7 — HTML Report Generation ✅ COMPLETED

**Goal:** Generate a polished, self-contained HTML report file after test completion.

### File to create

| File | Description |
|---|---|
| `Services/HtmlReportService.cs` | Method: `string GenerateReport(HtmlReportModel model)` → returns complete HTML string. Method: `async Task SaveAndOpenAsync(string html, string folderPath)` → writes `.html` file, optionally opens in default browser via `Process.Start`. |

### Report contents
- Inline CSS (no external dependencies).
- Sections:
  1. **Header**: App name, version (`Assembly.GetExecutingAssembly().GetName().Version`), report date/time.
  2. **Drive Info**: Letter, label, file system, total size, free space, removable status.
  3. **Test Configuration**: Mode, block size, configured test size, verify thread count.
  4. **Results Summary**: Bytes written, bytes verified, write speed, read speed, duration.
  5. **Verdict**: Large colored banner matching the UI verdict.
  6. **Capacity Analysis**: First failure offset, verified good capacity, percentage of reported capacity that is real.
  7. **Issues Table**: Block index, offset, issue kind, detail — for every `VerificationIssue`.
  8. **Heatmap**: Inline SVG generated by `HeatmapService.GenerateSvg(RegionStatus[])`.
- Clean, modern styling: sans-serif font, card-based layout, colored verdict bar.

### MainViewModel integration
- After verify phase completes (or is cancelled), build `HtmlReportModel` from `TestResult`.
- Call `HtmlReportService.SaveAndOpenAsync(...)`.
- Report file saved to `TestPlan.TestFolderPath` (on the drive) and optionally also to a local app data folder.
- Add `OpenReportCommand` to allow re-opening the last report.

### Acceptance criteria
- Report file is generated and opens in the default browser.
- All sections are populated with correct data.
- Heatmap SVG renders in modern browsers.
- Report is self-contained (no external CSS/JS/images).

---

## Phase 8 — Error Handling, Logging, and Polish ✅ COMPLETED

**Goal:** Harden the application for real-world use and finalize the user experience.

### Error handling additions

| Scenario | Handling |
|---|---|
| Drive removed during test | Catch `IOException` / `DirectoryNotFoundException` in write/verify loops; set `Verdict = Cancelled`; log details; show user-friendly message. |
| Insufficient free space | Pre-check in `StartTestCommand`; refuse to start with a message if `FreeSpace < BlockSize`. |
| `UnauthorizedAccessException` | Catch on folder creation and file write; suggest running as administrator or checking write protection. |
| Path too long | Use `\\?\` prefix on the test folder path. |
| Corrupt/partial block read | Handled by verifier issue types (`ShortRead`, `Unreadable`); ensure no unhandled exceptions escape. |

### Logging
- `ObservableCollection<string> LogEntries` in MainViewModel, displayed in the UI log section.
- Each entry prefixed with UTC timestamp.
- Optionally write `FlashDriveTester_Log.txt` alongside test files.
- Log start/end of each phase, every Nth block (configurable, e.g. every 100 blocks), all errors, and final verdict.

### UI polish
- Verdict banner styles:
  - **Verified**: green background, white text, checkmark icon.
  - **Suspect**: yellow background, dark text, warning icon.
  - **Fake capacity detected**: red background, white text, X icon.
  - **Corruption detected**: red background, white text, X icon.
  - **Cancelled**: gray background, white text.
- Disable Start button when no drive is selected or test is running.
- Disable settings controls while test is running.
- Auto-scroll log to bottom.
- Show elapsed time updating every second via a `DispatcherTimer`.
- Window title shows phase during test: `"FlashDriveTester — Writing 42%"`.

### Acceptance criteria
- Removing a USB drive during a test does not crash the application.
- Attempting to test a full drive (0 bytes free) shows a clear message.
- Log file is created alongside test files.
- All UI states (idle, running, completed, cancelled, error) look correct and polished.

---

## Phase Dependency Graph

```
Phase 1 (Foundation)
  └─► Phase 2 (Core Services)
        ├─► Phase 3 (Write Service)
        │     └─► Phase 4 (Verify Service)
        │           └─► Phase 7 (HTML Report)
        └─► Phase 5 (UI Layout)
              └─► Phase 6 (Heatmap)
                    └─► Phase 8 (Polish)
```

Phases 5 and 6 can be developed in parallel with Phases 3 and 4 since they depend only on Phase 2 and ViewModel property contracts. Phase 8 is the integration and hardening pass that depends on all prior phases.

---

## Implementation Rules

1. **No stubs or TODOs** — every file must contain complete, compilable code.
2. **Favor correctness and readability** over cleverness or maximum performance.
3. **File-based testing only** — no raw disk access, no P/Invoke for disk IO.
4. **Deterministic patterns** — verification must be able to regenerate expected data from header metadata alone.
5. **CancellationToken everywhere** — all async service methods accept and respect cancellation.
6. **Thread safety** — verify phase uses `ConcurrentBag` or equivalent; UI updates marshal to dispatcher.
7. **No external NuGet packages** unless absolutely necessary — prefer BCL types.
8. **Target framework**: `net10.0-windows` (as configured in `DriveVerify.csproj`).