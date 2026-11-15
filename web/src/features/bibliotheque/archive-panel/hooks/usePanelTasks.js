import { useState } from 'react';
import { isActive } from '../../../../core/jobs/jobs';
import { useJob } from '../../../../core/jobs/useJobs';
import { archiveActions } from '../archive-actions';
import { panelJobLine } from '../archive-model';

/**
 * Boutons du volet déplié : Tester, Vérifier, Mesurer l'occupation, Enregistrer,
 * et leur ligne d'état. Vérifier et Mesurer lancent une tâche suivie jusqu'à sa
 * fin ; leur bilan s'affiche alors et la jauge est relue.
 *
 * @param {ReturnType<typeof import('./useArchiveSettings').useArchiveSettings>} settings
 */
export function usePanelTasks(settings) {
  const { root } = settings;
  const [panelLine, setPanelLine] = useState(null);
  const [testing, setTesting] = useState(false);
  const [measureJobId, setMeasureJobId] = useState(null);
  const [checkJobId, setCheckJobId] = useState(null);
  const measureJob = useJob(measureJobId);
  const checkJob = useJob(checkJobId);

  // Tâche terminée : bilan affiché, suivi arrêté, jauge relue.
  if (measureJob && !isActive(measureJob)) {
    setMeasureJobId(null);
    setPanelLine(panelJobLine(measureJob));
    settings.requestRefresh('now');
  }
  if (checkJob && !isActive(checkJob)) {
    setCheckJobId(null);
    setPanelLine(panelJobLine(checkJob));
    settings.requestRefresh('now');
  }
  const measuring = !!measureJob && isActive(measureJob);
  const checking = !!checkJob && isActive(checkJob);

  const save = async () => {
    const saved = await archiveActions.saveConfig(root.trim());
    if (!saved) return;
    settings.setRoot(saved.root ?? '');
    setPanelLine({ text: saved.configured ? 'Archive enregistrée.' : "Indiquez le fichier d'archive.", error: false });
    settings.refresh(saved.root ?? '');
  };

  const test = async () => {
    if (testing) return;
    setTesting(true);
    setPanelLine({ text: "Vérification de l'archive…", error: false });
    try {
      const r = await archiveActions.test(root.trim() || null);
      setPanelLine({ text: r.message, error: !r.ok });
    } finally {
      setTesting(false);
    }
  };

  const measure = async () => {
    if (measuring) return;
    setPanelLine({ text: "Mesure de l'archive…", error: false });
    const job = await archiveActions.measure(root.trim() || null);
    setMeasureJobId(job?.id ?? null);
  };

  const check = async () => {
    if (checking) return;
    setPanelLine({ text: 'Vérification des titres archivés…', error: false });
    const job = await archiveActions.check();
    setCheckJobId(job?.id ?? null);
  };

  return { panelLine, testing, measuring, checking, save, test, measure, check };
}
