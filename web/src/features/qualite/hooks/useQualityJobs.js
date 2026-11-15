import { useMemo } from 'react';
import { isActive } from '../../../core/jobs/jobs';
import { useJobsByType } from '../../../core/jobs/useJobs';
import { rowBusy } from '../quality-model';

/** Tâches suivies par la page : analyse, durées de référence, réanalyses et retéléchargements. */
export function useQualityJobs(rows) {
  const scanJobs = useJobsByType('quality.scan');
  const refJobs = useJobsByType('quality.references');
  const reanalyzeJobs = useJobsByType('quality.reanalyze');
  const redownloadJobs = useJobsByType('redownload');

  const scanJob = scanJobs.find(isActive) ?? scanJobs[0] ?? null;
  const scanning = !!scanJob && isActive(scanJob);
  const redownloadById = useMemo(() => new Map(redownloadJobs.map(j => [j.id, j])), [redownloadJobs]);

  const downloading = useMemo(
    () => [...rows.values()].some(r => rowBusy(r, redownloadById)) || reanalyzeJobs.some(isActive),
    [rows, redownloadById, reanalyzeJobs],
  );

  return {
    scanJob,
    scanning,
    downloading,
    busy: scanning || downloading,
    refBusy: refJobs.some(isActive),
    redownloadJobs,
    redownloadById,
  };
}
