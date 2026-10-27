using System.Globalization;

namespace DeeKeep.Downloader.Extraction;

/// <summary>
/// Classement des échecs d'extraction — portage de <c>_is_permanent</c> et de la
/// boucle de tentatives de <c>TrackDownloaderThread.run</c> (<c>download_thread.py:68-78, 236-285</c>).
/// </summary>
public static class Failures
{
    /// <summary>
    /// _PERMANENT_MARKERS : la vidéo est hors d'atteinte, pas momentanément
    /// indisponible ; une nouvelle tentative rendrait le même verdict.
    /// </summary>
    public static readonly IReadOnlyList<string> PermanentMarkers =
    [
        "confirm your age", "age-restricted", "private video",
        "removed by the uploader", "members-only", "this video is unavailable",
        "video unavailable", "not available in your country", "who has blocked it",
    ];

    /// <summary>
    /// Refus de l'adresse IP du serveur par YouTube (spec 01 §6.2). L'original le
    /// tenait pour passager et épuisait dix tentatives en vain : ce refus ne vise pas
    /// la vidéo mais la machine, aucune autre vidéo ni aucun nouvel essai n'y change rien.
    /// </summary>
    public static readonly IReadOnlyList<string> BlockedMarkers =
    [
        "sign in to confirm you", "confirm you're not a bot", "confirm you are not a bot",
    ];

    /// <summary>Vrai si l'échec ne se dissipera pas à l'essai suivant (marqueurs de l'original).</summary>
    public static bool IsPermanent(string? message)
    {
        var text = (message ?? "").ToLowerInvariant();
        return PermanentMarkers.Any(text.Contains);
    }

    /// <summary>Vrai si YouTube refuse l'adresse du serveur elle-même (réclame une connexion).</summary>
    public static bool IsServerBlocked(string? message)
    {
        var text = (message ?? "").ToLowerInvariant();
        return BlockedMarkers.Any(text.Contains);
    }

    /// <summary>Message affiché quand YouTube bloque l'adresse du serveur.</summary>
    public static string ServerBlockedMessage(string detail, bool hasCookies) =>
        (hasCookies
            ? "YouTube refuse cette adresse IP malgré le fichier cookies.txt (session expirée ?) : exportez-en un nouveau depuis un navigateur connecté. "
            : "YouTube bloque l'adresse IP du serveur et réclame une connexion : fournissez un fichier cookies.txt dans les Paramètres. ")
        + $"({detail})";

    /// <summary>Délai avant l'essai suivant : <c>min(5, n × 5 / (max − 1))</c> s (0,56 s, 1,11 s… 4,44 s pour 10 essais).</summary>
    public static double RetryDelaySeconds(int attempt, int maxRetries) => Math.Min(5.0, attempt * 5.0 / Math.Max(1, maxRetries - 1));

    /// <summary>« Échec, nouvelle tentative dans 1.1 s (essai 3/10)... » (format Python <c>{delay:.1f}</c>).</summary>
    public static string RetryText(int attempt, int maxRetries) =>
        $"Échec, nouvelle tentative dans {RetryDelaySeconds(attempt, maxRetries).ToString("0.0", CultureInfo.InvariantCulture)} s (essai {attempt + 1}/{maxRetries})...";

    /// <summary>Texte de progression avec le rappel de l'essai en cours à partir du deuxième (<c>_emit_progress</c>).</summary>
    public static string WithAttempt(string text, int attempt, int maxRetries) =>
        attempt > 1 ? $"{text} (essai {attempt}/{maxRetries})" : text;

    /// <summary>Échec après la dernière tentative.</summary>
    public static string GaveUp(int maxRetries, string lastMessage) => $"Abandonné après {maxRetries} tentatives : {lastMessage}";

    /// <summary>Tous les candidats ont été refusés définitivement.</summary>
    public static string AllRefused(int candidates, string lastMessage) =>
        (candidates == 1 ? "La seule vidéo trouvée n'est pas accessible" : $"Aucune des {candidates} vidéos trouvées n'est accessible")
        + $" : {lastMessage}";

    /// <summary>Statut d'une ligne en échec : « Erreur » (1 essai) ou « Erreur / Abandonné (n essais) ».</summary>
    public static string FailedStatus(int attempts) => attempts > 1 ? $"Erreur / Abandonné ({attempts} essais)" : "Erreur";
}
