# MyFileManager (v0.3)

A lightweight, high-performance dual-pane file manager for Windows 11 built with **.NET 10 (Windows)**, **WPF**, and modern **MVVM** (`CommunityToolkit.Mvvm`).

---

## 1. What It Is

`MyFileManager` is a dual-pane desktop file manager designed for keyboard-driven productivity, data safety, and fluid Windows 11 integration. It offers side-by-side directory browsing, resilient two-phase navigation history, STA-isolated shell icon extraction, extended multi-selection, undoable file operations (New Folder, Rename), and a high-performance **Transfer Engine (Copy / Move)** with streaming verification, pause/resume capability, and undo support.

---

## 2. How to Build and Run

### Prerequisites
- **Operating System:** Windows 10/11
- **IDE:** Visual Studio 2022 (version 17.12 or later) with the **.NET desktop development** workload.
- **SDK:** .NET 10.0 SDK.

### Option A: Run in Visual Studio (Recommended)
1. Double-click `MyFileManager.sln` to open the solution in Visual Studio.
2. Ensure **`FM.App`** is set as the startup project.
3. Press **F5** (or click **Start**). The application will compile and launch with zero manual configuration.

### Option B: Run via .NET CLI
```bash
# Clone or navigate to the repository directory
cd MyFileManager

# Build entire solution (zero warnings, zero errors)
dotnet build MyFileManager.sln

# Run all test suites
dotnet test MyFileManager.sln

# Run synthetic directory streaming benchmark
dotnet run --project tests/FM.Benchmarks/FM.Benchmarks.csproj

# Run in-memory 2 GB transfer engine benchmark (0 disk writes)
dotnet run --project tests/FM.Benchmarks/FM.Benchmarks.csproj -- --transfer

# Launch the WPF application
dotnet run --project src/FM.App/FM.App.csproj
```

---

## 3. Project Structure

```
MyFileManager/
├── src/
│   ├── FM.Core/                 # Pure domain & business logic library (NO WPF dependency)
│   │   ├── FileSystem/
│   │   │   ├── IFileSystem.cs           # Pure filesystem abstraction for testing & storage
│   │   │   ├── PhysicalFileSystem.cs    # Production disk implementation with Win32 P/Invoke
│   │   │   └── InMemoryFileSystem.cs    # Zero-disk virtual streams & simulated volumes
│   │   ├── Helpers/
│   │   │   ├── FileEntrySorter.cs       # Folders-first deterministic sorting
│   │   │   └── FileNameValidator.cs     # Windows naming rules & reserved device name checks
│   │   ├── Models/
│   │   │   ├── FileEntry.cs             # Immutable file record
│   │   │   └── FileSortColumn.cs        # Sort column enumeration
│   │   ├── Navigation/
│   │   │   └── NavigationHistory.cs     # Two-phase Back/Forward navigation stack manager
│   │   ├── Operations/
│   │   │   ├── IFileOperation.cs        # Undoable operation contract
│   │   │   ├── UndoStack.cs             # In-memory undo history stack
│   │   │   ├── CreateFolderOperation.cs # Reversible directory creation
│   │   │   ├── RenameOperation.cs       # Reversible file/directory rename
│   │   │   └── Transfer/
│   │   │       ├── TransferModels.cs        # Enums, requests, results, and progress records
│   │   │       ├── TransferInterfaces.cs    # ITransferEngine, ITransferQueue, ITransferJob
│   │   │       ├── TransferPlanner.cs       # Hierarchy expansion, loop detection, free space check
│   │   │       ├── TransferEngine.cs        # Chunked copy, .fmpart staging, atomic rename, SHA-256
│   │   │       ├── PauseGate.cs             # Cooperative pause/resume gate with offset tracking
│   │   │       ├── TransferQueue.cs         # Per-volume serial lanes, parallel jobs, throttled progress
│   │   │       ├── TransferUndoOperation.cs # Safe undo for Copy and Move operations
│   │   │       └── IUndoJournalStore.cs     # Interface-only contract for persistent write-ahead journal
│   │   └── Services/
│   │       ├── IDirectoryService.cs     # Asynchronous batch file enumeration interface
│   │       └── DirectoryService.cs      # Resilient Channel-based background producer
│   │
│   └── FM.App/                  # WPF Application (Views & ViewModels only)
│       ├── Collections/
│       │   └── BulkObservableCollection.cs # High-performance batch collection
│       ├── Helpers/
│       │   ├── DependencyObjectExtensions.cs # Visual tree hit-testing helpers
│       │   └── FormatHelper.cs          # Size & type formatting utilities
│       ├── Services/
│       │   ├── IIconService.cs          # Shell icon retrieval contract
│       │   ├── IconService.cs           # Dedicated STA thread worker + SemaphoreSlim(4)
│       │   ├── ISettingsService.cs      # User settings persistence contract
│       │   ├── SettingsService.cs      # AppData settings.json persistence with clamping
│       │   ├── IInputDialogService.cs   # Dialog contract for user prompts
│       │   └── InputDialogService.cs    # WPF dialog window implementation
│       ├── ViewModels/
│       │   ├── FileItemViewModel.cs     # Lazy icon-loaded file item representation
│       │   ├── FilePaneViewModel.cs     # Pane navigation, sorting, multi-selection, and status
│       │   ├── SidebarItemViewModel.cs  # Favorites & drives sidebar item (with drive icons)
│       │   └── MainViewModel.cs         # Root dual-pane coordinator, transfer queue & shortcuts
│       ├── Views/
│       │   ├── FilePaneView.xaml(.cs)   # Virtualized listview, address bar, and action buttons
│       │   ├── InputDialogWindow.xaml   # Modern Windows 11 style text input dialog
│       │   └── MainWindow.xaml(.cs)     # Shell layout, splitter, and keyboard handling
│       ├── App.xaml(.cs)                # DI container setup & pre-Show window bounds clamping
│       └── FM.App.csproj
│
├── tests/
│   ├── Common/
│   │   └── TestWorkspace.cs             # Bounded %TEMP%\FM_* workspace manager (max 200 files / 1 MiB)
│   ├── FM.Core.Tests/           # Core library xUnit test suite (95 tests)
│   ├── FM.App.Tests/            # App-level xUnit test suite (23 tests)
│   └── FM.Benchmarks/           # Performance benchmark harness
│       └── Program.cs                   # Navigation and 2 GB transfer benchmarks
│
├── MyFileManager.sln            # Visual Studio Solution
├── .gitignore                   # Standard Visual Studio / dotnet template
└── README.md                    # Project documentation
```

