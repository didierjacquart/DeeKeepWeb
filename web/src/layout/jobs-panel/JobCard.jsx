import { isActive, jobs } from '../../core/jobs/jobs';
import { ProgressBar } from '../../shared/components/ProgressBar';
import { formatWhen } from '../../shared/format';
import { jobStatusChip, jobStatusText, jobTypeLabel } from './job-labels';

/** Une tâche de fond : type, état, progression, message, Arrêter / Relancer. */
export function JobCard({ job }) {
  const retryable = job.status === 'FAILED' || job.status === 'CANCELLED';
  return (
    <div className="job">
      <div className="line">
        <span className="name" data-tip={job.title || undefined}>
          {jobTypeLabel(job.type)}
        </span>
        <span className={`dk-chip ${jobStatusChip(job.status)}`}>{jobStatusText(job.status)}</span>
      </div>
      {job.title && <span className="dk-faint small ellipsis">{job.title}</span>}
      {job.status === 'RUNNING' && (
        <ProgressBar
          value={job.total > 0 ? job.progress : null}
          label={job.total > 0 ? `${job.done + job.failed} / ${job.total}` : ''}
        />
      )}
      {job.message && <span className="dk-muted small">{job.message}</span>}
      {job.error && <span className="dk-error-text small">{job.error}</span>}
      <div className="line">
        <span className="dk-faint small">{formatWhen(job.updatedAt)}</span>
        <span className="dk-spacer" />
        {isActive(job) && (
          <button className="control-btn small" onClick={() => void jobs.cancel(job)}>
            Arrêter
          </button>
        )}
        {retryable && (
          <button className="control-btn small" onClick={() => void jobs.retry(job)}>
            Relancer
          </button>
        )}
      </div>
    </div>
  );
}
