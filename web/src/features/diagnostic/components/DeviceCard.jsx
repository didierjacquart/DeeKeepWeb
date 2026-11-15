import { useStore } from '../../../lib/store';
import { cx } from '../../../lib/cx';
import { connectivity, linkState } from '../../../core/offline/connectivity';
import { offlineQueue } from '../../../core/offline/offline-queue';
import { offlineAudio } from '../../../core/player/offline-audio';
import { formatBytes } from '../../../shared/format';
import { linkLabel } from '../diagnostic-labels';
import { useDeviceStorage } from '../hooks/useDeviceStorage';

/** Cet appareil : liaison au serveur, file d'actions hors ligne, titres épinglés, stockage. */
export function DeviceCard() {
  const link = useStore(connectivity, linkState);
  const lastError = useStore(connectivity, s => s.lastError);
  const resyncTick = useStore(connectivity, s => s.resyncTick);
  const queueItems = useStore(offlineQueue, s => s.items);
  const replaying = useStore(offlineQueue, s => s.replaying);
  const pinned = useStore(offlineAudio, s => s.pinned);
  const storage = useDeviceStorage();

  return (
    <div className="dk-card">
      <dl>
        <dt>Liaison au serveur</dt>
        <dd>
          <span className={cx('dot', link === 'online' && 'ok', link === 'offline' && 'bad')}></span> {linkLabel(link)}
          {lastError && <span className="dk-faint"> · {lastError}</span>}
        </dd>
        <dt>Reconnexions</dt>
        <dd>{resyncTick} depuis l'ouverture</dd>
        <dt>Actions en attente</dt>
        <dd className={cx(queueItems.length > 0 && 'dk-error-text')}>
          {queueItems.length}
          {replaying ? ' (envoi en cours)' : ''}
        </dd>
        <dt>Titres épinglés</dt>
        <dd>{pinned.size} pour l'écoute hors ligne</dd>
        {storage && (
          <>
            <dt>Stockage de l'appareil</dt>
            <dd>
              {formatBytes(storage.used)} utilisés sur {formatBytes(storage.quota)}
            </dd>
          </>
        )}
      </dl>
      {queueItems.length > 0 && (
        <ul className="queue">
          {queueItems.map(q => (
            <li key={q.id}>
              {q.label}
              {q.lastError ? ' — ' + q.lastError : ''}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
