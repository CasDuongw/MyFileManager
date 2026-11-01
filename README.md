# MyFileManager (v0.4)

A lightweight, high-performance dual-pane file manager for Windows 11 built with **.NET 10 (Windows)**, **WPF**, and modern **MVVM** (`CommunityToolkit.Mvvm`).

---

## 1. What It Is

`MyFileManager` is a dual-pane desktop file manager designed for keyboard-driven productivity, uncompromising data safety, and fluid Windows 11 integration. It offers side-by-side directory browsing, resilient two-phase navigation history, STA-isolated shell icon extraction, extended multi-selection, undoable file operations (New Folder, Rename), and an enterprise-grade **Transfer Engine (Copy / Move)** featuring:
- **Transfer Drawer & Progress Panel UI:** Real-time multi-job drawer with live speeds, remaining time (ETA), individual progress bars, and cooperative `Pause`, `Resume`, and `Cancel` controls.
- **Interactive Conflict & Failure Dialog:** Fluent modal cards showing side-by-side file comparisons, STA thumbnail previews, visual badges (Newer / Larger), and a dynamic safety banner (`Overwrite is undoable` vs `Unbacked overwrite`).
- **Persistent Write-Ahead Undo Journal:** Append-only JSONL log (`%LOCALAPPDATA%\MyFileManager\journal\undo-journal.jsonl`) that survives application restarts and crashes, automatically recovering interrupted transfers and restoring the Undo stack on launch.
- **Staging Backup for Undo Overwrite:** Safe, reversible overwrites via staged backups (`%LOCALAPPDATA%\MyFileManager\staging\`) with SHA-256 verification, automatic quota enforcement, and retention cleanup.
- **Drag and Drop (OLE):** Seamless inter-pane and Windows Explorer drag-and-drop with modifier keys (`Ctrl` for Copy, `Shift` for Move), multi-selection preservation, drop effect resolution, and non-blocking fast drop dispatch.

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

# Run all test suites (276 tests)
dotnet test MyFileManager.sln

# Run synthetic directory streaming benchmark
dotnet run --project tests/FM.Benchmarks/FM.Benchmarks.csproj

# Run in-memory 2 GB transfer engine benchmark with write-ahead journal (0 disk writes)
dotnet run --project tests/FM.Benchmarks/FM.Benchmarks.csproj -c Release -- --transfer

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
│   │   ├── Search/
│   │   │   ├── NameMatcher.cs           # Diacritic- & case-insensitive matching & ranking
│   │   │   ├── SearchQuery.cs           # Immutable search parameters & root scopes
│   │   │   ├── SearchResult.cs          # Entry reference with parent folder & relevance score
│   │   │   ├── SearchScope.cs           # Scope enumeration (CurrentFolder, OpenPanes, ThisPC)
│   │   │   ├── ISearchService.cs        # Core search engine contract
│   │   │   └── SearchService.cs         # Recursive engine with loop avoidance & Channel batching
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
│   │   │       ├── TransferUndoOperation.cs # Safe undo for Copy, Move, and Replaced (Overwrite)
│   │   │       ├── IStagingBackupStore.cs   # Staging backup contract for undoable overwrites
│   │   │       ├── StagingBackupStore.cs    # Disk-backed staging, cross-volume verify, quota purge
│   │   │       ├── IUndoJournalStore.cs     # Persistent write-ahead undo journal contract
│   │   │       ├── FileUndoJournalStore.cs  # Append-only JSONL log, crash recovery, compaction
│   │   │       └── JournalCodec.cs          # Streaming JSONL serialization and corrupted line handling
│   │   └── Services/
│   │       ├── IDirectoryService.cs     # Asynchronous batch file enumeration interface
│   │       └── DirectoryService.cs      # Resilient Channel-based background producer
│   │
│   └── FM.App/                  # WPF Application (Views & ViewModels only)
│       ├── Collections/
│       │   └── BulkObservableCollection.cs # High-performance batch collection
│       ├── Helpers/
│       │   ├── DependencyObjectExtensions.cs # Visual tree hit-testing helpers
│       │   ├── DropEffectResolver.cs    # Pure OLE drop effect & destination validation helper
│       │   └── FormatHelper.cs          # Size & type formatting utilities
│       ├── Services/
│       │   ├── IIconService.cs          # Shell icon retrieval contract
│       │   ├── IconService.cs           # Dedicated STA thread worker + SemaphoreSlim(4)
│       │   ├── IThumbnailService.cs     # Shell thumbnail extraction contract
│       │   ├── ThumbnailService.cs      # STA thread worker with IShellItemImageFactory & 64-item cache
│       │   ├── ISettingsService.cs      # User settings persistence contract
│       │   ├── SettingsService.cs       # AppData settings.json persistence with clamping
│       │   ├── IInputDialogService.cs   # Dialog contract for user prompts
│       │   ├── InputDialogService.cs    # WPF dialog window implementation
│       │   ├── IShellLauncher.cs        # OS shell process launcher abstraction
│       │   ├── ShellLauncher.cs         # Shell execution implementation
│       │   ├── ITransferDialogService.cs# Transfer interaction dialog abstraction
│       │   ├── TransferDialogService.cs # WPF dialog implementation for conflicts and failures
│       │   └── WpfTransferInteraction.cs# Adapter connecting Core ITransferInteraction to UI
│       ├── ViewModels/
│       │   ├── FileItemViewModel.cs     # Lazy icon-loaded file item representation
│       │   ├── FilePaneViewModel.cs     # Pane navigation, sorting, multi-selection, and status
│       │   ├── SidebarItemViewModel.cs  # Favorites & drives sidebar item (with drive icons)
│       │   ├── ConflictDialogViewModel.cs# MVVM ViewModel for conflict comparison and thumbnails
│       │   ├── TransferJobViewModel.cs  # Individual transfer job wrapper with progress and commands
│       │   ├── TransferDrawerViewModel.cs# Bottom drawer coordinator, job list, auto-remove timer
│       │   └── MainViewModel.cs         # Root dual-pane coordinator, transfer queue & shortcuts
│       ├── Views/
│       │   ├── FilePaneView.xaml(.cs)   # Virtualized listview, address bar, OLE drag-drop handling
│       │   ├── TransferDrawerView.xaml(.cs)# Collapsible transfer progress drawer UserControl
│       │   ├── ConflictDialog.xaml(.cs) # Windows 11 Fluent conflict comparison modal dialog
│       │   ├── FailureDialog.xaml(.cs)  # Transfer failure and retry modal dialog
│       │   ├── InputDialogWindow.xaml   # Modern Windows 11 style text input dialog
│       │   └── MainWindow.xaml(.cs)     # Shell layout, drawer host, splitter, keyboard shortcuts
│       ├── App.xaml(.cs)                # DI container setup & pre-Show window bounds clamping
│       └── FM.App.csproj
│
├── tests/
│   ├── Common/
│   │   └── TestWorkspace.cs             # Bounded %TEMP%\FM_* workspace manager (max 200 files / 1 MiB)
│   ├── FM.Core.Tests/           # Core library xUnit test suite (159 tests)
│   ├── FM.App.Tests/            # App-level xUnit test suite (117 tests)
│   └── FM.Benchmarks/           # Performance benchmark harness
│       └── Program.cs                   # Navigation and 2 GB transfer benchmarks
│
├── MyFileManager.sln            # Visual Studio Solution
├── .gitignore                   # Standard Visual Studio / dotnet template
└── README.md                    # Project documentation
```

---

## 4. Keyboard & Mouse Shortcuts

| Shortcut / Action | Function | Scope |
| :--- | :--- | :--- |
| **`Tab`** | Switch active pane between Left and Right | File list focused (preserves normal Tab in address bar) |
| **`Ctrl + H`** | Toggle display of hidden and system files | Active pane & persisted in AppSettings |
| **`Ctrl + Shift + C`** | Copy selected items to the opposite pane | Active pane |
| **`Ctrl + Shift + M`** | Move selected items to the opposite pane | Active pane |
| **`Ctrl + Shift + N`** | Create a new folder | Active pane |
| **`F2`** | Rename selected item | Active pane |
| **`Ctrl + Z`** | Undo last file operation (New Folder, Rename, Copy, Move, Overwrite) | Active pane |
| **`Enter`** | Open folder, launch file with default app, or navigate address bar | Active pane |
| **`Backspace`** | Navigate up to parent folder | Active pane |
| **`Alt + Left`** | Navigate backward in history | Active pane |
| **`Alt + Right`** | Navigate forward in history | Active pane |
| **`Ctrl + L`** | Focus and select text in the address bar | Active pane |
| **`F5`** | Refresh file listing | Active pane |
| **`Ctrl + Shift + F`** | Focus folder filter box | Toolbar / Active pane |
| **`Esc`** | Clear filter and return focus to list (or cancel address editing) | Filter box / Active pane |
| **Printable chars** | Type-to-select matching item (prefix preferred, 1s idle timeout) | Active file list |
| **Mouse Drag** | Drag selected items between panes or to/from Windows Explorer | Multi-selection preserved |
| **`Ctrl + Drag`** | Force Copy operation during drag | Drag & Drop |
| **`Shift + Drag`** | Force Move operation during drag | Drag & Drop |

---

## 5. Transfer Architecture & Safety Guarantees

### 5.1 Architecture Overview
The transfer engine is organized into decoupled, testable components:
- **`TransferPlanner`**: Pre-scans selected source paths, expands directory hierarchies, skips reparse points/symlinks to prevent infinite loops, validates volume free disk space (accounting for existing target files), and computes collision-free `KeepBoth` names.
- **`TransferEngine`**: Orchestrates copy and move execution:
  - **Copy**: Transferred into a temporary staging file (`<dest_path>.<guid>.fmpart`) in 1–4 MiB chunks. Streams SHA-256 hashes concurrently during copy. Preserves original timestamps. Atomically renames `.fmpart` to the final destination upon successful verification. Cleans up `.fmpart` files if cancelled or failed.
  - **Same-Volume Move**: Executes an atomic OS filesystem move (no read streams opened, instantaneous metadata update). Cooperatively pauses between items if paused.
  - **Cross-Volume Move**: Performs a verified copy to destination first. **Only deletes the source file after destination byte count and SHA-256 hash are verified**. Cleans up empty source directories bottom-up.
- **`PauseGate`**: Implements cooperative thread-safe pausing between chunks. On pause, state transitions `Running` $\to$ `Pausing` $\to$ `Paused`. On resume, file streaming continues from the exact byte offset without restarting. Immediate cancellation is supported while paused.
- **`TransferQueue`**: Enforces strict serial execution for jobs sharing the same destination volume (preventing disk head thrashing), while allowing up to 2 parallel lanes across distinct physical volumes. Emits throttled progress notifications ($\le 10/\text{sec}$) via `TimeProvider`.
- **`IStagingBackupStore` / `StagingBackupStore`**: Stores pre-mutation copies of destination files before they are overwritten into `%LOCALAPPDATA%\MyFileManager\staging\<opId>\`. Validates destination free space and SHA-256 integrity on cross-volume moves. Enforces retention policies (deletes staging backups older than 7 days or when exceeding 5 GB quota).
- **`IUndoJournalStore` / `FileUndoJournalStore`**: Write-ahead append-only JSONL journal (`%LOCALAPPDATA%\MyFileManager\journal\undo-journal.jsonl`). Interrupted transfers are automatically detected and rolled back on launch. Committed entries are replayed into each pane's `UndoStack`. Performs atomic file compaction on startup to prevent unbounded journal growth.
- **`TransferUndoOperation`**: Encapsulates undo logic for transfers:
  - **Copy Undo**: Deletes created files only if their size and timestamps still match (rejects deletion of files modified post-copy) and removes empty parent directories bottom-up.
  - **Move Undo**: Restores moved items only if the original location is free and files are unmodified.
  - **Overwrite Undo**: Seamlessly restores original destination files from staging backups, verifies backup presence, and safely updates the journal state.

---

## 6. Performance Measurements & Benchmarks

The benchmark suite (`tests/FM.Benchmarks`) enforces a strict **zero-disk-generation** policy:
1. **Directory Streaming**: Synthetic in-memory streaming through the real `FilePaneViewModel` hosted in an active WPF `Window` (1024x768) measuring layout, virtualization, and UI stalls.
2. **Transfer Engine**: In-memory 2 GB virtual file copy between simulated volumes with persistent write-ahead undo journaling and deterministic byte patterns.

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

#### B. In-Memory Transfer Engine with Write-Ahead Journal (2 GB Virtual File Copy, 0 Disk Writes):
```
Command: dotnet run --project tests/FM.Benchmarks/FM.Benchmarks.csproj -c Release -- --transfer
```
| Metric | Measured Value |
| :--- | :--- |
| **Total Bytes Copied** | **2,147,483,648 bytes (2.00 GiB)** |
| **Execution Time** | **217 ms (0.217 s)** |
| **Throughput** | **9,421.19 MB/s** |
| **Total Allocations** | **4.49 MiB** |
| **Working Set Initial** | **25.78 MiB** |
| **Working Set Final** | **38.06 MiB (Delta: +12.29 MiB)** |
| **Physical Disk Writes** | **0 bytes** (Pure in-memory virtual stream) |

---

## 7. Strict Low-Disk Testing & Resource Policy

To protect developer SSD endurance and prevent storage bloat, all automated tests and benchmarks enforce strict resource limits:
1. **Hard Budget:** No command or test run may generate more than 200 files or 5 MB total on disk.
2. **Virtual Stream In-Memory Testing:** Engine unit tests run against `InMemoryFileSystem` with virtual files (length only, deterministic byte pattern generated from offset). A 5 GB test file costs **0 bytes of RAM and 0 bytes of disk**.
3. **Physical Disk Integration Tests:** Exactly 6 physical disk tests in `TransferIntegrationTests.cs` and `JournalRealDiskTests.cs`, strictly scoped to a combined total of **8 files and 15 KB** via `TestWorkspace`.
4. **TestWorkspace Lifecycle:** Temporary workspaces live under `%TEMP%\FM_{purpose}_{guid}`, delete automatically on `Dispose()`, and sweep stale directories older than 1 hour on startup.

---

## 8. Known Limitations & Roadmap for v0.5

The v0.4 release achieves feature completeness for all core dual-pane file management, OLE drag-drop, interactive conflict resolution, staging backup, and crash-resilient write-ahead journaling. Remaining limitations targeted for v0.5 include:
- **Virtual OLE Payloads:** Drag-and-drop of virtual files (e.g. Outlook email attachments, items directly dragged out of compressed archives) is not yet supported; only standard `FileDrop` paths are handled.
- **External Explorer Dropped Moves:** When dragging files from MyFileManager to Windows Explorer, Explorer's shell completes the move; because Explorer does not inform third-party apps of external mutations, external moves to Explorer cannot be recorded into the internal Undo stack.
- **Tabs & Breadcrumbs:** Multiple directory tabs and interactive breadcrumb navigation bars are planned for v0.5.

---

## 9. Manual Verification Checklist (F5 in Visual Studio)

### 9.1 Drag and Drop (OLE)
1. **Inter-Pane Drag:**
   - Select one or more files in the Left pane; drag and drop onto the Right pane's blank area:
     - If both panes are on the same drive (e.g. `C:\`), items are **Moved** by default.
     - If panes are on different drives (e.g. `C:\` and `D:\`), items are **Copied** by default.
   - Hold **`Ctrl`** while dragging: cursor shows `+` (Copy), files are copied regardless of drive.
   - Hold **`Shift`** while dragging: files are moved regardless of drive.
   - Drag to a subfolder row: target folder row highlights with blue accent, items drop directly into the subfolder.
   - Drag a folder into itself or its own subfolder: cursor shows `No-Drop` ($\oslash$), drop is safely rejected.
   - Drag files and drop into their own current directory: cursor shows `No-Drop` ($\oslash$), drop is rejected.
   - Press **`Esc`** while dragging: drag operation cancels cleanly with no mutations.
   - Multi-Selection Drag: select 3 items, drag one of them: all 3 items are dragged together without deselecting.
2. **Windows Explorer Integration:**
   - Drag files from Windows Explorer or Desktop into MyFileManager: files drop cleanly and enqueue into the Transfer Drawer.
   - Drag files from MyFileManager to Windows Explorer or Desktop: Explorer receives the files and places them at the destination.
   - Drop causing conflict: ConflictDialog opens; Windows Explorer does not freeze while the modal dialog is displayed.

### 9.2 Transfer Drawer & Progress Panel
1. **Drawer Display:**
   - Start a transfer: bottom drawer automatically expands, showing transfer cards with Title (*"Copying 3 items → D:\Backup"*), Current file, ProgressBar, speed, and ETA.
   - Enqueue a second job to the same volume: card shows *"Waiting for drive C..."* (Queued state).
   - Click the chevron button: drawer collapses into a compact 32 px summary bar (*"1 running · 1 queued · 45% · 120 MB/s"*).
   - Completed transfers display summary and automatically disappear after 8 seconds.
   - Click **"Clear finished"**: finished/failed/cancelled jobs are cleared immediately.
2. **Pause, Resume, and Cancel:**
   - Click **Pause** on a running job: state transitions to `Pausing` then `Paused`, progress bar stops.
   - Click **Resume**: streaming resumes from the exact byte offset.
   - Click **Cancel** on a `Queued` job: job cancels immediately without ever occupying a drive lane or running.
   - Click **Cancel** while a conflict dialog is open: transfer cancels cleanly and dialog dismisses safely.

### 9.3 Undo, Overwrite Staging, and Crash Recovery
1. **Undo Overwrite:**
   - Copy a file over an existing destination file: select **Overwrite** in `ConflictDialog` (green banner confirms *"Overwrite is undoable — original will be backed up"*).
   - After transfer finishes, press **`Ctrl + Z`**: destination file is restored to its exact original contents from staging backup.
2. **Persistent Journal Recovery:**
   - Perform a file transfer or new folder creation.
   - Close MyFileManager and reopen it.
   - Press **`Ctrl + Z`**: operation is restored from `%LOCALAPPDATA%\MyFileManager\journal\undo-journal.jsonl` and successfully undone!
3. **Crash Recovery (Kill Process):**
   - Start copying a large file (e.g. 500 MB) with Overwrite.
   - Mid-transfer, terminate MyFileManager via Windows Task Manager (`End Task`).
   - Relaunch MyFileManager: on startup, the crash recovery engine detects the uncommitted operation, automatically cleans up incomplete `.fmpart` files, rolls back the destination file to its pre-crash state, and compacts the journal. Zero user data is lost.
