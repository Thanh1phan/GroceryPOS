using GroceryPOS.Presentation.Catalog;
using GroceryPOS.Presentation.Partners;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation;

public static class DependencyInjection
{
    /// <summary>
    /// Registers presenters as transient: each screen or dialog gets its own presenter, which opens a
    /// DI scope per action, so presenters can be resolved from the root provider.
    /// </summary>
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<CategoryListPresenter>();
        services.AddTransient<CategoryEditPresenter>();
        services.AddTransient<SupplierListPresenter>();
        services.AddTransient<SupplierEditPresenter>();
        services.AddTransient<CustomerListPresenter>();
        services.AddTransient<CustomerEditPresenter>();
        return services;
    }
}
