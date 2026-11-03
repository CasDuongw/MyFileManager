using System.Windows;
using FM.App.ViewModels;
using FM.App.Views;
using FM.Core.FileSystem;
using FM.Core.Operations;
using FM.Core.Operations.MultiRename;

namespace FM.App.Services;

public class WindowsMultiRenameDialogService : IMultiRenameDialogService
{
    public Task<bool> ShowDialogAsync(
        IReadOnlyList<MultiRenameItemInput> items,
        IFileSystem fileSystem,
        Func<MultiRenamePlan, Task<OperationResult>> executeCallback,
        CancellationToken ct = default)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => ShowDialogAsync(items, fileSystem, executeCallback, ct));
        }

        var vm = new MultiRenameViewModel(items, fileSystem, executeCallback);
        var dialog = new MultiRenameDialog(vm);

        if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
        {
            dialog.Owner = Application.Current.MainWindow;
        }

        bool? result = dialog.ShowDialog();
        return Task.FromResult(result == true);
    }
}

