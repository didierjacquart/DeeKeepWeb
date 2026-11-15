import { useMemo } from 'react';
import { cx } from '../../../lib/cx';
import { toolText, toolsOf } from '../diagnostic-labels';
import { useYtDlpUpdate } from '../hooks/useYtDlpUpdate';

/** Outils externes déclarés par les services (versions, chemins) et mise à jour de yt-dlp. */
export function ToolsCard({ services }) {
  const tools = useMemo(() => toolsOf(services), [services]);
  const { updateJob, updating, updateYtDlp } = useYtDlpUpdate();

  return (
    <div className="dk-card">
      <ul className="tools">
        {tools.map(t => (
          <li key={t.service + t.name}>
            <span className={cx('mark', t.available && 'ok', !t.available && 'bad')}>{t.available ? '✓' : '✗'}</span>
            <span className="tname">{t.name}</span>
            <span className="dk-faint" data-tip={t.path || undefined}>
              {toolText(t)}
            </span>
            <span className="dk-faint svc-name">{t.service}</span>
          </li>
        ))}
        {!tools.length && <li className="dk-faint">Aucun outil déclaré.</li>}
      </ul>
      {updateJob && (
        <p className={cx('dk-help', updateJob.status === 'FAILED' && 'dk-error-text')}>
          yt-dlp : {updateJob.error ?? updateJob.message ?? updateJob.status}
        </p>
      )}
      <div className="dk-actions">
        <span className="dk-spacer"></span>
        <button className="primary-btn" disabled={updating} onClick={() => void updateYtDlp()}>
          {updating ? 'Mise à jour…' : 'Mettre à jour yt-dlp'}
        </button>
      </div>
    </div>
  );
}
