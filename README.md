# MyFileManager (v0.1 Skeleton)

A lightweight, high-performance dual-pane file manager for Windows 11 built with **.NET 8 (LTS)**, **WPF**, and modern **MVVM**.

---

## 1. What It Is

`MyFileManager` is a dual-pane desktop file manager designed for keyboard-driven productivity and fluid Windows 11 integration. It offers side-by-side directory browsing, fast navigation history, shell file icon extraction, and high-performance list virtualization capable of handling large directories without freezing the user interface.

---

## 2. How to Build and Run

### Prerequisites
- **Operating System:** Windows 10/11
- **IDE:** Visual Studio 2022 (version 17.8 or later) with the **.NET desktop development** workload installed.
- **SDK:** .NET 8.0 SDK (or later).

### Option A: Run in Visual Studio (Recommended)
1. Double-click `MyFileManager.sln` to open the solution in Visual Studio 2022.
2. Ensure **`FM.App`** is set as the startup project (it is set by default).
3. Press **F5** (or click the green **Start** button). The application will compile and launch with no manual configuration required.

### Option B: Run via .NET CLI
```bash
# Clone or navigate to the repository directory
cd MyFileManager

# Build entire solution
dotnet build MyFileManager.sln

# Run unit tests
dotnet test MyFileManager.sln

# Launch the WPF application
dotnet run --project src/FM.App/FM.App.csproj
```

---

## 3. Project Structure

```
MyFileManager/
├── src/
│   ├── FM.Core/                 # Domain & business logic library (NO WPF dependency)
│   │   ├── Helpers/
│   │   │   └── FileEntrySorter.cs       # Folders-first deterministic sorting
│   │   ├── Models/
│   │   │   ├── FileEntry.cs             # Immutable file record
│   │   │   └── FileSortColumn.cs        # Sort column enumeration
│   │   ├── Navigation/
│   │   │   └── NavigationHistory.cs     # Back/Forward navigation stack manager
│   │   └── Services/
│   │       ├── IDirectoryService.cs     # Asynchronous file enumeration interface
│   │       └── DirectoryService.cs      # Resilient, non-blocking Win32 file enumerator
│   │
│   └── FM.App/                  # WPF Application (Views & ViewModels only)
│       ├── Collections/
│       │   └── BulkObservableCollection.cs # High-performance batch collection
│       ├── Helpers/
│       │   └── FormatHelper.cs          # Size & type formatting utilities
│       ├── Services/
│       │   ├── IIconService.cs          # Shell icon retrieval contract
│       │   ├── IconService.cs           # P/Invoke SHGetFileInfo & extension cache
│       │   ├── ISettingsService.cs      # User settings persistence contract
│       │   └── SettingsService.cs      # AppData settings.json persistence
│       ├── ViewModels/
│       │   ├── FileItemViewModel.cs     # Individual file item representation
│       │   ├── FilePaneViewModel.cs     # Single pane navigation & listing logic
│       │   ├── SidebarItemViewModel.cs  # Favorites & drives sidebar item
│       │   └── MainViewModel.cs         # Root dual-pane & active pane coordinator
│       ├── Views/
│       │   ├── FilePaneView.xaml(.cs)   # Virtualized listview & address bar
│       │   └── MainWindow.xaml(.cs)     # Shell layout, splitter, and sidebar
│       ├── App.xaml(.cs)                # DI container setup (Microsoft.Extensions.DI)
│       └── FM.App.csproj
│
├── tests/
│   └── FM.Core.Tests/           # xUnit test suite
│       ├── DirectoryServiceTests.cs     # Tests for empty, missing, locked dirs & cancellation
│       ├── FileEntrySorterTests.cs      # Tests for folders-always-on-top sorting
│       └── NavigationHistoryTests.cs    # Tests for back/forward stack integrity
│
├── MyFileManager.sln            # Visual Studio Solution
├── .gitignore                   # Visual Studio & .NET Git ignore rules
└── README.md                    # Project documentation
```

---

## 4. Keyboard Shortcuts

| Shortcut | Description |
| :--- | :--- |
| **`Tab`** | Switch active pane between Left and Right |
| **`Enter`** | Open selected folder, execute file with default app, or navigate address bar |
| **`Backspace`** | Navigate up to parent folder |
| **`Alt + Left`** | Navigate backward in history for the active pane |
| **`Alt + Right`** | Navigate forward in history for the active pane |
| **`Ctrl + L`** | Focus and select text in the active pane's address bar |
| **`F5`** | Refresh file listing in the active pane |

---

## 5. Engineering Architecture & Highlights

- **Pure Core Logic (`FM.Core`):** `FM.Core` targets `.NET 8` standard class library and does not reference any WPF assemblies. All business logic, models, and directory streaming reside here.
- **Non-Blocking Directory Streaming:** `IDirectoryService.EnumerateAsync` yields items asynchronously with `IAsyncEnumerable<FileEntry>`. It calls `Task.Yield()` to immediately free the calling UI thread and periodic yields during large folder scans.
- **Fast Navigation & Stale Query Cancellation:** Each pane maintains an isolated `CancellationTokenSource`. Navigating cancels previous queries immediately so older slow queries never overwrite newer results.
- **High-Throughput Batch UI Updating:** Instead of adding items one by one to an `ObservableCollection` (which triggers 50,000 UI redraws), `BulkObservableCollection<T>` adds items in chunks (150 items per batch) and fires `NotifyCollectionChangedAction.Reset` when complete, keeping scrolling and rendering at 60 FPS.
- **Strict UI Virtualization:** The `ListView` uses `VirtualizingPanel.IsVirtualizing="True"` and `VirtualizationMode="Recycling"`, without being wrapped in an outer `ScrollViewer`.
- **Asynchronous Icon Extraction:** File icons are retrieved via Win32 `SHGetFileInfo` P/Invoke. Folder icons and generic file extension icons are cached in memory. Executables (`.exe`, `.lnk`, `.ico`) are cached by full path. Extracted `BitmapSource` instances are frozen (`Freeze()`) so they can be processed in background threads safely.
- **Graceful Error Handling:** If access is denied to a folder or entry, an inline status banner appears inside the affected pane instead of crashing the process or showing blocking dialog boxes.
- **Settings Persistence:** The application automatically preserves and restores window dimensions and last browsed directories in `%AppData%\MyFileManager\settings.json`.

---

## 6. Known Limitations / Not Done Yet (Honest Assessment)

The current version is an architecture-focused **v0.1 skeleton**:
- **File Operations:** Copy, Move, Delete, Rename, and New Folder are not implemented yet (scheduled for v0.2).
- **Undo / Redo:** No undo/redo stack exists for file operations yet.
- **Search & Quick Filter:** Real-time search/indexing and filename filtering within a pane are not yet implemented.
- **Tabs & Workspaces:** Multi-tab support per pane is not yet supported.
- **Drag and Drop:** Mouse drag-and-drop between panes is not implemented in v0.1.
- **Cloud Drives:** Specialized cloud status indicators (e.g., OneDrive dehydrate/hydrate status) are not yet integrated; files are browsed as standard local file system paths.
