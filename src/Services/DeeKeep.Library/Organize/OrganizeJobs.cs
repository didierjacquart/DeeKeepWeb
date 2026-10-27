using System.Text.Json;
using System.Text.Json.Nodes;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Data;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Text;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Library;
using DeeKeep.Contracts.Organize;
using DeeKeep.Library.Files;

namespace DeeKeep.Library.Organize;

/// <summary>Conversions entrée → DTO partagées par les tâches et les points d'API.</summary>
public static class OrganizeMapping
{
    public static OrganizeEntryDto ToDto(OrganizeEntry e, string root, LibraryPaths paths) => new(
        e.Index, e.Path, paths.ToDb(e.Path), PyPath.Basename(e.Path), OrganizeText.RelativeFolder(e.Path, root), e.Title, e.TitleTagged,
        e.Artist, e.Album, e.AlbumArtist, e.Genre, e.Date, e.Track, e.Format, e.NeedsExtras);
}

/// <summary>
/// <c>organize.analyze</c> — parcours du dossier et lecture des tags (<c>LocalScanThread</c>) :
/// lots de 200 fichiers écrits au fil de l'eau en base (un arrêt ou une coupure ne perd rien) et
/// émis en événements <c>job.item</c> ; le débit n'est pas lu. Reprenable : les fichiers déjà lus
/// (même taille et même date) ne sont pas relus.
/// </summary>
public sealed class OrganizeAnalyzeHandler(OrganizeStore store, OrganizeEngine engine, LibraryPaths paths, IConfiguration configuration,
    ILogger<OrganizeAnalyzeHandler> logger) : IJobHandler
{
    public const int BatchSize = 200;

    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var payload = context.GetPayload<OrganizeAnalyzePayload>();
        var sessionId = payload.SessionId;
        var root = payload.Root;
        if (!Directory.Exists(root))
        {
            store.UpdateSession(sessionId, state: "failed", message: "Dossier introuvable");
            throw new PermanentJobException($"Le dossier suivant n'existe pas :\n{root}");
        }

        // Pool de lectures : 8 sur SSD dans l'original ; plus bas par défaut pour une carte SD ou un disque USB.
        var workers = Math.Clamp(configuration.GetValue("Organize:ReadWorkers", 4), 1, 16);
        var cache = store.ReadCache();
        engine.ForgetSession(sessionId);
        try
        {
            context.Message("Analyse en cours…");
            var files = await Task.Run(() => AudioExtensions.IterAudioFiles(root, ct).ToList(), ct);
            var total = files.Count;
            store.UpdateSession(sessionId, total: total, read: 0, message: $"Analyse en cours… {total} fichier(s) à lire", jobId: context.Id);
            context.Report(0, total, $"Analyse en cours… {total} fichier(s) à lire", force: true);

            var read = 0;
            var gate = new SemaphoreSlim(workers);
            for (var start = 0; start < total; start += BatchSize)
            {
                ct.ThrowIfCancellationRequested();
                var slice = files.Skip(start).Take(BatchSize).ToList();
                var tasks = slice.Select(async (path, k) =>
                {
                    await gate.WaitAsync(ct);
                    try { return await Task.Run(() => ReadOne(path, start + k, cache), ct); }
                    finally { gate.Release(); }
                }).ToList();
                var batch = (await Task.WhenAll(tasks)).Where(e => e is not null).Select(e => e!).ToList();
                store.SaveEntries(sessionId, batch);
                read += slice.Count;
                store.UpdateSession(sessionId, read: read, message: $"Analyse en cours… {read} / {total} fichiers lus");
                context.Item($"batch:{start / BatchSize}", "batch", null, batch.Select(e => OrganizeMapping.ToDto(e, root, paths)).ToList());
                context.Report(read, total, $"Analyse en cours… {read} / {total} fichiers lus");
            }
            store.TrimEntries(sessionId, total);
            store.UpdateSession(sessionId, state: "done", read: total,
                message: total == 0 ? "Aucun fichier audio trouvé dans ce dossier." : $"{total} fichier(s) analysé(s)");
            store.DeleteOtherSessions(sessionId);
            context.Report(total, total, total == 0 ? "Aucun fichier audio trouvé dans ce dossier." : $"{total} fichier(s) analysé(s)", force: true);
            return JsonSerializer.SerializeToNode(new { sessionId, total }, JsonDefaults.Options);
        }
        catch (OperationCanceledException)
        {
            // « Arrêter » : la liste finale fait foi, même partielle ; l'arrêt du service laisse la tâche reprendre.
            store.UpdateSession(sessionId, state: "stopped", message: "Analyse arrêtée");
            throw;
        }
        catch (Exception ex) when (ex is not (TransientJobException or PermanentJobException))
        {
            logger.LogWarning(ex, "Analyse de {Root} en échec", root);
            store.UpdateSession(sessionId, state: "failed", message: ex.Message);
            throw;
        }
    }

    private static OrganizeEntry? ReadOne(string path, int index, Dictionary<string, OrganizeEntry> cache)
    {
        long size = 0, mtime = 0;
        try
        {
            var info = new FileInfo(path);
            size = info.Length;
            mtime = info.LastWriteTimeUtc.Ticks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }

        OrganizeEntry entry;
        if (cache.TryGetValue(OrganizeText.PathKey(path), out var cached) && cached.Size == size && cached.MtimeTicks == mtime) entry = cached;
        else entry = OrganizeEntry.FromTags(AudioTags.Read(path, withProperties: false, withCover: false));
        entry.Index = index;
        entry.Path = path;
        entry.Size = size;
        entry.MtimeTicks = mtime;
        return entry;
    }
}