---

## 4. Keyboard Shortcuts

| Shortcut | Action | Scope |
| :--- | :--- | :--- |
| **`Tab`** | Switch active pane between Left and Right | File list focused (preserves normal Tab in address bar) |
| **`Ctrl + Shift + C`** | Copy selected items to the other pane | Active pane |
| **`Ctrl + Shift + M`** | Move selected items to the other pane | Active pane |
| **`Ctrl + Shift + N`** | Create a new folder | Active pane |
| **`F2`** | Rename selected item | Active pane |
| **`Ctrl + Z`** | Undo last file operation | Active pane |
| **`Enter`** | Open folder, launch file with default app, or navigate address bar | Active pane |
| **`Backspace`** | Navigate up to parent folder | Active pane |
| **`Alt + Left`** | Navigate backward in history | Active pane |
| **`Alt + Right`** | Navigate forward in history | Active pane |
| **`Ctrl + L`** | Focus and select text in the address bar | Active pane |
| **`F5`** | Refresh file listing | Active pane |

---

## 5. Transfer Engine Architecture & Safety Guarantees

### 5.1 Architecture Overview
The transfer engine is organized into decoupled, highly testable components:
- **`TransferPlanner`**: Pre-scans selected source paths, expands directory hierarchies, skips reparse points/symlinks to prevent infinite loops, validates volume free disk space (accounting for existing target files), and computes collision-free `KeepBoth` names.
- **`TransferEngine`**: Orchestrates copy and move execution:
  - **Copy**: Transferred into a temporary staging file (`<dest_path>.<guid>.fmpart`) in 1–4 MiB chunks. Streams SHA-256 hashes concurrently during copy. Preserves original timestamps. Atomically renames `.fmpart` to the final destination upon successful verification. Cleans up `.fmpart` files if cancelled or failed.
  - **Same-Volume Move**: Executes an atomic OS filesystem move (no read streams opened, instantaneous metadata update).
  - **Cross-Volume Move**: Performs a verified copy to destination first. **Only deletes the source file after destination byte count and SHA-256 hash are verified**. Cleans up empty source directories bottom-up.
- **`PauseGate`**: Implements cooperative thread-safe pausing between chunks. On pause, state transitions `Running` $\to$ `Pausing` $\to$ `Paused`. On resume, file streaming continues from the exact byte offset without restarting. Immediate cancellation is supported while paused.
- **`TransferQueue`**: Enforces strict serial execution for jobs sharing the same destination volume (preventing disk head thrashing), while allowing up to 2 parallel lanes across distinct physical volumes. Emits throttled progress notifications ($\le 10/\text{sec}$) via `TimeProvider`.
- **`TransferUndoOperation`**: Encapsulates undo logic for transfers:
  - **Copy Undo**: Deletes created files only if their size and timestamps still match (rejects deletion of files modified post-copy) and removes empty parent directories bottom-up.
  - **Move Undo**: Restores moved items only if the original location is free and files are unmodified.
  - **Overwritten Protection**: If any file was replaced (`ItemOutcome.Replaced`), undo is disabled with an explanatory reason: *"Overwritten files cannot be restored without backup staging (planned for v0.4)."*

