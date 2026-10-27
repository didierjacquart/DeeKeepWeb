using System.Text.Json;
using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Common.Text;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Quality;
using DeeKeep.Quality.Analysis;

namespace DeeKeep.Quality.Data;

/// <summary>Mesure enregistrée dans la table <c>quality</c> (chemin tel qu'en base).</summary>
public sealed record StoredMeasure(string Path, string? Stamp, QualityMeasures Measures, double UpdatedAt);

/// <summary>
/// Ligne de l'écran Qualité (table <c>quality_entries</c> de <c>deekeep_web.db</c>) : ce que
/// l'écran desktop gardait en mémoire — tags, état de la référence, statut de remplacement.
/// La liste survit ainsi à la fermeture du navigateur et au redémarrage du service.
/// </summary>
public sealed class EntryRow
{
    public string Path { get; set; } = "";
    public string MatchKey { get; set; } = "";
    public long Uid { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public int Bitrate { get; set; }
    public string Format { get; set; } = "";
    public string? Tags { get; set; }
    public string RefState { get; set; } = QualityReferenceStates.Pending;
    public string? RefDurations { get; set; }
    public string? RefKey { get; set; }
    public string? Status { get; set; }
    public string? StatusDetail { get; set; }
    public string? RedownloadJob { get; set; }
    public double AddedAt { get; set; }
    public double UpdatedAt { get; set; }

    public IReadOnlyList<double> Durations =>
        string.IsNullOrEmpty(RefDurations) ? [] : JsonSerializer.Deserialize<List<double>>(RefDurations) ?? [];

    public IReadOnlyDictionary<string, string> TagMap =>
        string.IsNullOrEmpty(Tags) ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(Tags) ?? new();
}

/// <summary>
/// Accès aux données du service : table <c>quality</c> (propriété exclusive, écrite par
/// <see cref="DeeKeepDb.UpsertMany(TableSpec, IEnumerable{IReadOnlyDictionary{string, object?}}, bool, double?)"/>),
/// liste de l'écran et réglages dans <c>deekeep_web.db</c>.
/// </summary>
public sealed class QualityStore(DeeKeepDb db, WebDb webDb)
{
    private const string FolderKey = "quality:folder";
    private const string ListJobKey = "quality:list_job";
    private static readonly object SchemaLock = new();
    private static bool _schemaReady;

    // Cache mémoire des mesures (≈ 5 000 lignes) : la requête de l'écran les joint à chaque
    // appel ; il est rechargé quand la table change (empreinte COUNT + MAX(updated_at)),
    // y compris si l'application desktop l'a modifiée entre-temps.
    private readonly object _cacheLock = new();
    private Dictionary<string, StoredMeasure>? _measures;
    private (long Count, double Max) _measuresMark = (-1, -1);

    // ------------------------------------------------------------------ schéma web

    private System.Data.IDbConnection OpenWeb()
    {
        var conn = webDb.Open();
        if (_schemaReady) return conn;
        lock (SchemaLock)
        {
            if (!_schemaReady)
            {
                conn.Execute("""
                    CREATE TABLE IF NOT EXISTS quality_entries (
                        path TEXT PRIMARY KEY,
                        match_key TEXT NOT NULL,
                        uid INTEGER NOT NULL,
                        title TEXT, artist TEXT, album TEXT,
                        bitrate INTEGER NOT NULL DEFAULT 0,
                        format TEXT,
                        tags TEXT,
                        ref_state TEXT NOT NULL DEFAULT 'pending',
                        ref_durations TEXT,
                        ref_key TEXT,
                        status TEXT, status_detail TEXT, redownload_job TEXT,
                        added_at REAL NOT NULL,
                        updated_at REAL NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_quality_entries_key ON quality_entries (match_key);
                    CREATE INDEX IF NOT EXISTS idx_quality_entries_ref ON quality_entries (ref_state);
                    CREATE INDEX IF NOT EXISTS idx_quality_entries_job ON quality_entries (redownload_job);
                    """);
                _schemaReady = true;
            }
        }
        return conn;
    }

