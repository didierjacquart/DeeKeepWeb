import { useMemo, useState } from 'react';
import { useInterval } from '../../../lib/hooks/useInterval';
import { useSubscription } from '../../../lib/hooks/useSubscription';
import { api } from '../../../core/api/api';
import { SERVICES_Q } from '../diagnostics-api';

/** Période de rafraîchissement de l'état des services. */
export const POLL_MS = 5000;

/**
 * État des microservices et dossier d'enregistrement, affichés depuis le cache puis
 * rafraîchis toutes les 5 s tant que la page est affichée.
 */
export function useServices() {
  const [services, setServices] = useState([]);
  const [downloadDir, setDownloadDir] = useState('');
  const [loading, setLoading] = useState(true);
  const [stale, setStale] = useState(false);
  const [error, setError] = useState(null);

  const apply = data => {
    if (data?.services) setServices(data.services);
    const dir = data?.settings?.['download_dir'];
    if (typeof dir === 'string') setDownloadDir(dir);
  };

  const watched = useMemo(() => api.watch(SERVICES_Q), []);
  useSubscription(watched, s => {
    apply(s.data);
    setLoading(s.loading && !s.data);
    setStale(s.stale);
    setError(s.error && !s.data ? s.error : null);
  });

  // Rafraîchi toutes les 5 s tant que la page est affichée (la réponse met aussi le cache à jour).
  useInterval(() => {
    api.query(SERVICES_Q).then(apply, () => undefined);
  }, POLL_MS);

  const refresh = () => {
    api.refetchActive();
  };

  return { services, downloadDir, loading, stale, error, refresh };
}
