using GroceryPOS.Domain.Settings;

namespace GroceryPOS.Application.Abstractions;

/// <summary>Store-wide key/value settings (<c>AppSettings</c> table); <see cref="IRepository{T}.ListAsync"/> loads them all.</summary>
public interface IAppSettingRepository : IRepository<AppSetting>
{
}
