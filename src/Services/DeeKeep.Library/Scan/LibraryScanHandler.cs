using System.Text.Json.Nodes;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Common.Jobs;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Library.Files;
using DeeKeep.Library.Tracks;

namespace DeeKeep.Library.Scan;

/// <summary>
/// « Relire la bibliothèque » (tâche <c>library.scan</c>) — portage de <c>db_init.build_local</c>
/// avec parcours du disque, rendu incrémental : seuls les fichiers nouveaux ou modifiés
/// (taille ou date) sont relus ; les jaquettes sont dédoublonnées ; l'historique d'un titre
/// est conservé. Écriture par lots de 500 au fil de l'eau : un arrêt ne perd rien, et une
/// reprise après redémarrage ne relit que ce qui reste.
/// </summary>
public sealed class LibraryScanHandler(
    DeeKeepDb db,
    LibraryPaths paths,
    TrackWriter writer,
    DiskStateCache disk,
    EventLog events,
    ILogger<LibraryScanHandler> logger) : IJobHandler
{
    /// <summary>Extensions de <c>db_init.AUDIO_EXTENSIONS</c> (les 8 communes + ALAC/AIFF).</summary>
    public static bool IsScannedAudio(string name) =>
        AudioExtensions.IsAudioFile(name) || name.EndsWith(".alac", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".aif", StringComparison.OrdinalIgnoreCase);

    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var root = paths.LocalDownloadDir();
        if (!Directory.Exists(root)) throw new PermanentJobException($"Dossier de dépôt introuvable : {root}");

        context.Message($"Parcours de {root} …");
        var files = Walk(root, ct).ToList();
        context.Report(0, files.Count, $"{files.Count} fichiers audio relevés");

        Dictionary<string, Dictionary<string, object?>> existing;
        HashSet<string> archived;
        using (var conn = db.Open(readOnly: true))
        {
            existing = conn.QueryRows("SELECT * FROM tracks").GroupBy(r => LibraryPaths.Key(r.Str("path") ?? ""))
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Long("deleted") ?? 0).First(), StringComparer.Ordinal);
            archived = conn.QueryRows("SELECT path FROM archive WHERE deleted = 0 AND key IS NOT NULL AND key <> ''")
                .Select(r => LibraryPaths.Key(r.Str("path") ?? "")).ToHashSet(StringComparer.Ordinal);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pendingRows = new List<IReadOnlyDictionary<string, object?>>();
        var pendingCovers = new List<CoverRow>();
        int read = 0, unchanged = 0, added = 0, updated = 0, done = 0;

        void Flush()
        {
            if (pendingRows.Count == 0 && pendingCovers.Count == 0) return;
            writer.Write(pendingCovers, pendingRows);
            events.Publish(EventTypes.TracksChanged, new TracksChangedEvent(pendingRows.Select(r => (string)r["path"]!).ToList(), "scan-progress"));
            pendingRows.Clear();
            pendingCovers.Clear();
        }

        foreach (var local in files)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            var state = DiskStateCache.Stat(local);
            if (!state.Exists) continue;
            var dbPath = paths.ToDb(local);
            var key = LibraryPaths.Key(dbPath);
            seen.Add(key);
            existing.TryGetValue(key, out var current);
            var alive = current is not null && (current.Long("deleted") ?? 0) == 0;
            if (alive && current!.Long("file_size") == state.Size &&
                Math.Abs((current.Dbl("file_mtime") ?? -1) - PythonFloat.Round(state.MtimeSeconds, 3)) < 0.002 &&
                !string.IsNullOrEmpty(current.Str("codec")))
            {
                unchanged++;
                context.Report(done, files.Count, $"{done}/{files.Count} fichiers lus");
                continue;
            }

            var tags = AudioTags.Read(local);
            read++;
            var cover = tags.Cover is { } c ? new CoverRow(c.Sha1, c.Data) : null;
            if (cover is not null) pendingCovers.Add(cover);
            var row = TrackWriter.FromFile(alive ? current!.Str("path")! : dbPath, tags, state, cover?.Sha1);
            TrackWriter.MergeHistory(row, current);
            pendingRows.Add(row);
            if (alive) updated++; else added++;
            if (pendingRows.Count >= 500) Flush();
            context.Report(done, files.Count, $"{done}/{files.Count} fichiers lus");
            await Task.Yield();
        }
        Flush();

        // Fichiers disparus : un titre de l'historique devient « history » (consultable), un titre
        // que seul le disque connaissait reçoit une pierre tombale — sauf s'il est archivé, il
        // reste alors affiché et restaurable. Rien n'est touché si le parcours n'a rien trouvé
        // (disque démonté).
        var rootKey = LibraryPaths.Key(paths.ToDb(root)).TrimEnd('\\') + "\\";
        var tombstones = new List<string>();
        var toHistory = new List<IReadOnlyDictionary<string, object?>>();
        if (files.Count > 0)
        {
            foreach (var (key, row) in existing)
            {
                if ((row.Long("deleted") ?? 0) != 0 || seen.Contains(key) || !key.StartsWith(rootKey, StringComparison.Ordinal)) continue;
                var path = row.Str("path")!;
                if (File.Exists(paths.ToLocal(path))) continue;   // exclu du parcours (dossier caché), mais présent
                if ((row.Long("in_history") ?? 0) != 0)
                {
                    if (row.Str("source") == "history") continue;
                    var content = TrackWriter.Content(row);
                    content["source"] = "history";
                    toHistory.Add(content);
                }
                else if (!archived.Contains(key)) tombstones.Add(path);
            }
            writer.Write([], toHistory, tombstones);
        }

        disk.InvalidateAll();
        events.Publish(EventTypes.TracksChanged, new TracksChangedEvent([], "scan"));
        if (tombstones.Count > 0) events.Publish(EventTypes.TracksRemoved, new TracksChangedEvent(tombstones, "scan"));
        var message = $"{files.Count} fichiers audio relevés : {added} nouveau(x), {updated} relu(s), {unchanged} inchangé(s), {tombstones.Count} disparu(s)";
        logger.LogInformation("Collecte de la bibliothèque : {Message}", message);
        context.Report(files.Count, files.Count, message, force: true);
        return new JsonObject
        {
            ["files"] = files.Count, ["read"] = read, ["added"] = added, ["updated"] = updated, ["unchanged"] = unchanged,
            ["removed"] = tombstones.Count, ["toHistory"] = toHistory.Count, ["message"] = message,
        };
    }

    /// <summary>Parcours récursif, corbeilles et dossiers système exclus (« $RECYCLE.BIN », « .deekeep-trash »).</summary>
    public static IEnumerable<string> Walk(string root, CancellationToken ct)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            List<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(current).EnumerateFileSystemInfos("*", options).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { continue; }
            foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (entry is DirectoryInfo dir)
                {
                    if (!dir.Attributes.HasFlag(FileAttributes.ReparsePoint) && !LibraryPaths.IsExcludedFolder(dir.Name)) stack.Push(dir.FullName);
                }
                else if (IsScannedAudio(entry.Name)) yield return entry.FullName;
            }
        }
    }
}
