using System.Globalization;
using System.Text;

namespace GroceryPOS.UI.Controls.Feedback.Core;

/// <summary>Answer chosen in a message dialog (maps 1:1 to WinForms <c>DialogResult</c>).</summary>
public enum DialogChoice
{
    None,
    Ok,
    Cancel,
    Yes,
    No,
    Retry,
    Abort,
    Ignore,
}

/// <summary>Standard button sets (mirror <c>MessageBoxButtons</c>).</summary>
public enum DialogButtonSet
{
    Ok,
    OkCancel,
    YesNo,
    YesNoCancel,
    RetryCancel,
    AbortRetryIgnore,
}

public enum DialogButtonStyle
{
    Primary,
    Secondary,
    Danger,
}

/// <param name="AccessKey">Key that presses the button without Alt ('C' for "Có"); '\0' for none.</param>
public sealed record DialogButtonSpec(string Text, DialogChoice Choice, DialogButtonStyle Style, char AccessKey);

/// <summary>
/// Everything a themed message dialog shows: kind (icon/color), title, message, optional technical details
/// and its buttons, plus which button Enter presses (<see cref="DefaultChoice"/>) and what Esc / the close
/// box answers (<see cref="CancelChoice"/>). Built by the factory methods; UI-free and tested.
/// </summary>
public sealed record DialogSpec
{
    public const int MaxButtons = 4;

    private DialogSpec(FeedbackKind kind, string title, string message, string? details, IReadOnlyList<DialogButtonSpec> buttons, DialogChoice defaultChoice, DialogChoice cancelChoice)
    {
        if (buttons.Count is 0 or > MaxButtons)
        {
            throw new ArgumentException($"Hộp thoại cần từ 1 đến {MaxButtons} nút.", nameof(buttons));
        }

        if (buttons.Select(b => b.Choice).Distinct().Count() != buttons.Count || buttons.Any(b => b.Choice == DialogChoice.None))
        {
            throw new ArgumentException("Mỗi nút phải có một kết quả riêng.", nameof(buttons));
        }

        if (buttons.All(b => b.Choice != defaultChoice) || buttons.All(b => b.Choice != cancelChoice))
        {
            throw new ArgumentException("Nút mặc định và nút hủy phải nằm trong danh sách nút.", nameof(defaultChoice));
        }

        Kind = kind;
        Title = string.IsNullOrWhiteSpace(title) ? "GroceryPOS" : title.Trim();
        Message = message?.Trim() ?? string.Empty;
        Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        Buttons = buttons;
        DefaultChoice = defaultChoice;
        CancelChoice = cancelChoice;
    }

    public FeedbackKind Kind { get; }

    public string Title { get; }

    public string Message { get; }

    /// <summary>Technical text (exception, file path) behind a "Chi tiết" toggle; included when copying.</summary>
    public string? Details { get; init; }

    /// <summary>Buttons in display order, left to right.</summary>
    public IReadOnlyList<DialogButtonSpec> Buttons { get; }

    public DialogChoice DefaultChoice { get; }

    public DialogChoice CancelChoice { get; }

    /// <summary>A dialog with one of the standard button sets (what <c>MessageBox.Show</c> would show).</summary>
    /// <param name="defaultIndex">Zero-based index of the button Enter presses (clamped to the set).</param>
    public static DialogSpec Standard(FeedbackKind kind, string title, string message, DialogButtonSet buttons = DialogButtonSet.Ok, int defaultIndex = 0, string? details = null)
    {
        var choices = ChoicesOf(buttons);
        var list = choices
            .Select((choice, i) => Button(LabelOf(choice), choice, i == 0 ? FirstButtonStyle(kind) : DialogButtonStyle.Secondary))
            .ToArray();
        var defaultChoice = choices[Math.Clamp(defaultIndex, 0, choices.Length - 1)];
        return new DialogSpec(kind, title, message, details, WithAccessKeys(list), defaultChoice, CancelOf(buttons));
    }

    /// <summary>Single "Đồng ý" button.</summary>
    public static DialogSpec Alert(FeedbackKind kind, string title, string message, string? details = null) =>
        Standard(kind, title, message, DialogButtonSet.Ok, 0, details);

