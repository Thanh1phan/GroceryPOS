namespace GroceryPOS.UI.Controls.Theming;

public enum ButtonVariant
{
    /// <summary>Filled brand color – the main action of a screen or dialog.</summary>
    Primary,

    /// <summary>Outlined on the surface – cancel, close, secondary actions.</summary>
    Secondary,

    /// <summary>Filled red – destructive actions; always ask for confirmation.</summary>
    Danger,

    /// <summary>Text only – toolbar and inline actions.</summary>
    Ghost,
}

public enum InteractionState
{
    Normal,
    Hover,
    Pressed,
    Disabled,
}

public enum ValidationState
{
    None,
    Valid,
    Warning,
    Error,
}

public static class InteractionStates
{
    /// <summary>Disabled wins over pressed, pressed over hover.</summary>
    public static InteractionState From(bool enabled, bool hovered, bool pressed) =>
        !enabled ? InteractionState.Disabled
        : pressed ? InteractionState.Pressed
        : hovered ? InteractionState.Hover
        : InteractionState.Normal;
}
