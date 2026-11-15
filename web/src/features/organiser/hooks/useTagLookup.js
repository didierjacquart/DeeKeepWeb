import { useState } from 'react';
import { jobs } from '../../../core/jobs/jobs';
import { notify } from '../../../core/ui/notify';
import { confirm } from '../../../shared/dialog/confirm';
import { startLookup } from '../organiser-api';
import { longLookupMessage, NOTHING_TO_LOOKUP, TAGS_LOOKUP_WARN } from '../organize-labels';
import { openTagReviewDialog } from '../tags/open-tag-review-dialog';

/**
 * « Compléter les tags » : recherche au catalogue des tags manquants des fichiers cochés, puis revue.
 * La revue en cours est gardée (`lookup`, voir useLookupResume) pour être reprise après un rechargement.
 */
export function useTagLookup({ session, busy, lookupIndices, lookup, analyze }) {
  const [lookupBusy, setLookupBusy] = useState(false);

  const review = async r => {
    const written = await openTagReviewDialog({ sessionId: r.sessionId, job: r.job, files: r.files });
    if (written === undefined) {
      // Annulée : la recherche est arrêtée, rien n'est écrit.
      lookup.forget();
      return;
    }
    lookup.forget();
    // Des tags ont changé sur le disque : nouvelle analyse automatique pour que le plan suive.
    if (written > 0) await analyze();
  };

  const lookupTags = async () => {
    const s = session;
    const indices = lookupIndices;
    if (!s || busy) return;
    if (indices.length === 0) {
      await confirm({ title: 'Rien à compléter', message: NOTHING_TO_LOOKUP, confirm: 'OK', cancel: 'Fermer' });
      return;
    }
    if (indices.length > TAGS_LOOKUP_WARN) {
      const message = longLookupMessage(indices.length);
      const ok = await confirm({ title: 'Recherche longue', message, confirm: 'Oui', cancel: 'Non' });
      if (!ok) return;
    }
    setLookupBusy(true);
    const r = await startLookup(s.id, indices);
    setLookupBusy(false);
    if (!r.data) {
      notify.error(r.error ?? 'Recherche impossible.');
      return;
    }
    const { job: lookupJob, files } = r.data.startOrganizeLookup;
    jobs.upsert(lookupJob);
    lookup.remember({ job: lookupJob, files, sessionId: s.id });
    await review({ job: lookupJob, files, sessionId: s.id });
  };

  const reopenReview = async () => {
    if (lookup.resume) await review(lookup.resume);
  };

  return { lookupBusy, lookupTags, reopenReview };
}
