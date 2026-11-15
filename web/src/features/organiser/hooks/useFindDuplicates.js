import { notify } from '../../../core/ui/notify';
import { openFileDedupDialog } from '../dedup/open-file-dedup-dialog';
import { findDuplicates } from '../organiser-api';
import { waitForJob } from '../wait-for-job';

/** « Rechercher les doublons » : tâche serveur, puis écran fichier/fichier des copies trouvées. */
export function useFindDuplicates({ data, busy }) {
  return async () => {
    const s = data.session;
    if (!s) {
      notify.info("Lancez d'abord l'analyse du dossier.");
      return;
    }
    if (busy) {
      notify.info("Attendez la fin de l'analyse avant de chercher les doublons.");
      return;
    }
    data.setMessage('Recherche des doublons…');
    const r = await findDuplicates(s.id);
    if (!r.data) {
      data.setMessage('');
      notify.error(r.error ?? 'Recherche impossible.');
      return;
    }
    const done = await waitForJob(r.data.findOrganizeDuplicates, 300);
    data.setMessage('');
    if (done.status !== 'SUCCEEDED') {
      notify.error(done.error ?? 'Recherche des doublons interrompue.');
      return;
    }
    const found = done.result;
    if (!found || found.groups.length === 0) {
      notify.info("Aucun fichier ne semble être la copie d'un autre.");
      return;
    }
    const deleted = await openFileDedupDialog({ result: found, sessionId: s.id });
    if (deleted && deleted.length > 0) {
      // Les fichiers supprimés ne pèsent plus sur le plan (ils comptaient pour leur artiste, donc pour « Divers »).
      const list = await data.loadEntries(s);
      await data.loadPlanAndChecks(s, list, data.live.current.options);
    }
  };
}
