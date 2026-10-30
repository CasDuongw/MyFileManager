# MyFileManager (v0.2)

A lightweight, high-performance dual-pane file manager for Windows 11 built with **.NET 10 (Windows)**, **WPF**, and modern **MVVM** (`CommunityToolkit.Mvvm`).

---

## 1. What It Is

`MyFileManager` is a dual-pane desktop file manager designed for keyboard-driven productivity and fluid Windows 11 integration. It offers side-by-side directory browsing, robust two-phase navigation history, STA-isolated shell icon extraction, extended multi-selection, and non-blocking background streaming directory enumeration capable of handling directories with hundreds of thousands of files without freezing the user interface.

---

## 2. How to Build and Run

### Prerequisites
- **Operating System:** Windows 10/11
- **IDE:** Visual Studio 2022 (version 17.12 or later) with the **.NET desktop development** workload.
- **SDK:** .NET 10.0 SDK.

### Option A: Run in Visual Studio (Recommended)
1. Double-click `MyFileManager.sln` to open the solution in Visual Studio.
2. Ensure **`FM.App`** is set as the startup project (configured as primary in `.sln`).
3. Press **F5** (or click **Start**). The application will compile and launch with no manual configuration required.

### Option B: Run via .NET CLI
```bash
# Clone or navigate to the repository directory
cd MyFileManager

# Build entire solution (zero warnings, zero errors)
dotnet build MyFileManager.sln

# Run all test suites
dotnet test MyFileManager.sln

# Run benchmarks
dotnet run --project tests/FM.Benchmarks/FM.Benchmarks.csproj

# Launch the WPF application
dotnet run --project src/FM.App/FM.App.csproj
```

---

## 3. Project Structure

```
MyFileManager/
├── src/
│   ├── FM.Core/                 # Pure domain & business logic library (NO WPF dependency)
│   │   ├── Helpers/
│   │   │   └── FileEntrySorter.cs       # Folders-first deterministic sorting
│   │   ├── Models/
│   │   │   ├── FileEntry.cs             # Immutable file record
│   │   │   └── FileSortColumn.cs        # Sort column enumeration
│   │   ├── Navigation/
│   │   │   └── NavigationHistory.cs     # Two-phase Back/Forward navigation stack manager
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
│       │   └── SettingsService.cs      # AppData settings.json persistence with clamping
│       ├── ViewModels/
│       │   ├── FileItemViewModel.cs     # Lazy icon-loaded file item representation
│       │   ├── FilePaneViewModel.cs     # Pane navigation, sorting, and multi-selection
│       │   ├── SidebarItemViewModel.cs  # Favorites & drives sidebar item (with drive icons)
│       │   └── MainViewModel.cs         # Root dual-pane & active pane coordinator
│       ├── Views/
│       │   ├── FilePaneView.xaml(.cs)   # Virtualized listview & address bar
│       │   └── MainWindow.xaml(.cs)     # Shell layout, splitter, and sidebar
│       ├── App.xaml(.cs)                # DI container setup & pre-Show window bounds clamping
│       └── FM.App.csproj
│
├── tests/
│   ├── FM.Core.Tests/           # Core library xUnit test suite
│   │   ├── DirectoryServiceTests.cs     # Threading, channel streaming, locked dirs, cancellation
│   │   ├── FileEntrySorterTests.cs      # Folders-always-on-top sorting
│   │   └── NavigationHistoryTests.cs    # Two-phase back/forward stack integrity
│   │
│   ├── FM.App.Tests/            # App-level xUnit test suite (WPF ViewModels & Services)
│   │   ├── Fakes/
│   │   │   ├── FakeDirectoryService.cs  # Hand-written configurable directory fake
│   │   │   └── FakeIconService.cs       # Hand-written zero-overhead icon fake
│   │   ├── FilePaneViewModelTests.cs    # Rapid navigations, error states, sort, multi-select
│   │   └── SettingsServiceTests.cs      # Roundtrip, corrupted JSON, missing file, invalid values
│   │
│   └── FM.Benchmarks/           # Performance benchmark harness
│       └── Program.cs                   # 10k, 50k, 200k files load & UI-thread stall benchmark
│
├── MyFileManager.sln            # Visual Studio Solution
├── .gitignore                   # Visual Studio, .NET & WPF Git ignore rules
└── README.md                    # Project documentation
```

---

## 4. Keyboard Shortcuts

| Shortcut | Description |
| :--- | :--- |
| **`Tab`** | Switch active pane between Left and Right (when focus is inside a file list; address bar keeps standard Tab behavior) |
| **`Enter`** | Open selected folder, execute file with default app, or navigate address bar |
| **`Backspace`** | Navigate up to parent folder |
| **`Alt + Left`** | Navigate backward in history for the active pane |
| **`Alt + Right`** | Navigate forward in history for the active pane |
| **`Ctrl + L`** | Focus and select text in the active pane's address bar |
| **`F5`** | Refresh file listing in the active pane |

---

## 5. Performance Measurements & Benchmarks

To eliminate unverified claims, `MyFileManager` features a benchmark harness (`tests/FM.Benchmarks`) adhering to a strict **zero-disk-generation** policy. It measures directory loading and UI responsiveness using synthetic in-memory streaming through the real `FilePaneViewModel` hosted inside an active WPF `Window` (1024x768) to capture authentic layout, data-binding, and UI virtualization overhead. UI thread stalls are tracked with a 16 ms precision `DispatcherTimer`.

