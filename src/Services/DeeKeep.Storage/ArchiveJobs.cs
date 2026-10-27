using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Common.Files;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Outbox;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Contracts.Storage;

namespace DeeKeep.Storage;

/// <summary>Charge utile des tâches de mesure : emplacement saisi (null = enregistré).</summary>
public sealed record ArchiveRootPayload(string? Root);

/// <summary>Suite d'un titre (état, ligne à écrire, message, octets).</summary>
internal sealed record ItemOutcome(string State, ArchiveRow? Row, string Message, long Bytes);

/// <summary>
/// <c>archive.upload</c> — <c>MirrorThread</c> : copie « telle quelle » des titres
/// retenus. L'empreinte date+taille dit si un titre a changé depuis sa dernière copie ;
/// le SHA-1 de son contenu donne la clé et dédoublonne ; un objet déjà présent n'est pas
/// réécrit. Chaque ligne <c>archive</c> est écrite dès que son titre est traité : un
/// arrêt ne perd rien et une reprise ne recopie rien.
/// </summary>
public sealed class ArchiveUploadHandler(
    ArchiveSettings settings, ArchiveIndex index, PathMap pathMap, ArchiveLocks locks, EventLog events, ILogger<ArchiveUploadHandler> logger)
    : IJobHandler
{
    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var request = context.GetPayload<ArchiveRequest>();
        var paths = request.DbPaths.Distinct(StringComparer.Ordinal).ToList();
        ObjectStore store;
        try
        {
            store = settings.Open();
        }
        catch (ArchiveException ex)
        {
            throw new PermanentJobException(ex.Message, ex);
        }

        context.Report(0, paths.Count, "En attente de l'opération d'archive en cours…", force: true);
        await locks.Work.WaitAsync(ct);
        try
        {
            context.Report(0, paths.Count, $"Envoi de {paths.Count} titres en cours…", force: true);
            var known = index.Live();
            var bitrates = index.Tracks().GroupBy(t => t.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Bitrate ?? 0, StringComparer.Ordinal);
            var batch = new ArchiveEventBatch(events, ArchiveUpdateReasons.Archived);
            int sent = 0, reused = 0, skipped = 0, failed = 0;
            long written = 0;
            try
            {
                foreach (var dbPath in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    var outcome = await UploadOneAsync(store, known, dbPath, bitrates.GetValueOrDefault(dbPath), batch, ct);
                    switch (outcome.State)
                    {
                        case ArchiveItemStates.Sent:
                            sent++;
                            written += outcome.Bytes;
                            break;
                        case ArchiveItemStates.Reused:
                            reused++;
                            break;
                        case ArchiveItemStates.Skipped:
                            skipped++;
                            break;
                        default:
                            failed++;
                            break;
                    }
                    context.Item(dbPath, outcome.State, outcome.Message.Length > 0 ? outcome.Message : null,
                        new ArchiveItemResult(dbPath, outcome.State, outcome.Message.Length > 0 ? outcome.Message : null, outcome.Bytes, outcome.Row?.Key));
                    context.Report(sent + reused + skipped, paths.Count, null, failed);
                }
            }
            finally
            {
                batch.Flush();
            }

            var parts = new List<string> { $"{sent} titres envoyés ({ArchiveRules.HumanBytes(written)})" };
            if (reused + skipped > 0) parts.Add($"{reused + skipped} déjà à jour");
            if (failed > 0) parts.Add($"{failed} en échec");
            context.Report(sent + reused + skipped, paths.Count, string.Join(" · ", parts), failed, force: true);
            return JsonSerializer.SerializeToNode(new { sent, skipped = reused + skipped, failed, bytes = written }, JsonDefaults.Options);
        }
        finally
        {
            locks.Work.Release();
        }
    }

    /// <summary><c>_mirror_one</c>, avec un contrôle de modification entre hachage et copie.</summary>
    private async Task<ItemOutcome> UploadOneAsync(ObjectStore store, ArchiveRows known, string dbPath, long trackBitrate, ArchiveEventBatch batch, CancellationToken ct)
    {
        var local = pathMap.ToLocal(dbPath);
        var file = new FileInfo(local);
        if (!file.Exists) return new ItemOutcome(ArchiveItemStates.Failed, null, "Fichier absent du disque.", 0);

        var current = Stamp.Archive(Stamp.MtimeNs(file), file.Length);
        var previous = known.Find(dbPath);
        if (ArchiveRules.StateOf(previous?.Key, previous?.Stamp, current) == ArchiveState.Fresh)
            return new ItemOutcome(ArchiveItemStates.Skipped, null, "Déjà dans l'archive.", 0);
        if (file.Length > ArchiveRules.MaxObjectBytes)
            return new ItemOutcome(ArchiveItemStates.Failed, null, "Fichier trop volumineux pour l'archive (une copie est limitée à un milliard d'octets).", 0);

        // L'empreinte est toujours relue : deux fichiers distincts peuvent partager date
        // et taille, et une clé déduite de celles-ci rangerait un titre sous celle d'un autre.
        string sha1;
        try
        {
            sha1 = await ArchiveRules.FileSha1Async(local, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ItemOutcome(ArchiveItemStates.Failed, null, $"Lecture impossible : {ex.Message}", 0);
        }

        var key = ArchiveRules.ObjectKey(store.Prefix, sha1, Path.GetExtension(local));
        var bitrate = trackBitrate > 0 ? trackBitrate : ArchiveRules.ProbeBitrate(local);
        // Une ligne retrouvée sans tenir compte de la casse garde son chemin : pas de doublon d'index.
        var row = new ArchiveRow(previous?.Path ?? dbPath, sha1, current, key, null, bitrate, file.Length, Clock.UnixNow());

        await locks.Write.WaitAsync(ct);
        try
        {
            long? existing;
            try
            {
                existing = store.Head(key);
            }
            catch (ArchiveException ex)
            {
                return new ItemOutcome(ArchiveItemStates.Failed, null, ex.Message, 0);
            }

            string state;
            long bytes;
            if (existing is { } size)
            {
                // Objet déjà là : ni lecture ni écriture (doublon, ou copie interrompue reprise).
                state = ArchiveItemStates.Reused;
                bytes = size;
            }
            else
            {
                file.Refresh();
                if (!file.Exists || Stamp.Archive(Stamp.MtimeNs(file), file.Length) != current)
                    return new ItemOutcome(ArchiveItemStates.Failed, null, "Le fichier a changé pendant la copie : il sera repris au prochain archivage.", 0);
                try
                {
                    await using var source = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, ArchiveRules.Chunk, FileOptions.SequentialScan | FileOptions.Asynchronous);
                    bytes = await store.PutAsync(key, source, file.Length, sha1, ct);
                }
                catch (ArchiveException ex)
                {
                    return new ItemOutcome(ArchiveItemStates.Failed, null, ex.Message, 0);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new ItemOutcome(ArchiveItemStates.Failed, null, $"Lecture impossible : {ex.Message}", 0);
                }
                state = ArchiveItemStates.Sent;
            }

            row = row with { Bytes = bytes };
            index.Upsert(row);
            batch.Add(row.Path);
            return new ItemOutcome(state, row, state == ArchiveItemStates.Reused ? "Déjà dans l'archive." : "", bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ArchiveException)
        {
            logger.LogWarning(ex, "Archivage de {Path} impossible", dbPath);
            return new ItemOutcome(ArchiveItemStates.Failed, null, ex.Message, 0);
        }
        finally
        {
            locks.Write.Release();
        }
    }
}