---

## 6. Performance Measurements & Benchmarks

The benchmark suite (`tests/FM.Benchmarks`) enforces a strict **zero-disk-generation** policy:
1. **Directory Streaming**: Synthetic in-memory streaming through the real `FilePaneViewModel` hosted in an active WPF `Window` (1024x768) measuring layout, virtualization, and UI stalls.
2. **Transfer Engine**: In-memory 2 GB virtual file copy between simulated volumes with deterministic byte patterns.

### Benchmark Environment:
- **CPU:** 12th Gen Intel(R) Core(TM) i5-12450HX (12 logical processors)
- **RAM:** 16 GB DDR5
- **OS:** Windows 11 (NT 10.0.26300.0)
- **Runtime:** .NET 10.0 (Windows Desktop)

### Measured Results:

#### A. Directory Streaming (10,000 entries, synthetic in-memory):
| Metric | Warm-up Run | Measured Runs (Median of 5) | Note |
| :--- | :---: | :---: | :--- |
| **Time to First Batch** | 65 ms | **3 ms** | First 50 items displayed immediately |
| **Time to Full List** | 778 ms | **293 ms** | Complete 10,000 item streaming & sorting |
| **Max UI Thread Stall** | 191.20 ms | **65.22 ms** | 16 ms timer drift during layout |
| **Collection Reset Events** | 1 | **1** | Target: <= 1 per navigation |
| **Initial Working Set** | 28.10 MiB | 28.10 MiB | Startup working set |
| **Final Working Set** | 135.57 MiB | 135.57 MiB | Working set after 10k items + WPF visuals |

#### B. In-Memory Transfer Engine (2 GB Virtual File Copy, 0 Disk Writes):
```
Command: dotnet run --project tests/FM.Benchmarks/FM.Benchmarks.csproj -c Release -- --transfer
```
| Metric | Measured Value |
| :--- | :--- |
| **Total Bytes Copied** | **2,147,483,648 bytes (2.00 GiB)** |
| **Execution Time** | **196 ms (0.197 s)** |
| **Throughput** | **10,399.40 MB/s** |
| **Total Allocations** | **4.16 MiB** |
| **Working Set Initial** | **26.56 MiB** |
| **Working Set Final** | **34.65 MiB (Delta: +8.09 MiB)** |
| **Physical Disk Writes** | **0 bytes** (Pure in-memory virtual stream) |

---

## 7. Strict Low-Disk Testing & Resource Policy

To protect developer SSD endurance and prevent storage bloat, all automated tests and benchmarks enforce strict resource limits:
1. **Hard Budget:** No command or test run may generate more than 200 files or 5 MB total on disk.
2. **Virtual Stream In-Memory Testing:** Engine unit tests run against `InMemoryFileSystem` with virtual files (length only, deterministic byte pattern generated from offset). A 5 GB test file costs **0 bytes of RAM and 0 bytes of disk**.
3. **Physical Disk Integration Tests:** Exactly 3 physical disk tests in `TransferIntegrationTests.cs`, strictly scoped to a combined total of **5 files and 10 KB** via `TestWorkspace`.
4. **TestWorkspace Lifecycle:** Temporary workspaces live under `%TEMP%\FM_{purpose}_{guid}`, delete automatically on `Dispose()`, and sweep stale directories older than 1 hour on startup.

---

## 8. Known Limitations & Next Steps (v0.4 Roadmap)

The current version is **v0.3** (Transfer Engine & Core Wiring):
- **Progress Panel UI:** v0.3 provides a live status bar in each pane (`"Copying 3 of 12, 45%, 120 MB/s"`). A dedicated multi-job drawer/panel UI with Pause/Resume/Cancel buttons for each job is planned for v0.4.
- **Interactive Conflict Dialog:** v0.3 defaults to `ConflictAction.KeepBoth`. An interactive prompt modal allowing Overwrite/Skip/KeepBoth/ApplyToAll during runtime is scheduled for v0.4.
- **Persistent Write-Ahead Journal:** The `IUndoJournalStore` interface is designed and ready; file-backed persistent disk journaling (surviving app crashes) is scheduled for v0.4.
- **Drag-and-Drop:** Mouse drag-and-drop between panes is planned for future versions.