/// <summary>
/// <c>organize.apply</c> — suppression des doublons sûrs (vers la corbeille) PUIS déplacements et
/// renommages (l'exemplaire conservé vise parfois l'emplacement d'un doublon), puis suppression des
/// dossiers vides. Reprenable : le journal (une ligne par opération) fait que l'on ne refait pas
/// ce qui est fait. Chaque fichier déplacé est ré-enregistré dans <c>tracks</c> et l'index (corrige
/// le défaut de l'original : un rangement périmait la base).
/// </summary>
public sealed class OrganizeApplyHandler(OrganizeStore store, OrganizeEngine engine, LibraryPaths paths, FileRegistrar registrar,
    ILogger<OrganizeApplyHandler> logger) : IJobHandler
{
    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var payload = context.GetPayload<OrganizeApplyPayload>();
        var journal = store.Journal(context.Id);
        var ops = payload.Operations.OrderBy(o => o.Kind == OrganizeOperationKinds.Delete ? 0 : 1).ToList();
        var failures = new List<OrganizeFailure>();
        int moved = 0, deleted = 0, done = 0;
        var toMove = ops.Count(o => o.Kind == OrganizeOperationKinds.Move);
        var toDelete = ops.Count - toMove;
        context.Report(0, ops.Count, "Rangement…", force: true);

        foreach (var op in ops)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            var name = PyPath.Basename(op.Source);
            if (journal.TryGetValue((op.Index, op.Kind), out var previous) && previous.Status is "ok" or "failed")
            {
                // Reprise : déjà traité avant la coupure.
                if (previous.Status == "ok") { if (op.Kind == OrganizeOperationKinds.Move) moved++; else deleted++; }
                else failures.Add(new OrganizeFailure(op.Source, name, previous.Message ?? "échec"));
                continue;
            }

            string status = "ok";
            string? message = null;
            try
            {
                if (op.Kind == OrganizeOperationKinds.Delete) await DeleteAsync(op, ct);
                else await MoveAsync(op, payload.Root, ct);
                if (op.Kind == OrganizeOperationKinds.Move) moved++; else deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                status = "failed";
                message = ex.Message;
                failures.Add(new OrganizeFailure(op.Source, name, ex.Message));
            }
            var entry = new OrganizeJournalEntry(context.Id, op.Index, op.Kind, op.Source, op.Destination, status, message, Clock.UnixNow());
            store.Log(entry);
            context.Item($"{op.Index}:{op.Kind}", status, message, entry);
            context.Report(done, ops.Count, $"Rangement… {done} — {name}", failures.Count);
        }

        var removed = payload.RemoveEmpty ? PruneEmptyDirs(payload.Root) : [];
        engine.ForgetSession(payload.SessionId);

        var lines = new List<string> { $"{moved} fichier(s) rangé(s) sur {toMove}." };
        if (toDelete > 0) lines.Add($"{deleted} doublon(s) supprimé(s) sur {toDelete}.");
        if (removed.Count > 0) lines.Add($"{removed.Count} dossier(s) vide(s) supprimé(s).");
        if (failures.Count > 0)
        {
            lines.Add($"\n{failures.Count} échec(s) :");
            lines.AddRange(failures.Take(10).Select(f => $"  • {f.Name} — {f.Message}"));
            if (failures.Count > 10) lines.Add($"  … et {failures.Count - 10} autre(s).");
        }
        var result = new OrganizeApplyResult(moved, toMove, deleted, toDelete, removed, failures, string.Join("\n", lines));
        context.Report(ops.Count, ops.Count, "Rangement terminé : relancez l'analyse pour vérifier.", failures.Count, force: true);
        return JsonSerializer.SerializeToNode(result, JsonDefaults.Options);
    }

    private async Task DeleteAsync(OrganizeOperation op, CancellationToken ct)
    {
        // Une suppression ne se rattrape pas : l'exemplaire conservé doit encore exister.
        if (op.Destination.Length > 0 && !File.Exists(op.Destination)) throw new IOException("l'exemplaire conservé est introuvable");
        if (!File.Exists(op.Source)) return;
        var result = await registrar.DeleteAsync(new DeleteFilesRequest([paths.ToDb(op.Source)], "organize"), ct);
        if (result.Failed.Count > 0) throw new IOException(result.Errors.FirstOrDefault() ?? "suppression impossible");
    }

    private async Task MoveAsync(OrganizeOperation op, string root, CancellationToken ct)
    {
        var src = op.Source;
        var dst = ResolveCase(root, op.Destination);
        if (!File.Exists(src))
        {
            // Reprise après coupure entre le déplacement et son enregistrement.
            if (File.Exists(dst)) { await RegisterAsync(src, dst, ct); return; }
            throw new IOException("fichier introuvable");
        }
        if (OrganizeText.SamePath(src, dst))
        {
            // Même emplacement : au plus un changement de casse, qu'un renommage suffit à appliquer.
            if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.Ordinal)) return;
            File.Move(src, dst);
        }
        else
        {
            if (File.Exists(dst)) throw new IOException("un fichier porte déjà ce nom à destination");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            // File.Move gère l'inter-volumes (copie puis suppression) ; jamais d'écrasement.
            File.Move(src, dst, overwrite: false);
        }
        await RegisterAsync(src, dst, ct);
    }

    private async Task RegisterAsync(string src, string dst, CancellationToken ct)
    {
        try
        {
            await registrar.RegisterAsync(new RegisterFileRequest(dst, "organize", AddToHistory: false, ReplacedLocalPath: src), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Le fichier est déjà rangé : la base se rattrapera au prochain balayage de la bibliothèque.
            logger.LogWarning(ex, "Fichier rangé mais non enregistré en base : {Dst}", dst);
        }
    }

    /// <summary>
    /// Sous Linux, « madonna » existant n'est pas « Madonna » : chaque dossier visé est résolu sur le
    /// disque sans tenir compte de la casse avant d'être créé (pas de doublon de dossier).
    /// </summary>
    public static string ResolveCase(string root, string destination)
    {
        if (OperatingSystem.IsWindows() || !OrganizeText.IsInside(destination, root)) return destination;
        var rel = destination[(root.TrimEnd('/', '\\').Length + 1)..].Replace('\\', '/').Split('/');
        var current = root;
        for (var i = 0; i < rel.Length - 1; i++)
            current = OrganizeEngine.FindChildDirectory(current, rel[i]) ?? Path.Join(current, rel[i]);
        return Path.Join(current, rel[^1]);
    }

    /// <summary><c>prune_empty_dirs</c> : dossiers vides sous la racine, enfants avant parents (racine, dossiers cachés et corbeille exclus).</summary>
    public static List<string> PruneEmptyDirs(string root)
    {
        var removed = new List<string>();
        if (!Directory.Exists(root)) return removed;
        void Walk(string dir)
        {
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(dir).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
            foreach (var child in children)
            {
                if (AudioExtensions.IsExcludedFolder(Path.GetFileName(child))) continue;
                var info = new DirectoryInfo(child);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                Walk(child);
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(child).Any())
                    {
                        Directory.Delete(child);
                        removed.Add(child);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        Walk(root);
        return removed;
    }
}

/// <summary>
/// <c>organize.duplicates</c> — rapprochement approximatif (union-find sur trois clés), puis choix
/// du meilleur exemplaire de chaque groupe par <see cref="BestCopy"/> (le débit n'est lu que pour
/// les fichiers des groupes).
/// </summary>
public sealed class OrganizeDuplicatesHandler(OrganizeStore store, OrganizeEngine engine, LibraryPaths paths) : IJobHandler
{
    public Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var request = context.GetPayload<OrganizeDuplicatesRequest>();
        var sessionId = request.SessionId;
        var entries = store.Entries(sessionId);
        context.Report(0, 1, "Recherche des doublons…", force: true);
        var index = new ArtistIndex(entries);
        var groups = DuplicateFinder.Groups(entries, index.EntryKey);
        var target = engine.TargetFormat();

        var result = new List<OrganizeDuplicateGroup>();
        var n = 0;
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var candidates = group.Select(e =>
            {
                var (bitrate, size) = engine.Quality(sessionId, e);
                return new CopyCandidate(e.Path, bitrate, size, e.Format, OrganizeText.EntryArtist(e));
            }).ToList();
            var keep = BestCopy.Choose(candidates, target);
            var files = group.Select((e, i) => new OrganizeDuplicateFile(e.Index, e.Path, paths.ToDb(e.Path), e.Title, e.Artist, e.Format,
                candidates[i].Bitrate, candidates[i].Size, i == keep)).ToList();
            result.Add(new OrganizeDuplicateGroup(++n, files));
            context.Report(n, groups.Count, $"{n} / {groups.Count} groupe(s)");
        }
        var outcome = new OrganizeDuplicatesResult(sessionId, result, result.Sum(g => g.Files.Count), target);
        context.Report(1, 1, result.Count == 0 ? "Aucun fichier ne semble être la copie d'un autre." : $"{outcome.FileCount} fichiers dans {result.Count} groupe(s)", force: true);
        return Task.FromResult<JsonNode?>(JsonSerializer.SerializeToNode(outcome, JsonDefaults.Options));
    }
}

