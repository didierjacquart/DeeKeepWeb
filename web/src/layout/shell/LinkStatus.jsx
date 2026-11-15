import { useStore } from '../../lib/store';
import { cx } from '../../lib/cx';
import { connectivity, linkState } from '../../core/offline/connectivity';
import { offlineQueue } from '../../core/offline/offline-queue';
import { jobs } from '../../core/jobs/jobs';

const LINK_LABELS = { online: 'Serveur connecté', reconnecting: 'Reconnexion…', offline: 'Hors ligne' };

/** Pastille de l'état de la liaison avec le serveur. */
export function LinkDot() {
  const link = useStore(connectivity, linkState);
  return <span className={cx('dot', link)} aria-hidden="true" />;
}

/**
 * Pied de la barre latérale : état de la liaison, actions en attente du retour
 * de la connexion, tâches de fond en cours.
 */
export function LinkStatus({ onOpenJobs, onToggleJobs }) {
  const link = useStore(connectivity, linkState);
  const lastError = useStore(connectivity, s => s.lastError);
  const pending = useStore(offlineQueue, s => s.items.length);
  const activeJobs = useStore(jobs, s => s.active.length);
  return (
    <div className="status">
      <button
        className="status-line"
        onClick={onToggleJobs}
        data-tip={lastError ?? 'Tâches de fond et état de la liaison'}
      >
        <LinkDot />
        <span>{LINK_LABELS[link]}</span>
      </button>
      {pending > 0 && (
        <span className="status-sub pending" data-tip="Actions faites hors ligne, envoyées au retour de la connexion">
          {pending} action(s) en attente
        </span>
      )}
      {activeJobs > 0 && (
        <button className="status-sub link" onClick={onOpenJobs}>
          {activeJobs} tâche(s) en cours
        </button>
      )}
    </div>
  );
}
