import { offlineQueue } from '../../core/offline/offline-queue';

/** Action faite hors ligne, en attente du retour de la connexion. */
export function QueuedActionCard({ action }) {
  return (
    <div className="job queued">
      <div className="line">
        <span className="name">{action.label}</span>
        <span className="dk-chip warn">en attente</span>
      </div>
      {action.lastError && <span className="dk-faint small">{action.lastError}</span>}
      <button className="control-btn small" onClick={() => void offlineQueue.remove(action.id)}>
        Abandonner
      </button>
    </div>
  );
}
