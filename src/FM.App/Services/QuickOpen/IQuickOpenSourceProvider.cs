using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FM.Core.Search;

namespace FM.App.Services.QuickOpen;

public interface IQuickOpenSourceProvider
{
    IReadOnlyList<QuickOpenItemViewModel> GetPlaces();
    IReadOnlyList<QuickOpenItemViewModel> GetCommands();
    Task CheckAvailabilityAsync(IEnumerable<QuickOpenItemViewModel> items, CancellationToken cancellationToken = default);
    IFolderIndex? FolderIndex => null;
    string GetStatusMessage() => "Search open tabs, favorites, and recents";
}

