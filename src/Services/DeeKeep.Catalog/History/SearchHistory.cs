using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Common.Text;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Catalog;

namespace DeeKeep.Catalog.History;

/// <summary>
/// Historique des recherches (table <c>searches</c>, dont Catalog est propriétaire) :
/// <c>position</c> 0 = la plus récente, pas de limite de taille. Règles de l'original :
/// la nouvelle recherche est insérée en tête après retrait du doublon, les positions sont
/// réécrites (décalage de toute la liste), une requête retirée devient pierre tombale.
/// Écritures par <see cref="DeeKeepDb.UpsertMany"/> / <see cref="DeeKeepDb.MarkDeleted"/>, sérialisées.
/// </summary>
public sealed class SearchHistory(DeeKeepDb db, EventLog events)
{
    private readonly object _write = new();

    public IReadOnlyList<SearchHistoryEntry> List()
    {
        using var conn = db.Open(readOnly: true);
        return conn.Query<(string query, long position, double updated_at)>(
                "SELECT query, position, updated_at FROM searches WHERE deleted = 0 ORDER BY position, query")
            .Select(r => new SearchHistoryEntry(r.query, (int)r.position, r.updated_at)).ToList();
    }

    /// <summary>Place <paramref name="query"/> en tête. Requête vide : rien. Doublon retiré sans tenir compte de la casse.</summary>
    public IReadOnlyList<SearchHistoryEntry> Remember(string query)
    {
        var text = PyText.Strip(query ?? "");
        if (text.Length == 0) return List();
        lock (_write)
        {
            var current = List().Select(e => e.Query).Where(q => !q.Equals(text, StringComparison.OrdinalIgnoreCase)).ToList();
            current.Insert(0, text);
            Apply(current);
        }
        return Changed();
    }

    public IReadOnlyList<SearchHistoryEntry> Delete(string query)
    {
        lock (_write)
        {
            var current = List().Select(e => e.Query).Where(q => !q.Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();
            Apply(current);
        }
        return Changed();
    }

    public IReadOnlyList<SearchHistoryEntry> Clear()
    {
        lock (_write) Apply([]);
        return Changed();
    }

    /// <summary>Aligne la table sur la liste voulue : lignes déplacées réécrites, disparues en pierre tombale (<c>_mirror_searches</c>).</summary>
    private void Apply(IReadOnlyList<string> wanted)
    {
        var existing = List().ToDictionary(e => e.Query, e => e.Position, StringComparer.Ordinal);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        for (var i = 0; i < wanted.Count; i++)
            if (!existing.TryGetValue(wanted[i], out var pos) || pos != i)
                rows.Add(new Dictionary<string, object?> { ["query"] = wanted[i], ["position"] = (long)i });
        if (rows.Count > 0) db.UpsertMany(Schema.Searches, rows);
        var gone = existing.Keys.Where(q => !wanted.Contains(q, StringComparer.Ordinal)).ToList();
        if (gone.Count > 0) db.MarkDeleted(Schema.Searches, gone);
    }

    private IReadOnlyList<SearchHistoryEntry> Changed()
    {
        var list = List();
        events.Publish(EventTypes.SearchesChanged, new { count = list.Count });
        return list;
    }
}
