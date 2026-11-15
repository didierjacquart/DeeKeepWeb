/**
 * Revue des tags en cours, gardée dans le navigateur : une revue interrompue par un
 * rechargement se rouvre (les propositions sont gardées côté serveur).
 */

export const LOOKUP_KEY = 'dk-organize-lookup';

/** Revue enregistrée pour cette session d'analyse, ou null. */
export function readLookup(sessionId) {
  try {
    const raw = localStorage.getItem(LOOKUP_KEY);
    if (!raw) return null;
    const r = JSON.parse(raw);
    return r.sessionId === sessionId ? r : null;
  } catch {
    return null;
  }
}

export function writeLookup(r) {
  try {
    localStorage.setItem(LOOKUP_KEY, JSON.stringify(r));
  } catch {
    /* stockage indisponible */
  }
}

export function clearLookup() {
  try {
    localStorage.removeItem(LOOKUP_KEY);
  } catch {
    /* stockage indisponible */
  }
}
