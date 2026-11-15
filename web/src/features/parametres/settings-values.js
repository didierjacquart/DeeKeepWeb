export const BROWSERS = ['Aucun (anonyme)', 'Chrome', 'Edge', 'Firefox', 'Brave', 'Opera', 'Vivaldi'];
export const FORMATS = ['MP3', 'FLAC', 'WAV', 'M4A'];
export const DIVERS_RANGE = [2, 3, 4, 5, 6, 7, 8, 9, 10];

/** Qualités proposées selon le format (le format est appliqué d'abord, puis la qualité). */
export function qualitiesFor(format) {
  return format === 'FLAC' || format === 'WAV'
    ? ['Lossless (Qualité Source)']
    : ['320 kbps (HQ)', '128 kbps (Standard)'];
}

/** Qualité retenue pour un format : celle demandée si le format la propose, sinon la première. */
export function qualityFor(format, quality) {
  const qualities = qualitiesFor(format);
  return qualities.includes(quality) ? quality : qualities[0];
}

export function readStr(settings, key, fallback = '') {
  const v = settings[key];
  return typeof v === 'string' ? v : fallback;
}

export function readBool(settings, key, fallback = false) {
  const v = settings[key];
  return typeof v === 'boolean' ? v : fallback;
}

export function readNum(settings, key, fallback) {
  const v = settings[key];
  return typeof v === 'number' ? v : fallback;
}
