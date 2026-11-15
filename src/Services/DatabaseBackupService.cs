using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;

namespace VisitorManagement.Services;

public record BackupFile(string Name, long Bytes, DateTime CreatedAt);

/// <summary>
/// Whole-database backups for the platform admin (all tenants). Uses SQLite's online backup API,
/// so the copy is consistent while the app keeps running. Backups are compressed, integrity-checked,
/// and kept in a folder outside the website (Backup:Directory, default App_Data/backups).
/// Scheduled server-level backups can write to the same folder.
/// </summary>
public partial class DatabaseBackupService(AppDbContext db, IConfiguration config, IWebHostEnvironment env, ILogger<DatabaseBackupService> logger)
{
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    public string Directory
    {
        get
        {
            var dir = config["Backup:Directory"] ?? "App_Data/backups";
            return Path.IsPathRooted(dir) ? dir : Path.Combine(env.ContentRootPath, dir);
        }
    }

    public int KeepLatest => int.TryParse(config["Backup:Keep"], out var n) && n > 0 ? n : 20;

    public List<BackupFile> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        return new DirectoryInfo(Directory).GetFiles("*.zip")
            .Where(f => IsBackupName(f.Name))
            .OrderByDescending(f => f.CreationTime)
            .Select(f => new BackupFile(f.Name, f.Length, f.CreationTime))
            .ToList();
    }

    /// <summary>Creates a backup now. Returns the file, or an error message.</summary>
    public async Task<(BackupFile? File, string? Error)> CreateAsync(string createdBy)
    {
        if (!await OneAtATime.WaitAsync(TimeSpan.Zero)) return (null, "A backup is already running. Try again in a moment.");
        var tempDb = Path.Combine(Path.GetTempPath(), $"vms-backup-{Guid.NewGuid():N}.db");
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var name = $"vms-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            var target = Path.Combine(Directory, name);

            // Consistent online copy of the live database.
            await using (var source = new SqliteConnection(db.Database.GetConnectionString()))
            await using (var copy = new SqliteConnection($"Data Source={tempDb};Pooling=False"))
            {
                await source.OpenAsync();
                await copy.OpenAsync();
                source.BackupDatabase(copy);

                await using var check = copy.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                var result = (string?)await check.ExecuteScalarAsync();
                if (result != "ok")
                {
                    logger.LogError("Backup integrity check failed: {Result}", result);
                    return (null, $"The backup copy failed its integrity check ({result}). Nothing was saved.");
                }
            }

            var tenants = await db.Tenants.AsNoTracking().Select(t => new { t.Slug, t.Name, t.IsActive }).ToListAsync();
            await using (var zipFile = new FileStream(target, FileMode.CreateNew))
            using (var zip = new ZipArchive(zipFile, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(tempDb, "visitors.db", CompressionLevel.Optimal);
                var manifest = zip.CreateEntry("manifest.json");
                await using var m = manifest.Open();
                await JsonSerializer.SerializeAsync(m, new
                {
                    format = "vms-database-backup/1",
                    createdAt = DateTime.Now.ToString("o"),
                    createdBy,
                    database = "SQLite",
                    integrityCheck = "ok",
                    tenants,
                    restore = "Stop the app, replace App_Data/visitors.db with visitors.db from this ZIP (delete any visitors.db-wal / -shm files), start the app."
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            Prune();
            var info = new FileInfo(target);
            logger.LogInformation("Database backup {Name} created by {By} ({Bytes} bytes)", name, createdBy, info.Length);
            return (new BackupFile(name, info.Length, info.CreationTime), null);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(tempDb); } catch (IOException) { /* best effort */ }
            OneAtATime.Release();
        }
    }

    /// <summary>Full path of a backup by name, or null. Only names this service creates are accepted (no paths).</summary>
    public string? PathOf(string name)
    {
        if (!IsBackupName(name)) return null;
        var path = Path.Combine(Directory, name);
        return File.Exists(path) ? path : null;
    }

    public bool Delete(string name, string deletedBy)
    {
        if (PathOf(name) is not { } path) return false;
        File.Delete(path);
        logger.LogInformation("Database backup {Name} deleted by {By}", name, deletedBy);
        return true;
    }

    /// <summary>Keeps the newest <see cref="KeepLatest"/> backups.</summary>
    private void Prune()
    {
        foreach (var old in List().Skip(KeepLatest))
        {
            try { File.Delete(Path.Combine(Directory, old.Name)); }
            catch (IOException ex) { logger.LogWarning(ex, "Couldn't delete old backup {Name}", old.Name); }
        }
    }

    private static bool IsBackupName(string name) => BackupName().IsMatch(name);

    [GeneratedRegex(@"^vms-backup-\d{8}-\d{6}\.zip$")]
    private static partial Regex BackupName();
}
