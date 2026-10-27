using System.Text.Json;
using DeeKeep.Catalog.Deezer;
using DeeKeep.Catalog.Tags;
using DeeKeep.Common.Text;

namespace DeeKeep.Catalog.MusicBrainz;

/// <summary>Lecture des enregistrements MusicBrainz.</summary>
public static class MusicBrainzRecords
{
    /// <summary>
    /// <c>search_musicbrainz</c> → candidat : artiste = noms crédités joints par une espace
    /// (les « feat. » et « & » de liaison sont perdus, comme dans l'original), album = première
    /// parution, durée arrondie à la seconde.
    /// </summary>
    public static TagCandidate ToCandidate(JsonElement record)
    {
        var credit = record.ValueKind == JsonValueKind.Object && record.TryGetProperty("artist-credit", out var c) && c.ValueKind == JsonValueKind.Array
            ? PyText.Strip(string.Join(" ", c.EnumerateArray().Select(p => p.Str("name") ?? "")))
            : "";
        var releases = record.ValueKind == JsonValueKind.Object && record.TryGetProperty("releases", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Select(x => x.Str("title") ?? "").ToList()
            : [];
        var length = LengthMs(record);
        return new TagCandidate(
            record.Str("title") ?? "",
            credit,
            releases.Count > 0 ? releases[0] : "",
            length is { } ms and > 0 ? RoundSeconds(ms) : 0,
            "",
            Albums: releases);
    }

    /// <summary>Durée en millisecondes (<c>length</c>), null si absente.</summary>
    public static double? LengthMs(JsonElement record) =>
        record.ValueKind == JsonValueKind.Object && record.TryGetProperty("length", out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetDouble(out var d) ? d : null;

    /// <summary><c>int(round(ms / 1000))</c> : arrondi au pair, comme Python.</summary>
    public static int RoundSeconds(double milliseconds) => (int)Math.Round(milliseconds / 1000.0, MidpointRounding.ToEven);

    /// <summary>Score de confiance du moteur (<c>int(score or 0)</c>).</summary>
    public static int Score(JsonElement record) => (int)(record.Long("score") ?? 0);
}
