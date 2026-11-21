using System.Text.Json.Nodes;
using DeeKeep.Contracts.Library;

namespace DeeKeep.Gateway.Api;

/// <summary>Liste de chemins (en base, ou du serveur selon la route).</summary>
public sealed record PathsBody(IReadOnlyList<string> Paths);

public sealed record DeleteFilesBody(IReadOnlyList<string> Paths, string Reason);

public sealed record DesktopImportBody(string? DataDir = null, bool? Settings = null, bool? History = null);

/// <summary>Une valeur de tag à écrire (title, artist, album, albumartist, genre, date, track…).</summary>
public sealed record TagValueBody(string Field, string? Value);

public sealed record WriteTagsItemBody(string Path, IReadOnlyList<TagValueBody> Tags, string? CoverUrl = null, bool OverwriteExisting = false);

public sealed record WriteTagsBody(IReadOnlyList<WriteTagsItemBody> Items);

public static class LibraryApi
{
    public static void MapLibraryApi(this RouteGroupBuilder api)
    {
        // Réglages de l'application (objet JSON, défauts compris) ; PUT rend les réglages complets.
        api.MapGet("/settings", async (LibraryClient library, CancellationToken ct) => ToObject(await library.GetSettingsAsync(ct)));
        api.MapPut("/settings", async (LibraryClient library, JsonObject values, CancellationToken ct) =>
            ToObject(await library.SetSettingsAsync(values.ToDictionary(kv => kv.Key, kv => kv.Value?.DeepClone()), ct)));

        var g = api.MapGroup("/library");

        // Titres : recherche multi-tags, tri, pagination, état disque et archive (corps = TrackQuery).
        // Bibliothèque personnelle : un utilisateur ne voit que ses titres ; l'administrateur voit tout
        // (ou les titres d'un utilisateur choisi, champ userId) avec, pour chaque titre, ses utilisateurs.
        g.MapPost("/tracks/query", (HttpContext ctx, LibraryClient library, TrackQuery? query, CancellationToken ct) =>
        {
            var q = query ?? new TrackQuery();
            q = UserTracksApi.SeesEverything(ctx)
                ? q with { IncludeUsers = true }
                : q with { UserId = UserTracksApi.UserIdOf(ctx) ?? "", IncludeUsers = false };
            return library.QueryTracksAsync(q, ct);
        });
        g.MapGet("/tracks/one", async (LibraryClient library, string path, CancellationToken ct) => Results.Json(await library.GetTrackAsync(path, ct)));
        // Cinq propositions de valeurs de tags (dès 3 caractères).
        g.MapGet("/suggestions", (LibraryClient library, string text, int? limit, CancellationToken ct) => library.SuggestAsync(text, limit ?? 5, ct));

        // Historique des téléchargements, du plus ancien au plus récent.
        g.MapGet("/history", (LibraryClient library, CancellationToken ct) => library.GetHistoryAsync(ct));
        g.MapPost("/history/purge-missing", (LibraryClient library, CancellationToken ct) => library.PurgeMissingHistoryAsync(ct));
        g.MapPost("/history/clear", (LibraryClient library, CancellationToken ct) => library.ClearHistoryAsync(ct));

        // Pastilles de présence (✓ ≈ ○ ⏳) des titres proposés.
        g.MapPost("/presence", (LibraryClient library, IReadOnlyList<PresenceQuery> items, CancellationToken ct) => library.LookupPresenceAsync(items, ct));

        // Contenu d'un dossier du serveur (racines autorisées uniquement).
        g.MapGet("/browse", (LibraryClient library, string? path, bool? audioOnly, CancellationToken ct) =>
            library.ListDirectoryAsync(string.IsNullOrEmpty(path) ? null : path, audioOnly ?? false, ct));

        // Base : chemin, taille, lignes par table, index de présence.
        g.MapGet("/database", (LibraryClient library, CancellationToken ct) => library.GetDatabaseInfoAsync(ct));

        // Tâches : lecture des tags de fichiers du serveur, relecture de la bibliothèque, import desktop, écriture de tags.
        g.MapPost("/scan-files", (LibraryClient library, PathsBody body, CancellationToken ct) => library.ScanLocalFilesAsync(new LocalScanRequest(body.Paths), ct));
        g.MapPost("/rescan", (LibraryClient library, CancellationToken ct) => library.StartLibraryScanAsync(ct));
        g.MapPost("/import-desktop", (LibraryClient library, DesktopImportBody? body, CancellationToken ct) =>
            library.StartDesktopImportAsync(new DesktopImportRequest(body?.DataDir, body?.Settings ?? true, body?.History ?? true), ct));
        g.MapPost("/write-tags", (LibraryClient library, WriteTagsBody body, CancellationToken ct) =>
            library.WriteTagsAsync(new WriteTagsRequest(body.Items.Select(i => new WriteTagsItem(
                i.Path, i.Tags.GroupBy(t => t.Field).ToDictionary(t => t.Key, t => t.Last().Value), i.CoverUrl, i.OverwriteExisting)).ToList()), ct));

        // Supprime des fichiers (vers la corbeille du serveur) et leurs lignes de la base.
        // Supprimer un titre de sa bibliothèque : le fichier ne part à la corbeille que si plus personne ne l'utilise.
        g.MapPost("/delete-files", (HttpContext ctx, LibraryClient library, DeleteFilesBody body, CancellationToken ct) =>
            library.DeleteFilesAsync(new DeleteFilesRequest(body.Paths, body.Reason, UserId: UserTracksApi.UserIdOf(ctx),
                IsAdmin: UserTracksApi.SeesEverything(ctx)), ct));
    }

    private static JsonObject ToObject(IReadOnlyDictionary<string, JsonNode?> values)
    {
        var obj = new JsonObject();
        foreach (var (k, v) in values) obj[k] = v?.DeepClone();
        return obj;
    }
}
