using System.Globalization;

namespace GroceryPOS.UI.Controls.Display.Core;

/// <summary>A formatted change against a previous period, e.g. "▲ 12,5%" in green.</summary>
public readonly record struct TrendInfo(string Text, BadgeKind Kind, string Tooltip);

/// <summary>Formats percentage changes for KPI cards (doanh thu hôm nay so với hôm qua…).</summary>
public static class Trend
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    /// <param name="percent">Change in percent (12.5 = +12,5%), or null when there is nothing to compare with.</param>
    /// <param name="higherIsBetter">False for figures where growth is bad (hàng trả lại, tiền hoàn).</param>
    /// <param name="comparedTo">Name of the previous period for the tooltip ("hôm qua", "tuần trước").</param>
    public static TrendInfo Of(decimal? percent, bool higherIsBetter = true, string comparedTo = "kỳ trước")
    {
        if (percent is null)
        {
            return new TrendInfo("—", BadgeKind.Neutral, $"Chưa có số liệu {comparedTo} để so sánh");
        }

        var value = Math.Round(percent.Value, 1, MidpointRounding.AwayFromZero);
        var magnitude = Math.Abs(value).ToString("#,##0.#", Vietnamese) + "%";

        if (value == 0)
        {
            return new TrendInfo("● 0%", BadgeKind.Neutral, $"Không đổi so với {comparedTo}");
        }

        var up = value > 0;
        var good = up == higherIsBetter;
        var text = (up ? "▲ " : "▼ ") + magnitude;
        var tooltip = $"{(up ? "Tăng" : "Giảm")} {magnitude} so với {comparedTo}";
        return new TrendInfo(text, good ? BadgeKind.Success : BadgeKind.Danger, tooltip);
    }
}
