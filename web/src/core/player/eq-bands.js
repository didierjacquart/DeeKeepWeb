/** Fréquences centrales des 10 bandes d'octave de l'égaliseur d'analyse. */
export const EQ_BANDS = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

/** Libellés courts des bandes (« 31 », « 1k », « 16k »). */
export const EQ_BAND_LABELS = EQ_BANDS.map(f => (f >= 1000 ? `${f / 1000}k` : `${f}`));

/**
 * Niveau (dB) de chaque bande d'octave à partir d'un spectre FFT en dB :
 * énergie de la bande = somme des puissances de ses raies (centre / √2 … centre × √2).
 *
 * @param {Float32Array} bins Spectre en dB (AnalyserNode.getFloatFrequencyData).
 * @param {number} hzPerBin Largeur d'une raie (fréquence d'échantillonnage / taille FFT).
 * @param {Float32Array} target Reçoit un niveau par bande ; -120 dB si la bande est muette.
 */
export function bandLevels(bins, hzPerBin, target) {
  EQ_BANDS.forEach((center, i) => {
    const lo = Math.max(1, Math.floor(center / Math.SQRT2 / hzPerBin));
    const hi = Math.min(bins.length - 1, Math.ceil((center * Math.SQRT2) / hzPerBin));
    let power = 0;
    for (let b = lo; b <= hi; b++) power += 10 ** (bins[b] / 10);
    target[i] = power > 0 ? 10 * Math.log10(power) : -120;
  });
  return target;
}

/** Hauteur relative 0..1 d'un niveau : de -80 dB (vide) à -10 dB (plein). */
export function levelRatio(db) {
  return Math.max(0, Math.min(1, (db + 80) / 70));
}
