using GroceryPOS.Application.Reports.Export;

namespace GroceryPOS.Application.Abstractions;

/// <summary>
/// Writes report sheets to a spreadsheet file (.xlsx). Pure transcoding – the content and layout decisions are
/// made by <see cref="ReportSheets"/>. Implemented in Infrastructure with ClosedXML.
/// </summary>
public interface IReportWorkbookWriter
{
    /// <summary>File extension including the dot, e.g. ".xlsx".</summary>
    string FileExtension { get; }

    Task<byte[]> WriteAsync(IReadOnlyList<ReportSheet> sheets, CancellationToken cancellationToken = default);
}