/// <summary>
/// <c>archive.restore</c> — <c>RestoreThread</c> : ressort de l'archive les titres que le
/// disque n'a plus. Rien n'est jamais écrasé, l'arborescence est recréée, le fichier est
/// écrit dans un temporaire, son SHA-1 revérifié, puis mis en place d'un coup ; la ligne
/// d'archive reçoit la nouvelle empreinte date+taille (sans perdre <c>uploaded_at</c>)
/// et le fichier est enregistré auprès de la Bibliothèque par la boîte d'envoi.
/// </summary>
public sealed class ArchiveRestoreHandler(
    ArchiveSettings settings, ArchiveIndex index, PathMap pathMap, ArchiveLocks locks, EventLog events, OutboxQueue outbox,
    ILogger<ArchiveRestoreHandler> logger) : IJobHandler
{
    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var request = context.GetPayload<ArchiveRequest>();
        var paths = request.DbPaths.Distinct(StringComparer.Ordinal).ToList();
        ObjectStore store;
        try
        {
            store = settings.Open();
        }
        catch (ArchiveException ex)
        {
            throw new PermanentJobException(ex.Message, ex);
        }

        context.Report(0, paths.Count, "En attente de l'opération d'archive en cours…", force: true);
        await locks.Work.WaitAsync(ct);
        try
        {
            context.Report(0, paths.Count, $"Restauration de {paths.Count} titres en cours…", force: true);
            var rows = index.Live();
            var batch = new ArchiveEventBatch(events, ArchiveUpdateReasons.Restored);
            int restored = 0, skipped = 0, failed = 0;
            long received = 0;
            try
            {
                foreach (var dbPath in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    var row = rows.Find(dbPath);
                    var outcome = row is null
                        ? new ItemOutcome(ArchiveItemStates.Failed, null, "Ce titre n'est pas dans l'archive.", 0)
                        : await RestoreOneAsync(store, row, ct);
                    if (outcome.Row is { } updated)
                    {
                        index.Upsert(updated);
                        batch.Add(updated.Path);
                    }
                    switch (outcome.State)
                    {
                        case ArchiveItemStates.Restored:
                            restored++;
                            received += outcome.Bytes;
                            break;
                        case ArchiveItemStates.Skipped:
                            skipped++;
                            break;
                        default:
                            failed++;
                            break;
                    }
                    context.Item(dbPath, outcome.State, outcome.Message.Length > 0 ? outcome.Message : null,
                        new ArchiveItemResult(dbPath, outcome.State, outcome.Message.Length > 0 ? outcome.Message : null, outcome.Bytes, row?.Key));
                    context.Report(restored + skipped, paths.Count, null, failed);
                }
            }
            finally
            {
                batch.Flush();
            }

            var parts = new List<string> { $"{restored} titres rendus à l'identique ({ArchiveRules.HumanBytes(received)})" };
            if (skipped > 0) parts.Add($"{skipped} déjà sur le disque");
            if (failed > 0) parts.Add($"{failed} en échec");
            context.Report(restored + skipped, paths.Count, string.Join(" · ", parts), failed, force: true);
            return JsonSerializer.SerializeToNode(new { restored, skipped, failed, bytes = received }, JsonDefaults.Options);
        }
        finally
        {
            locks.Work.Release();
        }
    }

    /// <summary><c>_restore_one</c>.</summary>
    private async Task<ItemOutcome> RestoreOneAsync(ObjectStore store, ArchiveRow row, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(row.Path) || string.IsNullOrEmpty(row.Key))
            return new ItemOutcome(ArchiveItemStates.Failed, null, "Ligne d'archive incomplète.", 0);
        var (destination, identical) = ArchiveRules.RestoreTarget(row.Path, row.Key);
        if (!identical)
        {
            return new ItemOutcome(ArchiveItemStates.Failed, null,
                "Cette ligne désigne une ancienne copie réduite, absente de l'archive : le titre est à recopier depuis le disque.", 0);
        }

        var local = pathMap.ToLocal(destination);
        // Rien n'est écrasé : la restauration comble des trous, elle ne remplace pas ce qui est là.
        if (File.Exists(local)) return new ItemOutcome(ArchiveItemStates.Skipped, null, "Déjà sur le disque.", 0);

        try
        {
            var folder = Path.GetDirectoryName(local);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new ItemOutcome(ArchiveItemStates.Failed, null, $"Dossier impossible à créer : {ex.Message}", 0);
        }

        long size;
        try
        {
            size = await store.DownloadAsync(row.Key, local, row.Sha1, ct);
        }
        catch (ArchiveException ex)
        {
            return new ItemOutcome(ArchiveItemStates.Failed, null, ex.Message, 0);
        }
        catch (IOException) when (File.Exists(local))
        {
            // Fichier apparu pendant la copie : il est gardé tel quel.
            return new ItemOutcome(ArchiveItemStates.Skipped, null, "Déjà sur le disque.", 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ItemOutcome(ArchiveItemStates.Failed, null, $"Écriture impossible : {ex.Message}", 0);
        }

        // Le fichier revient avec une date neuve : sans cette empreinte remise à jour, tout
        // ce qui vient d'être restauré passerait « à renvoyer ». La ligne lue en entier
        // garde son uploaded_at (l'original le remettait à vide).
        var updated = row with { Stamp = Stamp.Archive(local) };
        try
        {
            outbox.Enqueue(ServiceNames.Library, HttpMethod.Post, "/api/files/register",
                new RegisterFileRequest(local, "restore", AddToHistory: false));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Enregistrement de {Path} auprès de la Bibliothèque impossible", local);
        }
        return new ItemOutcome(ArchiveItemStates.Restored, updated, "", size);
    }
}

