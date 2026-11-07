using System.Globalization;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Auditing;

namespace GroceryPOS.Application.Reports.Export;

public enum ReportKind
{
    Revenue = 0,
    BestSellers = 1,
    ProfitByCategory = 2,
    InventoryValue = 3,
}

/// <summary>
/// What to export: one or more reports over the same period. <paramref name="CategoryName"/> is only used for
/// the best-seller sheet subtitle.
/// </summary>
public sealed record ReportExportRequest(
    IReadOnlyList<ReportKind> Reports,
    ReportPeriod Period,
    ProductRanking RankBy = ProductRanking.Quantity,
    int Top = 50,
    int? CategoryId = null,
    string? CategoryName = null);

public interface IReportExportService
{
    /// <summary>
    /// Runs the requested reports (same permissions as <see cref="IReportService"/>) and writes them as one
    /// workbook, one sheet per report. Audited as <c>report.exported</c>.
    /// </summary>
    Task<Result<ReportFile>> ExportAsync(ReportExportRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the requested reports and returns their sheets for printing or PDF, without writing a file or auditing –
    /// call <see cref="RecordPrintedAsync"/> once the document has actually been printed or saved.
    /// </summary>
    Task<Result<ReportDocument>> PrepareAsync(ReportExportRequest request, CancellationToken cancellationToken = default);

    /// <summary>Audits a printed report or a saved PDF as <c>report.printed</c>.</summary>
    Task RecordPrintedAsync(ReportDocument document, ReportOutput output, string? target = null, CancellationToken cancellationToken = default);
}

/// <summary>Where a prepared report went.</summary>
public enum ReportOutput
{
    Printer = 0,
    Pdf = 1,
}

/// <summary>
/// Prepared report sheets plus a file name stem ("BaoCao_DoanhThu_20261101-20261130"), the period line and the
/// store / store-local time they were generated for.
/// </summary>
public sealed record ReportDocument(
    string FileStem, string PeriodLine, IReadOnlyList<ReportSheet> Sheets, string StoreName, DateTime GeneratedAt)
{
    /// <summary>"Doanh thu" or "Doanh thu, Bán chạy" – used in titles and the audit trail.</summary>
    public string Names => string.Join(", ", Sheets.Select(s => s.Name));
}

public sealed class ReportExportService : IReportExportService
{
    public static readonly Error NothingSelected = Error.Validation("Chọn ít nhất một báo cáo để xuất.");

    private readonly IReportService _reports;
    private readonly IReportWorkbookWriter _writer;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly StoreProfile _store;
    private readonly SalesOptions _options;

    public ReportExportService(
        IReportService reports,
        IReportWorkbookWriter writer,
        ICurrentUser currentUser,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        StoreProfile? store = null,
        SalesOptions? options = null)
    {
        _reports = reports;
        _writer = writer;
        _currentUser = currentUser;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _store = store ?? StoreProfile.Default;
        _options = options ?? SalesOptions.Default;
    }

    public async Task<Result<ReportFile>> ExportAsync(ReportExportRequest request, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request, cancellationToken);
        if (prepared.IsFailure)
        {
            return Result.Failure<ReportFile>(prepared.Error);
        }

        var document = prepared.Value;
        var content = await _writer.WriteAsync(document.Sheets, cancellationToken);
        var fileName = document.FileStem + _writer.FileExtension;

        _audit.Record(AuditActions.ReportExported, details: $"{document.Names} – {document.PeriodLine}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ReportFile(fileName, content);
    }

    public async Task<Result<ReportDocument>> PrepareAsync(ReportExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kinds = request.Reports.Distinct().ToList();
        if (kinds.Count == 0)
        {
            return Result.Failure<ReportDocument>(NothingSelected);
        }

        var now = _clock.GetUtcNow().ToOffset(_options.StoreUtcOffset).DateTime;
        var session = _currentUser.Session;
        var header = new ReportHeader(_store.Name, session?.FullName ?? session?.Username ?? "?", now);

        var sheets = new List<ReportSheet>(kinds.Count);
        foreach (var kind in kinds)
        {
            var sheet = await BuildAsync(kind, request, header, now, cancellationToken);
            if (sheet.IsFailure)
            {
                return Result.Failure<ReportDocument>(sheet.Error);
            }

            sheets.Add(sheet.Value);
        }

        return new ReportDocument(FileName(kinds, request.Period, now), ReportSheets.PeriodLine(request.Period), sheets, _store.Name, now);
    }

    public async Task RecordPrintedAsync(
        ReportDocument document, ReportOutput output, string? target = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var how = output == ReportOutput.Pdf ? "PDF" : "máy in";
        var where = string.IsNullOrWhiteSpace(target) ? string.Empty : $" ({target.Trim()})";
        _audit.Record(AuditActions.ReportPrinted, details: $"{document.Names} – {document.PeriodLine} – {how}{where}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>"BaoCao_DoanhThu_20261101-20261130", or "BaoCao_20261101-20261130" for several reports.</summary>
    public static string FileName(IReadOnlyList<ReportKind> kinds, ReportPeriod period, DateTime now)
    {
        var name = kinds.Count == 1 ? $"BaoCao_{Slug(kinds[0])}" : "BaoCao";
        if (kinds.Count == 1 && kinds[0] == ReportKind.InventoryValue)
        {
            return $"{name}_{now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}";
        }

        var from = period.From.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var to = period.To.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return period.From == period.To ? $"{name}_{from}" : $"{name}_{from}-{to}";
    }

    private static string Slug(ReportKind kind) => kind switch
    {
        ReportKind.Revenue => "DoanhThu",
        ReportKind.BestSellers => "BanChay",
        ReportKind.ProfitByCategory => "LoiNhuan",
        ReportKind.InventoryValue => "TonKho",
        _ => kind.ToString(),
    };

    private async Task<Result<ReportSheet>> BuildAsync(
        ReportKind kind, ReportExportRequest request, ReportHeader header, DateTime now, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case ReportKind.Revenue:
            {
                var report = await _reports.GetRevenueAsync(request.Period, cancellationToken);
                return report.IsFailure ? Result.Failure<ReportSheet>(report.Error) : ReportSheets.Revenue(report.Value, header);
            }

            case ReportKind.BestSellers:
            {
                var query = new BestSellerQuery(request.Period, request.RankBy, request.Top, request.CategoryId);
                var report = await _reports.GetBestSellersAsync(query, cancellationToken);
                return report.IsFailure
                    ? Result.Failure<ReportSheet>(report.Error)
                    : ReportSheets.BestSellers(report.Value, header, request.CategoryId is null ? null : request.CategoryName);
            }

            case ReportKind.ProfitByCategory:
            {
                var report = await _reports.GetProfitByCategoryAsync(request.Period, cancellationToken);
                return report.IsFailure ? Result.Failure<ReportSheet>(report.Error) : ReportSheets.ProfitByCategory(report.Value, header);
            }

            case ReportKind.InventoryValue:
            {
                var report = await _reports.GetInventoryValueAsync(cancellationToken: cancellationToken);
                return report.IsFailure ? Result.Failure<ReportSheet>(report.Error) : ReportSheets.InventoryValue(report.Value, header, now);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }
}