/// <summary>
/// <c>organize.tags</c> — écriture des propositions acceptées dans la revue. Par défaut seuls les
/// champs VIDES du fichier sont écrits ; <c>OverwriteExisting</c> (case « Corriger aussi… ») remplace
/// les tags contredits. Chaque fichier écrit est ré-enregistré dans <c>tracks</c>.
/// </summary>
public sealed class OrganizeWriteTagsHandler(LibraryPaths paths, FileRegistrar registrar, ILogger<OrganizeWriteTagsHandler> logger) : IJobHandler
{
    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var request = context.GetPayload<OrganizeWriteTagsRequest>();
        var failures = new List<OrganizeFailure>();
        var written = 0;
        var done = 0;
        foreach (var item in request.Items)
        {
            ct.ThrowIfCancellationRequested();
            var local = item.Path.StartsWith('/') || Path.IsPathRooted(item.Path) && File.Exists(item.Path) ? item.Path : paths.ToLocal(item.Path);
            var name = PyPath.Basename(local);
            var result = await Task.Run(() => AudioTagWriter.Write(local, item.Tags, null,
                item.OverwriteExisting ? TagWriteMode.Replace : TagWriteMode.FillEmpty), ct);
            if (result.Success)
            {
                written++;
                try { await registrar.RegisterAsync(new RegisterFileRequest(local, "tags", AddToHistory: false), ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Tags écrits mais non enregistrés : {Path}", local); }
            }
            else failures.Add(new OrganizeFailure(item.Path, name, result.Message));
            context.Item(item.Path, result.Success ? "ok" : "failed", result.Success ? null : result.Message);
            context.Report(++done, request.Items.Count, null, failures.Count);
        }
        var lines = new List<string> { $"{written} fichier(s) complété(s) sur {request.Items.Count}." };
        if (failures.Count > 0)
        {
            lines.Add($"\n{failures.Count} échec(s) :");
            lines.AddRange(failures.Take(10).Select(f => $"  • {f.Name} — {f.Message}"));
            if (failures.Count > 10) lines.Add($"  … et {failures.Count - 10} autre(s).");
        }
        return JsonSerializer.SerializeToNode(new OrganizeWriteTagsResult(written, request.Items.Count, failures, string.Join("\n", lines)), JsonDefaults.Options);
    }
}
