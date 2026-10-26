using GroceryPOS.App.Forms.Pos;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Shifts;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Shifts;

/// <summary>Opens the receipt-printer preview for a shift report (giữa ca while open, kết ca once closed).</summary>
internal static class ShiftReportViewer
{
    public static void Show(IWin32Window owner, IServiceScopeFactory scopeFactory, ShiftSummaryDto shift)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetService<StoreProfile>() ?? StoreProfile.Default;
        var sales = scope.ServiceProvider.GetService<SalesOptions>() ?? SalesOptions.Default;
        var clock = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        var printedAt = clock.GetUtcNow();

        var title = shift.ClosedAt is null ? $"Báo cáo giữa ca {shift.Code}" : $"Báo cáo kết ca {shift.Code}";
        using var preview = new ReceiptPreviewForm(
            columns => ShiftReportFormatter.Format(shift, store, columns, sales.StoreUtcOffset, printedAt),
            title,
            title,
            store.ReceiptPaperWidthMm);
        preview.ShowDialog(owner);
    }
}
