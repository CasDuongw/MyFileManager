using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FM.App.Services.QuickOpen;

public interface IQuickOpenSourceProvider
{
    IReadOnlyList<QuickOpenItemViewModel> GetPlaces();
    IReadOnlyList<QuickOpenItemViewModel> GetCommands();
    Task CheckAvailabilityAsync(IEnumerable<QuickOpenItemViewModel> items, CancellationToken cancellationToken = default);
    string GetStatusMessage() => "Search open tabs, favorites, and recents";
}