    /// <summary>
    /// Two-button confirmation with action-specific wording ("Xóa" / "Hủy"). Destructive confirmations
    /// (<paramref name="danger"/>) use a red button and put the default on the safe answer, so a reflexive
    /// Enter never deletes anything.
    /// </summary>
    public static DialogSpec Confirm(string title, string message, string confirmText = "Đồng ý", string cancelText = "Hủy", bool danger = false, string? details = null)
    {
        var buttons = WithAccessKeys(new[]
        {
            Button(string.IsNullOrWhiteSpace(confirmText) ? "Đồng ý" : confirmText.Trim(), DialogChoice.Yes, danger ? DialogButtonStyle.Danger : DialogButtonStyle.Primary),
            Button(string.IsNullOrWhiteSpace(cancelText) ? "Hủy" : cancelText.Trim(), DialogChoice.No, DialogButtonStyle.Secondary),
        });
        return new DialogSpec(danger ? FeedbackKind.Danger : FeedbackKind.Question, title, message, details, buttons, danger ? DialogChoice.No : DialogChoice.Yes, DialogChoice.No);
    }

    /// <summary>Button pressed by a plain key press (no Alt), or null. Handles the button's access key plus Y/N for Có/Không.</summary>
    public DialogChoice? ChoiceForKey(char key)
    {
        var upper = char.ToUpperInvariant(key);
        if (upper == '\0' || char.IsWhiteSpace(upper))
        {
            return null;
        }

        var match = Buttons.FirstOrDefault(b => b.AccessKey == upper);
        if (match is not null)
        {
            return match.Choice;
        }

        var alias = upper switch
        {
            'Y' => DialogChoice.Yes,
            'N' => DialogChoice.No,
            _ => DialogChoice.None,
        };
        return alias != DialogChoice.None && Buttons.Any(b => b.Choice == alias) ? alias : null;
    }

    /// <summary>Plain-text copy in the same layout as Ctrl+C on a Windows message box.</summary>
    public string ToClipboardText()
    {
        const string rule = "---------------------------";
        var sb = new StringBuilder();
        sb.AppendLine(rule).AppendLine(Title).AppendLine(rule).AppendLine(Message);
        if (Details is not null)
        {
            sb.AppendLine().AppendLine(Details);
        }

        sb.AppendLine(rule).AppendLine(string.Join("   ", Buttons.Select(b => b.Text))).Append(rule);
        return sb.ToString();
    }

    public static string LabelOf(DialogChoice choice) => choice switch
    {
        DialogChoice.Ok => "Đồng ý",
        DialogChoice.Cancel => "Hủy",
        DialogChoice.Yes => "Có",
        DialogChoice.No => "Không",
        DialogChoice.Retry => "Thử lại",
        DialogChoice.Abort => "Dừng",
        DialogChoice.Ignore => "Bỏ qua",
        _ => string.Empty,
    };

    private static DialogChoice[] ChoicesOf(DialogButtonSet set) => set switch
    {
        DialogButtonSet.OkCancel => new[] { DialogChoice.Ok, DialogChoice.Cancel },
        DialogButtonSet.YesNo => new[] { DialogChoice.Yes, DialogChoice.No },
        DialogButtonSet.YesNoCancel => new[] { DialogChoice.Yes, DialogChoice.No, DialogChoice.Cancel },
        DialogButtonSet.RetryCancel => new[] { DialogChoice.Retry, DialogChoice.Cancel },
        DialogButtonSet.AbortRetryIgnore => new[] { DialogChoice.Abort, DialogChoice.Retry, DialogChoice.Ignore },
        _ => new[] { DialogChoice.Ok },
    };

    /// <summary>Esc / close box: the "no harm done" answer of each set.</summary>
    private static DialogChoice CancelOf(DialogButtonSet set) => set switch
    {
        DialogButtonSet.OkCancel or DialogButtonSet.YesNoCancel or DialogButtonSet.RetryCancel => DialogChoice.Cancel,
        DialogButtonSet.YesNo => DialogChoice.No,
        DialogButtonSet.AbortRetryIgnore => DialogChoice.Abort,
        _ => DialogChoice.Ok,
    };

    private static DialogButtonStyle FirstButtonStyle(FeedbackKind kind) =>
        kind == FeedbackKind.Danger ? DialogButtonStyle.Danger : DialogButtonStyle.Primary;

    private static DialogButtonSpec Button(string text, DialogChoice choice, DialogButtonStyle style) => new(text, choice, style, '\0');

    /// <summary>First unaccented letter of each label, skipping letters already taken by an earlier button.</summary>
    private static DialogButtonSpec[] WithAccessKeys(IReadOnlyList<DialogButtonSpec> buttons)
    {
        var taken = new HashSet<char>();
        var result = new DialogButtonSpec[buttons.Count];
        for (var i = 0; i < buttons.Count; i++)
        {
            var key = Unaccent(buttons[i].Text).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).FirstOrDefault(c => !taken.Contains(c));
            if (key != '\0')
            {
                taken.Add(key);
            }

            result[i] = buttons[i] with { AccessKey = key };
        }

        return result;
    }

    private static string Unaccent(string text)
    {
        var decomposed = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }
}
