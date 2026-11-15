/** Clé de la liste conservée dans le navigateur. */
export const STORAGE_KEY = 'dk-reencode-v1';

/** Sauvegarde locale de la liste (20 000 fichiers au plus). */
export function saveItems(items) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(items.slice(0, 20000)));
  } catch {
    // Stockage plein ou refusé : la liste reste en mémoire.
  }
}

/** Liste sauvegardée ; une correspondance interrompue (« pending ») devient « none ». */
export function loadItems() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw
      ? JSON.parse(raw).map(i => ({ ...i, matchState: i.matchState === 'pending' ? 'none' : i.matchState }))
      : [];
  } catch {
    return [];
  }
}