    // ------------------------------------------------------------------ table quality

    /// <summary>Toutes les mesures vivantes, par clé de rapprochement (chemin sans casse).</summary>
    public IReadOnlyDictionary<string, StoredMeasure> Measures()
    {
        using var conn = db.Open(readOnly: true);
        var mark = conn.QueryFirst<(long, double)>("SELECT COUNT(*), COALESCE(MAX(updated_at), 0.0) FROM quality");
        lock (_cacheLock)
        {
            if (_measures is not null && mark == _measuresMark) return _measures;
        }
        var map = new Dictionary<string, StoredMeasure>(StringComparer.Ordinal);
        foreach (var (path, stamp, measures, updated) in conn.Query<(string, string?, string?, double)>(
                     "SELECT path, stamp, measures, updated_at FROM quality WHERE deleted = 0"))
        {
            if (QualityMeasures.FromJson(measures) is not { } m) continue;
            var key = LibraryText.MatchKey(path);
            // Plusieurs casses d'un même chemin (la base en contient) : la plus récente l'emporte.
            if (map.TryGetValue(key, out var known) && known.UpdatedAt > updated) continue;
            map[key] = new StoredMeasure(path, stamp, m, updated);
        }
        lock (_cacheLock)
        {
            _measures = map;
            _measuresMark = mark;
        }
        return map;
    }

    /// <summary>
    /// Enregistre des mesures (ligne complète : chemin, empreinte, mesures, défauts). Une ligne
    /// de même chemin à une autre casse (clé du cache desktop, en minuscules) est remplacée
    /// par une pierre tombale : la table ne garde qu'une mesure par fichier.
    /// </summary>
    public void SaveMeasures(IReadOnlyList<(string DbPath, string? Stamp, QualityMeasures Measures, IReadOnlyList<string> Defects)> items)
    {
        if (items.Count == 0) return;
        var known = Measures();
        var stale = new List<string>();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var (path, stamp, measures, defects) in items)
        {
            if (known.TryGetValue(LibraryText.MatchKey(path), out var previous) && previous.Path != path) stale.Add(previous.Path);
            rows.Add(new Dictionary<string, object?>
            {
                ["path"] = path,
                ["stamp"] = stamp ?? "",
                ["measures"] = measures.ToJsonText(),
                ["defects"] = SettingsJson.Dump(JsonSerializer.SerializeToNode(defects)),
            });
        }
        using var conn = db.Open();
        using var tx = conn.BeginTransaction(deferred: false);
        DeeKeepDb.UpsertMany(conn, tx, Schema.Quality, rows);
        if (stale.Count > 0) DeeKeepDb.UpsertMany(conn, tx, Schema.Quality, Tombstones(stale));
        tx.Commit();
        Invalidate();
    }

    /// <summary>Pierres tombales (mesure d'un fichier remplacé ou disparu).</summary>
    public void DeleteMeasures(IReadOnlyCollection<string> dbPaths)
    {
        if (dbPaths.Count == 0) return;
        var known = Measures();
        var targets = dbPaths.Select(p => known.TryGetValue(LibraryText.MatchKey(p), out var m) ? m.Path : null)
            .OfType<string>().Distinct().ToList();
        if (targets.Count == 0) return;
        db.MarkDeleted(Schema.Quality, targets);
        Invalidate();
    }

