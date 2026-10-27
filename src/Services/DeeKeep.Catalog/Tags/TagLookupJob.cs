using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using DeeKeep.Common.Audio;
using DeeKeep.Common.Files;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Catalog;

namespace DeeKeep.Catalog.Tags;

/// <summary>
/// Propositions déjà produites, conservées dans <c>deekeep_web.db</c> (<c>catalog_proposals</c>) :
/// l'écran de revue les relit après un rechargement de page, et une tâche reprise après
/// redémarrage saute les fichiers déjà examinés.
/// </summary>
public sealed class ProposalStore(WebDb web)
{
    private static bool _created;

    private Microsoft.Data.Sqlite.SqliteConnection Open()
    {
        var conn = web.Open();
        if (!_created)
        {
            conn.Execute("""
                CREATE TABLE IF NOT EXISTS catalog_proposals (
                    job_id TEXT NOT NULL, idx INTEGER NOT NULL, path TEXT NOT NULL, data TEXT NOT NULL, created_at REAL NOT NULL,
                    PRIMARY KEY (job_id, idx));
                """);
            _created = true;
        }
        return conn;
    }

    public void Save(Guid jobId, TagProposal proposal)
    {
        using var conn = Open();
        conn.Execute("INSERT OR REPLACE INTO catalog_proposals (job_id, idx, path, data, created_at) VALUES (@j, @i, @p, @d, @t)",
            new { j = jobId.ToString(), i = proposal.Index, p = proposal.Path, d = JsonSerializer.Serialize(proposal, JsonDefaults.Options), t = DeeKeep.Common.Data.Clock.UnixNow() });
    }

    public IReadOnlyList<TagProposal> List(Guid jobId)
    {
        using var conn = Open();
        return conn.Query<string>("SELECT data FROM catalog_proposals WHERE job_id = @j ORDER BY idx", new { j = jobId.ToString() })
            .Select(d => JsonSerializer.Deserialize<TagProposal>(d, JsonDefaults.Options)!).ToList();
    }

    public int Prune(double olderThan)
    {
        using var conn = Open();
        return conn.Execute("DELETE FROM catalog_proposals WHERE created_at < @t", new { t = olderThan });
    }
}

/// <summary>
/// Tâche « tags.find » : un fichier à la fois (API publique, pas de parallélisme), une
/// <see cref="TagProposal"/> par fichier émise au fil de l'eau (<c>job.item</c>), y compris
/// « aucune correspondance ». Arrêt coopératif : le résultat du fichier en vol est jeté.
/// L'écran de revue et l'écriture des tags appartiennent à la tranche « Organiser ».
/// </summary>
public sealed class TagLookupJob(TagFinder finder, ProposalStore proposals, PathMap pathMap, ILogger<TagLookupJob> logger) : IJobHandler
{
    private static readonly string[] Fields = ["title", "artist", "album", "albumartist", "genre", "date", "track", "tracktotal", "disc"];

    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var request = context.GetPayload<TagLookupRequest>();
        var files = request.Files;
        var done = proposals.List(context.Id).Select(p => p.Index).ToHashSet();
        int sure = 0, probable = 0, none = 0;
        context.Report(done.Count, files.Count, "Recherche en ligne…", force: true);

        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (done.Contains(i)) continue;
            TagProposal proposal;
            try
            {
                proposal = await ExamineAsync(files[i], i, request, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Toute exception vaut « aucune proposition », comme TagLookupThread.
                logger.LogDebug(ex, "Recherche de tags en échec pour {Path}", files[i].Path);
                proposal = Empty(files[i], i, TagFinder.FromRequest(files[i]), "erreur pendant la recherche");
            }
            proposals.Save(context.Id, proposal);
            switch (proposal.Confidence)
            {
                case TagConfidence.Sure: sure++; break;
                case TagConfidence.Probable: probable++; break;
                default: none++; break;
            }
            context.Item(proposal.Path, proposal.Confidence, proposal.Note, proposal);
            context.Report(i + 1, files.Count, $"{i + 1} / {files.Count} fichiers", force: i + 1 == files.Count);
        }
        return JsonSerializer.SerializeToNode(new { sure, probable, none, total = files.Count });
    }

    private async Task<TagProposal> ExamineAsync(TagLookupFile file, int index, TagLookupRequest request, CancellationToken ct)
    {
        string? local = null;
        TagEntry entry;
        double? seconds = file.Duration;
        try
        {
            var candidate = pathMap.ToLocal(file.Path);
            if (File.Exists(candidate))
            {
                local = candidate;
                var tags = AudioTags.Read(candidate, withProperties: true, withCover: false);
                entry = TagFinder.FromTags(file.Path, tags);
                seconds = tags.Duration ?? seconds;
            }
            else entry = TagFinder.FromRequest(file);
        }
        catch (Exception)
        {
            entry = TagFinder.FromRequest(file);
        }

        var duration = TagFinder.LocalDuration(seconds);
        var found = await finder.FindAsync(entry, local, duration, request.UseAcoustId, request.Extras, ct);
        if (found is null) return Empty(file, index, entry, null);

        var current = Fields.ToDictionary(f => f, f => (string?)entry.Get(f));
        var best = found.Best;
        long? deezerId = found.Source == "Deezer" && long.TryParse(best.Id, out var id) ? id : null;
        return new TagProposal(
            file.Path, found.Sure ? TagConfidence.Sure : TagConfidence.Probable, found.Score, found.Source, current,
            found.Values.ToDictionary(kv => kv.Key, kv => (string?)kv.Value), found.Values.Keys.ToList(), found.Replaces.Keys.ToList(),
            string.IsNullOrEmpty(best.CoverUrl) ? null : best.CoverUrl, deezerId, found.Reason,
            found.Replaces.ToDictionary(kv => kv.Key, kv => (string?)kv.Value),
            best.Title, best.Artist, best.Album, best.Seconds, found.Query, found.Duration, index, entry.TitleTagged);
    }

    private static TagProposal Empty(TagLookupFile file, int index, TagEntry entry, string? note) => new(
        file.Path, TagConfidence.None, 0, "", Fields.ToDictionary(f => f, f => (string?)entry.Get(f)),
        new Dictionary<string, string?>(), TagMatcher.MissingFields(entry), [], null, null, note,
        Index: index, TitleTagged: entry.TitleTagged);
}
