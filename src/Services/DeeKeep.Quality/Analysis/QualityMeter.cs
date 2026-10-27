using DeeKeep.Common.Audio;
using DeeKeep.Common.Processes;

namespace DeeKeep.Quality.Analysis;

/// <summary>
/// <c>quality_scan.measure</c> : une lecture ffmpeg par fichier, jamais d'exception pour un
/// fichier illisible (les mesures valent alors leurs valeurs neutres et <c>error</c> explique).
/// L'annulation (« Arrêter ») tue ffmpeg sur-le-champ et lève <see cref="OperationCanceledException"/> :
/// une mesure interrompue n'est jamais enregistrée.
/// </summary>
public sealed class QualityMeter(ProcessRunner runner, AudioProbe probe)
{
    public async Task<QualityMeasures> MeasureAsync(string localPath, bool deep, double? expectedDuration, CancellationToken ct)
    {
        if (!await probe.IsFfmpegAvailableAsync(ct)) return QualityMeasures.Empty(deep, "FFmpeg introuvable");
        if (!File.Exists(localPath)) return QualityMeasures.Empty(deep, "Fichier introuvable");

        // 900 s suffisaient sur un poste de travail ; sur ARM, un mix d'une heure en analyse
        // approfondie peut les dépasser : le délai suit la durée annoncée.
        var timeout = TimeSpan.FromSeconds(Math.Max(FfmpegLog.ProbeTimeoutSeconds, 0.5 * (expectedDuration ?? 0)));
        var result = await runner.RunAsync(runner.Tools.Ffmpeg, FfmpegLog.Arguments(localPath, deep), ct, timeout: timeout);
        var log = result.StdOut + result.StdErr;
        if (result.ExitCode == -2) log += "\nAnalyse interrompue (délai dépassé).";

        var measures = FfmpegLog.Parse(log) with { Deep = deep };
        if (result.ExitCode != 0 && measures.Duration == 0)
        {
            // Code non nul sans aucune mesure : fichier illisible. Avec une durée lue, ffmpeg a
            // travaillé et seule la fin a échoué : les mesures sont gardées.
            var line = FfmpegLog.ErrorLine(log);
            return QualityMeasures.Empty(deep, line.Length > 0 ? line : "Analyse impossible");
        }
        return measures;
    }
}
