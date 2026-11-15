import { useEffect, useMemo, useState } from 'react';
import { debounceTime } from 'rxjs';
import { useSubscription } from '../../../lib/hooks/useSubscription';
import { api } from '../../../core/graphql/api';
import { navBadges } from '../../../layout/nav-badges';
import { HISTORY_CHANGED, HISTORY_Q } from '../history-api';

/**
 * Historique du serveur (téléchargements et recherches), rafraîchi à chaque changement
 * signalé ; renseigne le compteur de l'entrée de navigation.
 */
export function useHistoryData() {
  const [history, setHistory] = useState([]);
  const [searches, setSearches] = useState([]);
  const [loading, setLoading] = useState(true);
  const [stale, setStale] = useState(false);

  const history$ = useMemo(() => api.watch(HISTORY_Q), []);
  useSubscription(history$, s => {
    if (s.data) {
      setHistory(s.data.history ?? []);
      setSearches(s.data.searchHistory ?? []);
    }
    setLoading(s.loading && !s.data);
    setStale(s.stale);
  });

  const changed$ = useMemo(() => api.subscribe(HISTORY_CHANGED).pipe(debounceTime(800)), []);
  useSubscription(changed$, () => api.refetchActive());

  // Compteur de l'entrée de navigation : recherches + téléchargements.
  const count = history.length + searches.length;
  useEffect(() => {
    navBadges.setHistory(count || null);
  }, [count]);

  return { history, searches, loading, stale };
}
