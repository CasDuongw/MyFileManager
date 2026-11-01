using System.Collections.Generic;

namespace FM.App.Services.QuickOpen;

public interface ICommandCatalog
{
    IReadOnlyList<AppCommand> GetCommands();
}
