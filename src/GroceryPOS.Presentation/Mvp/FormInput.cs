using FluentValidation;

namespace GroceryPOS.Presentation.Mvp;

/// <summary>Helpers edit presenters share: optional text and "first invalid field" validation.</summary>
public static class FormInput
{
    /// <summary>Trimmed text, or null when blank (optional fields are stored as null).</summary>
    public static string? Optional(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// Validates <paramref name="request"/>; null when valid, otherwise the first failing property name
    /// (e.g. <c>Phone</c>) and its message, so the dialog can focus that field and show the message.
    /// </summary>
    public static (string Field, string Message)? FirstError<T>(IValidator<T> validator, T request)
    {
        ArgumentNullException.ThrowIfNull(validator);
        var result = validator.Validate(request);
        if (result.IsValid)
        {
            return null;
        }

        var first = result.Errors[0];
        return (first.PropertyName, first.ErrorMessage);
    }
}
