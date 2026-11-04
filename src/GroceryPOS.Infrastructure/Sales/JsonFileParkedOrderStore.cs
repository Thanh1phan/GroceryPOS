using System.Text.Encodings.Web;
using System.Text.Json;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Sales;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroceryPOS.Infrastructure.Sales;

/// <summary>Where this counter keeps its parked carts (section <c>ParkedOrders</c> in appsettings.json).</summary>
public sealed class ParkedOrderOptions
{
    public const string SectionName = "ParkedOrders";

    /// <summary>JSON file path; empty = <c>%LocalAppData%\GroceryPOS\parked-orders.json</c>.</summary>
    public string? FilePath { get; set; }

    public string ResolveFilePath() =>
        string.IsNullOrWhiteSpace(FilePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GroceryPOS", "parked-orders.json")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(FilePath));
}

/// <summary>
/// Keeps parked carts in one small JSON file per machine. Writes go to a temp file that then replaces the old one,
/// so a crash mid-write never loses the list; an unreadable file is moved aside (<c>.bad</c>) and the list starts empty.
/// Thread-safe within the process (singleton).
/// </summary>
public sealed class JsonFileParkedOrderStore : IParkedOrderStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Vietnamese readable in the file
    };

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileParkedOrderStore(string path, ILogger<JsonFileParkedOrderStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public string FilePath => _path;

    public async Task<IReadOnlyList<ParkedOrder>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await ReadAsync(cancellationToken)).OrderBy(o => o.ParkedAt).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ParkedOrder?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken)).FirstOrDefault(o => o.Id == id);

    public async Task SaveAsync(ParkedOrder order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var orders = await ReadAsync(cancellationToken);
            orders.RemoveAll(o => o.Id == order.Id);
            orders.Add(order);
            await WriteAsync(orders, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var orders = await ReadAsync(cancellationToken);
            if (orders.RemoveAll(o => o.Id == id) == 0)
            {
                return false;
            }

            await WriteAsync(orders, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<ParkedOrder>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new List<ParkedOrder>();
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var orders = await JsonSerializer.DeserializeAsync<List<ParkedOrder>>(stream, Json, cancellationToken);
            return orders?.Where(o => o is { Lines: not null }).ToList() ?? new List<ParkedOrder>();
        }
        catch (JsonException ex)
        {
            var bad = _path + $".{DateTime.Now:yyyyMMddHHmmss}.bad";
            _logger.LogWarning(ex, "Parked orders file {Path} is unreadable; moved to {Bad}", _path, bad);
            File.Move(_path, bad, overwrite: true);
            return new List<ParkedOrder>();
        }
    }

    private async Task WriteAsync(List<ParkedOrder> orders, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, orders, Json, cancellationToken);
        }

        File.Move(temp, _path, overwrite: true);
    }
}
