import { api } from '../../../core/api/api';
import { isActive, jobs } from '../../../core/jobs/jobs';
import { useJobsByType } from '../../../core/jobs/useJobs';
import { notify } from '../../../core/ui/notify';
import { UPDATE_YTDLP } from '../diagnostics-api';

/** Mise à jour de yt-dlp : tâche serveur suivie par `jobs` (l'état survit à la navigation). */
export function useYtDlpUpdate() {
  const toolJobs = useJobsByType('tools.update');
  const updateJob = toolJobs[0];
  const updating = !!updateJob && isActive(updateJob);

  const updateYtDlp = async () => {
    const r = await api.mutate(UPDATE_YTDLP, {}, { label: 'Mettre à jour yt-dlp', queueable: false });
    if (r.error) notify.error(r.error);
    else if (r.data) jobs.upsert(r.data.updateYtDlp);
  };

  return { updateJob, updating, updateYtDlp };
}
