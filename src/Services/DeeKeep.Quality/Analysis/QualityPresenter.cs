using DeeKeep.Contracts.Quality;

namespace DeeKeep.Quality.Analysis;

/// <summary>
/// Textes des cellules et des infobulles de l'écran Qualité — portage de
/// <c>quality_page._fill_row</c>, <c>_measure_cell</c>, <c>_duration_cell</c>,
/// <c>_silence_cell</c>, de leurs infobulles et de <c>_player_subtitle</c>.
/// Clés des cellules : score, defects, hiss, crackle, clipping, gaps, duration, blanks,
/// format, context (sous-titre du lecteur), status. Clés des infobulles : defects, hiss,
/// crackle, gaps, duration, blanks, status.
/// </summary>
public static class QualityPresenter
{
    /// <summary>« 3:36 » : <c>int(round(s or 0))</c> puis minutes:secondes.</summary>
    public static string Mmss(double seconds)
    {
        var total = double.IsNaN(seconds) ? 0 : Py.RoundInt(seconds);
        return $"{total / 60}:{total % 60:00}";
    }

    public static string FormatCell(string? format, int bitrate)
    {
        var fmt = string.IsNullOrEmpty(format) ? "?" : format;
        return bitrate != 0 ? $"{fmt} · {bitrate} kbps" : fmt;
    }

    public static (Dictionary<string, string> Cells, Dictionary<string, string> Tooltips) Present(
        QualityMeasures m, Verdict v, QualityLimits limits, string referenceState, double? reference, string? format, int bitrate,
        string? status, string? statusDetail)
    {
        var cells = new Dictionary<string, string>();
        var tips = new Dictionary<string, string>();
        var error = m.HasError;

        cells["score"] = error ? "—" : v.Score.ToString();
        if (error)
        {
            cells["defects"] = "Illisible";
            tips["defects"] = m.Error;
        }
        else
        {
            cells["defects"] = v.Defects.Count == 0 ? "Aucun" : string.Join(", ", v.Defects.Select(d => Defects.Labels[d]));
        }

        foreach (var defect in new[] { Defects.Hiss, Defects.Crackle, Defects.Clipping, Defects.Gaps })
        {
            if (error)
            {
                cells[defect] = "—";
                continue;
            }
            if (Defects.Deep.Contains(defect) && m.CracklePct is null)
            {
                cells[defect] = "non mesuré";
                tips[defect] = "Cochez « Détecter les craquements » puis relancez l'analyse.";
                continue;
            }
            cells[defect] = QualityVerdict.Describe(m, defect);
            if (defect == Defects.Hiss) tips[defect] = HissTooltip(m);
            else if (defect == Defects.Gaps) tips[defect] = GapsTooltip(m);
        }

        cells["duration"] = DurationCell(m, referenceState, reference);
        tips["duration"] = DurationTooltip(m, referenceState, reference, limits);
        cells["blanks"] = BlanksCell(m, v, referenceState, limits);
        tips["blanks"] = BlanksTooltip(m, referenceState, reference, limits);
        cells["format"] = FormatCell(format, bitrate);
        cells["context"] = PlayerContext(m, v, format, bitrate);

        var (statusText, statusTip) = StatusText(status, statusDetail, v, error);
        if (statusText.Length > 0) cells["status"] = statusText;
        if (statusTip is not null) tips["status"] = statusTip;
        return (cells, tips);
    }

    /// <summary><c>_hiss_tooltip</c>.</summary>
    public static string HissTooltip(QualityMeasures m)
    {
        if (!QualityVerdict.HissMeasurable(m))
            return $"Fichier échantillonné à {m.SampleRate} Hz : il ne contient rien au-dessus de 8 kHz,\noù se mesure le souffle.";
        if (QualityVerdict.TroughGap(m) is not { } gap)
            return "Plancher de bruit au-dessus de 8 kHz.\nLe fichier comporte du silence numérique franc.";
        var verdict = gap <= QualityVerdict.HissTroughGapDb
            ? "bruit large bande : c'est un souffle"
            : "aigus absents des passages calmes : c'est de la musique, pas un souffle";
        return $"Plancher de bruit au-dessus de 8 kHz.\nAu moment le plus calme, la bande haute est {Py.F(gap, 0)} dB sous le signal entier\n({verdict}).";
    }

