using System.Drawing;
using System.Globalization;

namespace GroceryPOS.UI.Controls.Display.Core;

/// <summary>One bar of a bar chart: axis label ("T2 26/10", "14h"), value and an optional tooltip.</summary>
/// <param name="Label">Category label under the bar.</param>
/// <param name="Value">Bar height in data units (negative values are drawn as zero).</param>
/// <param name="Tooltip">Hover text; when null the chart shows "label: formatted value".</param>
public sealed record ChartPoint(string Label, decimal Value, string? Tooltip = null);

/// <summary>A value axis: 0 … <see cref="Max"/> in <see cref="Step"/> increments ("nice" numbers 1/2/2,5/5 × 10ⁿ).</summary>
public sealed record ChartAxis(decimal Max, decimal Step)
{
    /// <summary>Gridline values from 0 up to and including <see cref="Max"/>.</summary>
    public IReadOnlyList<decimal> Ticks
    {
        get
        {
            var ticks = new List<decimal>();
            for (var v = 0m; v <= Max; v += Step)
            {
                ticks.Add(v);
            }

            return ticks;
        }
    }
}

/// <summary>
/// UI-free maths for the dashboard and report charts: a readable value axis, compact VND labels
/// ("850k", "1,2tr", "2,5 tỷ"), bar geometry and which category labels fit.
/// </summary>
public static class ChartScale
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly decimal[] NiceFactors = [1m, 2m, 2.5m, 5m, 10m];

    /// <summary>
    /// Axis covering <paramref name="maxValue"/> with about <paramref name="targetTicks"/> intervals. An empty or
    /// all-zero series still gets an axis (0 … 4 in steps of 1) so the gridlines are drawn.
    /// </summary>
    public static ChartAxis NiceAxis(decimal maxValue, int targetTicks = 4)
    {
        targetTicks = Math.Max(1, targetTicks);
        if (maxValue <= 0)
        {
            return new ChartAxis(targetTicks, 1);
        }

        var rough = maxValue / targetTicks;
        var magnitude = 1m;
        while (magnitude * 10 <= rough)
        {
            magnitude *= 10;
        }

        while (magnitude > rough && magnitude > 0.0001m)
        {
            magnitude /= 10;
        }

        var step = NiceFactors.Select(f => f * magnitude).First(s => s >= rough);
        var max = Math.Ceiling(maxValue / step) * step;
        return new ChartAxis(max, step);
    }

    /// <summary>
    /// Short money label for axes and bar tops: 0, 500, 850k, 1,2tr, 15tr, 2,5 tỷ (vi-VN decimals, at most one
    /// decimal place). Negative amounts keep their sign.
    /// </summary>
    public static string CompactVnd(decimal amount)
    {
        var sign = amount < 0 ? "-" : string.Empty;
        var value = Math.Abs(amount);
        return sign + value switch
        {
            >= 1_000_000_000m => Short(value / 1_000_000_000m) + " tỷ",
            >= 1_000_000m => Short(value / 1_000_000m) + "tr",
            >= 1_000m => Short(value / 1_000m) + "k",
            _ => Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("#,0", Vietnamese),
        };
    }

    /// <summary>
    /// Bar rectangles inside <paramref name="plot"/>: equal slots, each bar <paramref name="barRatio"/> of its slot
    /// (capped at <paramref name="maxBarWidth"/>), centred, height proportional to value / <paramref name="axisMax"/>.
    /// Zero and negative values give a zero-height bar sitting on the baseline.
    /// </summary>
    public static IReadOnlyList<RectangleF> Bars(
        RectangleF plot,
        IReadOnlyList<decimal> values,
        decimal axisMax,
        float barRatio = 0.6f,
        float maxBarWidth = float.MaxValue)
    {
        if (values.Count == 0 || plot.Width <= 0 || plot.Height <= 0)
        {
            return [];
        }

        var slot = plot.Width / values.Count;
        var width = Math.Min(slot * Math.Clamp(barRatio, 0.05f, 1f), maxBarWidth);
        var bars = new RectangleF[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var fraction = axisMax <= 0 ? 0f : (float)Math.Clamp(values[i] / axisMax, 0m, 1m);
            var height = plot.Height * fraction;
            var x = plot.Left + (slot * i) + ((slot - width) / 2f);
            bars[i] = new RectangleF(x, plot.Bottom - height, width, height);
        }

        return bars;
    }

    /// <summary>
    /// Show every n-th category label so labels of <paramref name="labelWidth"/> pixels don't overlap in slots of
    /// <paramref name="slotWidth"/> pixels (1 = every label).
    /// </summary>
    public static int LabelEvery(float slotWidth, float labelWidth, float gap = 6f)
    {
        if (slotWidth <= 0)
        {
            return int.MaxValue;
        }

        return Math.Max(1, (int)Math.Ceiling((labelWidth + gap) / slotWidth));
    }

    /// <summary>Index of the bar whose slot contains <paramref name="x"/>, or -1 outside the plot.</summary>
    public static int HitTest(RectangleF plot, int count, float x)
    {
        if (count <= 0 || x < plot.Left || x >= plot.Right)
        {
            return -1;
        }

        return Math.Min(count - 1, (int)((x - plot.Left) / (plot.Width / count)));
    }

    private static string Short(decimal value)
    {
        var rounded = Math.Round(value, value >= 100 ? 0 : 1, MidpointRounding.AwayFromZero);
        return rounded.ToString("#,0.#", Vietnamese);
    }
}
