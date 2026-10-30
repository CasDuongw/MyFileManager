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

To eliminate unverified performance claims, `MyFileManager` includes a benchmark harness (`tests/FM.Benchmarks`) that generates physical test files in temporary directories on disk, streams them through the real `DirectoryService` and `FilePaneViewModel`, and measures UI thread stalls using `DispatcherTimer` drift monitoring (5 ms precision timer on the WPF Dispatcher thread).

### Measured Results on Windows 11 (.NET 10, 12 cores):

| File Count | Time to First Batch (ms) | Time to Full List (ms) | Max UI Thread Stall (ms) |
| :---: | :---: | :---: | :---: |
| **10,000 files** | **20 ms** | **42 ms** | **15.73 ms** |
| **50,000 files** | **10 ms** | **98 ms** | **14.52 ms** |
| **200,000 files** | **40 ms** | **560 ms** | **26.46 ms** |

### Performance Observations:
- **Immediate User Feedback:** The first batch of files is visible in under 40 ms regardless of directory size, providing instant interactivity.
- **Minimal UI Stall:** Maximum UI thread stall remains below **27 ms** even when loading a 200,000-file directory, ensuring the window remains responsive to clicks, resizing, and keyboard inputs throughout the load.
- **Zero UI-Thread Disk IO:** Directory traversal runs completely off the UI thread via `Task.Run` and streams batches through a bounded `System.Threading.Channels.Channel<IReadOnlyList<FileEntry>>`.

---

## 6. Engineering Architecture & Highlights

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
