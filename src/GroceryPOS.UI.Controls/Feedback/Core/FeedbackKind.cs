namespace GroceryPOS.UI.Controls.Feedback.Core;

/// <summary>Meaning of a toast or a message dialog; picks its icon and accent color.</summary>
public enum FeedbackKind
{
    /// <summary>Blue – neutral information ("Đã có bản cập nhật danh mục").</summary>
    Info,

    /// <summary>Green – an action finished ("Đã lưu sản phẩm", "Đã xuất Excel").</summary>
    Success,

    /// <summary>Amber – needs attention but nothing failed ("Sắp hết hàng", validation problems).</summary>
    Warning,

    /// <summary>Red – the action failed ("Không lưu được file").</summary>
    Error,

    /// <summary>Brand color – a question that needs an answer (dialogs only).</summary>
    Question,

    /// <summary>Red – confirmation of a destructive or irreversible action (dialogs only).</summary>
    Danger,
}
