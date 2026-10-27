using System.Text.Json.Nodes;
using DeeKeep.Contracts.Library;
using DeeKeep.Contracts.Organize;

namespace DeeKeep.Gateway.Domains.Organize;

/// <summary>Options de rangement (volet « Filtres » de la page Organiser).</summary>
public sealed record OrganizeOptionsInput(
    bool Artist = true, bool Album = true, bool Rename = false, string Pattern = "%titre% - %artiste%", bool Divers = false,
    int DiversMin = 3, bool KeepAlbums = true, bool Dedup = true, bool RemoveEmpty = true, bool ReportUntagged = true, bool FiltersOpen = false)
{
    public OrganizeOptions ToOptions() => new(Artist, Album, Rename, Pattern, Divers, DiversMin, KeepAlbums, Dedup, RemoveEmpty, ReportUntagged, FiltersOpen);
}

/// <summary>Un champ de tag (title, artist, album, albumartist, genre, date, track, tracktotal, disc…).</summary>
public sealed record TagFieldInput(string Field, string? Value);

/// <summary>Écriture des tags d'une correspondance acceptée dans la revue.</summary>
public sealed record OrganizeTagWriteInput(string Path, IReadOnlyList<TagFieldInput> Fields, bool OverwriteExisting = false)
{
    public WriteTagsItem ToItem() => new(Path, Fields.GroupBy(f => f.Field).ToDictionary(g => g.Key, g => g.Last().Value), null, OverwriteExisting);
}

/// <summary>Proposition reçue de la recherche des tags : mêmes données que l'événement jobItem (type « tags.find »).</summary>
public sealed record OrganizeProposalRow(Guid JobId, string ItemKey, string Status, string? Message, string? Path, JsonNode? Data, double At);
