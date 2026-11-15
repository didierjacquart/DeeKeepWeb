import { useState } from 'react';
import { jobs } from '../../../core/jobs/jobs';
import { notify } from '../../../core/ui/notify';
import { confirm } from '../../../shared/dialog/confirm';
import { isRunning } from '../organize-labels';
import { waitForJob } from '../wait-for-job';
import { writeItems, writeQuestion } from './tag-review-model';
import { writeTags } from './tag-review-api';

/**
 * Écriture des correspondances cochées (tâche serveur), après confirmation ; la recherche encore en cours est
 * arrêtée d'abord. Ferme la revue avec le nombre de fichiers écrits.
 */
export function useWriteTags({ data, rows, checked, correct, running, examined, close }) {
  const [busy, setBusy] = useState(false);
  /** Lecture hors rendu (après une attente) : l'état courant de la tâche. */
  const runningNow = () => isRunning(jobs.get(data.job.id) ?? data.job);

  const write = async () => {
    const chosen = rows.filter(r => r.state === 'found' && checked.has(r.file.dbPath));
    if (chosen.length === 0) {
      notify.info('Cochez au moins une correspondance à écrire.');
      return;
    }
    const message = writeQuestion(chosen, correct, running ? data.files.length - examined : 0);
    if (!(await confirm({ title: 'Écrire les tags', message, confirm: 'Oui', cancel: 'Non' }))) return;

    setBusy(true);
    if (runningNow()) await jobs.cancel(data.job);
    // « Corriger aussi » ne peut changer pendant la question (boîte modale) : la valeur du rendu fait foi.
    const res = await writeTags(data.sessionId, writeItems(chosen, correct));
    if (!res.data?.writeOrganizeTags) {
      setBusy(false);
      notify.error(res.error ?? 'Écriture impossible.');
      return;
    }
    const done = await waitForJob(res.data.writeOrganizeTags, 400);
    setBusy(false);
    const summary = done.result ?? {};
    if (done.status === 'FAILED') notify.error(done.error ?? "Échec de l'écriture.");
    else notify.info(summary.summary ?? 'Tags écrits.');
    close(summary.written ?? 0);
  };

  const cancel = async () => {
    if (runningNow()) await jobs.cancel(data.job);
    close(undefined);
  };

  return { busy, write, cancel };
}
