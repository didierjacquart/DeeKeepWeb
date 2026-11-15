import { useLatest } from '../../../lib/hooks/useLatest';
import { isActive, jobs } from '../../../core/jobs/jobs';
import { notify } from '../../../core/ui/notify';
import { confirm } from '../../../shared/dialog/confirm';
import { openFsBrowser } from '../../../shared/fs-browser/open-fs-browser';
import { clearQuality, setQualityFolder, startQualityScan } from '../quality-api';

/**
 * Dossier analysé, lancement et arrêt de l'analyse, vidage de la liste.
 * `onReset` vide la liste et la sélection affichées (nouvelle analyse).
 */
export function useScanActions({ folder, setFolder, deep, busy, scanJob, rowCount, onReset }) {
  const folderRef = useLatest(folder);

  const changeFolder = async () => {
    if (busy) return;
    const picked = await openFsBrowser({
      mode: 'folder',
      start: folder || undefined,
      audioOnly: true,
      title: 'Choisir le dossier à analyser',
    });
    const chosen = picked?.[0];
    if (!chosen || chosen === folderRef.current) return;
    const r = await setQualityFolder(chosen);
    if (r.error) notify.error(r.error);
    else {
      setFolder(chosen);
      notify.info("Dossier modifié : relancez l'analyse.");
    }
  };

  const startScan = async () => {
    if (busy) return;
    if (!folder) {
      notify.error("Choisissez d'abord le dossier à analyser.");
      return;
    }
    const r = await startQualityScan(folder, deep);
    if (r.error) notify.error(r.error);
    else onReset();
  };

  const stopScan = async () => {
    const job = scanJob;
    if (job && isActive(job)) await jobs.cancel(job);
  };

  const clearList = async () => {
    if (busy) {
      notify.info("Attendez la fin de l'analyse et des retéléchargements en cours avant de vider la liste.");
      return;
    }
    const n = rowCount;
    if (n === 0) return;
    const ok = await confirm({
      title: 'Vider la liste',
      message: `Retirer les ${n} résultat(s) d'analyse ?\nLes fichiers du disque sont conservés ; il faudra relancer l'analyse pour les revoir.`,
      confirm: 'Oui',
      cancel: 'Non',
    });
    if (!ok) return;
    const r = await clearQuality();
    if (r.error) notify.error(r.error);
    else notify.info("Écran vidé : relancez l'analyse.");
  };

  return { changeFolder, startScan, stopScan, clearList };
}
