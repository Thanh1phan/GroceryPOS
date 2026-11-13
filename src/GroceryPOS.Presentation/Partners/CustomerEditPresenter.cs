using FluentValidation;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Partners;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Partners;

/// <summary>The add/edit customer dialog as seen by its presenter. Field names are <see cref="SaveCustomerRequest"/> property names.</summary>
public interface ICustomerEditView : IView
{
    string CustomerName { get; }

    string Phone { get; }

    string Email { get; }

    string Address { get; }

    /// <summary>Moves focus to the field for <paramref name="field"/> (a <see cref="SaveCustomerRequest"/> property name).</summary>
    void FocusField(string field);
}

/// <summary>
/// Add/edit customer (loyalty member) logic. The phone number is how the cashier finds the member at the counter,
/// so the service keeps it unique; points and total spent are read-only (only sales, voids and returns change them).
/// </summary>
public sealed class CustomerEditPresenter : Presenter<ICustomerEditView>
{
    public CustomerEditPresenter(IServiceScopeFactory scopeFactory)
        : base(scopeFactory)
    {
    }

    /// <summary>The customer being edited, or null when adding.</summary>
    public CustomerDto? Customer { get; private set; }

    public bool IsNew => Customer is null;

    public string Title => Customer is null ? "Thêm khách hàng" : $"Sửa khách hàng – {Customer.Code}";

    /// <summary>Points and total spent are only shown for an existing customer.</summary>
    public bool ShowsLoyalty => !IsNew;

    /// <summary>Chooses what the dialog edits; call before the view is built.</summary>
    public CustomerEditPresenter For(CustomerDto? customer)
    {
        Customer = customer;
        return this;
    }

    /// <summary>Blank optional fields become null; optional text is trimmed.</summary>
    public static SaveCustomerRequest BuildRequest(string? name, string? phone, string? email, string? address) =>
        new(name ?? string.Empty, FormInput.Optional(phone), FormInput.Optional(email), FormInput.Optional(address));

    /// <summary>Validates and saves; returns the message for the dialog's error banner, or null when saved.</summary>
    public Task<string?> SaveAsync() => UseScopeAsync<string?>(async services =>
    {
        var request = BuildRequest(View.CustomerName, View.Phone, View.Email, View.Address);
        if (FormInput.FirstError(services.GetRequiredService<IValidator<SaveCustomerRequest>>(), request) is { } invalid)
        {
            View.FocusField(invalid.Field);
            return invalid.Message;
        }

        var customers = services.GetRequiredService<ICustomerService>();
        Result result = Customer is null
            ? await customers.CreateAsync(request)
            : await customers.UpdateAsync(Customer.Id, request);
        return result.IsSuccess ? null : result.Error.Message;
    });
}
