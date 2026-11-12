using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Mvp;

/// <summary>
/// Base class for presenters: holds the attached view and runs each use case in its own DI scope
/// (one DbContext per action, matching how the screens worked before MVP).
/// </summary>
public abstract class Presenter<TView>
    where TView : class, IView
{
    private readonly IServiceScopeFactory _scopeFactory;
    private TView? _view;

    protected Presenter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <summary>The attached view; throws before <see cref="Attach"/> so wiring mistakes surface immediately.</summary>
    protected TView View => _view ?? throw new InvalidOperationException($"{GetType().Name} has no view attached.");

    /// <summary>True when a view is attached and still open.</summary>
    protected bool ViewAlive => _view is { IsAlive: true };

    /// <summary>Attaches the view (once). Views call this from their constructor.</summary>
    public void Attach(TView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_view is not null && !ReferenceEquals(_view, view))
        {
            throw new InvalidOperationException($"{GetType().Name} is already attached to another view.");
        }

        _view = view;
        OnAttached();
    }

    /// <summary>Called after <see cref="Attach"/>; push initial state to the view here.</summary>
    protected virtual void OnAttached()
    {
    }

    /// <summary>Resolves <typeparamref name="TService"/> in a fresh scope and runs <paramref name="work"/> with it.</summary>
    protected async Task<TResult> UseAsync<TService, TResult>(Func<TService, Task<TResult>> work)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(work);
        using var scope = _scopeFactory.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TService>()).ConfigureAwait(true);
    }

    /// <summary>Runs <paramref name="work"/> with the whole scoped provider (several services in one unit of work).</summary>
    protected async Task<TResult> UseScopeAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        using var scope = _scopeFactory.CreateScope();
        return await work(scope.ServiceProvider).ConfigureAwait(true);
    }
}
