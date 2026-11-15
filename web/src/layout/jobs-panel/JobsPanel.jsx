import { useState } from 'react';
import { useStore } from '../../lib/store';
import { jobs } from '../../core/jobs/jobs';
import { connectivity, linkState } from '../../core/offline/connectivity';
import { offlineQueue } from '../../core/offline/offline-queue';
import { JobCard } from './JobCard';
import { QueuedActionCard } from './QueuedActionCard';
import './jobs-panel.scss';

const LINK_HELP = {
  online: 'Connecté au serveur.',
  reconnecting: 'Reconnexion au serveur…',
  offline: 'Hors ligne : affichage du dernier état connu.',
};

/** Tâches terminées montrées au plus (les plus récentes). */
const DONE_LIMIT = 100;

/**
 * Panneau des tâches de fond : tout ce qui tourne sur le serveur, quelle que
 * soit la page. Fermer le navigateur n'arrête rien ; revenir montre où en est
 * chaque tâche.
 */
export function JobsPanel({ onClose }) {
  const { all, active } = useStore(jobs);
  const link = useStore(connectivity, linkState);
  const queued = useStore(offlineQueue, s => s.items);
  const [showDone, setShowDone] = useState(false);
  const visible = showDone ? all.slice(0, DONE_LIMIT) : active;

  return (
    <aside className="dk-jobs-panel" aria-label="Tâches de fond">
      <header>
        <span className="dk-overline">Tâches de fond</span>
        <span className="dk-spacer" />
        <button
          className={showDone ? 'control-btn small checked' : 'control-btn small'}
          aria-pressed={showDone}
          onClick={() => setShowDone(!showDone)}
        >
          Terminées
        </button>
        <button className="icon-btn" onClick={onClose} aria-label="Fermer">
          ✕
        </button>
      </header>
      <p className="dk-help">
        {LINK_HELP[link]}
        {queued.length > 0 && ` ${queued.length} action(s) attendent le retour de la connexion.`}
      </p>
      {queued.map(q => (
        <QueuedActionCard key={q.id} action={q} />
      ))}
      {visible.map(job => (
        <JobCard key={job.id} job={job} />
      ))}
      {!visible.length && <p className="dk-empty">Aucune tâche {showDone ? '' : 'en cours'}.</p>}
    </aside>
  );
}