    /// <summary><c>_gaps_tooltip</c>.</summary>
    public static string GapsTooltip(QualityMeasures m)
    {
        if (m.GapCount == 0)
            return "Aucun silence numérique au milieu du titre.\nLes blancs de début et de fin ont leur propre colonne.";
        return $"{m.GapCount} coupure(s) au milieu du titre, {Py.F(m.GapSeconds, 2)} s en tout.\n" +
               $"Passage de dix secondes le plus troué : {Py.F(100.0 * m.GapDensity, 0)} % de silence.\n" +
               "C'est cette densité qui fait le verdict — un morceau bâti sur\ndes silences en compte beaucoup sans en être abîmé.";
    }

    /// <summary><c>_duration_cell</c> (texte). « Référence indisponible » : état propre à la version web.</summary>
    public static string DurationCell(QualityMeasures m, string referenceState, double? reference)
    {
        var duration = m.Duration;
        if (m.HasError || duration == 0 || double.IsNaN(duration)) return "—";
        return referenceState switch
        {
            QualityReferenceStates.Value when reference is { } r => $"{Mmss(duration)}  ({Py.Signed(duration - r, 0)} s)",
            QualityReferenceStates.Pending => $"{Mmss(duration)} — référence…",
            QualityReferenceStates.Network => $"{Mmss(duration)} — référence indisponible",
            _ => $"{Mmss(duration)} — sans référence",
        };
    }

    /// <summary><c>_duration_tooltip</c>.</summary>
    public static string DurationTooltip(QualityMeasures m, string referenceState, double? reference, QualityLimits limits)
    {
        var duration = double.IsNaN(m.Duration) ? 0 : m.Duration;
        if (referenceState == QualityReferenceStates.Pending)
            return $"Durée du fichier : {Mmss(duration)}.\nDurée du morceau en cours de recherche…";
        if (referenceState == QualityReferenceStates.Network)
            return $"Durée du fichier : {Mmss(duration)}.\nDeezer et MusicBrainz sont injoignables pour l'instant : la\ndurée du morceau sera cherchée de nouveau.";
        if (referenceState != QualityReferenceStates.Value || reference is not { } r)
            return $"Durée du fichier : {Mmss(duration)}.\nNi Deezer ni MusicBrainz ne connaissent ce titre : sa\ndurée n'est comparée à rien.";
        var gap = duration - r;
        var verdict = Math.Abs(gap) > limits.DurationGap
            ? $"Le fichier est plus {(gap < 0 ? "court" : "long")} que le morceau : ce n'est pas la\nversion attendue — montage écourté, édition radio, ou\nextraction d'une mauvaise source."
            : "Le fichier dure ce que dure le morceau.";
        return $"Durée du fichier : {Mmss(duration)} — morceau : {Mmss(r)} ({Py.Signed(gap, 0)} s).\n{verdict}\n" +
               "La référence retenue est, parmi les versions connues, celle\ndont la durée est la plus proche.";
    }

