using FluentValidation;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Partners;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Partners;

/// <summary>The add/edit supplier dialog as seen by its presenter. Field names are <see cref="SaveSupplierRequest"/> property names.</summary>
public interface ISupplierEditView : IView
{
    string Code { get; }

    string SupplierName { get; }

    string ContactPerson { get; }

    string Phone { get; }

    string Email { get; }

    string Address { get; }

    string TaxCode { get; }

    /// <summary>Moves focus to the field for <paramref name="field"/> (a <see cref="SaveSupplierRequest"/> property name).</summary>
    void FocusField(string field);
}

/// <summary>
/// Add/edit supplier logic. On add the code is pre-filled with the next <c>NCC0001</c> code (it may be overwritten);
/// on edit it is read-only. The debt balance only changes through receipts, payments and returns.
/// </summary>
public sealed class SupplierEditPresenter : Presenter<ISupplierEditView>
{
    public SupplierEditPresenter(IServiceScopeFactory scopeFactory)
        : base(scopeFactory)
    {
    }

    /// <summary>The supplier being edited, or null when adding.</summary>
    public SupplierDto? Supplier { get; private set; }

    public bool IsNew => Supplier is null;

    public string Title => IsNew ? "Thêm nhà cung cấp" : "Sửa nhà cung cấp";

    /// <summary>The code can only be chosen when adding.</summary>
    public bool CodeReadOnly => !IsNew;

    public decimal DebtBalance => Supplier?.DebtBalance ?? 0;

    /// <summary>Chooses what the dialog edits; call before the view is built.</summary>
    public SupplierEditPresenter For(SupplierDto? supplier)
    {
        Supplier = supplier;
        return this;
    }

    /// <summary>The next free supplier code when adding; null on edit or when it cannot be fetched (the service assigns one on save).</summary>
    public async Task<string?> SuggestCodeAsync(CancellationToken cancellationToken = default)
    {
        if (!IsNew)
        {
            return null;
        }

        var result = await UseAsync<ISupplierService, Result<string>>(suppliers => suppliers.SuggestCodeAsync(cancellationToken));
        return result.IsSuccess && ViewAlive ? result.Value : null;
    }

    /// <summary>Blank optional fields become null; text is trimmed except the name (validated as typed).</summary>
    public static SaveSupplierRequest BuildRequest(
        string? code, string? name, string? contactPerson, string? phone, string? email, string? address, string? taxCode) =>
        new(
            FormInput.Optional(code),
            name ?? string.Empty,
            FormInput.Optional(contactPerson),
            FormInput.Optional(phone),
            FormInput.Optional(email),
            FormInput.Optional(address),
            FormInput.Optional(taxCode));

    /// <summary>Validates and saves; returns the message for the dialog's error banner, or null when saved.</summary>
    public Task<string?> SaveAsync() => UseScopeAsync<string?>(async services =>
    {
        var request = BuildRequest(View.Code, View.SupplierName, View.ContactPerson, View.Phone, View.Email, View.Address, View.TaxCode);
        if (FormInput.FirstError(services.GetRequiredService<IValidator<SaveSupplierRequest>>(), request) is { } invalid)
        {
            View.FocusField(invalid.Field);
            return invalid.Message;
        }

        var suppliers = services.GetRequiredService<ISupplierService>();
        Result result = Supplier is null
            ? await suppliers.CreateAsync(request)
            : await suppliers.UpdateAsync(Supplier.Id, request);
        return result.IsSuccess ? null : result.Error.Message;
    });
}
