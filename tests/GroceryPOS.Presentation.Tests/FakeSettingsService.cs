using GroceryPOS.Application.Common;
using GroceryPOS.Application.Settings;

namespace GroceryPOS.Presentation.Tests;

/// <summary>In-memory settings store: <see cref="Stored"/> is what another counter would see.</summary>
internal sealed class FakeSettingsService : ISettingsService
{
    public StoreSettings Stored { get; set; } = new() { StoreName = "Tạp hóa Cô Ba", Phone = "0909123456" };
    public StoreSettings Defaults { get; set; } = new();
    public Error? FailGetWith { get; set; }
    public Error? FailSaveWith { get; set; }
    public List<StoreSettings> SavedRequests { get; } = new();
    public int Gets { get; private set; }

    public Task<Result<StoreSettings>> GetAsync(CancellationToken cancellationToken = default)
    {
        Gets++;
        return Task.FromResult(FailGetWith is { } e ? Result.Failure<StoreSettings>(e) : Result.Success(Stored));
    }

    public Result<StoreSettings> GetDefaults() => Defaults;

    public Task<Result<SettingsSaveResult>> SaveAsync(StoreSettings settings, CancellationToken cancellationToken = default)
    {
        SavedRequests.Add(settings);
        if (FailSaveWith is { } e)
        {
            return Task.FromResult(Result.Failure<SettingsSaveResult>(e));
        }

        var next = settings.Normalize();
        var changes = StoreSettingsCodec.Diff(Stored, next);
        Stored = next;
        return Task.FromResult(Result.Success(new SettingsSaveResult(next, changes)));
    }

    public Task<StoreSettings> ReloadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Stored);
}
