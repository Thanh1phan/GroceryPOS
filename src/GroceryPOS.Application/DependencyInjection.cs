using FluentValidation;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Sales;
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
    /// <see cref="ICurrentUser"/>), store settings and every FluentValidation validator of this assembly.
    /// Use cases are scoped so they share the scope's unit of work with the repositories.
    /// </summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services, SalesOptions? sales = null, LockoutPolicy? lockout = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sales ?? SalesOptions.Default);
        services.TryAddSingleton(lockout ?? LockoutPolicy.Default);

        services.TryAddSingleton<IUserSessionStore, UserSessionStore>();
        services.TryAddSingleton<ICurrentUser>(sp => sp.GetRequiredService<IUserSessionStore>());

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IInvoiceNumberGenerator, InvoiceNumberGenerator>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IPosLookupService, PosLookupService>();
        services.AddScoped<IReturnService, ReturnService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IGoodsReceiptService, GoodsReceiptService>();
        services.AddScoped<ISupplierPaymentService, SupplierPaymentService>();
        services.AddScoped<ISupplierReturnService, SupplierReturnService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IPriceHistoryService, PriceHistoryService>();
        services.AddScoped<IProductImportExportAppService, ProductImportExportService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IRoleService, RoleService>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton, includeInternalTypes: false);
        return services;
    }
}
