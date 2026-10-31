using Pharmacy.Application.Authorization;
using Pharmacy.Application.Import;

namespace Pharmacy.Web.Import;

public static class ImportEndpoints
{
    /// <summary>The blank products sheet in the current language. Needs Catalog.Manage, like the import itself.</summary>
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ImportSheets.TemplatePath, (ImportSheets sheets, IProductSheetWriter writer) =>
            Results.File(writer.Write(sheets.Template()), ImportSheets.ExcelContentType, "products-template.xlsx"))
            .RequireAuthorization(Permissions.CatalogManage);

        return endpoints;
    }
}
