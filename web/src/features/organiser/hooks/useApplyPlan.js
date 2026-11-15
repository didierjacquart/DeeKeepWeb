import { useState } from 'react';
import { jobs } from '../../../core/jobs/jobs';
import { useJob } from '../../../core/jobs/useJobs';
import { notify } from '../../../core/ui/notify';
import { confirm } from '../../../shared/dialog/confirm';
import { applyOrganize } from '../organiser-api';
import { applyConfirmText, applyOperations } from '../organize-checks';
import { applyResultText, isRunning, isTerminal } from '../organize-labels';
import { optionsInput } from '../organize-options';

/**
 * « Organiser » : rangement des fichiers cochés (tâche serveur suivie par jobs), puis compte rendu.
 * Rend aussi `busy` : analyse ou rangement en cours, les autres actions attendent.
 */
export function useApplyPlan({ data, checkedSet }) {
  const [busyApply, setBusyApply] = useState(false);
  const [applyJobId, setApplyJobId] = useState(null);
  const [result, setResult] = useState(null);
  const [seenStatus, setSeenStatus] = useState('');

  const applyJob = useJob(applyJobId);
  const applying = isRunning(applyJob);
  const busy = data.scanning || applying || busyApply;

  // Rangement terminé (motif « ajuster l'état quand une valeur change ») : les chemins analysés ont changé,
  // plan vidé, aucune relance automatique.
  const status = applyJob?.status ?? '';
  if (status !== seenStatus) {
    setSeenStatus(status);
    if (applyJob && isTerminal(status)) {
      setResult(applyResultText(applyJob));
      data.setPlan(null);
      data.setEntries([]);
      data.setTouched(new Map());
      data.setMessage("Rangement terminé : relancez l'analyse pour vérifier.");
      setApplyJobId(null);
    }
  }

  const apply = async () => {
    const { session: s, plan: p, options } = data;
    if (!s || !p || busy) return;
    const ops = applyOperations(p.rows, checkedSet);
    if (ops.length === 0) {
      notify.info('Cochez au moins un fichier à ranger.');
      return;
    }
    const text = applyConfirmText(ops, s.root, options.removeEmpty);
    if (!(await confirm({ title: 'Confirmer le rangement', message: text, confirm: 'Organiser', cancel: 'Non' })))
      return;

    setBusyApply(true);
    setResult(null);
    const r = await applyOrganize(
      s.id,
      ops.map(o => o.index),
      optionsInput(options),
    );
    setBusyApply(false);
    if (!r.data) {
      notify.error(r.error ?? 'Rangement impossible.');
      return;
    }
    jobs.upsert(r.data.applyOrganize);
    setApplyJobId(r.data.applyOrganize.id);
  };

  const stopApply = () => {
    if (applyJob) void jobs.cancel(applyJob);
  };

  return { busy, applying, applyJob, result, setResult, apply, stopApply };
}
