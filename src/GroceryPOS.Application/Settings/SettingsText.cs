using System.Globalization;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Settings;

/// <summary>Explanations and previews shown next to the settings while they are edited (UI-free, tested).</summary>
public static class SettingsText
{
    /// <summary>Basket used in the loyalty example and the sample receipt.</summary>
    public const decimal ExampleBasket = 250_000m;

    public const int ExamplePoints = 100;

    /// <summary>Rebate above this share of the bill gets a warning (most shops give 0,5–3%).</summary>
    public const decimal HighRebatePercent = 10m;

    /// <summary>Plain-language summary of the loyalty policy, one sentence per line.</summary>
    public static IReadOnlyList<string> LoyaltySummary(decimal vndPerPoint, decimal pointValueVnd)
    {
        var policy = new LoyaltyPolicy(vndPerPoint, pointValueVnd);
        var lines = new List<string>();
        if (vndPerPoint <= 0 && pointValueVnd <= 0)
        {
            lines.Add("Chương trình tích điểm đang tắt: khách không được cộng điểm và không đổi được điểm.");
            return lines;
        }

        lines.Add(vndPerPoint > 0
            ? $"Mỗi {Vnd.Format(vndPerPoint)} được 1 điểm – hóa đơn {Vnd.Format(ExampleBasket)} được {policy.PointsFor(ExampleBasket).ToString("#,0", Vnd.Culture)} điểm."
            : "Không cộng điểm khi mua hàng (khách chỉ dùng điểm đang có).");

        lines.Add(pointValueVnd > 0
            ? $"Đổi {ExamplePoints} điểm được giảm {Vnd.Format(policy.ValueOf(ExamplePoints))}."
            : "Không cho đổi điểm lấy giảm giá.");

        if (RebatePercent(vndPerPoint, pointValueVnd) is { } rebate)
        {
            lines.Add($"Tương đương hoàn lại {rebate.ToString("0.##", Vnd.Culture)}% giá trị mua hàng.");
        }

        return lines;
    }

    /// <summary>Share of the bill given back when every earned point is redeemed; null when earning or redeeming is off.</summary>
    public static decimal? RebatePercent(decimal vndPerPoint, decimal pointValueVnd) =>
        vndPerPoint > 0 && pointValueVnd > 0 ? Math.Round(pointValueVnd / vndPerPoint * 100m, 2) : null;

    /// <summary>Non-blocking remarks the owner should double-check before saving.</summary>
    public static IReadOnlyList<string> Warnings(StoreSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var warnings = new List<string>();
        if (RebatePercent(settings.VndPerPoint, settings.PointValueVnd) is { } rebate and > HighRebatePercent)
        {
            warnings.Add($"Tỷ lệ hoàn lại {rebate.ToString("0.##", Vnd.Culture)}% khá cao (thường 0,5–3%).");
        }

        if (!settings.RequireOpenShift)
        {
            warnings.Add("Không bắt buộc mở ca: tiền bán hàng sẽ không được đối chiếu với két khi kết ca.");
        }

        if (settings.AllowNegativeStock)
        {
            warnings.Add("Cho bán khi hết tồn: tồn kho có thể âm cho đến khi nhập hàng bù.");
        }

        if (settings.ReturnWindowDays == 0)
        {
            warnings.Add("Hạn đổi trả 0 ngày: thu ngân chỉ nhận trả hàng trong ngày bán.");
        }

        return warnings;
    }

    /// <summary>A realistic receipt for the live preview on the settings screen, using the edited store profile and loyalty policy.</summary>
    public static ReceiptData SampleReceipt(StoreProfile store, LoyaltyPolicy loyalty, DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(loyalty);

        var items = new[]
        {
            new ReceiptItem("Sữa tươi Vinamilk 1L", "hộp", 2, 35_000, 70_000, null, 0, 70_000),
            new ReceiptItem("Mì Hảo Hảo tôm chua cay", "gói", 10, 4_500, 45_000, null, 0, 45_000),
            new ReceiptItem("Nước mắm Nam Ngư 500ml", "chai", 1, 38_000, 38_000, null, 0, 38_000),
            new ReceiptItem("Gạo ST25 túi 5kg", "túi", 1, 97_000, 97_000, null, 0, 97_000),
        };
        var total = items.Sum(i => i.Amount); // = ExampleBasket
        var vat = Vnd.Round(total * 8m / 108m);
        var earned = loyalty.PointsFor(total);
        var member = loyalty.VndPerPoint > 0 || loyalty.PointValueVnd > 0
            ? new ReceiptMember("Nguyễn Văn A", "0901***567", earned, 0, 120 + earned)
            : null;

        return new ReceiptData(
            store,
            $"HD{issuedAt.ToString("yyMMdd", CultureInfo.InvariantCulture)}-0001",
            issuedAt,
            "Thu ngân mẫu",
            SaleStatus.Completed,
            items,
            GrossAmount: total,
            LineDiscountTotal: 0,
            InvoiceDiscountLabel: null,
            InvoiceDiscountAmount: 0,
            PointsDiscountAmount: 0,
            Total: total,
            Payments: [new ReceiptPayment("Tiền mặt", 300_000, null)],
            AmountPaid: 300_000,
            Change: 300_000 - total,
            VatBreakdown: [new VatSummary(8, total, vat, total - vat)],
            Member: member,
            Note: null,
            IsReprint: false);
    }
}
