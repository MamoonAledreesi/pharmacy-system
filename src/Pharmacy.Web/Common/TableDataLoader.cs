using MudBlazor;

namespace Pharmacy.Web.Common;

public static class TableDataLoader
{
    /// <summary>
    /// Loads one page for a server-side table. MudBlazor cancels a load that a newer one replaces (typing in the
    /// search box, opening a dialog, changing page), and SQL Server reports a cancelled query as an ordinary
    /// "severe error" exception. The result of a cancelled load is thrown away anyway, so it is not an error.
    /// </summary>
    public static async Task<TableData<T>> LoadAsync<T>(Func<Task<TableData<T>>> load, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);

        try
        {
            return await load();
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return new TableData<T> { Items = [], TotalItems = 0 };
        }
    }

    /// <summary>The same protection for autocomplete searches, which are also cancelled by the next keystroke.</summary>
    public static async Task<IEnumerable<T>> SearchAsync<T>(Func<Task<IEnumerable<T>>> search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        try
        {
            return await search();
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }
}
