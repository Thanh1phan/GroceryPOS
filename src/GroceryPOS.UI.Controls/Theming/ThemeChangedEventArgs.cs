namespace GroceryPOS.UI.Controls.Theming;

public sealed class ThemeChangedEventArgs(Theme previous, Theme current) : EventArgs
{
    public Theme Previous { get; } = previous;

    public Theme Current { get; } = current;
}
