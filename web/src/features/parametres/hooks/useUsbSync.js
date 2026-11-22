import { useMemo, useState } from 'react';
import { debounceTime } from 'rxjs';
import { api } from '../../../core/api/api';
import { isActive } from '../../../core/jobs/jobs';
import { useJob } from '../../../core/jobs/useJobs';
import { notify } from '../../../core/ui/notify';
import { useWatch } from '../../../core/api/useWatch';
import { useSubscription } from '../../../lib/hooks/useSubscription';
import { SYNC_EJECT, SYNC_JOB, SYNC_MOUNT, SYNC_RUN, SYNC_STATUS } from '../usb-sync-api';

/**
 * Synchronisation du disque USB : état lu toutes les 30 s (branchement, retrait) et à chaque
 * avancement de la tâche ; « Synchroniser » lance une tâche suivie en direct.
 */
export function useUsbSync() {
  const watched = useWatch(SYNC_STATUS, undefined, 30000);
  const status = watched.data?.status ?? null;
  const [startedId, setStartedId] = useState(null);
  const live = useJob(startedId ?? status?.running?.id ?? null);
  const job = live ?? status?.running ?? null;
  const running = !!job && isActive(job);

  // La fin d'une synchronisation change le bilan : l'état est relu (groupé, la tâche publie souvent).
  const changes = useMemo(() => api.subscribe(SYNC_JOB).pipe(debounceTime(1500)), []);
  useSubscription(changes, () => api.refetch(['UsbSyncStatus']));

  const run = async () => {
    const r = await api.mutate(SYNC_RUN, {}, { label: 'Synchroniser le disque USB', queueable: false });
    if (r.error) notify.error(r.error);
    else if (r.data?.run) {
      setStartedId(r.data.run.id);
      notify.info('Synchronisation lancée.');
    }
  };

  const [ejecting, setEjecting] = useState(false);
  const now = { queueable: false, refetch: ['UsbSyncStatus'] };

  const eject = async () => {
    setEjecting(true);
    const r = await api.mutate(SYNC_EJECT, {}, { ...now, label: 'Éjecter le disque USB' });
    setEjecting(false);
    if (r.error) notify.error(r.error);
    else notify.success('Disque éjecté : vous pouvez le débrancher.');
  };

  const mount = async () => {
    const r = await api.mutate(SYNC_MOUNT, {}, { ...now, label: 'Remonter le disque USB' });
    if (r.error) notify.error(r.error);
    else notify.info('Disque remonté.');
  };

  return {
    status,
    job,
    running,
    ejecting,
    eject,
    mount,
    loading: watched.loading && !status,
    error: watched.error && !status ? watched.error : null,
    run,
  };
}
