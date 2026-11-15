const VOLUME_KEY = 'dk-player-volume';

/** Volume « perçu » mémorisé (0..1), 0,8 par défaut. */
export function loadVolume() {
  try {
    const v = Number(localStorage.getItem(VOLUME_KEY));
    return Number.isFinite(v) && v > 0 ? Math.min(1, v) : 0.8;
  } catch {
    return 0.8;
  }
}

export function saveVolume(volume) {
  try {
    localStorage.setItem(VOLUME_KEY, String(volume));
  } catch {
    /* stockage indisponible */
  }
}
