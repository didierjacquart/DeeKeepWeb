/**
 * Égaliseur à diodes de la fenêtre de lecture, repris du lecteur de l'application desktop
 * (SpectrumView) : dix colonnes de 18 diodes, vert franc en bas, ambre puis rouge doux au
 * sommet, trait de crête cyan. La montée est immédiate (l'attaque du son), la descente
 * amortie : des barres qui suivraient l'analyse à la lettre clignoteraient.
 */
export const LED_SEGMENTS = 18;

/** Descente, en fraction de hauteur par seconde (0,055 toutes les 40 ms dans l'original). */
const DECAY_PER_S = 1.375;
/** Maintien de la crête avant sa chute, en secondes (12 rafraîchissements de 40 ms). */
const PEAK_HOLD_S = 0.48;
/** Chute de la crête, en fraction de hauteur par seconde (0,02 toutes les 40 ms). */
const PEAK_FALL_PER_S = 0.5;

const LOW = [0x22, 0xd3, 0x66];
const MID = [0x7d, 0xff, 0x9b];
const HIGH = [0xff, 0xcf, 0x7a];
const TOP = [0xff, 0x8a, 0x80];

const blend = (a, b, t) => a.map((v, i) => Math.round(v + (b[i] - v) * Math.min(1, Math.max(0, t))));

/** Couleur [r, g, b] de la diode de rang `index` (0 = bas de colonne). */
export function segmentColor(index, segments = LED_SEGMENTS) {
  const ratio = index / (segments - 1);
  if (ratio < 0.55) return blend(LOW, MID, ratio / 0.55);
  if (ratio < 0.82) return blend(MID, HIGH, (ratio - 0.55) / 0.27);
  return blend(HIGH, TOP, (ratio - 0.82) / 0.18);
}

/** État des colonnes : niveau affiché, crête et temps de maintien restant (0..1 et secondes). */
export function createMeter(columns) {
  return { levels: new Float32Array(columns), peaks: new Float32Array(columns), holds: new Float32Array(columns) };
}

/**
 * Avance l'affichage de `dt` secondes vers les niveaux `targets` (0..1, ou null : tout retombe).
 * @returns {boolean} vrai s'il reste quelque chose d'allumé.
 */
export function stepMeter(meter, targets, dt) {
  const { levels, peaks, holds } = meter;
  let lit = false;
  for (let i = 0; i < levels.length; i++) {
    const target = targets ? targets[i] : 0;
    levels[i] = target >= levels[i] ? target : Math.max(target, levels[i] - DECAY_PER_S * dt);
    if (levels[i] >= peaks[i]) {
      peaks[i] = levels[i];
      holds[i] = PEAK_HOLD_S;
    } else if (holds[i] > 0) {
      holds[i] -= dt;
    } else {
      peaks[i] = Math.max(levels[i], peaks[i] - PEAK_FALL_PER_S * dt);
    }
    if (levels[i] > 0 || peaks[i] > 0) lit = true;
  }
  return lit;
}
