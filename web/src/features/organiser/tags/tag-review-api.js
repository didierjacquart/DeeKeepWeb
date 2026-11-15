import { api } from '../../../core/graphql/api';
import { PROPOSALS, WRITE_TAGS } from '../organiser.queries';

/** Propositions déjà reçues par une recherche de tags (gardées côté serveur). */
export async function fetchProposals(jobId) {
  return (await api.query(PROPOSALS, { jobId })).organizeProposals;
}

export const writeTags = (sessionId, items) =>
  api.mutate(WRITE_TAGS, { sessionId, items }, { label: 'Écrire des tags', queueable: false });
