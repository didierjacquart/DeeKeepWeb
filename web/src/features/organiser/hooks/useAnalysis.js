import { jobs } from '../../../core/jobs/jobs';
import { notify } from '../../../core/ui/notify';
import { openFsBrowser } from '../../../shared/fs-browser/open-fs-browser';
import { startAnalysis } from '../organiser-api';

/**
 * Choix du dossier et analyse (tâche serveur ; l'arbre se remplit au fil des lots reçus).
 * `busyRef` et `data.live` sont relus à l'appel : `analyze` est aussi relancée après la revue des tags.
 */
export function useAnalysis({ data, busyRef, resetFolds, clearResult }) {
  const resetView = () => {
    data.clearAnalysis();
    resetFolds();
  };

  const chooseFolder = async () => {
    if (busyRef.current) return;
    const { session, defaultRoot } = data.live.current;
    const picked = await openFsBrowser({
      mode: 'folder',
      start: session?.root || defaultRoot || undefined,
      title: 'Choisir le dossier à organiser',
    });
    const root = picked?.[0];
    if (!root) return;
    if (root === data.live.current.session?.root) return;
    // Dossier modifié : plan entièrement vidé, nouvelle analyse à lancer.
    resetView();
    data.setSession({ id: '', root, rootDb: '', jobId: null, state: 'idle', total: 0, read: 0, message: null });
    data.setMessage("Dossier modifié : relancez l'analyse.");
  };

  const analyze = async () => {
    if (busyRef.current) return;
    const { session, defaultRoot } = data.live.current;
    const root = session?.root || defaultRoot;
    if (!root) {
      notify.info("Choisissez d'abord un dossier à analyser.");
      return;
    }
    resetView();
    clearResult();
    data.setMessage('Analyse en cours…');
    const r = await startAnalysis(root);
    if (!r.data) {
      data.setMessage('');
      notify.error(r.error ?? 'Analyse impossible.');
      return;
    }
    jobs.upsert(r.data.startOrganizeAnalysis.job);
    data.setSession(r.data.startOrganizeAnalysis.session);
    data.listenBatches(r.data.startOrganizeAnalysis.session);
  };

  const stopScan = () => {
    if (data.job) void jobs.cancel(data.job);
  };

  return { chooseFolder, analyze, stopScan };
}
