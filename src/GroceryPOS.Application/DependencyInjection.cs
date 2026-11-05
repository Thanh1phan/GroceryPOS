using FluentValidation;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Auditing;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Reports.Export;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Settings;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Application.Users;
using GroceryPOS.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GroceryPOS.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use cases, the desktop session (singleton <see cref="IUserSessionStore"/>, also exposed as
    /// <see cref="ICurrentUser"/>), live store settings (<see cref="StoreSettingsState"/>) and every FluentValidation validator of this assembly.
    /// Use cases are scoped so they share the scope's unit of work with the repositories.
    /// </summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services, SalesOptions? sales = null, LockoutPolicy? lockout = null, StoreProfile? store = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(lockout ?? LockoutPolicy.Default);

        // Store profile and sales options come from the live settings (appsettings.json defaults + values saved on
        // the Cấu hình screen); resolved per use, so a scope created after a save sees the new values.
        services.TryAddSingleton(new StoreSettingsState(store, sales));
        services.TryAddTransient(sp => sp.GetRequiredService<StoreSettingsState>().Store);
        services.TryAddTransient(sp => sp.GetRequiredService<StoreSettingsState>().Sales);

        services.TryAddSingleton<IUserSessionStore, UserSessionStore>();
        services.TryAddSingleton<ICurrentUser>(sp => sp.GetRequiredService<IUserSessionStore>());

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IInvoiceNumberGenerator, InvoiceNumberGenerator>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IPosLookupService, PosLookupService>();
        services.AddScoped<IParkedOrderService, ParkedOrderService>();
        services.AddScoped<IReceiptService, ReceiptService>();
        services.AddScoped<IReturnService, ReturnService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IGoodsReceiptService, GoodsReceiptService>();
        services.AddScoped<ISupplierPaymentService, SupplierPaymentService>();
        services.AddScoped<ISupplierReturnService, SupplierReturnService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IReportExportService, ReportExportService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IPriceHistoryService, PriceHistoryService>();
        services.AddScoped<IProductImportExportAppService, ProductImportExportService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ISettingsService, SettingsService>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton, includeInternalTypes: false);
        return services;
    }
}
