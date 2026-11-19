namespace GroceryPOS.Presentation.Mvp;

/// <summary>
/// Semantic color of a piece of text a presenter asks the view to show (status cells, preview notes).
/// Views map it to their theme colors; presenters stay free of any UI type.
/// </summary>
public enum Tone
{
    Normal,
    Muted,

    /// <summary>Informational accent (e.g. a draft that still needs finishing).</summary>
    Info,
    Success,
    Warning,
    Danger,
}
