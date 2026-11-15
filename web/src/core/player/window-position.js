const KEY = 'dk-player-window';
/** Part de la fenêtre qui doit rester visible quand on la pousse hors de l'écran. */
const KEEP_VISIBLE = 80;

/** Position retenue de la fenêtre de lecture (coin haut gauche), ou null. */
export function loadWindowPosition() {
  try {
    const p = JSON.parse(localStorage.getItem(KEY) ?? 'null');
    return p && Number.isFinite(p.x) && Number.isFinite(p.y) ? { x: p.x, y: p.y } : null;
  } catch {
    return null;
  }
}

export function saveWindowPosition(p) {
  try {
    localStorage.setItem(KEY, JSON.stringify(p));
  } catch {
    // Stockage indisponible (navigation privée) : la position n'est simplement pas retenue.
  }
}

/**
 * Garde la fenêtre atteignable : sa barre de titre reste dans l'écran, et au moins
 * `KEEP_VISIBLE` px de large restent visibles de chaque côté.
 */
export function clampWindowPosition(p, size, viewport) {
  return {
    x: Math.round(Math.min(viewport.width - KEEP_VISIBLE, Math.max(KEEP_VISIBLE - size.width, p.x))),
    y: Math.round(Math.min(viewport.height - 48, Math.max(0, p.y))),
  };
}
