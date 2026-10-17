using FluentValidation;
using FluentValidation.Results;
using GroceryPOS.Application.Common;

namespace GroceryPOS.Application.Validation;

/// <summary>Bridges FluentValidation results to the application's <see cref="Result"/> pattern and to per-field UI errors.</summary>
public static class ValidationExtensions
{
    /// <summary>First message as a <c>validation</c> error (what a toast or message box shows).</summary>
    public static Error ToError(this ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsValid ? Error.None : Error.Validation(result.Errors[0].ErrorMessage);
    }

    public static Result ToResult(this ValidationResult result) =>
        result.IsValid ? Result.Success() : Result.Failure(result.ToError());

    /// <summary>
    /// Messages grouped by property path (e.g. <c>Lines[2].Quantity</c>), first message per path first,
    /// so edit dialogs can put each text box into its error state.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> ToFieldErrors(this ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Errors
            .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray(), StringComparer.Ordinal);
    }

    /// <summary>Validates and converts to a <see cref="Result"/> in one call (presenters: <c>if (validator.Check(input) is { IsFailure: true } r) ...</c>).</summary>
    public static Result Check<T>(this IValidator<T> validator, T instance)
    {
        ArgumentNullException.ThrowIfNull(validator);
        return validator.Validate(instance).ToResult();
    }
}

/// <summary>Shared rules for VND amounts and free-text fields.</summary>
public static class RuleExtensions
{
    public const int NoteMaxLength = 500;
    public const int ReasonMaxLength = 200;
    public const int ReferenceMaxLength = 100;

    /// <summary>Whole đồng (no fractional VND), not negative.</summary>
    public static IRuleBuilderOptions<T, decimal> Money<T>(this IRuleBuilder<T, decimal> rule, string label) =>
        rule.GreaterThanOrEqualTo(0).WithMessage($"{label} không được âm.")
            .Must(IsWholeDong).WithMessage($"{label} phải là số tiền chẵn đồng.");

    /// <summary>Whole đồng and strictly positive.</summary>
    public static IRuleBuilderOptions<T, decimal> PositiveMoney<T>(this IRuleBuilder<T, decimal> rule, string label) =>
        rule.GreaterThan(0).WithMessage($"{label} phải lớn hơn 0.")
            .Must(IsWholeDong).WithMessage($"{label} phải là số tiền chẵn đồng.");

    public static IRuleBuilderOptions<T, decimal> PositiveQuantity<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0).WithMessage("Số lượng phải lớn hơn 0.");

    public static IRuleBuilderOptions<T, string?> OptionalText<T>(this IRuleBuilder<T, string?> rule, string label, int maxLength = NoteMaxLength) =>
        rule.MaximumLength(maxLength).WithMessage($"{label} tối đa {maxLength} ký tự.");

    public static IRuleBuilderOptions<T, string?> RequiredText<T>(this IRuleBuilder<T, string?> rule, string message, string label, int maxLength = ReasonMaxLength) =>
        rule.Must(s => !string.IsNullOrWhiteSpace(s)).WithMessage(message)
            .MaximumLength(maxLength).WithMessage($"{label} tối đa {maxLength} ký tự.");

    public static IRuleBuilderOptions<T, int> Id<T>(this IRuleBuilder<T, int> rule, string message) =>
        rule.GreaterThan(0).WithMessage(message);

    /// <summary>Fails when two items share the same key (e.g. the same product twice).</summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<TItem>> UniqueBy<T, TItem, TKey>(
        this IRuleBuilder<T, IReadOnlyList<TItem>> rule, Func<TItem, TKey> key, string message) =>
        rule.Must(items => items is null || items.Select(key).Distinct().Count() == items.Count).WithMessage(message);

    internal static bool IsWholeDong(decimal amount) => decimal.Truncate(amount) == amount;
}
