using GroceryPOS.Application.Common;

namespace GroceryPOS.App.Forms.Shared;

/// <summary>Bridges application <see cref="Result"/>s to the UI library, which knows nothing about them.</summary>
internal static class ResultMessages
{
    /// <summary>Null on success, otherwise the Vietnamese error message (the contract of <c>BaseEditDialog.SaveAsync</c>).</summary>
    public static string? ErrorMessageOrNull(this Result result) => result.IsSuccess ? null : result.Error.Message;
}
