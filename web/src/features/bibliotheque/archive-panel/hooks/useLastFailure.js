import { useMemo, useState } from 'react';
import { EMPTY, catchError, merge } from 'rxjs';
import { useSubscription } from '../../../../lib/hooks/useSubscription';
import { jobs } from '../../../../core/jobs/jobs';
import { ARCHIVE_WORK_TYPES, failureLine } from '../archive-model';

/**
 * Dernier titre en échec pendant une copie (archivage ou restauration), suivi
 * élément par élément ; effacé par le volet au lancement d'une nouvelle copie.
 */
export function useLastFailure() {
  const [lastFailure, setLastFailure] = useState(null);
  // Un flux par type, chacun indépendant : la coupure de l'un n'arrête pas l'autre.
  const items = useMemo(
    () => merge(...ARCHIVE_WORK_TYPES.map(type => jobs.items({ type }).pipe(catchError(() => EMPTY)))),
    [],
  );
  useSubscription(items, d => {
    const line = failureLine(d?.jobItem);
    if (line) setLastFailure(line);
  });
  return [lastFailure, setLastFailure];
}
