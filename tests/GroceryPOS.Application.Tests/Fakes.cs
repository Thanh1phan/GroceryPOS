using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Tests.Fakes;

internal sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 8, 2, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now = Now.Add(by);
}

/// <summary>Reversible fake: hash = "hashed:" + password.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "hashed:" + password;

    public bool Verify(string password, string passwordHash) => passwordHash == Hash(password);
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult(1);
    }
}

internal sealed class RecordingAuditService : IAuditService
{
    public List<(string Action, int? UserId, string? Username, string? Details)> Entries { get; } = new();

    public IEnumerable<string> Actions => Entries.Select(e => e.Action);

    public void Record(string action, string? entityName = null, string? entityId = null, string? details = null) =>
        Entries.Add((action, null, null, details));

    public void RecordFor(int? userId, string? username, string action, string? details = null) =>
        Entries.Add((action, userId, username, details));
}

internal sealed class InMemoryUserRepository : IUserRepository
{
    private readonly List<User> _users = new();
    private int _nextId = 1;

    public Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.FirstOrDefault(u => u.Id == id));

    public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<User>>(_users.ToList());

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.FirstOrDefault(u => u.Username == username));

    public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Any(u => u.Username == username));

    public void Add(User entity)
    {
        if (entity.IsTransient)
        {
            typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, _nextId++);
        }

        _users.Add(entity);
    }

    public void Remove(User entity) => _users.Remove(entity);
}
