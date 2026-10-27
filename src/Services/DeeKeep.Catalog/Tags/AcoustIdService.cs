using System.Globalization;
using System.Text.Json;
using DeeKeep.Catalog.Http;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Processes;
using DeeKeep.Contracts;
using DeeKeep.Contracts.Catalog;

namespace DeeKeep.Catalog.Tags;

/// <summary>Calculateur d'empreinte retenu : fpcalc (préféré) ou ffmpeg muni du muxer chromaprint.</summary>
public sealed record FingerprintTool(string Kind, string Path);

/// <summary>
/// Identification par empreinte acoustique (<c>acoustid_lookup.py</c>) : dernier recours
/// de la recherche de tags, quand ni les tags ni les noms ne disent rien. Deux prérequis
/// extérieurs : un calculateur d'empreinte (détecté en l'exécutant réellement) et une clé
/// AcoustID (secret de configuration <c>Secrets:AcoustIdKey</c>, jamais en base).
/// </summary>
public sealed class AcoustIdService(
    ProcessRunner runner,
    IHttpClientFactory httpFactory,
    CatalogGates gates,
    IConfiguration configuration,
    ILogger<AcoustIdService> logger) : IToolStatusProvider
{
    public const string LookupUrl = "https://api.acoustid.org/v2/lookup";
    public const string HttpClientName = "acoustid";
    /// <summary>Durée analysée : celle sur laquelle la base AcoustID a été construite.</summary>
    public const int FingerprintSeconds = 120;
    /// <summary>En dessous, au mieux un fragment commun.</summary>
    public const double MinMatch = 0.5;

    private readonly SemaphoreSlim _detection = new(1, 1);
    private bool _searched;
    private FingerprintTool? _tool;

    public string Key => PyText(configuration["Secrets:AcoustIdKey"]);

    private static string PyText(string? s) => DeeKeep.Common.Text.PyText.Strip(s ?? "");

    /// <summary><c>fingerprint_tool</c> : détection faite une fois (les appels concurrents attendent sa fin).</summary>
    public async Task<FingerprintTool?> ToolAsync(CancellationToken ct, bool refresh = false)
    {
        if (_searched && !refresh) return _tool;
        await _detection.WaitAsync(ct);
        try
        {
            if (_searched && !refresh) return _tool;
            _tool = await DetectAsync(ct);
            _searched = true;
            return _tool;
        }
        finally
        {
            _detection.Release();
        }
    }

    private async Task<FingerprintTool?> DetectAsync(CancellationToken ct)
    {
        var fpcalc = runner.Tools.Fpcalc;
        if (!string.IsNullOrWhiteSpace(fpcalc))
        {
            var result = await runner.RunAsync(fpcalc, ["-version"], ct, timeout: TimeSpan.FromSeconds(20));
            if (result.ExitCode == 0 || result.StdOut.Contains("fpcalc", StringComparison.OrdinalIgnoreCase))
                return new FingerprintTool("fpcalc", fpcalc);
        }
        var ffmpeg = runner.Tools.Ffmpeg;
        if (!string.IsNullOrWhiteSpace(ffmpeg))
        {
            var result = await runner.RunAsync(ffmpeg, ["-hide_banner", "-muxers"], ct, timeout: TimeSpan.FromSeconds(30));
            if (result.ExitCode == 0 && result.StdOut.Contains("chromaprint", StringComparison.Ordinal))
                return new FingerprintTool("ffmpeg", ffmpeg);
        }
        return null;
    }

    /// <summary><c>availability</c> : (disponible, message) — ce qui manque, dit en clair.</summary>
    public async Task<AcoustIdStatus> StatusAsync(CancellationToken ct)
    {
        var tool = await ToolAsync(ct);
        if (tool is null)
            return new AcoustIdStatus(false,
                "aucun calculateur d'empreinte : installez fpcalc (Chromaprint, paquet libchromaprint-tools), " +
                "ou un ffmpeg compilé avec le muxer chromaprint");
        if (Key.Length == 0)
            return new AcoustIdStatus(false,
                "clé AcoustID absente : elle est gratuite sur acoustid.org/new-application, " +
                "à renseigner dans la configuration du serveur (Secrets:AcoustIdKey)");
        return new AcoustIdStatus(true, tool.Kind == "fpcalc" ? "empreinte calculée par fpcalc" : "empreinte calculée par ffmpeg (muxer chromaprint)");
    }

    /// <summary><c>compute_fingerprint</c> : empreinte Chromaprint en base64, vide en cas d'échec.</summary>
    public async Task<string> FingerprintAsync(string localPath, CancellationToken ct)
    {
        var tool = await ToolAsync(ct);
        if (tool is null || !File.Exists(localPath)) return "";
        var seconds = FingerprintSeconds.ToString(CultureInfo.InvariantCulture);
        if (tool.Kind == "fpcalc")
        {
            var result = await runner.RunAsync(tool.Path, ["-json", "-length", seconds, localPath], ct, timeout: TimeSpan.FromSeconds(120));
            if (result.ExitCode != 0) return "";
            try
            {
                using var doc = JsonDocument.Parse(result.StdOut);
                return doc.RootElement.TryGetProperty("fingerprint", out var fp) ? fp.GetString() ?? "" : "";
            }
            catch (JsonException)
            {
                // Sortie texte (« FINGERPRINT=… ») des versions anciennes.
                foreach (var line in result.StdOut.Split('\n'))
                    if (line.StartsWith("FINGERPRINT=", StringComparison.OrdinalIgnoreCase))
                        return line[(line.IndexOf('=') + 1)..].Trim();
                return "";
            }
        }
        var ff = await runner.RunAsync(tool.Path,
            ["-hide_banner", "-loglevel", "error", "-i", localPath, "-t", seconds, "-ac", "1", "-ar", "22050",
             "-f", "chromaprint", "-fp_format", "base64", "-"], ct, timeout: TimeSpan.FromSeconds(120));
        return ff.ExitCode == 0 ? ff.StdOut.Trim() : "";
    }

    /// <summary>
    /// <c>lookup</c> : pistes identifiées par empreinte (jamais null). L'empreinte (plusieurs Ko)
    /// part en formulaire POST plutôt que dans l'URL ; la clé n'est jamais journalisée.
    /// </summary>
    public async Task<IReadOnlyList<TagCandidate>> LookupAsync(string localPath, int duration, CancellationToken ct)
    {
        var status = await StatusAsync(ct);
        if (!status.Available) return [];
        var fingerprint = await FingerprintAsync(localPath, ct);
        if (fingerprint.Length == 0) return [];
        try
        {
            await gates.AcoustId.WaitAsync(ct);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(20));
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client"] = Key,
                ["meta"] = "recordings+releasegroups",
                ["duration"] = duration.ToString(CultureInfo.InvariantCulture),
                ["fingerprint"] = fingerprint,
            });
            using var response = await httpFactory.CreateClient(HttpClientName).PostAsync(LookupUrl, content, linked.Token);
            if (!response.IsSuccessStatusCode) return [];
            using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(linked.Token));
            return ParseResults(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogDebug("AcoustID injoignable : {Error}", ex.Message);
            return [];
        }
    }

    /// <summary><c>parse_results</c> : enregistrements des résultats de score ≥ 0,5, meilleure concordance d'abord.</summary>
    public static IReadOnlyList<TagCandidate> ParseResults(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("status", out var s) || s.GetString() != "ok") return [];
        var tracks = new List<TagCandidate>();
        if (!payload.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return [];
        foreach (var result in results.EnumerateArray())
        {
            var match = result.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 0.0;
            if (match < MinMatch) continue;
            if (!result.TryGetProperty("recordings", out var recordings) || recordings.ValueKind != JsonValueKind.Array) continue;
            foreach (var recording in recordings.EnumerateArray())
            {
                var artists = recording.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
                    ? DeeKeep.Common.Text.PyText.Strip(string.Join(" ", a.EnumerateArray().Select(x => Deezer.DeezerJson.Str(x, "name") ?? "")))
                    : "";
                var groups = recording.TryGetProperty("releasegroups", out var g) && g.ValueKind == JsonValueKind.Array
                    ? g.EnumerateArray().Select(x => Deezer.DeezerJson.Str(x, "title") ?? "").ToList()
                    : [];
                tracks.Add(new TagCandidate(
                    Deezer.DeezerJson.Str(recording, "title") ?? "",
                    artists,
                    groups.Count > 0 ? groups[0] : "",
                    (int)(Deezer.DeezerJson.Long(recording, "duration") ?? 0),
                    "",
                    Albums: groups,
                    Match: match));
            }
        }
        // Tri stable, meilleure concordance d'abord (sort(key=-match)).
        return tracks.OrderByDescending(t => t.Match).ToList();
    }

    /// <summary>État de l'outil d'empreinte pour la page Diagnostic.</summary>
    public async Task<IReadOnlyList<ToolStatus>> GetAsync(CancellationToken ct)
    {
        var tool = await ToolAsync(ct);
        var status = await StatusAsync(ct);
        return [new ToolStatus("empreinte acoustique", status.Available, null, tool?.Path, status.Message)];
    }
}
