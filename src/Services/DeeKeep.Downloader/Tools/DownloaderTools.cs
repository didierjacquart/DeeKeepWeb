using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Jobs;
using DeeKeep.Common.Processes;
using DeeKeep.Contracts;
using DeeKeep.Downloader.Settings;

namespace DeeKeep.Downloader.Tools;

/// <summary>
/// État des outils de l'extraction pour la page Diagnostic (et <c>/internal/info</c>) :
/// yt-dlp, ffmpeg, ffprobe, deno, fichier cookies.txt. Mis en cache une minute :
/// lancer quatre processus à chaque affichage pèserait sur un Raspberry Pi.
/// </summary>
public sealed class DownloaderToolStatus(ProcessRunner runner, DownloaderSettings settings) : IToolStatusProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<ToolStatus>? _cache;
    private DateTime _cachedAt;

    public const string ServerIpNote =
        "Depuis un serveur, YouTube bloque souvent les adresses IP de centres de données (« Sign in to confirm you're not a bot », " +
        "erreurs 403 répétées) : fournissez un fichier cookies.txt (format Netscape) d'un compte dédié dans les Paramètres. " +
        "yt-dlp évolue chaque semaine : mettez-le à jour depuis cette page en cas d'échecs généralisés.";

    public void Invalidate() => _cache = null;

    public async Task<IReadOnlyList<ToolStatus>> GetAsync(CancellationToken ct)
    {
        if (_cache is { } cached && DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(1)) return cached;
        await _lock.WaitAsync(ct);
        try
        {
            if (_cache is { } again && DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(1)) return again;
            var tools = runner.Tools;
            var probes = await Task.WhenAll(
                ProbeAsync("yt-dlp", tools.YtDlp, ["--version"], ServerIpNote, ct),
                ProbeAsync("ffmpeg", tools.Ffmpeg, ["-hide_banner", "-version"], "Conversion audio de l'extraction (sans lui, la conversion échoue).", ct),
                ProbeAsync("ffprobe", tools.Ffprobe, ["-hide_banner", "-version"], null, ct),
                ProbeAsync("deno", tools.Deno, ["--version"], "Runtime JavaScript exigé par yt-dlp pour résoudre les défis de YouTube.", ct));
            var cookies = settings.ConfiguredCookiesFile();
            var found = settings.CookiesFile();
            var cookieStatus = new ToolStatus("cookies.txt", found is not null, null, found ?? cookies,
                found is not null ? "Cookies YouTube transmis à yt-dlp."
                : string.IsNullOrEmpty(cookies) ? "Aucun fichier : extraction anonyme (souvent refusée depuis un serveur)."
                : "Fichier introuvable sur le serveur : extraction anonyme.");
            _cache = [.. probes, cookieStatus];
            _cachedAt = DateTime.UtcNow;
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<ToolStatus> ProbeAsync(string name, string exe, string[] args, string? detail, CancellationToken ct)
    {
        var result = await runner.RunAsync(exe, args, ct, timeout: TimeSpan.FromSeconds(20));
        if (!result.Success)
            return new ToolStatus(name, false, null, exe, (result.ExitCode == -1 ? "Non détecté" : $"Erreur (code {result.ExitCode})") +
                                                       (detail is null ? "" : " — " + detail));
        var first = (result.StdOut.Length > 0 ? result.StdOut : result.StdErr).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return new ToolStatus(name, true, Version(name, first), exe, detail);
    }

    /// <summary>« ffmpeg version 8.1.2-essentials… » → « 8.1.2-essentials… » ; « deno 2.5.0 (stable…) » → « 2.5.0 ».</summary>
    private static string Version(string name, string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (name is "ffmpeg" or "ffprobe" && parts.Length >= 3 && parts[1] == "version") return parts[2];
        if (name == "deno" && parts.Length >= 2) return parts[1];
        return line;
    }
}

/// <summary>
/// Tâche « tools.update » : mise à jour de yt-dlp. D'abord <c>yt-dlp -U</c> (binaire
/// officiel autonome) ; s'il refuse (installation par pip, paquet système), le binaire
/// de la dernière version est téléchargé depuis les publications officielles GitHub et
/// remplace l'ancien de façon atomique — à condition que le chemin configuré soit un
/// fichier accessible en écriture.
/// </summary>
public sealed class UpdateYtDlpJobHandler(ProcessRunner runner, DownloaderToolStatus status, IHttpClientFactory http,
    ILogger<UpdateYtDlpJobHandler> logger) : IJobHandler
{
    public const string HttpClientName = "github";

    public async Task<JsonNode?> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var exe = runner.Tools.YtDlp;
        context.Report(0, 3, "Version actuelle…", force: true);
        var before = await VersionAsync(exe, ct);

        context.Report(1, 3, "yt-dlp -U…", force: true);
        var update = await runner.RunAsync(exe, ["-U"], ct, timeout: TimeSpan.FromMinutes(5));
        var log = (update.StdOut + "\n" + update.StdErr).Trim();
        var method = "yt-dlp -U";
        if (!update.Success || log.Contains("ERROR", StringComparison.Ordinal))
        {
            var path = ResolveExecutable(exe);
            if (path is null)
                throw new PermanentJobException($"Mise à jour impossible : {LastLine(log)} (« {exe} » n'est pas un fichier remplaçable ; mettez yt-dlp à jour avec le gestionnaire de paquets).");
            context.Report(2, 3, "Téléchargement du binaire officiel…", force: true);
            await DownloadReleaseAsync(path, ct);
            method = "binaire officiel";
        }
        status.Invalidate();
        var after = await VersionAsync(exe, ct);
        context.Report(3, 3, before == after ? $"yt-dlp est à jour ({after})" : $"yt-dlp mis à jour : {before} → {after}", force: true);
        return new JsonObject { ["before"] = before, ["after"] = after, ["method"] = method, ["log"] = LastLine(log) };
    }

    private async Task<string?> VersionAsync(string exe, CancellationToken ct)
    {
        var r = await runner.RunAsync(exe, ["--version"], ct, timeout: TimeSpan.FromSeconds(30));
        return r.Success ? r.StdOut.Trim() : null;
    }

    private static string LastLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";

    /// <summary>Fichier réel de l'exécutable configuré, s'il est remplaçable (chemin absolu existant).</summary>
    private static string? ResolveExecutable(string exe)
    {
        if (!Path.IsPathRooted(exe) || !File.Exists(exe)) return null;
        var info = new FileInfo(exe);
        return info.LinkTarget is { } target ? Path.GetFullPath(target, info.DirectoryName!) : info.FullName;
    }

    /// <summary>Nom de l'exécutable autonome publié par yt-dlp pour cette machine.</summary>
    public static string ReleaseAsset() =>
        OperatingSystem.IsWindows() ? (RuntimeInformation.OSArchitecture == Architecture.X86 ? "yt-dlp_x86.exe" : "yt-dlp.exe")
        : OperatingSystem.IsMacOS() ? "yt-dlp_macos"
        : RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "yt-dlp_linux_aarch64"
        : RuntimeInformation.OSArchitecture == Architecture.Arm ? "yt-dlp_linux_armv7l"
        : "yt-dlp_linux";

    private async Task DownloadReleaseAsync(string path, CancellationToken ct)
    {
        var url = $"https://github.com/yt-dlp/yt-dlp/releases/latest/download/{ReleaseAsset()}";
        var temp = path + ".new";
        try
        {
            var client = http.CreateClient(HttpClientName);
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var output = File.Create(temp);
                await response.Content.CopyToAsync(output, ct);
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                           UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            // Le nouveau binaire doit au moins se lancer avant de remplacer l'ancien.
            var check = await runner.RunAsync(temp, ["--version"], ct, timeout: TimeSpan.FromSeconds(60));
            if (!check.Success) throw new PermanentJobException($"Binaire téléchargé inutilisable : {LastLine(check.StdErr)}");
            File.Move(temp, path, overwrite: true);
            logger.LogInformation("yt-dlp remplacé par {Url}", url);
        }
        catch (HttpRequestException ex)
        {
            throw new TransientJobException($"Téléchargement de yt-dlp impossible : {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
