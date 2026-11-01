using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FM.App.ViewModels;
using FM.Core.FileSystem;

namespace FM.App.Services.QuickOpen;

public class QuickOpenSourceProvider : IQuickOpenSourceProvider
{
    private readonly MainViewModel _mainViewModel;
    private readonly IFileSystem _fileSystem;
    private readonly ICommandCatalog _commandCatalog;

    public QuickOpenSourceProvider(
        MainViewModel mainViewModel,
        IFileSystem fileSystem,
        ICommandCatalog? commandCatalog = null)
    {
        _mainViewModel = mainViewModel;
        _fileSystem = fileSystem;
        _commandCatalog = commandCatalog ?? new CommandCatalog(mainViewModel);
    }

    public IReadOnlyList<QuickOpenItemViewModel> GetPlaces()
    {
        var vm = _mainViewModel;
        var results = new List<QuickOpenItemViewModel>();
        var favoritePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Sidebar Favorites
        foreach (var fav in vm.SidebarFavorites)
        {
            if (string.IsNullOrWhiteSpace(fav.Path)) continue;
            favoritePaths.Add(fav.Path);

            results.Add(new QuickOpenItemViewModel
            {
                Title = fav.Name,
                Subtitle = fav.Path,
                Badge = "Favorite",
                Icon = fav.Icon,
                Kind = QuickOpenItemKind.Place,
                TargetPath = fav.Path,
                Priority = 2,
                ExecuteAction = () => vm.ActivePane?.Navigate(fav.Path)
            });
        }

        // 2. Recent Folders (Deduplicate: Favorite badge wins over Recent for identical paths)
        foreach (var path in vm.RecentFolders.Items)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (favoritePaths.Contains(path)) continue;

            string title = Path.GetFileName(path);
            if (string.IsNullOrEmpty(title))
            {
                title = path; // e.g. "C:\"
            }

            results.Add(new QuickOpenItemViewModel
            {
                Title = title,
                Subtitle = path,
                Badge = "Recent",
                Kind = QuickOpenItemKind.Place,
                TargetPath = path,
                Priority = 3,
                ExecuteAction = () => vm.ActivePane?.Navigate(path)
            });
        }

        // 3. Open Panes (the app's "tabs")
        foreach (var pane in vm.Panes)
        {
            string title = string.IsNullOrWhiteSpace(pane.CurrentFolderName)
                ? (string.IsNullOrWhiteSpace(pane.CurrentPath) ? "New Tab" : Path.GetFileName(pane.CurrentPath))
                : pane.CurrentFolderName;

            if (string.IsNullOrEmpty(title)) title = pane.CurrentPath;

            results.Add(new QuickOpenItemViewModel
            {
                Title = string.IsNullOrWhiteSpace(title) ? "Pane" : title,
                Subtitle = pane.CurrentPath,
                Badge = "Open pane",
                Kind = QuickOpenItemKind.Place,
                TargetPath = pane.CurrentPath,
                Priority = 1,
                ExecuteAction = () => vm.SetActivePane(pane)
            });
        }

        // 4. Sidebar Drives
        foreach (var drive in vm.SidebarDrives)
        {
            if (string.IsNullOrWhiteSpace(drive.Path)) continue;
            if (favoritePaths.Contains(drive.Path)) continue;

            results.Add(new QuickOpenItemViewModel
            {
                Title = drive.Name,
                Subtitle = drive.Path,
                Badge = "Drive",
                Icon = drive.Icon,
                Kind = QuickOpenItemKind.Place,
                TargetPath = drive.Path,
                Priority = 4,
                ExecuteAction = () => vm.ActivePane?.Navigate(drive.Path)
            });
        }

        return results;
    }

    public IReadOnlyList<QuickOpenItemViewModel> GetCommands()
    {
        var cmds = _commandCatalog.GetCommands();
        var results = new List<QuickOpenItemViewModel>();

        foreach (var cmd in cmds)
        {
            try
            {
                if (!cmd.CanExecute()) continue;

                results.Add(new QuickOpenItemViewModel
                {
                    Title = cmd.Title,
                    Subtitle = cmd.Category,
                    Badge = cmd.ShortcutText,
                    Glyph = cmd.Glyph,
                    Kind = QuickOpenItemKind.Command,
                    ExecuteAction = cmd.Execute,
                    Priority = 0
                });
            }
            catch
            {
                // Ignore commands whose CanExecute throws
            }
        }

        return results;
    }

    public async Task CheckAvailabilityAsync(IEnumerable<QuickOpenItemViewModel> items, CancellationToken cancellationToken = default)
    {
        var placeItems = items.Where(i => i.Kind == QuickOpenItemKind.Place && !string.IsNullOrWhiteSpace(i.TargetPath)).ToList();
        if (placeItems.Count == 0) return;

        await Task.Run(() =>
        {
            foreach (var item in placeItems)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    var task = Task.Run(() => _fileSystem.DirectoryExists(item.TargetPath!), cancellationToken);
                    if (task.Wait(TimeSpan.FromMilliseconds(500), cancellationToken))
                    {
                        item.IsAvailable = task.Result;
                    }
                    else
                    {
                        item.IsAvailable = false;
                    }
                }
                catch
                {
                    item.IsAvailable = false;
                }
            }
        }, cancellationToken);
    }
}
