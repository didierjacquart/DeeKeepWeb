import { useCallback, useEffect, useEffectEvent, useMemo, useRef, useState } from 'react';
import { useLatest } from '../../../lib/hooks/useLatest';
import { useSubscription } from '../../../lib/hooks/useSubscription';
import { useStore } from '../../../lib/store';
import { jobs } from '../../../core/jobs/jobs';
import { useJobsByType } from '../../../core/jobs/useJobs';
import { connectivity } from '../../../core/offline/connectivity';
import { fetchQueue, queueChanges } from '../music-api';
import { buildRows, unknownLiveKey } from '../music-rows';

/**
 * La file d'extraction : lignes en attente et tâches « download » lues sur le serveur,
 * tenues à jour par l'abonnement aux changements de la file, par l'état vivant des
 * tâches (`jobs`) et par un rechargement à chaque reconnexion.
 */
export function useDownloadQueue() {
  const [base, setBase] = useState([]);
  const [pending, setPending] = useState([]);
  const [error, setError] = useState(null);
  const [stale, setStale] = useState(false);
  const live = useJobsByType('download');
  const resyncTick = useStore(connectivity, s => s.resyncTick);

  const rows = useMemo(() => buildRows(base, pending, live), [base, pending, live]);
  // Valeurs fraîches pour les traitements asynchrones (lues après un await).
  const rowsRef = useLatest(rows);

  const reloading = useRef(false);
  const again = useRef(false);

  /** Recharge la file ; un appel pendant un chargement en relance un autre à la suite. */
  const reload = useCallback(() => {
    if (reloading.current) {
      again.current = true;
      return Promise.resolve();
    }
    reloading.current = true;
    const pass = () => {
      again.current = false;
      return fetchQueue().then(queue => {
        setBase(queue.jobs);
        setPending(queue.pending);
        setError(null);
        setStale(false);
        return again.current ? pass() : undefined;
      });
    };
    return pass()
      .catch(e => {
        setStale(true);
        setError(rowsRef.current.length ? null : e instanceof Error ? e.message : String(e));
      })
      .finally(() => {
        reloading.current = false;
      });
  }, [rowsRef]);

  const changes = useMemo(() => queueChanges(), []);
  useSubscription(changes, d => {
    const removed = d?.downloadQueueChanged?.removedJobIds ?? [];
    if (removed.length) jobs.forget(removed);
    void reload();
  });

  const reloadNow = useEffectEvent(() => void reload());

  // Premier chargement, puis à chaque reconnexion (resyncTick).
  useEffect(() => {
    reloadNow();
  }, [resyncTick]);

  // Nouvelle tâche créée ailleurs (autre appareil) : sa charge utile ne vient que de la requête.
  const unknownLive = useMemo(() => unknownLiveKey(base, live), [base, live]);
  useEffect(() => {
    if (unknownLive) reloadNow();
  }, [unknownLive]);

  return { rows, rowsRef, error, stale, reload };
}
