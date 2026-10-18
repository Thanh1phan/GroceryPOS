namespace GroceryPOS.UI.Controls.Theming;

/// <summary>
/// Holds the application-wide theme and pushes changes to the UI.
/// Modern controls in this library repaint themselves on <see cref="ThemeChanged"/>; plain WinForms controls
/// get default colors through <see cref="Apply(Control)"/> or, to follow later changes, <see cref="Track(Control)"/>.
/// </summary>
public static class ThemeManager
{
    private static Theme _current = Themes.Light;

    /// <summary>Raised on the thread that changed the theme (normally the UI thread).</summary>
    public static event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    public static Theme Current => _current;

    public static ThemeMode Mode => _current.Mode;

    public static void SetTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var previous = _current;
        if (ReferenceEquals(previous, theme))
        {
            return;
        }

        _current = theme;
        ThemeChanged?.Invoke(null, new ThemeChangedEventArgs(previous, theme));
    }

    public static void SetMode(ThemeMode mode) => SetTheme(Themes.For(mode));

    public static void ToggleMode() => SetMode(Mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);

    /// <summary>Applies the current theme to <paramref name="root"/> and all of its descendants.</summary>
    public static void Apply(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.SuspendLayout();
        try
        {
            ApplyCore(root, _current);
        }
        finally
        {
            root.ResumeLayout(true);
        }

        root.Invalidate(true);
    }

    /// <summary>
    /// Applies the theme now and again on every later change, until <paramref name="root"/> is disposed.
    /// Call once per form (BaseForm does this).
    /// </summary>
    public static void Track(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Apply(root);

        void OnChanged(object? sender, ThemeChangedEventArgs e)
        {
            if (root.IsDisposed || root.Disposing)
            {
                return;
            }

            if (root.InvokeRequired)
            {
                root.BeginInvoke(() => Apply(root));
            }
            else
            {
                Apply(root);
            }
        }

        ThemeChanged += OnChanged;
        root.Disposed += (_, _) => ThemeChanged -= OnChanged;
    }

    private static void ApplyCore(Control control, Theme theme)
    {
        if (control is IThemeable themeable)
        {
            themeable.ApplyTheme(theme);
            if (themeable.ThemesChildren)
            {
                return;
            }
        }
        else
        {
            ApplyDefault(control, theme);
        }

        foreach (Control child in control.Controls)
        {
            ApplyCore(child, theme);
        }
    }

    /// <summary>Default look for standard WinForms controls that have no themed counterpart yet.</summary>
    private static void ApplyDefault(Control control, Theme theme)
    {
        var c = theme.Colors;
        switch (control)
        {
            case Form form:
                form.BackColor = c.Background;
                form.ForeColor = c.TextPrimary;
                form.Font = ThemeFonts.Get(FontRole.Body);
                break;
            case TextBoxBase or ListControl or DateTimePicker or NumericUpDown:
                control.BackColor = control.Enabled ? c.Surface : c.DisabledBackground;
                control.ForeColor = c.TextPrimary;
                break;
            case DataGridView grid:
                grid.BackgroundColor = c.Surface;
                grid.GridColor = c.Border;
                grid.DefaultCellStyle.BackColor = c.Surface;
                grid.DefaultCellStyle.ForeColor = c.TextPrimary;
                grid.AlternatingRowsDefaultCellStyle.BackColor = c.SurfaceAlt;
                grid.ColumnHeadersDefaultCellStyle.BackColor = c.SurfaceAlt;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = c.TextSecondary;
                grid.EnableHeadersVisualStyles = false;
                break;
            case Label or CheckBox or RadioButton or GroupBox:
                control.ForeColor = c.TextPrimary;
                break;
            case Panel or TableLayoutPanel or FlowLayoutPanel or UserControl or SplitContainer:
                // Containers blend into their parent; transparent ones stay transparent.
                if (control.BackColor != Color.Transparent)
                {
                    control.BackColor = control.Parent?.BackColor ?? c.Background;
                }

                control.ForeColor = c.TextPrimary;
                break;
        }
    }
}
