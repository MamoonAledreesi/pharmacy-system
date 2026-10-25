using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Reports;
using Pharmacy.Web.Localization;

namespace Pharmacy.Web.Reports;

public static class ReportEndpoints
{
    public const string ExcelFormat = "xlsx";
    public const string PdfFormat = "pdf";

    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PdfContentType = "application/pdf";

    /// <summary>
    /// <c>GET /reports/{key}/export?format=xlsx|pdf</c> plus the report's filters. A file download cannot be made from
    /// inside a Blazor circuit, so the page links here; the service still checks Reports.Export and the cost permission.
    /// </summary>
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/reports/{key}/export", async (
            string key,
            string? format,
            [FromQuery(Name = "from")] DateOnly? periodFrom,
            [FromQuery(Name = "to")] DateOnly? periodTo,
            int? customerId,
            int? supplierId,
            int? categoryId,
            string? text,
            [FromServices] ReportService reports,
            [FromServices] ReportDocumentBuilder builder,
            [FromServices] IReportExporter exporter,
            [FromServices] IStringLocalizer<SharedResource> localizer,
            [FromServices] TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<ReportKey>(key, ignoreCase: true, out var reportKey) || !Enum.IsDefined(reportKey))
            {
                return Results.NotFound();
            }

            var extension = string.Equals(format, PdfFormat, StringComparison.OrdinalIgnoreCase) ? PdfFormat
                : string.Equals(format, ExcelFormat, StringComparison.OrdinalIgnoreCase) ? ExcelFormat
                : null;
            if (extension is null)
            {
                return Results.BadRequest();
            }

            var request = new ReportRequest(reportKey, periodFrom, periodTo, customerId, supplierId, categoryId, text);
            var result = await reports.RunForExportAsync(request, cancellationToken);
            if (result.IsFailure)
            {
                return result.Error!.Code switch
                {
                    CommonErrors.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                    CommonErrors.NotFound => Results.NotFound(),
                    _ => Results.Text(localizer.Message(result.Error), statusCode: StatusCodes.Status400BadRequest),
                };
            }

            var document = builder.Build(result.Value);
            var bytes = extension == PdfFormat ? exporter.ToPdf(document) : exporter.ToExcel(document);
            var fileName = $"report-{reportKey}-{clock.GetLocalNow():yyyyMMdd}.{extension}";
            return Results.File(bytes, extension == PdfFormat ? PdfContentType : ExcelContentType, fileName);
        }).RequireAuthorization();

        return endpoints;
    }
}
