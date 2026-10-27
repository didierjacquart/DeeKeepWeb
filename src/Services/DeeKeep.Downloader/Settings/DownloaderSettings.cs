using System.Text.Json.Nodes;
using DeeKeep.Common.Data;
using DeeKeep.Common.Files;
using DeeKeep.Common.Processes;
using DeeKeep.Contracts.Downloader;
using DeeKeep.Downloader.Extraction;
using Microsoft.Extensions.Options;

namespace DeeKeep.Downloader.Settings;

/// <summary>Configuration du service (section <c>Downloader</c>).</summary>
public sealed class DownloaderOptions
{
    /// <summary>
    /// Extractions simultanées, tous écrans confondus (Musique, Réencoder, Qualité).
    /// L'original en lançait 10 par écran sans plafond commun ; sur un Raspberry Pi,
    /// ffmpeg et la bande passante saturent bien avant (et YouTube se méfie).
    /// </summary>
    public int MaxParallel { get; set; } = 2;

    /// <summary>Tentatives par piste (<c>max_retries</c>).</summary>
    public int MaxRetries { get; set; } = 10;

    /// <summary>Clients de lecture YouTube imposés à yt-dlp (figés en configuration, spec 01 §6.2).</summary>
    public List<string> PlayerClients { get; set; } = [.. YtDlpCommand.DefaultPlayerClients];

    /// <summary>Composants distants autorisés (scripts EJS de résolution des défis JavaScript) ; vide pour s'en passer.</summary>
    public string? RemoteComponents { get; set; } = "ejs:github";

    /// <summary>Fichier cookies.txt par défaut quand le réglage <c>cookies_file</c> est vide.</summary>
    public string? CookiesFile { get; set; }

    /// <summary>Mandataire éventuel pour YouTube (une IP de centre de données y est souvent bloquée).</summary>
    public string? Proxy { get; set; }

    /// <summary>Délai maximal d'une extraction (yt-dlp + ffmpeg) avant abandon de la tentative.</summary>
    public TimeSpan ExtractTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>Délai maximal d'une recherche YouTube.</summary>
    public TimeSpan SearchTimeout { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>Formats et qualités d'export, et lecture des réglages qui les fixent.</summary>
public static class ExportFormats
{
    public const string Lossless = "Lossless (Qualité Source)";
    public const string High = "320 kbps (HQ)";
    public const string Standard = "128 kbps (Standard)";

    public static readonly IReadOnlyList<ExportFormatChoice> Choices =
    [
        new("MP3", [High, Standard]),
        new("FLAC", [Lossless]),
        new("WAV", [Lossless]),
        new("M4A", [High, Standard]),
    ];

    public static readonly ExportOptions Default = new("MP3", High);

    /// <summary>Format en majuscules et qualité permise pour ce format (la première à défaut), comme les listes des Paramètres.</summary>
    public static ExportOptions Normalize(string? format, string? quality)
    {
        var choice = Choices.FirstOrDefault(c => c.Format.Equals((format ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) ?? Choices[0];
        var q = choice.Qualities.FirstOrDefault(x => x.Equals((quality ?? "").Trim(), StringComparison.Ordinal)) ?? choice.Qualities[0];
        return new ExportOptions(choice.Format, q);
    }

    /// <summary>Débit visé : 128 ou 320 si la qualité le dit, 0 sinon (sans perte).</summary>
    public static int TargetBitrate(string? quality) =>
        (quality ?? "").Contains("128", StringComparison.Ordinal) ? 128 : (quality ?? "").Contains("320", StringComparison.Ordinal) ? 320 : 0;
}

/// <summary>Réglages lus dans <c>app_settings</c> au moment du lancement d'une extraction.</summary>
public sealed class DownloaderSettings(DeeKeepDb db, PathMap pathMap, IOptions<DownloaderOptions> options, IOptions<ToolOptions> tools,
    ILogger<DownloaderSettings> logger)
{
    public DownloaderOptions Options => options.Value;

    private Dictionary<string, JsonNode?> Read()
    {
        try
        {
            return db.AllSettings();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Réglages illisibles, valeurs par défaut");
            return [];
        }
    }

    private static string? Text(Dictionary<string, JsonNode?> settings, string key) =>
        settings.TryGetValue(key, out var node) && node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Format et qualité : ceux demandés, sinon ceux des Paramètres au moment de l'appel.</summary>
    public ExportOptions Export(ExportOptions? requested = null)
    {
        if (requested is not null && !string.IsNullOrWhiteSpace(requested.Format)) return ExportFormats.Normalize(requested.Format, requested.Quality);
        var settings = Read();
        return ExportFormats.Normalize(Text(settings, "file_format") ?? ExportFormats.Default.Format, Text(settings, "quality") ?? ExportFormats.Default.Quality);
    }

    /// <summary>
    /// Fichier cookies.txt à transmettre à yt-dlp (réglage <c>cookies_file</c>, sinon la configuration),
    /// s'il existe. Les cookies d'un navigateur (<c>youtube_browser</c>) n'ont pas de sens sur un
    /// serveur sans navigateur : seul le fichier est repris (spec 01 §6.1).
    /// </summary>
    public string? CookiesFile()
    {
        var configured = Text(Read(), "cookies_file");
        foreach (var candidate in new[] { configured, options.Value.CookiesFile })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (File.Exists(candidate)) return candidate;
            var local = pathMap.ToLocal(candidate);
            if (File.Exists(local)) return local;
        }
        return null;
    }

    /// <summary>Chemin configuré du fichier cookies.txt, existant ou non (page Diagnostic).</summary>
    public string? ConfiguredCookiesFile() => Text(Read(), "cookies_file") is { Length: > 0 } s ? s : options.Value.CookiesFile;

    /// <summary>Réglages d'appel de yt-dlp (cookies, ffmpeg, deno, clients de lecture, mandataire).</summary>
    public YtDlpSettings YtDlp(string? cookiesFile)
    {
        var t = tools.Value;
        return new YtDlpSettings(
            CookiesFile: cookiesFile,
            FfmpegLocation: Path.IsPathRooted(t.Ffmpeg) ? Path.GetDirectoryName(t.Ffmpeg) : null,
            DenoPath: Path.IsPathRooted(t.Deno) && File.Exists(t.Deno) ? t.Deno : null,
            PlayerClients: options.Value.PlayerClients,
            RemoteComponents: options.Value.RemoteComponents,
            Proxy: options.Value.Proxy);
    }

    /// <summary>
    /// Environnement de yt-dlp : sortie en UTF-8 (titres non latins), et dossiers de deno
    /// et de ffmpeg en tête du PATH (runtime JavaScript exigé par YouTube, <c>ensure_deno_on_path</c>).
    /// </summary>
    public Dictionary<string, string> YtDlpEnvironment()
    {
        var t = tools.Value;
        var env = new Dictionary<string, string> { ["PYTHONIOENCODING"] = "utf-8", ["PYTHONUTF8"] = "1" };
        var dirs = new[] { t.Deno, t.Ffmpeg }.Where(Path.IsPathRooted).Select(Path.GetDirectoryName).OfType<string>().Distinct().ToList();
        if (dirs.Count > 0)
            env["PATH"] = string.Join(Path.PathSeparator, dirs) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        return env;
    }
}