/// <summary>
/// <c>archive.usage</c> — <c>UsageThread</c> : compte ce que l'archive contient
/// réellement (objets sous le préfixe, octets) et la place libre de son volume.
/// </summary>
public sealed class ArchiveUsageHandler(ArchiveSettings settings) : IJobHandler
{
    public Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var payload = context.Job.Payload is null ? new ArchiveRootPayload(null) : context.GetPayload<ArchiveRootPayload>();
        context.Message("Mesure de l'archive…");
        long count, total;
        ObjectStore store;
        try
        {
            store = settings.Open(payload.Root);
            store.OpenWrite().Dispose();
            (count, total) = store.Usage();
        }
        catch (ArchiveException ex)
        {
            throw new PermanentJobException(ex.Message, ex);
        }
        var (free, _) = Volumes.Space(store.Path);
        // Total à 1 : le message de mesure est le résultat lisible, il ne doit pas être remplacé par « Terminé ».
        context.Report(1, 1, $"{count} titres archivés, {ArchiveRules.HumanBytes(total)} — {ArchiveRules.HumanBytes(free)} de libre sur le volume.", force: true);
        return Task.FromResult(JsonSerializer.SerializeToNode(
            new { objectCount = count, storedBytes = total, archiveFileBytes = store.FileBytes(), diskFreeBytes = free, archiveFile = store.Path },
            JsonDefaults.Options));
    }
}

