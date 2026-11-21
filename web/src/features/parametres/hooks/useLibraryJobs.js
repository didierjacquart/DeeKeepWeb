import { useMemo } from 'react';
import { api } from '../../../core/api/api';
import { isActive, jobs } from '../../../core/jobs/jobs';
import { useJobsByType } from '../../../core/jobs/useJobs';
import { notify } from '../../../core/ui/notify';
import { confirm } from '../../../shared/dialog/confirm';
import { IMPORT, RESCAN } from '../settings-api';

/**
 * Tâches de collecte et d'import : l'état survit à la navigation (la tâche tourne sur le serveur).
 * @param {string} desktopDataDir Dossier de l'application desktop, lu sur le serveur.
 */
export function useLibraryJobs(desktopDataDir) {
  const libraryJobs = useJobsByType('library.scan', 'library.import');
  const scanJob = useMemo(() => libraryJobs.find(j => j.type === 'library.scan'), [libraryJobs]);
  const importJob = useMemo(() => libraryJobs.find(j => j.type === 'library.import'), [libraryJobs]);
  const scanning = !!scanJob && isActive(scanJob);
  const importing = !!importJob && isActive(importJob);

  const rescan = async () => {
    const r = await api.mutate(RESCAN, {}, { label: 'Relire la bibliothèque', queueable: false });
    if (r.error) notify.error(r.error);
    else if (r.data) jobs.upsert(r.data.rescanLibrary);
  };

  const importDesktop = async () => {
    const ok = await confirm({
      title: "Importer depuis l'application desktop",
      message: `Reprendre les réglages et l'historique des téléchargements de l'application desktop ?\nDossier lu sur le serveur : ${desktopDataDir}\nLes clés secrètes ne sont pas importées.`,
      confirm: 'Importer',
    });
    if (!ok) return;
    const r = await api.mutate(IMPORT, {}, { label: "Importer depuis l'application desktop", queueable: false });
    if (r.error) notify.error(r.error);
    else if (r.data) jobs.upsert(r.data.importDesktopData);
  };

  return { scanJob, importJob, scanning, importing, rescan, importDesktop };
}
