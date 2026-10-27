using System.Text.Json;
using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Contracts.Catalog;

namespace DeeKeep.Catalog.Reference;

/// <summary>Table <c>reference_durations</c> (propriété de Catalog) : clé « artiste|titre » normalisée, durées JSON, horodatage.</summary>
public sealed class ReferenceStore(DeeKeepDb db)
{
    /// <summary>Une recherche restée vide expire après 30 jours (un catalogue s'enrichit).</summary>
    public static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(30);

    /// <summary>Durées connues, ou null si jamais cherché (ou recherche négative expirée).</summary>
    public (IReadOnlyList<double> Durations, double? FetchedAt)? Get(string key)
    {
        using var conn = db.Open(readOnly: true);
        var row = conn.QueryFirstOrDefault<(string? durations, double? fetched_at)?>(
            "SELECT durations, fetched_at FROM reference_durations WHERE key = @key AND deleted = 0", new { key });
        if (row is null || row.Value.durations is null) return null;
        List<double> list;
        try { list = JsonSerializer.Deserialize<List<double>>(row.Value.durations) ?? []; }
        catch (JsonException) { return null; }
        if (list.Count == 0 && Clock.UnixNow() - (row.Value.fetched_at ?? 0) > NegativeTtl.TotalSeconds) return null;
        return (list, row.Value.fetched_at);
    }

    public void Put(string key, IReadOnlyList<double> durations, double? fetchedAt = null) =>
        PutMany([(key, durations, fetchedAt ?? Math.Round(Clock.UnixNow(), 3))]);

    public int PutMany(IEnumerable<(string Key, IReadOnlyList<double> Durations, double FetchedAt)> items) =>
        db.UpsertMany(Schema.ReferenceDurations, items.Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["key"] = i.Key,
            ["durations"] = SettingsJson.Dump(i.Durations.Select(d => (long)Math.Round(d)).ToList()),
            ["fetched_at"] = Math.Round(i.FetchedAt, 3),
        }).ToList());

    public HashSet<string> Keys()
    {
        using var conn = db.Open(readOnly: true);
        return conn.Query<string>("SELECT key FROM reference_durations WHERE deleted = 0").ToHashSet(StringComparer.Ordinal);
    }
}
