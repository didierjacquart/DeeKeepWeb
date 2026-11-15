import { useMemo } from 'react';
import { isActive } from '../../../../core/jobs/jobs';
import { useJobsByType } from '../../../../core/jobs/useJobs';
import { ARCHIVE_WORK_TYPES } from '../archive-model';

/**
 * Tâches de copie suivies par un composant : `busy` (archivage ou restauration
 * en cours ou en file) et `currentWork` (la plus récente, en cours ou terminée).
 */
export function useArchiveWork() {
  const list = useJobsByType(...ARCHIVE_WORK_TYPES);
  return useMemo(() => ({ busy: list.some(isActive), currentWork: list[0] ?? null }), [list]);
}
