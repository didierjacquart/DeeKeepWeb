import { useEffect, useState } from 'react';
import { api } from './api';

const LOADING = { data: null, loading: true, error: null, stale: false };

/**
 * Requête observée depuis un composant (cache puis réseau, mises à jour au fil
 * du cache). Les variables sont comparées par valeur ; `null` suspend la requête.
 *
 * @returns {import('./api').QueryState}
 */
export function useWatch(query, variables, pollInterval) {
  const [state, setState] = useState(LOADING);
  const key = variables === null ? null : JSON.stringify(variables ?? {});
  useEffect(() => {
    if (key === null) return undefined;
    const sub = api.watch(query, JSON.parse(key), pollInterval).subscribe({
      next: setState,
      error: e => setState(s => ({ ...s, loading: false, error: e instanceof Error ? e.message : String(e) })),
    });
    return () => sub.unsubscribe();
  }, [query, key, pollInterval]);
  return state;
}