    private static List<IReadOnlyDictionary<string, object?>> Tombstones(IEnumerable<string> paths) =>
        paths.Distinct().Select(p => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["path"] = p, ["stamp"] = null, ["measures"] = null, ["defects"] = null, ["deleted"] = 1L,
        }).ToList();

    private void Invalidate()
    {
        lock (_cacheLock) _measuresMark = (-1, -1);
    }

    // ------------------------------------------------------------------ autres tables (lecture)

    /// <summary>Paramètres <c>app_settings</c> (lecture directe autorisée).</summary>
    public Dictionary<string, System.Text.Json.Nodes.JsonNode?> Settings() => db.AllSettings();

    /// <summary>Durées de référence connues (table <c>reference_durations</c> du service Catalog), par clé.</summary>
    public IReadOnlyDictionary<string, (IReadOnlyList<double> Durations, double? FetchedAt)> ReferenceDurations(IEnumerable<string> keys)
    {
        var wanted = keys.Distinct().ToList();
        var result = new Dictionary<string, (IReadOnlyList<double>, double?)>(StringComparer.Ordinal);
        if (wanted.Count == 0) return result;
        using var conn = db.Open(readOnly: true);
        foreach (var chunk in wanted.Chunk(500))
        {
            foreach (var (key, durations, fetched) in conn.Query<(string, string?, double?)>(
                         "SELECT key, durations, fetched_at FROM reference_durations WHERE deleted = 0 AND key IN @keys", new { keys = chunk }))
            {
                List<double>? list = null;
                try { list = string.IsNullOrEmpty(durations) ? null : JsonSerializer.Deserialize<List<double>>(durations); }
                catch (JsonException) { }
                if (list is not null) result[key] = (list, fetched);
            }
        }
        return result;
    }

    /// <summary>Chemins réels (casse de <c>tracks</c>) par clé de rapprochement, pour l'import du cache desktop.</summary>
    public IReadOnlyDictionary<string, string> TrackPaths()
    {
        using var conn = db.Open(readOnly: true);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in conn.Query<string>("SELECT path FROM tracks WHERE deleted = 0")) map.TryAdd(LibraryText.MatchKey(path), path);
        return map;
    }

    // ------------------------------------------------------------------ liste de l'écran

    public IReadOnlyList<EntryRow> Entries()
    {
        using var conn = OpenWeb();
        return conn.Query<EntryRow>(SelectEntries + " ORDER BY uid").ToList();
    }

    public IReadOnlyList<EntryRow> Entries(IEnumerable<string> dbPaths)
    {
        var keys = dbPaths.Select(LibraryText.MatchKey).Distinct().ToList();
        if (keys.Count == 0) return [];
        using var conn = OpenWeb();
        var rows = new List<EntryRow>();
        foreach (var chunk in keys.Chunk(500)) rows.AddRange(conn.Query<EntryRow>(SelectEntries + " WHERE match_key IN @keys", new { keys = chunk }));
        return rows;
    }

    public EntryRow? EntryByJob(Guid jobId)
    {
        using var conn = OpenWeb();
        return conn.QueryFirstOrDefault<EntryRow>(SelectEntries + " WHERE redownload_job = @job", new { job = jobId.ToString() });
    }

    public IReadOnlyList<EntryRow> EntriesNeedingReference(bool includeNetwork, int limit)
    {
        using var conn = OpenWeb();
        return conn.Query<EntryRow>(SelectEntries + " WHERE ref_state = 'pending'" + (includeNetwork ? " OR ref_state = 'network'" : "") +
                                    " ORDER BY uid LIMIT @limit", new { limit }).ToList();
    }

    public int CountReferencesPending()
    {
        using var conn = OpenWeb();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM quality_entries WHERE ref_state IN ('pending', 'network')");
    }

    private const string SelectEntries = """
        SELECT path AS Path, match_key AS MatchKey, uid AS Uid, title AS Title, artist AS Artist, album AS Album,
               bitrate AS Bitrate, format AS Format, tags AS Tags, ref_state AS RefState, ref_durations AS RefDurations,
               ref_key AS RefKey, status AS Status, status_detail AS StatusDetail, redownload_job AS RedownloadJob,
               added_at AS AddedAt, updated_at AS UpdatedAt
        FROM quality_entries
        """;

    /// <summary>
    /// Ajoute ou met à jour des lignes (clé : chemin sans casse). Une ligne existante garde son
    /// identifiant d'écran (<c>uid</c>) et sa date d'ajout.
    /// </summary>
    public void UpsertEntries(IEnumerable<EntryRow> entries)
    {
        using var conn = OpenWeb();
        using var tx = conn.BeginTransaction();
        var now = Clock.UnixNow();
        var next = conn.ExecuteScalar<long>("SELECT COALESCE(MAX(uid), 0) FROM quality_entries", transaction: tx);
        foreach (var e in entries)
        {
            var x = conn.QueryFirstOrDefault<Existing>(
                "SELECT path AS Path, uid AS Uid, added_at AS AddedAt FROM quality_entries WHERE match_key = @k", new { k = e.MatchKey }, tx);
            if (x is not null)
            {
                e.Uid = x.Uid;
                e.AddedAt = x.AddedAt;
                if (x.Path != e.Path) conn.Execute("DELETE FROM quality_entries WHERE path = @p", new { p = x.Path }, tx);
            }
            else
            {
                e.Uid = ++next;
                e.AddedAt = now;
            }
            e.UpdatedAt = now;
            conn.Execute("""
                INSERT INTO quality_entries (path, match_key, uid, title, artist, album, bitrate, format, tags, ref_state, ref_durations,
                                             ref_key, status, status_detail, redownload_job, added_at, updated_at)
                VALUES (@Path, @MatchKey, @Uid, @Title, @Artist, @Album, @Bitrate, @Format, @Tags, @RefState, @RefDurations,
                        @RefKey, @Status, @StatusDetail, @RedownloadJob, @AddedAt, @UpdatedAt)
                ON CONFLICT(path) DO UPDATE SET match_key = excluded.match_key, uid = excluded.uid, title = excluded.title,
                    artist = excluded.artist, album = excluded.album, bitrate = excluded.bitrate, format = excluded.format,
                    tags = excluded.tags, ref_state = excluded.ref_state, ref_durations = excluded.ref_durations,
                    ref_key = excluded.ref_key, status = excluded.status, status_detail = excluded.status_detail,
                    redownload_job = excluded.redownload_job, updated_at = excluded.updated_at
                """, e, tx);
        }
        tx.Commit();
    }

    private sealed class Existing
    {
        public string Path { get; set; } = "";
        public long Uid { get; set; }
        public double AddedAt { get; set; }
    }

    /// <summary>Remplace le chemin d'une ligne (fichier retéléchargé rangé ailleurs).</summary>
    public void MoveEntry(string fromDbPath, string toDbPath)
    {
        using var conn = OpenWeb();
        using var tx = conn.BeginTransaction();
        var toKey = LibraryText.MatchKey(toDbPath);
        conn.Execute("DELETE FROM quality_entries WHERE match_key = @k AND match_key <> @from", new { k = toKey, from = LibraryText.MatchKey(fromDbPath) }, tx);
        conn.Execute("UPDATE quality_entries SET path = @to, match_key = @k, updated_at = @now WHERE match_key = @from",
            new { to = toDbPath, k = toKey, from = LibraryText.MatchKey(fromDbPath), now = Clock.UnixNow() }, tx);
        tx.Commit();
    }

    public int ClearEntries()
    {
        using var conn = OpenWeb();
        return conn.Execute("DELETE FROM quality_entries");
    }

    // ------------------------------------------------------------------ état de l'écran

    /// <summary>Dossier analysé (chemin local du serveur), null si jamais choisi.</summary>
    public string? Folder
    {
        get => webDb.GetValue(FolderKey);
        set => webDb.SetValue(FolderKey, value);
    }

    /// <summary>Tâche d'analyse qui a rempli la liste : une reprise après redémarrage ne la vide pas.</summary>
    public string? ListJob
    {
        get => webDb.GetValue(ListJobKey);
        set => webDb.SetValue(ListJobKey, value);
    }
}
