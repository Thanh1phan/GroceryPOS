using GroceryPOS.Application.Abstractions;
using Serilog.Core;
using Serilog.Events;

namespace GroceryPOS.Infrastructure.Logging;

/// <summary>Stamps every event with the signed-in user (<c>UserName</c>, "-" before login).</summary>
public sealed class CurrentUserEnricher : ILogEventEnricher
{
    public const string PropertyName = "UserName";

    private readonly Func<ICurrentUser?> _currentUser;

    public CurrentUserEnricher(Func<ICurrentUser?> currentUser)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var name = _currentUser()?.Session?.Username ?? "-";
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(PropertyName, name));
    }
}
