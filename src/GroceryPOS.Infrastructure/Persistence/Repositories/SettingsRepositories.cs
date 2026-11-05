using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Settings;

namespace GroceryPOS.Infrastructure.Persistence.Repositories;

internal sealed class AppSettingRepository : EfRepository<AppSetting>, IAppSettingRepository
{
    public AppSettingRepository(GroceryDbContext context) : base(context)
    {
    }
}
