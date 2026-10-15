using GroceryPOS.Application.Common;

namespace GroceryPOS.Application.Reports;

public static class ReportErrors
{
    public static readonly Error InvalidPeriod = Error.Validation("Khoảng thời gian không hợp lệ (từ ngày phải trước hoặc bằng đến ngày).");

    public static Error PeriodTooLong(int maxDays) =>
        Error.Validation($"Khoảng thời gian báo cáo tối đa {maxDays} ngày. Vui lòng chọn khoảng ngắn hơn.");

    public static readonly Error Denied = Error.Forbidden("Bạn không có quyền xem báo cáo.");

    public static readonly Error DashboardDenied = Error.Forbidden("Bạn không có quyền xem trang tổng quan.");
}