/// <summary>
/// <c>archive.check</c> : chaque ligne vivante de la table <c>archive</c> retrouve-t-elle
/// son objet, à la bonne taille ? Utile après un déménagement de l'archive. Rien n'est
/// corrigé : une archive mal désignée ne doit pas effacer l'index.
/// </summary>
public sealed class ArchiveCheckHandler(ArchiveSettings settings, ArchiveIndex index) : IJobHandler
{
    public Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        ObjectStore store;
        IReadOnlyList<(string Key, long Bytes)> objects;
        try
        {
            store = settings.Open();
            if (!store.Exists) throw new PermanentJobException($"Archive absente : {store.Path}");
            objects = store.ListObjects("");
        }
        catch (ArchiveException ex)
        {
            throw new PermanentJobException(ex.Message, ex);
        }
        var sizes = objects.ToDictionary(o => o.Key, o => o.Bytes, StringComparer.Ordinal);
        var rows = index.Live().All.Where(r => !string.IsNullOrEmpty(r.Key)).OrderBy(r => r.Path, StringComparer.Ordinal).ToList();
        int ok = 0, missing = 0, mismatched = 0;
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            used.Add(row.Key!);
            if (!sizes.TryGetValue(row.Key!, out var size))
            {
                missing++;
                context.Item(row.Path, ArchiveItemStates.Failed, "Copie introuvable dans l'archive.",
                    new ArchiveItemResult(row.Path, ArchiveItemStates.Failed, "Copie introuvable dans l'archive.", 0, row.Key));
            }
            else if (row.Bytes is { } expected && expected != size)
            {
                mismatched++;
                context.Item(row.Path, ArchiveItemStates.Failed, "Copie de taille différente.",
                    new ArchiveItemResult(row.Path, ArchiveItemStates.Failed, "Copie de taille différente.", size, row.Key));
            }
            else ok++;
            context.Report(ok, rows.Count, null, missing + mismatched);
        }
        var orphans = sizes.Keys.Count(k => k.StartsWith(store.Prefix, StringComparison.Ordinal) && !used.Contains(k));
        var parts = new List<string> { $"{ok} titres retrouvés dans l'archive" };
        if (missing > 0) parts.Add($"{missing} copies introuvables");
        if (mismatched > 0) parts.Add($"{mismatched} de taille différente");
        if (orphans > 0) parts.Add($"{orphans} objets sans titre");
        context.Report(rows.Count == 0 ? 1 : ok, Math.Max(1, rows.Count), string.Join(" · ", parts), missing + mismatched, force: true);
        return Task.FromResult(JsonSerializer.SerializeToNode(new { ok, missing, mismatched, orphans }, JsonDefaults.Options));
    }
}