### Benchmark Environment:
- **CPU:** 12th Gen Intel(R) Core(TM) i5-12450HX (8 cores, 12 logical processors)
- **RAM:** 16 GB DDR5
- **OS:** Windows 11 (NT 10.0.26300.0)
- **Runtime:** .NET 10.0 (Windows Desktop)
- **Command:** `dotnet run --project tests/FM.Benchmarks/FM.Benchmarks.csproj --no-build`

### Measured Results:

| Metric | Warm-up Run | Measured Runs (Median of 5) | Target / Note |
| :--- | :---: | :---: | :--- |
| **Directory Mode** | Synthetic In-Memory | Synthetic In-Memory | 10,000 entries (0 disk files created) |
| **Time to First Batch** | 65 ms | **3 ms** | First 50 items displayed immediately |
| **Time to Full List** | 778 ms | **293 ms** | Complete 10,000 item streaming & sorting |
| **Max UI Thread Stall** | 191.20 ms | **65.22 ms** | 16 ms timer drift during full layout |
| **Collection Reset Events** | 1 | **1** | Target: <= 1 per navigation (never per batch) |
| **Initial Working Set** | 28.10 MiB | 28.10 MiB | Process memory at startup |
| **Final Working Set** | 135.57 MiB | 135.57 MiB | Working set after 10k items + WPF visuals |

> *Note: Only verified, locally executed benchmarks are reported. Real-disk mode is available via `dotnet run --project tests/FM.Benchmarks/FM.Benchmarks.csproj -- --path <existing_folder>` and runs in READ-ONLY mode without writing to disk.*

---

## 6. Strict Low-Disk Testing & Resource Policy

To protect developer storage and ensure fast, deterministic CI pipelines, all tests and benchmarks enforce strict resource limits:
1. **Hard Budget:** No test run or command may create more than 200 files or 1 MiB total on disk.
2. **Pure In-Memory ViewModel Tests:** All tests in `FM.App.Tests` use hand-written fakes (`FakeDirectoryService`, `FakeIconService`) and synthetic paths (`C:\MockTest\...`), touching **zero** physical disk files.
3. **Managed TestWorkspace:** Core disk tests (`FM.Core.Tests`) use `TestWorkspace` under `%TEMP%\FM_{purpose}_{guid}`. It enforces maximum file count and total size, cleans up in `finally`/`Dispose`, and sweeps stale `FM_*` folders older than 1 hour on startup.
4. **Read-Only Benchmarks:** Benchmarks never create files or folders on disk.

---

## 7. Engineering Architecture & Highlights

- **Pure Core Logic (`FM.Core`):** `FM.Core` targets `.NET 10` standard class library and does not reference any WPF assemblies. All business logic, models, and directory streaming reside here.
- **Channel-Based Producer/Consumer Streaming:** `IDirectoryService.EnumerateBatchesAsync` runs directory enumeration in a `Task.Run` producer writing batches (150–256 items) into a bounded channel. The UI thread consumes batches asynchronously without performing disk I/O or Win32 calls.
- **STA-Isolated Shell Icon Extraction:** Win32 `SHGetFileInfo` P/Invoke calls run exclusively on a dedicated background STA worker thread with a work queue to ensure complete COM apartment safety with third-party Windows shell extensions. Extraction concurrency is throttled via `SemaphoreSlim(4, 4)`, and requests are cancelled when navigating away.
- **Lazy Icon Loading:** `FileItemViewModel` requests shell icons lazily only when its `Icon` property is accessed by realized rows in the virtualized `ListView`, avoiding eager icon extraction for off-screen items.
- **Two-Phase Navigation History:** `NavigationHistory` peeks targets prior to navigation and commits history transitions only after a target folder successfully loads, preventing history desync when encountering missing or deleted folders.
- **Superseded Navigation Protection:** Each navigation carries a monotonic version token. If a navigation is cancelled or superseded by a newer user action, all catch handlers, error messages, and sorting passes from older requests are ignored.
- **Robust Window State Persistence:** Saved dimensions, position, and window state are validated and clamped to the virtual screen before `MainWindow.Show()`, preventing window flicker or off-screen placement after monitor configuration changes.
- **Extended Multi-Selection:** Full support for Ctrl+Click, Shift+Click, and Ctrl+A with live status bar feedback displaying count and total byte size of selected files.

---

## 7. Known Limitations / Next Steps (Honest Assessment)

The current version is **v0.2** (review fixes and file operations preparation):
- **File Operations Engine:** Copy, Move, Delete, Rename, and New Folder are planned for v0.3. Architecture is prepared with multi-selection and path models.
- **Persistent Undo/Redo:** An undo journal for file operations is not yet implemented.
- **Search & Quick Filter:** Real-time search/indexing and filename filtering within a pane are scheduled for future iterations.
- **Tabs & Workspaces:** Multi-tab browsing per pane is not yet supported.
- **Drag and Drop:** Mouse drag-and-drop between panes is not implemented in v0.2.
- **Cloud Storage Integration:** Files are browsed as standard local file system paths without specialized OneDrive/Google Drive hydration badges.
