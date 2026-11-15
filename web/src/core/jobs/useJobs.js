import { useMemo } from 'react';
import { useStore } from '../../lib/store';
import { jobs } from './jobs';

/** Tâches des types donnés, les plus récentes d'abord. */
export function useJobsByType(...types) {
  const all = useStore(jobs, s => s.all);
  const key = types.join('|');
  return useMemo(() => {
    const wanted = key.split('|');
    return all.filter(j => wanted.includes(j.type));
  }, [all, key]);
}

/** Tâches d'un même groupe (ex. un lot d'archivage). */
export function useJobsByGroup(groupKey) {
  const all = useStore(jobs, s => s.all);
  return useMemo(() => (groupKey ? all.filter(j => j.groupKey === groupKey) : []), [all, groupKey]);
}

/** Une tâche suivie par son identifiant (null si inconnue). */
export function useJob(id) {
  return useStore(jobs, s => (id ? (s.byId.get(id) ?? null) : null));
}
