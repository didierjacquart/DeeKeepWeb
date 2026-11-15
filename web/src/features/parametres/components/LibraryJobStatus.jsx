import { cx } from '../../../lib/cx';
import { jobs } from '../../../core/jobs/jobs';
import { ProgressBar } from '../../../shared/components/ProgressBar';

/**
 * Suivi d'une tâche de la bibliothèque : progression et « Arrêter » tant qu'elle tourne,
 * puis son dernier message (`doneLabel` : « Dernière relecture », « Dernier import »).
 */
export function LibraryJobStatus({ job, active, doneLabel }) {
  if (!job) return null;
  if (!active) {
    return (
      <p className={cx('dk-help', job.status === 'FAILED' && 'dk-error-text')}>
        {doneLabel} : {job.error ?? job.message}
      </p>
    );
  }
  return (
    <>
      <ProgressBar value={job.total ? job.progress : null} label={job.message} />
      <button className="control-btn small" onClick={() => void jobs.cancel(job)}>
        Arrêter
      </button>
    </>
  );
}
