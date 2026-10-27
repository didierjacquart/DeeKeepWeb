using System.Text.Json.Nodes;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Jobs;
using DeeKeep.Contracts.Library;

namespace DeeKeep.Library.Files;

/// <summary>Navigation dans le système de fichiers du serveur, limitée aux racines autorisées.</summary>
public sealed class FsService(LibraryPaths paths)
{
    public FsListing List(string? localPath, bool audioOnly)
    {
        var roots = paths.Roots();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            var first = roots.FirstOrDefault(Directory.Exists);
            if (first is null) return new FsListing("", null, roots.Select(r => new FsEntry(r, r, true, null, null, false)).ToList(), roots);
            localPath = first;
        }
        string full;
        try { full = Path.GetFullPath(localPath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { throw new ArgumentException("Chemin invalide."); }
        if (!paths.IsAllowed(full)) throw new ArgumentException("Dossier hors des racines autorisées.");
        if (!Directory.Exists(full)) throw new ArgumentException("Dossier introuvable.");

        var entries = new List<FsEntry>();
        try
        {
            foreach (var e in new DirectoryInfo(full).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
            {
                var mtime = new DateTimeOffset(e.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000.0;
                if (e is DirectoryInfo d)
                {
                    if (LibraryPaths.IsExcludedFolder(d.Name)) continue;
                    entries.Add(new FsEntry(d.Name, d.FullName, true, null, mtime, false));
                }
                else if (e is FileInfo f)
                {
                    var audio = AudioExtensions.IsAudioFile(f.Name);
                    if (audioOnly && !audio) continue;
                    entries.Add(new FsEntry(f.Name, f.FullName, false, f.Length, mtime, audio));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new ArgumentException("Dossier illisible."); }

        var parentPath = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(full));
        var parent = parentPath is not null && paths.IsAllowed(parentPath) ? parentPath : null;
        entries = entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new FsListing(full, parent, entries, roots);
    }

    public LocalFileInfo? Info(string localPath)
    {
        if (!File.Exists(localPath) || !paths.IsAllowed(localPath)) return null;
        var t = AudioTags.Read(localPath);
        return new LocalFileInfo(localPath, paths.ToDb(localPath), t.Title, t.Artist, t.Album, t.AlbumArtist, t.Genre, t.Year,
            t.TrackNo, t.Duration, t.Bitrate > 0 ? t.Bitrate : null, t.SampleRate, t.Codec, t.Ext, new FileInfo(localPath).Length, t.Cover is not null);
    }
}

/// <summary>Tâche <c>files.scan</c> : tags d'une liste de fichiers ou dossiers, un <see cref="LocalFileInfo"/> par élément.</summary>
public sealed class LocalScanHandler(FsService fs, LibraryPaths paths) : IJobHandler
{
    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var request = context.GetPayload<LocalScanRequest>();
        var files = new List<string>();
        foreach (var p in request.Paths)
        {
            if (!paths.IsAllowed(p)) continue;
            if (Directory.Exists(p)) files.AddRange(AudioExtensions.IterAudioFiles(p, ct));
            else if (File.Exists(p) && AudioExtensions.IsAudioFile(p)) files.Add(p);
        }
        files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int done = 0, failed = 0;
        context.Report(0, files.Count, $"Analyse en cours… 0 / {files.Count} fichier(s) analysé(s)");
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var info = await Task.Run(() => fs.Info(file), ct);
            if (info is null) { failed++; context.Item(file, "failed", "Fichier illisible"); }
            else { done++; context.Item(file, "ok", null, info); }
            context.Report(done, files.Count, $"Analyse en cours… {done + failed} / {files.Count} fichier(s) analysé(s)", failed);
        }
        return new JsonObject { ["files"] = files.Count, ["failed"] = failed };
    }
}

/// <summary>Tâche <c>tags.write</c> : écrit des tags (et une jaquette), puis relit la ligne de <c>tracks</c>.</summary>
public sealed class WriteTagsHandler(LibraryPaths paths, FileRegistrar registrar, IHttpClientFactory http) : IJobHandler
{
    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var request = context.GetPayload<WriteTagsRequest>();
        int done = 0, failed = 0;
        context.Report(0, request.Items.Count);
        foreach (var item in request.Items)
        {
            ct.ThrowIfCancellationRequested();
            var local = paths.ToLocal(item.Path);
            byte[]? cover = null;
            try
            {
                if (!string.IsNullOrEmpty(item.CoverUrl))
                {
                    using var client = http.CreateClient();
                    cover = await client.GetByteArrayAsync(item.CoverUrl, ct);
                }
            }
            catch (HttpRequestException)
            {
                // Sans jaquette, les tags sont écrits quand même.
            }
            var result = !paths.IsAllowed(local)
                ? new TagWriteResult(false, "hors des dossiers de la bibliothèque", [])
                : await Task.Run(() => AudioTagWriter.Write(local, item.Tags, cover, item.OverwriteExisting ? TagWriteMode.Replace : TagWriteMode.FillEmpty), ct);
            if (result.Success)
            {
                done++;
                if (result.Written.Count > 0) await registrar.RegisterAsync(new RegisterFileRequest(local, "tags", false), ct);
                context.Item(item.Path, "ok", null, new JsonObject { ["written"] = new JsonArray(result.Written.Select(w => (JsonNode?)JsonValue.Create(w)).ToArray()) });
            }
            else { failed++; context.Item(item.Path, "failed", result.Message); }
            context.Report(done, request.Items.Count, null, failed);
        }
        return new JsonObject { ["written"] = done, ["failed"] = failed };
    }
}
