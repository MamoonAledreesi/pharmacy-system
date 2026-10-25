using Pharmacy.Application.Reports;

namespace Pharmacy.Infrastructure.Reporting;

/// <summary>Writes finished reports to Excel (ClosedXML) and PDF (QuestPDF).</summary>
public sealed class ReportExporter : IReportExporter
{
    public byte[] ToExcel(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ExcelReportWriter.Write(document);
    }

    public byte[] ToPdf(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return PdfReportWriter.Write(document);
    }
}
