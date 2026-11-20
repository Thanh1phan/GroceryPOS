using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Presentation.Tests;

internal sealed class FakeShiftService : IShiftService
{
    public CurrentShiftDto Current { get; set; } = new(null, 0);
    public List<ShiftListItemDto> Shifts { get; } = new();
    public Dictionary<int, ShiftSummaryDto> Summaries { get; } = new();
    public List<ShiftFilter> Searches { get; } = new();
    public List<string> Calls { get; } = new();
    public Error? FailCurrentWith { get; set; }
    public Error? FailListWith { get; set; }

    /// <summary>Awaited inside a history search when set (to overlap two loads).</summary>
    public Func<Task>? BeforeListReturns { get; set; }

    public Task<Result<CurrentShiftDto>> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("current");
        return Task.FromResult(FailCurrentWith is { } e ? Result.Failure<CurrentShiftDto>(e) : Result.Success(Current));
    }

    public Task<Result<ShiftSummaryDto>> OpenAsync(OpenShiftRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The open-shift dialog opens on its own.");

    public Task<Result<ShiftSummaryDto>> RecordCashMovementAsync(CashMovementRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The cash movement dialog saves on its own.");

    public Task<Result<ShiftSummaryDto>> CloseAsync(CloseShiftRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The close-shift dialog closes on its own.");

    public Task<Result<ShiftSummaryDto>> GetSummaryAsync(int shiftId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"summary:{shiftId}");
        return Task.FromResult(Summaries.TryGetValue(shiftId, out var dto)
            ? Result.Success(dto)
            : Result.Failure<ShiftSummaryDto>(new Error("shift.not_found", "Không tìm thấy ca làm việc.")));
    }

    public async Task<Result<IReadOnlyList<ShiftListItemDto>>> ListAsync(ShiftFilter filter, CancellationToken cancellationToken = default)
    {
        Searches.Add(filter);
        if (BeforeListReturns is { } wait)
        {
            await wait();
        }

        if (FailListWith is { } e)
        {
            return Result.Failure<IReadOnlyList<ShiftListItemDto>>(e);
        }

        return Shifts
            .Where(s => (filter.From is null || s.OpenedAt >= filter.From) && (filter.To is null || s.OpenedAt < filter.To))
            .Where(s => filter.Status is null || s.Status == filter.Status)
            .ToList();
    }
}

/// <summary>A clock the tests can set.</summary>
internal sealed class FixedClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(7));

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
}
