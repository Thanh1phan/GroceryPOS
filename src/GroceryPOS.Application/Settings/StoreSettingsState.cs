using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;

namespace GroceryPOS.Application.Settings;

/// <summary>
/// Process-wide holder of the settings in force. Starts from the appsettings.json defaults, is overlaid with the
/// database values at start-up (<see cref="ISettingsService.ReloadAsync"/>) and replaced after each save. The DI
/// container resolves <see cref="StoreProfile"/> and <see cref="SalesOptions"/> from here per scope, so the next
/// use case or screen opened after a save sees the new values.
/// </summary>
public sealed class StoreSettingsState
{
    private readonly object _gate = new();
    private Snapshot _current;

    public StoreSettingsState(StoreProfile? defaultStore = null, SalesOptions? defaultSales = null)
    {
        DefaultStore = defaultStore ?? StoreProfile.Default;
        DefaultSales = defaultSales ?? SalesOptions.Default;
        Defaults = StoreSettings.From(DefaultStore, DefaultSales);
        _current = new Snapshot(DefaultStore, DefaultSales, Defaults);
    }

    /// <summary>Raised after <see cref="Apply"/> changed the settings (on the caller's thread).</summary>
    public event EventHandler? Changed;

    /// <summary>Values from appsettings.json – what "Khôi phục mặc định" goes back to.</summary>
    public StoreSettings Defaults { get; }

    public StoreProfile DefaultStore { get; }

    public SalesOptions DefaultSales { get; }

    public StoreProfile Store => Volatile.Read(ref _current).Store;

    public SalesOptions Sales => Volatile.Read(ref _current).Sales;

    public StoreSettings Settings => Volatile.Read(ref _current).Settings;

    /// <summary>Makes <paramref name="settings"/> the values in force (applied on top of the appsettings defaults).</summary>
    public void Apply(StoreSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        bool changed;
        lock (_gate)
        {
            changed = normalized != _current.Settings;
            Volatile.Write(ref _current, new Snapshot(normalized.ApplyTo(DefaultStore), normalized.ApplyTo(DefaultSales), normalized));
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed record Snapshot(StoreProfile Store, SalesOptions Sales, StoreSettings Settings);
}
