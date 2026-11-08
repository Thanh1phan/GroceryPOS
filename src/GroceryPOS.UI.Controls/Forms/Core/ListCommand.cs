namespace GroceryPOS.UI.Controls.Forms.Core;

/// <summary>What a toolbar button of a list screen does when the user lacks the permission for it.</summary>
public enum UnauthorizedBehavior
{
    /// <summary>Shown but disabled (default: the user sees that the action exists).</summary>
    Disable,

    /// <summary>Not shown at all.</summary>
    Hide,
}

/// <summary>Resolved look of a command for the current selection.</summary>
public readonly record struct CommandState(bool Visible, bool Enabled, string Text);

/// <summary>
/// The rules of one toolbar command on a list screen: permission, whether it needs a selected row, an extra
/// condition on that row, and a text that may depend on it ("Ngừng dùng" / "Dùng lại"). The WinForms
/// <c>BaseListView</c> re-evaluates every command when the selection or the rows change.
/// </summary>
public sealed class ListCommand<TItem>
    where TItem : class
{
    public ListCommand(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text;
    }

    public string Text { get; }

    /// <summary>False when the current user lacks the permission (see <see cref="WhenUnauthorized"/>).</summary>
    public bool Allowed { get; init; } = true;

    public UnauthorizedBehavior WhenUnauthorized { get; init; } = UnauthorizedBehavior.Disable;

    /// <summary>True when the command acts on the selected row and is disabled without one.</summary>
    public bool RequiresSelection { get; init; }

    /// <summary>Extra condition on the selected row (only evaluated when a row is selected).</summary>
    public Func<TItem, bool>? EnabledWhen { get; init; }

    /// <summary>Text for the selected row; <see cref="Text"/> when null or when nothing is selected.</summary>
    public Func<TItem, string>? TextFor { get; init; }

    /// <summary>Longest text <see cref="TextFor"/> can return; sizes the button so it does not jump. Defaults to <see cref="Text"/>.</summary>
    public string? LongestText { get; init; }

    /// <summary>Text used to size the button: the longer of <see cref="Text"/> and <see cref="LongestText"/>.</summary>
    public string SizingText => LongestText is { Length: > 0 } longest && longest.Length > Text.Length ? longest : Text;

    /// <summary>Extra condition that does not depend on the row (e.g. "the screen is not busy").</summary>
    public Func<bool>? Available { get; init; }

    public CommandState Evaluate(TItem? selected)
    {
        var visible = Allowed || WhenUnauthorized == UnauthorizedBehavior.Disable;
        var text = selected is not null && TextFor is not null ? TextFor(selected) : Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = Text;
        }

        var enabled = Allowed
            && (Available?.Invoke() ?? true)
            && (selected is not null || !RequiresSelection)
            && (selected is null || EnabledWhen is null || EnabledWhen(selected));
        return new CommandState(visible, visible && enabled, text);
    }
}
