import { useCallback, useEffect, useRef } from 'react';
import { useLatest } from '../../../../lib/hooks/useLatest';

/** Nouvelles tentatives au plus, espacées de 3 s, puis 4,5 s, 6 s… */
const MAX_ATTEMPTS = 8;

/**
 * Relance différée du calcul des pastilles tant que l'index de présence n'est
 * pas prêt. `schedule(n)` programme `refresh(n + 1)` ; une seule relance à la
 * fois ; tout s'arrête à la fermeture de la fenêtre.
 * @param {(attempt: number) => void} refresh
 */
export function usePresenceRetry(refresh) {
  const timer = useRef(null);
  const latestRefresh = useLatest(refresh);

  const clear = useCallback(() => {
    if (timer.current) clearTimeout(timer.current);
    timer.current = null;
  }, []);

  const schedule = useCallback(
    (attempt = 0) => {
      clear();
      if (attempt >= MAX_ATTEMPTS) return;
      timer.current = setTimeout(() => latestRefresh.current(attempt + 1), 3000 + attempt * 1500);
    },
    [clear, latestRefresh],
  );

  useEffect(() => {
    return clear;
  }, [clear]);

  return { schedule, clear };
}
