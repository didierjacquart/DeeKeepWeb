import { useState } from 'react';
import { isActive } from '../../../../core/jobs/jobs';
import { workLine } from '../archive-model';

/**
 * Ligne d'état du volet : message de la tâche de copie en cours, puis son bilan
 * (les actions du volet y écrivent aussi). Chaque nouvel état de la tâche est
 * comparé au dernier statut vu pour elle ; une copie qui se termine fait relire la jauge.
 *
 * @param {object | null} work tâche de copie la plus récente
 * @param {(kind: 'later') => void} requestRefresh setter d'état (appelable pendant le rendu)
 * @returns {[{ text: string, error: boolean } | null, (line: { text: string, error: boolean } | null) => void]}
 */
export function useWorkLine(work, requestRefresh) {
  const [line, setLine] = useState(null);
  const [seen, setSeen] = useState({ work: null, statuses: {} });
  if (work && work !== seen.work) {
    const previous = seen.statuses[work.id];
    setSeen({ work, statuses: { ...seen.statuses, [work.id]: work.status } });
    const next = workLine(work, previous);
    if (next) setLine(next);
    if (previous && previous !== work.status && !isActive(work)) requestRefresh('later');
  }
  return [line, setLine];
}
