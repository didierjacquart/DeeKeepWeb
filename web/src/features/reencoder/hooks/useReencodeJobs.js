import { useEffect, useMemo } from 'react';
import { useStore } from '../../../lib/store';
import { useInterval } from '../../../lib/hooks/useInterval';
import { isActive, jobs } from '../../../core/jobs/jobs';
import { reencodeStore } from '../reencode-store';
import { batchProgressOf, scanTextOf } from '../reencode-items';

/**
 * Suivi des tâches de la page : analyses de fichiers en cours et dernier lot de conversions.
 * @param {{ scanJobIds: string[], batchIds: string[] }} state état de {@link reencodeStore}
 */
export function useReencodeJobs({ scanJobIds, batchIds }) {
  const all = useStore(jobs, s => s.all);

  useEffect(() => {
    reencodeStore.start();
  }, []);

  // Filet : si l'événement de fin d'une analyse a été manqué, on relit les tâches.
  useInterval(() => {
    if (reencodeStore.state.scanJobIds.length) void jobs.load();
  }, 4000);

  const scanText = useMemo(() => scanTextOf(all, scanJobIds), [all, scanJobIds]);

  const batchJobs = useMemo(() => {
    const ids = new Set(batchIds);
    return all.filter(j => ids.has(j.id));
  }, [all, batchIds]);

  return {
    scanText,
    scanning: scanText !== null,
    busy: batchJobs.some(isActive),
    batchTotal: batchJobs.length,
    batchProgress: batchProgressOf(batchJobs),
  };
}
