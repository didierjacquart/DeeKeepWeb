import { useEffect, useMemo, useReducer } from 'react';
import { jobs } from '../../../core/jobs/jobs';
import { useSubscription } from '../../../lib/hooks/useSubscription';
import { fetchProposals } from './tag-review-api';
import { emptyReview, reviewReducer } from './tag-review-state';

/**
 * Propositions de la recherche `jobId` et cases « Écrire » (voir reviewReducer).
 * @returns {[{ results: Map<string, any>, checked: Set<string> }, (action: object) => void]}
 */
export function useTagProposals(jobId) {
  const [state, dispatch] = useReducer(reviewReducer, undefined, emptyReview);

  // Propositions déjà reçues (rechargement de la page, coupure) puis flux en direct.
  useEffect(() => {
    fetchProposals(jobId)
      .then(list =>
        list.forEach(p => dispatch({ type: 'proposal', key: p.itemKey, status: p.status, proposal: p.data })),
      )
      .catch(() => undefined);
  }, [jobId]);

  const items = useMemo(() => jobs.items({ jobId }), [jobId]);
  useSubscription(items, d => {
    const item = d?.jobItem;
    if (item && item.type === 'tags.find')
      dispatch({ type: 'proposal', key: item.itemKey, status: item.status, proposal: item.data });
  });

  return [state, dispatch];
}
