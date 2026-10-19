namespace GroceryPOS.UI.Controls.Inputs.Core;

/// <summary>
/// Rules for a numeric input: precision, range and shortcuts. Separators always follow vi-VN
/// ("1.250.000,5") regardless of the operating-system culture, so receipts and screens match.
/// </summary>
public sealed record NumericFormat
{
    /// <summary>Largest value an input accepts by default (999 tỷ) – keeps every value well inside <see cref="decimal"/>.</summary>
    public const decimal DefaultMaximum = 999_999_999_999m;

    private readonly int _decimalPlaces;

    /// <summary>Digits after the decimal comma, 0–6. Typed values are rounded half away from zero.</summary>
    public int DecimalPlaces
    {
        get => _decimalPlaces;
        init => _decimalPlaces = value is >= 0 and <= 6
            ? value
            : throw new ArgumentOutOfRangeException(nameof(DecimalPlaces), value, "Số chữ số thập phân phải từ 0 đến 6.");
    }

    public decimal Minimum { get; init; }

    public decimal Maximum { get; init; } = DefaultMaximum;

    /// <summary>Lets cashiers type "25k" for 25.000.</summary>
    public bool AllowThousandsShortcut { get; init; }

    public bool AllowNegative => Minimum < 0;

    /// <summary>Whole numbers from 0 (số lượng cái, điểm).</summary>
    public static NumericFormat Integer { get; } = new();

    /// <summary>Whole đồng from 0, with the "k" shortcut.</summary>
    public static NumericFormat Currency { get; } = new() { AllowThousandsShortcut = true };

    /// <summary>Weighed goods: up to 3 decimals (kg).</summary>
    public static NumericFormat Quantity { get; } = new() { DecimalPlaces = 3 };

    /// <summary>Percentages 0–100 with up to 2 decimals (chiết khấu %).</summary>
    public static NumericFormat Percent { get; } = new() { DecimalPlaces = 2, Maximum = 100 };
}
