using System;
using System.Collections.Generic;
using FM.App.Models;
using FM.App.ViewModels;

namespace FM.App.Services.QuickOpen;

public class CommandCatalog : ICommandCatalog
{
    private readonly MainViewModel _mainViewModel;

    public CommandCatalog(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    public IReadOnlyList<AppCommand> GetCommands()
    {
        var vm = _mainViewModel;
        var pane = vm.ActivePane;

        var list = new List<AppCommand>
        {
            // --- File ---
            new()
            {
                Id = "file.new-folder",
                Title = "New Folder",
                Category = "File",
                ShortcutText = "Ctrl+Shift+N",
                CanExecute = () => pane?.NewFolderCommand.CanExecute(null) == true,
                Execute = () => pane?.NewFolderCommand.Execute(null)
            },
            new()
            {
                Id = "file.new-tab",
                Title = "New Tab",
                Category = "File",
                ShortcutText = "Ctrl+T",
                CanExecute = () => true,
                Execute = () => vm.OpenNewTab()
            },
            new()
            {
                Id = "file.close-tab",
                Title = "Close Tab",
                Category = "File",
                ShortcutText = "Ctrl+W",
                CanExecute = () => vm.CanClosePanes,
                Execute = () => vm.ClosePaneCommand.Execute(null)
            },
            new()
            {
                Id = "file.copy",
                Title = "Copy",
                Category = "File",
                ShortcutText = "Ctrl+C",
                CanExecute = () => pane != null && (pane.SelectedItems.Count > 0 || pane.SelectedItem != null),
                Execute = () => pane?.Copy()
            },
            new()
            {
                Id = "file.cut",
                Title = "Cut",
                Category = "File",
                ShortcutText = "Ctrl+X",
                CanExecute = () => pane != null && (pane.SelectedItems.Count > 0 || pane.SelectedItem != null),
                Execute = () => pane?.Cut()
            },
            new()
            {
                Id = "file.paste",
                Title = "Paste",
                Category = "File",
                ShortcutText = "Ctrl+V",
                CanExecute = () => pane?.PasteCommand.CanExecute(null) == true,
                Execute = () => pane?.PasteCommand.Execute(null)
            },
            new()
            {
                Id = "file.copy-path",
                Title = "Copy Path",
                Category = "File",
                ShortcutText = "",
                CanExecute = () => pane != null,
                Execute = () => pane?.CopyPath()
            },
            new()
            {
                Id = "file.delete",
                Title = "Delete to Recycle Bin",
                Category = "File",
                ShortcutText = "Delete",
                CanExecute = () => pane != null && (pane.SelectedItems.Count > 0 || pane.SelectedItem != null),
                Execute = () => pane?.DeleteToRecycleBinCommand.Execute(null)
            },
            new()
            {
                Id = "file.rename",
                Title = "Rename",
                Category = "File",
                ShortcutText = "F2",
                CanExecute = () => pane?.RenameCommand.CanExecute(null) == true,
                Execute = () => pane?.RenameCommand.Execute(null)
            },
            new()
            {
                Id = "file.undo",
                Title = "Undo",
                Category = "File",
                ShortcutText = "Ctrl+Z",
                CanExecute = () => pane?.UndoCommand.CanExecute(null) == true,
                Execute = () => pane?.UndoCommand.Execute(null)
            },
            new()
            {
                Id = "file.compress",
                Title = "Compress to Zip",
                Category = "File",
                ShortcutText = "",
                CanExecute = () => pane != null && (pane.SelectedItems.Count > 0 || pane.SelectedItem != null),
                Execute = () => pane?.CompressToZipCommand.Execute(null)
            },
            new()
            {
                Id = "file.terminal",
                Title = "Open in Terminal",
                Category = "File",
                ShortcutText = "",
                CanExecute = () => pane?.OpenInTerminalCommand.CanExecute(null) == true,
                Execute = () => pane?.OpenInTerminalCommand.Execute(null)
            },
            new()
            {
                Id = "file.vscode",
                Title = "Open with VS Code",
                Category = "File",
                ShortcutText = "",
                CanExecute = () => pane?.OpenWithCodeCommand.CanExecute(null) == true,
                Execute = () => pane?.OpenWithCodeCommand.Execute(null)
            },

            // --- View ---
            new()
            {
                Id = "view.details",
                Title = "View: Details",
                Category = "View",
                ShortcutText = "",
                CanExecute = () => pane != null && pane.ViewMode != FilePaneViewMode.Details,
                Execute = () => pane?.SetViewMode("Details")
            },
            new()
            {
                Id = "view.icons",
                Title = "View: Icons",
                Category = "View",
                ShortcutText = "",
                CanExecute = () => pane != null && pane.ViewMode != FilePaneViewMode.Icons,
                Execute = () => pane?.SetViewMode("Icons")
            },
            new()
            {
                Id = "view.toggle-hidden",
                Title = "Toggle Hidden Files",
                Category = "View",
                ShortcutText = "Ctrl+H",
                CanExecute = () => true,
                Execute = () => vm.ToggleHiddenFiles()
            },
            new()
            {
                Id = "view.refresh",
                Title = "Refresh",
                Category = "View",
                ShortcutText = "F5",
                CanExecute = () => pane != null,
                Execute = () => pane?.Refresh()
            },
            new()
            {
                Id = "view.filter",
                Title = "Filter Current Folder",
                Category = "View",
                ShortcutText = "Ctrl+Shift+F",
                CanExecute = () => true,
                Execute = () =>
                {
                    // Focus toolbar filter box in main view
                    vm.QuickOpen.Close();
                }
            },

            // --- Pane Layout ---
            new()
            {
                Id = "pane.single",
                Title = "Layout: Single Pane",
                Category = "Pane",
                ShortcutText = "Ctrl+Shift+1",
                CanExecute = () => vm.LayoutMode != PaneLayoutMode.Single,
                Execute = () => vm.LayoutMode = PaneLayoutMode.Single
            },
            new()
            {
                Id = "pane.dual-vertical",
                Title = "Layout: Dual Vertical",
                Category = "Pane",
                ShortcutText = "Ctrl+Shift+2",
                CanExecute = () => vm.Panes.Count >= 2 && vm.LayoutMode != PaneLayoutMode.DualVertical,
                Execute = () => vm.LayoutMode = PaneLayoutMode.DualVertical
            },
            new()
            {
                Id = "pane.triple-left",
                Title = "Layout: Triple Left",
                Category = "Pane",
                ShortcutText = "Ctrl+Shift+3",
                CanExecute = () => vm.Panes.Count >= 3 && vm.LayoutMode != PaneLayoutMode.TripleLeft,
                Execute = () => vm.LayoutMode = PaneLayoutMode.TripleLeft
            },
            new()
            {
                Id = "pane.quad-grid",
                Title = "Layout: Quad Grid",
                Category = "Pane",
                ShortcutText = "Ctrl+Shift+4",
                CanExecute = () => vm.Panes.Count >= 4 && vm.LayoutMode != PaneLayoutMode.QuadGrid,
                Execute = () => vm.LayoutMode = PaneLayoutMode.QuadGrid
            },
            new()
            {
                Id = "pane.switch",
                Title = "Switch Active Pane",
                Category = "Pane",
                ShortcutText = "Tab",
                CanExecute = () => vm.Panes.Count > 1,
                Execute = () => vm.SwitchActivePane()
            },
            new()
            {
                Id = "pane.copy-to-other",
                Title = "Copy to Other Pane",
                Category = "Pane",
                ShortcutText = "Ctrl+Shift+C",
                CanExecute = () => vm.CopyToOtherPaneCommand.CanExecute(null),
                Execute = () => vm.CopyToOtherPaneCommand.Execute(null)
            },
            new()
            {
                Id = "pane.move-to-other",
                Title = "Move to Other Pane",
                Category = "Pane",
                ShortcutText = "Ctrl+Shift+M",
                CanExecute = () => vm.MoveToOtherPaneCommand.CanExecute(null),
                Execute = () => vm.MoveToOtherPaneCommand.Execute(null)
            },

            // --- Go ---
            new()
            {
                Id = "go.back",
                Title = "Go Back",
                Category = "Go",
                ShortcutText = "Alt+Left",
                CanExecute = () => pane?.GoBackCommand.CanExecute(null) == true,
                Execute = () => pane?.GoBackCommand.Execute(null)
            },
            new()
            {
                Id = "go.forward",
                Title = "Go Forward",
                Category = "Go",
                ShortcutText = "Alt+Right",
                CanExecute = () => pane?.GoForwardCommand.CanExecute(null) == true,
                Execute = () => pane?.GoForwardCommand.Execute(null)
            },
            new()
            {
                Id = "go.up",
                Title = "Go Up",
                Category = "Go",
                ShortcutText = "Alt+Up",
                CanExecute = () => pane?.GoUpCommand.CanExecute(null) == true,
                Execute = () => pane?.GoUpCommand.Execute(null)
            },
            new()
            {
                Id = "go.quick-open",
                Title = "Quick Open",
                Category = "Go",
                ShortcutText = "Ctrl+P",
                CanExecute = () => true,
                Execute = () => vm.QuickOpen.Open()
            },
            new()
            {
                Id = "go.command-palette",
                Title = "Command Palette",
                Category = "Go",
                ShortcutText = "Ctrl+Shift+P",
                CanExecute = () => true,
                Execute = () => vm.QuickOpen.Open(">")
            }
        };

        return list;
    }
}
