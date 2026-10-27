using System.Globalization;
using System.Text.RegularExpressions;
using DeeKeep.Common.Processes;

namespace DeeKeep.Common.Audio;

/// <summary>Exécution d'un outil : (exécutable, arguments, délai, annulation) → résultat.</summary>
public delegate Task<ProcessResult> ToolInvoker(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct);

/// <summary>
/// Mesures faites sur le signal d'un fichier, via ffprobe/ffmpeg — portage de
/// <c>deekeep/audio/audio_probe.py</c>. La durée déclarée par l'en-tête n'est pas toujours
/// celle de la musique : un mauvais découpage laisse parfois une longue plage muette à la
/// fin (un titre de 4'22 devient un fichier de 6'54) et toute comparaison avec un
/// catalogue échoue. <see cref="EffectiveDurationAsync"/> retranche ce silence.
/// </summary>
public sealed partial class AudioProbe
{
    /// <summary>Niveau sous lequel le signal est tenu pour muet (le souffle analogique doit passer au-dessus).</summary>
    public const string SilenceNoise = "-45dB";
    /// <summary>Durée minimale d'une plage muette pour être signalée, en secondes.</summary>
    public const int SilenceMinSeconds = 3;
    /// <summary>Tolérance entre la fin d'un silence et la fin du fichier : ffmpeg clôt la dernière plage sur la dernière trame décodée.</summary>
    public const double SilenceEndMargin = 1.5;

    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(30);

    [GeneratedRegex(@"silence_start:\s*([0-9.]+)")]
    private static partial Regex SilenceStartRe();

    [GeneratedRegex(@"silence_end:\s*([0-9.]+)")]
    private static partial Regex SilenceEndRe();

    private readonly ToolOptions _tools;
    private readonly ToolInvoker _run;
    private readonly SemaphoreSlim _detection = new(1, 1);
    private bool? _ffmpegAvailable;

    /// <summary>Constructeur d'injection : outils lancés par <see cref="ProcessRunner"/> (arbre tué à l'annulation).</summary>
    public AudioProbe(ProcessRunner runner)
        : this(runner.Tools, (file, args, timeout, ct) => runner.RunAsync(file, args, ct, timeout: timeout))
    {
    }

    /// <summary>Constructeur de test : l'exécution des outils est fournie par l'appelant.</summary>
    public AudioProbe(ToolOptions tools, ToolInvoker run)
    {
        _tools = tools;
        _run = run;
    }

    /// <summary>
    /// ffmpeg répond-il (<c>-hide_banner -version</c>, code 0) ? Détection faite une fois ;
    /// les appels concurrents attendent sa fin plutôt que de lire un état intermédiaire.
    /// </summary>
    public async Task<bool> IsFfmpegAvailableAsync(CancellationToken ct, bool refresh = false)
    {
        if (_ffmpegAvailable is { } known && !refresh) return known;
        await _detection.WaitAsync(ct);
        try
        {
            if (_ffmpegAvailable is { } found && !refresh) return found;
            var result = await _run(_tools.Ffmpeg, ["-hide_banner", "-version"], VersionTimeout, ct);
            _ffmpegAvailable = result.ExitCode == 0;
            return _ffmpegAvailable.Value;
        }
        finally
        {
            _detection.Release();
        }
    }

    /// <summary>
    /// Durée du flux en secondes selon ffprobe (<c>format=duration</c>), null si illisible.
    /// L'original lisait la durée par mutagen ; ffprobe sert ici de mesure indépendante des tags.
    /// </summary>
    public async Task<double?> DurationAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        var result = await _run(_tools.Ffprobe,
            ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", path],
            ProbeTimeout, ct);
        if (result.ExitCode != 0) return null;
        var text = result.StdOut.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 ? seconds : null;
    }

    /// <summary>
    /// <c>trailing_silence_start</c> : instant où débute le silence final, ou null s'il n'y en a
    /// pas. <paramref name="declared"/> (durée annoncée par l'en-tête) permet de reconnaître,
    /// parmi les plages muettes, celle qui court jusqu'au bout du fichier. Décode le fichier
    /// entier : <c>ffmpeg -hide_banner -nostats -i &lt;fichier&gt; -af silencedetect=noise=-45dB:d=3 -f null -</c>.
    /// </summary>
    public async Task<double?> TrailingSilenceStartAsync(string path, double declared, CancellationToken ct)
    {
        if (declared == 0 || !File.Exists(path)) return null;
        if (!await IsFfmpegAvailableAsync(ct)) return null;
        var result = await _run(_tools.Ffmpeg,
            ["-hide_banner", "-nostats", "-i", path,
             "-af", $"silencedetect=noise={SilenceNoise}:d={SilenceMinSeconds}",
             "-f", "null", "-"],
            SilenceTimeout, ct);
        if (result.ExitCode != 0) return null;
        return ParseTrailingSilence(result.StdErr + result.StdOut, declared);
    }

    /// <summary>
    /// <c>effective_duration</c> : durée de la musique en secondes entières (arrondi au pair,
    /// comme <c>round</c>), silence final retranché ; null quand il n'y a rien à retrancher —
    /// la durée déclarée fait alors foi.
    /// </summary>
    public async Task<int?> EffectiveDurationAsync(string path, double declared, CancellationToken ct)
    {
        var start = await TrailingSilenceStartAsync(path, declared, ct);
        if (start is null) return null;
        var useful = (int)Math.Round(start.Value, MidpointRounding.ToEven);
        return useful > 0 ? useful : null;
    }

    /// <summary>
    /// Lecture du journal de silencedetect : le dernier début de silence est retenu si une plage
    /// est restée ouverte (plus de débuts que de fins) ou si la dernière fin coïncide, à 1,5 s
    /// près, avec la durée déclarée. Avec ffmpeg 8, silencedetect ferme la plage ouverte en fin
    /// de flux : c'est la seconde règle qui joue alors. Les deux sont nécessaires.
    /// </summary>
    public static double? ParseTrailingSilence(string log, double declared)
    {
        var starts = Numbers(SilenceStartRe(), log);
        var ends = Numbers(SilenceEndRe(), log);
        if (starts.Count == 0) return null;
        if (starts.Count > ends.Count || Math.Abs(ends[^1] - declared) <= SilenceEndMargin) return starts[^1];
        return null;
    }

    private static List<double> Numbers(Regex regex, string log)
    {
        var values = new List<double>();
        foreach (Match m in regex.Matches(log))
        {
            // float() de Python refuserait « 1.2.3 » en levant une exception ; on l'ignore.
            if (double.TryParse(m.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) values.Add(v);
        }
        return values;
    }
}
