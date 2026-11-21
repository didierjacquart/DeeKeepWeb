using DeeKeep.Contracts.Clients;

namespace DeeKeep.Contracts.Library;

/// <summary>Utilisateur associé à un titre (l'a téléchargé, ou en a hérité).</summary>
public sealed record TrackUser(string Id, string Name, double AddedAt, string? Origin);

/// <summary>Lien utilisateur / titres (chemins en base).</summary>
public sealed record UserTracksRequest(string UserId, IReadOnlyList<string> Paths, string Origin = "manual");

/// <summary>Nombre de titres par utilisateur (filtre de la Bibliothèque, page Administration).</summary>
/// <remarks><c>long</c> : SQLite rend COUNT(*) en entier 64 bits, et Dapper ne convertit pas pour un constructeur de record.</remarks>
public sealed record UserTrackCount(string Id, string Name, long Tracks);

/// <summary>
/// Bibliothèques personnelles : chaque utilisateur a ses titres, un même fichier pouvant être
/// partagé par plusieurs. Table <c>library_user_tracks</c> de <c>deekeep_web.db</c> (service Library).
/// </summary>
[ServiceClient(ServiceNames.Library)]
public sealed class UserTracksClient(HttpClient http) : ServiceClient(http, ServiceNames.Library)
{
    public Task<IReadOnlyList<TrackUser>> UsersOfAsync(string dbPath, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<TrackUser>>($"/api/user-tracks/users?path={Uri.EscapeDataString(dbPath)}", ct);

    public Task<IReadOnlyList<UserTrackCount>> CountsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<UserTrackCount>>("/api/user-tracks/counts", ct);

    /// <summary>Associe des titres existants à un utilisateur ; rend le nombre de liens créés.</summary>
    public Task<int> LinkAsync(UserTracksRequest request, CancellationToken ct = default) => PostAsync<int>("/api/user-tracks/link", request, ct);

    /// <summary>Rompt des liens (sans toucher aux fichiers) ; rend le nombre de liens rompus.</summary>
    public Task<int> UnlinkAsync(UserTracksRequest request, CancellationToken ct = default) => PostAsync<int>("/api/user-tracks/unlink", request, ct);

    public Task<ClaimResult> PostClaimAsync(ClaimRequest request, CancellationToken ct = default) =>
        PostAsync<ClaimResult>("/api/user-tracks/claim", request, ct);
}

/// <summary>Piste demandée au téléchargement (clé = identifiant de la ligne côté appelant).</summary>
public sealed record ClaimItem(string Key, string Title, string Artist, string? Album, long? DeezerId);

/// <summary>
/// « Extraire » pour un utilisateur : les pistes déjà présentes (pastille ✓) lui sont associées sans
/// nouveau téléchargement ; <see cref="ExpectDeezerIds"/> : pistes déjà en cours d'extraction pour
/// quelqu'un d'autre, à lui associer quand leur fichier arrivera.
/// </summary>
public sealed record ClaimRequest(string UserId, IReadOnlyList<ClaimItem> Items, IReadOnlyList<long>? ExpectDeezerIds = null);

/// <summary>Clés des pistes associées à un fichier existant (à ne pas télécharger).</summary>
public sealed record ClaimResult(IReadOnlyList<string> Claimed);
