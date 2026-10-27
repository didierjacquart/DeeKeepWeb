using System.Runtime.CompilerServices;
using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Common.Files;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Outbox;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using DeeKeep.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeeKeep.Tests.Storage;

/// <summary>Bases temporaires partagées (WebDb initialise son schéma une fois par processus) et assemblage du service.</summary>
internal sealed class StorageHarness
{
    private static readonly string BaseDir = Path.Combine(Path.GetTempPath(), "deekeep-storage-tests-" + Guid.NewGuid().ToString("N")[..8]);
    private static readonly DatabaseOptions Database = CreateDatabase();

    private static DatabaseOptions CreateDatabase()
    {
        Directory.CreateDirectory(BaseDir);
        var options = new DatabaseOptions { MainPath = Path.Combine(BaseDir, "main.db"), WebPath = Path.Combine(BaseDir, "web.db") };
        new DeeKeepDb(Options.Create(options)).EnsureSchema();
        return options;
    }

    public static string AssetsDir([CallerFilePath] string source = "") => Path.Combine(Path.GetDirectoryName(source)!, "Assets");

    public StorageHarness()
    {
        Root = Path.Combine(BaseDir, Guid.NewGuid().ToString("N")[..8]);
        Library = Path.Combine(Root, "lib");
        ArchiveDir = Path.Combine(Root, "archive");
        Directory.CreateDirectory(Library);
        var options = Options.Create(Database);
        Db = new DeeKeepDb(options);
        var web = new WebDb(options);
        var identity = new ServiceIdentity(ServiceNames.Storage);
        Events = new EventLog(web, identity, NullLogger<EventLog>.Instance);
        Jobs = new JobStore(web, identity, Events);
        Outbox = new OutboxQueue(web, identity);
        Index = new ArchiveIndex(Db);
        Settings = new ArchiveSettings(web, Options.Create(new StorageOptions { ArchiveRoot = ArchiveDir }));
        Locks = new ArchiveLocks();
        Service = new ArchiveService(Settings, Index, Map, Locks, Events);
    }

    public string Root { get; }
    public string Library { get; }
    public string ArchiveDir { get; }
    public DeeKeepDb Db { get; }
    public EventLog Events { get; }
    public JobStore Jobs { get; }
    public OutboxQueue Outbox { get; }
    public ArchiveIndex Index { get; }
    public ArchiveSettings Settings { get; }
    public ArchiveLocks Locks { get; }
    public ArchiveService Service { get; }
    public PathMap Map => new(Options.Create(new PathMapOptions()));

    public string WriteFile(string name, byte[] data, DateTime? mtimeUtc = null)
    {
        var path = Path.Combine(Library, name.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        File.SetLastWriteTimeUtc(path, mtimeUtc ?? new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        return path;
    }

    public static byte[] Pattern(int length, int seed) => Enumerable.Range(0, length).Select(i => (byte)((i * 7 + seed) % 256)).ToArray();

    public JobContext Context(string type, object payload) =>
        new(Jobs.Enqueue(new EnqueueJobRequest(type, "test", System.Text.Json.JsonSerializer.SerializeToNode(payload, JsonDefaults.Options), MaxAttempts: 1)), Jobs);

    public ArchiveUploadHandler Uploader() => new(Settings, Index, Map, Locks, Events, NullLogger<ArchiveUploadHandler>.Instance);

    public ArchiveRestoreHandler Restorer() => new(Settings, Index, Map, Locks, Events, Outbox, NullLogger<ArchiveRestoreHandler>.Instance);
}
