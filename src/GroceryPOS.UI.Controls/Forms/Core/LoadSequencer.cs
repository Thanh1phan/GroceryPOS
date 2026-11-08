namespace GroceryPOS.UI.Controls.Forms.Core;

/// <summary>A started load: its sequence number and the token that is cancelled when a newer load starts.</summary>
public readonly record struct LoadTicket(long Version, CancellationToken Token);

/// <summary>
/// "Latest wins" coordination for screens that reload asynchronously (F5, after a dialog, after a filter change).
/// Starting a load cancels the previous one, and only the newest load may apply its result, so a slow early
/// query can never overwrite the rows of a later one. Pure BCL, so the rules are unit-tested.
/// </summary>
public sealed class LoadSequencer : IDisposable
{
    private CancellationTokenSource? _current;
    private long _version;
    private long _completedVersion;
    private bool _disposed;

    /// <summary>True while the newest load has not completed yet.</summary>
    public bool IsLoading => !_disposed && _version > _completedVersion;

    /// <summary>Number of loads started so far.</summary>
    public long Version => _version;

    /// <summary>Starts a load and cancels the one before it.</summary>
    public LoadTicket Begin()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var previous = _current;
        _current = new CancellationTokenSource();
        _version++;
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        return new LoadTicket(_version, _current.Token);
    }

    /// <summary>True when <paramref name="ticket"/> belongs to the newest load and may apply its result.</summary>
    public bool IsCurrent(LoadTicket ticket) => !_disposed && ticket.Version == _version && !ticket.Token.IsCancellationRequested;

    /// <summary>
    /// Marks <paramref name="ticket"/> finished. Returns true when it was the newest load (apply the result);
    /// false for a superseded load (drop the result).
    /// </summary>
    public bool Complete(LoadTicket ticket)
    {
        if (!IsCurrent(ticket))
        {
            return false;
        }

        _completedVersion = ticket.Version;
        return true;
    }

    /// <summary>Cancels the running load, if any (the screen is closing).</summary>
    public void Cancel()
    {
        if (_current is { IsCancellationRequested: false } cts)
        {
            cts.Cancel();
        }

        _completedVersion = _version;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Cancel();
        _current?.Dispose();
        _current = null;
        _disposed = true;
    }
}
