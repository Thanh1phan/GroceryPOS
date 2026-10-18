namespace GroceryPOS.UI.Controls.Theming;

/// <summary>
/// Implemented by controls that style themselves. <see cref="ThemeManager.Apply(Control)"/> calls
/// <see cref="ApplyTheme"/> instead of applying default colors, and does not descend into the children
/// when <see cref="ThemesChildren"/> is true.
/// </summary>
public interface IThemeable
{
    void ApplyTheme(Theme theme);

    /// <summary>True when the control styles its own child controls (composite controls).</summary>
    bool ThemesChildren => false;
}
