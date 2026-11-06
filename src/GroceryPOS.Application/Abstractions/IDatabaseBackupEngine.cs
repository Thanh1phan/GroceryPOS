using GroceryPOS.Application.Backup;

namespace GroceryPOS.Application.Abstractions;

/// <summary>Provider-specific copy of the whole database to and from a file (Infrastructure: SQLite / SQL Server).</summary>
public interface IDatabaseBackupEngine
{
    /// <summary>"SQLite" or "SQL Server", shown on the backup screen.</summary>
    string ProviderName { get; }

    /// <summary>Extension of backup files, with the dot (".db", ".bak").</summary>
    string FileExtension { get; }

    /// <summary>False when restoring has to be done with the provider's own tools.</summary>
    bool SupportsRestore { get; }

    /// <summary>Writes a consistent copy of the live database to <paramref name="destinationPath"/> (must not exist).</summary>
    Task CreateAsync(string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>Checks that the file is a readable backup and summarises what it holds, without changing anything.</summary>
    Task<BackupInspection> InspectAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Replaces the live database with the backup, then applies pending migrations.</summary>
    Task RestoreAsync(string sourcePath, CancellationToken cancellationToken = default);
}
