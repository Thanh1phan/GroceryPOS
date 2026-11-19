namespace GroceryPOS.App.Forms.Shared;

/// <summary>Layout pieces shared by the main-window screens.</summary>
internal static class ScreenLayout
{
    /// <summary>
    /// Row of action buttons docked at the top of a screen, aligned with the 24 px gutter. It wraps onto a second
    /// line on narrow windows instead of pushing the KPI cards or the buttons themselves off screen.
    /// </summary>
    public static FlowLayoutPanel Toolbar(params Control[] buttons)
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(16, 16, 24, 8) };
        bar.Controls.AddRange(buttons);
        return bar;
    }
}