    /// <summary><c>_silence_cell</c> (texte).</summary>
    public static string BlanksCell(QualityMeasures m, Verdict v, string referenceState, QualityLimits limits)
    {
        var parts = new List<string>();
        if (v.Defects.Contains(Defects.Head)) parts.Add(Py.F(m.HeadSeconds, 0) + " s début");
        if (v.Defects.Contains(Defects.Tail)) parts.Add(Py.F(m.TailSeconds, 0) + " s fin");
        if (parts.Count > 0) return string.Join(" / ", parts);
        if (m.HasError) return "—";
        var silence = Math.Max(m.HeadSeconds, m.TailSeconds);
        var measured = m.TailSeconds > limits.TailSeconds || m.HeadSeconds > limits.HeadSeconds;
        if (!measured) return "ok";
        return referenceState switch
        {
            QualityReferenceStates.Pending => $"{Py.F(silence, 0)} s — référence…",
            QualityReferenceStates.Value => $"{Py.F(silence, 0)} s — dans le master",
            QualityReferenceStates.Network => $"{Py.F(silence, 0)} s — référence indisponible",
            _ => $"{Py.F(silence, 0)} s — sans référence",
        };
    }

    /// <summary><c>_silence_tooltip</c>.</summary>
    public static string BlanksTooltip(QualityMeasures m, string referenceState, double? reference, QualityLimits limits)
    {
        var lines = new List<string> { $"Début : {Py.F(m.HeadSeconds, 1)} s — Fin : {Py.F(m.TailSeconds, 1)} s" };
        var duration = double.IsNaN(m.Duration) ? 0 : m.Duration;
        if (referenceState == QualityReferenceStates.Pending)
            lines.Add("Durée de référence en cours de recherche…");
        else if (referenceState == QualityReferenceStates.Network)
            lines.Add("Deezer et MusicBrainz sont injoignables pour l'instant : la durée\nde référence sera cherchée de nouveau. D'ici là, aucun blanc n'est signalé.");
        else if (referenceState != QualityReferenceStates.Value || reference is not { } r)
            lines.Add($"Durée du fichier : {Py.F(duration, 0)} s.\nNi Deezer ni MusicBrainz ne connaissent ce titre : sans\n" +
                      "durée de référence, rien ne dit si ce silence appartient au\nmorceau. Aucun blanc n'est donc signalé.");
        else
        {
            var excess = duration - r;
            var verdict = excess > limits.DurationExcess
                ? "le fichier dépasse le morceau : le silence a été ajouté"
                : "le silence appartient au master, il n'y a rien à corriger";
            lines.Add($"Durée du fichier : {Py.F(duration, 0)} s — référence : {(long)r} s ({Py.Signed(excess, 0)} s).\n{verdict}");
        }
        return string.Join("\n", lines);
    }

    /// <summary><c>_player_subtitle</c> : contexte affiché par le lecteur.</summary>
    public static string PlayerContext(QualityMeasures m, Verdict v, string? format, int bitrate)
    {
        var parts = new List<string> { FormatCell(format, bitrate) };
        if (m.HasError) parts.Add("fichier illisible");
        else
        {
            parts.Add($"note {v.Score}");
            if (v.Defects.Count > 0) parts.Add(string.Join(", ", v.Defects.Select(d => Defects.Labels[d])).ToLowerInvariant());
        }
        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// Colonne STATUT pour les états tenus par le service ; l'avancement d'un retéléchargement
    /// (« En attente… », « {texte} — {n}% », « Échec ») vient de la tâche d'extraction, suivie par le client.
    /// Le verdict « corrigé / persistant » est celui du moment : il suit les seuils et la référence.
    /// </summary>
    public static (string Text, string? Tooltip) StatusText(string? status, string? detail, Verdict v, bool error) => status switch
    {
        QualityStatuses.Reanalyzing => ("Réanalyse…", null),
        QualityStatuses.Replaced when error => ("Remplacé ✓ (réanalyse impossible)", detail),
        QualityStatuses.Replaced => (v.Defects.Count == 0 ? "Remplacé ✓ défaut corrigé" : "Remplacé ✓ défaut persistant", detail),
        QualityStatuses.ReplacedUnreadable => ("Remplacé ✓ (réanalyse impossible)", detail),
        QualityStatuses.Redownload => ("En attente…", detail),
        _ => ("", detail),
    };
}
