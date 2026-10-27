using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeeKeep.Common.Processes;

public sealed class ToolOptions
{
    public string Ffmpeg { get; set; } = "ffmpeg";
    public string Ffprobe { get; set; } = "ffprobe";
    public string YtDlp { get; set; } = "yt-dlp";
    public string Fpcalc { get; set; } = "fpcalc";
    public string Deno { get; set; } = "deno";

    /// <summary>Priorité « nice » des outils lourds (0 = normale, 19 = la plus basse) : l'interface reste réactive pendant une analyse.</summary>
    public int Niceness { get; set; } = 10;
}

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, TimeSpan Elapsed)
{
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Exécution d'outils externes (ffmpeg, ffprobe, yt-dlp, fpcalc).
/// L'annulation tue l'arbre de processus sur-le-champ — « Arrêter » ne
/// doit pas attendre la fin d'une lecture ffmpeg.
/// </summary>
public sealed class ProcessRunner(IOptions<ToolOptions> options, ILogger<ProcessRunner> logger)
{
    public ToolOptions Tools { get; } = options.Value;

    public async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct,
        Action<string>? onStdErrLine = null, Action<string>? onStdOutLine = null, Stream? stdoutSink = null,
        TimeSpan? timeout = null, string? workingDirectory = null, IDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = stdoutSink is null ? Encoding.UTF8 : null,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? "",
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        if (environment is not null)
            foreach (var kv in environment) psi.Environment[kv.Key] = kv.Value;

        var sw = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Impossible de lancer {Tool}", fileName);
            return new ProcessResult(-1, "", ex.Message, sw.Elapsed);
        }
        TryLowerPriority(process);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) linked.CancelAfter(t);
        await using var registration = linked.Token.Register(() => Kill(process));

        var errTask = PumpLinesAsync(process.StandardError, stderr, onStdErrLine);
        Task outTask = stdoutSink is not null
            ? process.StandardOutput.BaseStream.CopyToAsync(stdoutSink, CancellationToken.None)
            : PumpLinesAsync(process.StandardOutput, stdout, onStdOutLine);

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(errTask, outTask);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Flux fermé par l'arrêt forcé.
        }

        ct.ThrowIfCancellationRequested();
        if (linked.IsCancellationRequested)
            return new ProcessResult(-2, stdout.ToString(), stderr + "\n[délai dépassé]", sw.Elapsed);
        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), sw.Elapsed);
    }

    /// <summary>Démarre un processus dont la sortie standard est lue par l'appelant (flux continu).</summary>
    public Process StartStreaming(string fileName, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        var process = Process.Start(psi) ?? throw new InvalidOperationException($"Impossible de lancer {fileName}");
        TryLowerPriority(process);
        _ = Task.Run(async () =>
        {
            try { await process.StandardError.ReadToEndAsync(); } catch { /* ignoré */ }
        });
        return process;
    }

    private static async Task PumpLinesAsync(StreamReader reader, StringBuilder sink, Action<string>? onLine)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            sink.Append(buffer, 0, read);
            if (onLine is null) continue;
            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];
                if (c is '\n' or '\r')
                {
                    if (line.Length > 0) onLine(line.ToString());
                    line.Clear();
                }
                else line.Append(c);
            }
        }
        if (onLine is not null && line.Length > 0) onLine(line.ToString());
    }

    private void TryLowerPriority(Process process)
    {
        if (Tools.Niceness <= 0) return;
        try
        {
            process.PriorityClass = Tools.Niceness >= 15 ? ProcessPriorityClass.Idle : ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // Sans importance : le processus a pu se terminer.
        }
    }

    public static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Déjà terminé.
        }
    }
}
