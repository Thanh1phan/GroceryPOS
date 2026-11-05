using FluentValidation;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Validation;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Settings;

namespace GroceryPOS.Application.Settings;

public interface ISettingsService
{
    /// <summary>Settings in force, for the Cấu hình screen (<c>settings.manage</c>).</summary>
    Task<Result<StoreSettings>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>The appsettings.json values that "Khôi phục mặc định" restores (<c>settings.manage</c>).</summary>
    Result<StoreSettings> GetDefaults();

    /// <summary>
    /// Validates and stores the settings (only changed keys are written), audits the changes as
    /// <c>settings.changed</c> and puts them in force at once (<c>settings.manage</c>).
    /// </summary>
    Task<Result<SettingsSaveResult>> SaveAsync(StoreSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Loads the stored settings into <see cref="StoreSettingsState"/>; called at start-up, before anyone signs in.</summary>
    Task<StoreSettings> ReloadAsync(CancellationToken cancellationToken = default);
}

public static class SettingsErrors
{
    public static readonly Error Denied = Error.Forbidden("Bạn không có quyền thay đổi cấu hình hệ thống.");
}

/// <summary>Reads and saves the store-wide settings (cửa hàng, hóa đơn, tích điểm, quy tắc bán hàng).</summary>
public sealed class SettingsService : ISettingsService
{
    private readonly IAppSettingRepository _settings;
    private readonly StoreSettingsState _state;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<StoreSettings> _validator;

    public SettingsService(
        IAppSettingRepository settings,
        StoreSettingsState state,
        ICurrentUser currentUser,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        IValidator<StoreSettings>? validator = null)
    {
        _settings = settings;
        _state = state;
        _currentUser = currentUser;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _validator = validator ?? new StoreSettingsValidator();
    }

    public async Task<Result<StoreSettings>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } error)
        {
            return error;
        }

        // Re-read so a change saved on another counter (shared SQL Server database) shows up.
        return await ReloadAsync(cancellationToken);
    }

    public Result<StoreSettings> GetDefaults() =>
        Authorize() is { } error ? error : _state.Defaults;

    public async Task<Result<SettingsSaveResult>> SaveAsync(StoreSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (Authorize() is { } error)
        {
            return error;
        }

        var next = settings.Normalize();
        if (_validator.Check(next) is { IsFailure: true } invalid)
        {
            return invalid.Error;
        }

        var rows = (await _settings.ListAsync(cancellationToken)).ToDictionary(r => r.Key, StringComparer.Ordinal);
        var before = StoreSettingsCodec.Read(rows.ToDictionary(r => r.Key, r => r.Value.Value, StringComparer.Ordinal), _state.Defaults);
        var changes = StoreSettingsCodec.Diff(before, next);
        if (changes.Count == 0)
        {
            _state.Apply(before);
            return new SettingsSaveResult(before, changes);
        }

        foreach (var (key, value) in StoreSettingsCodec.ToValues(next))
        {
            if (rows.TryGetValue(key, out var row))
            {
                row.SetValue(value);
            }
            else
            {
                _settings.Add(new AppSetting(key, value));
            }
        }

        _audit.Record(
            AuditActions.SettingsChanged,
            "Settings",
            string.Join(", ", changes.Select(c => c.Section).Distinct()),
            StoreSettingsCodec.DescribeForAudit(changes));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _state.Apply(next);
        return new SettingsSaveResult(next, changes);
    }

    public async Task<StoreSettings> ReloadAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _settings.ListAsync(cancellationToken);
        var settings = StoreSettingsCodec.Read(rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal), _state.Defaults);
        _state.Apply(settings);
        return settings;
    }

    private Error? Authorize()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.SettingsManage) ? null : SettingsErrors.Denied;
    }
}
