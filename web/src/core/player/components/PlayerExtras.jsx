import { useStore } from '../../../lib/store';
import { cx } from '../../../lib/cx';
import { offlineAudio } from '../offline-audio';
import { player } from '../player';

/** Volume, épinglage pour l'écoute hors ligne et fermeture (masqués sur mobile). */
export function PlayerExtras({ track, volume, expanded }) {
  const pinned = useStore(offlineAudio, s => s.pinned).has(track.src);
  const pinLabel = pinned
    ? "Disponible hors ligne — retirer de l'appareil"
    : "Garder sur l'appareil pour l'écoute hors ligne";
  const togglePin = () => void (pinned ? offlineAudio.unpin(track.src) : offlineAudio.pin(track.src));
  return (
    <div className="extras dk-hide-mobile">
      <input
        type="range"
        min="0"
        max="100"
        value={volume * 100}
        onChange={e => player.setVolume(Number(e.target.value) / 100)}
        aria-label="Volume"
      />
      <button
        className={cx('icon-btn', expanded && 'active')}
        onClick={() => player.setExpanded(!expanded)}
        data-tip={expanded ? 'Fermer la fenêtre du lecteur' : 'Fenêtre du lecteur (égaliseur)'}
        aria-label="Fenêtre du lecteur"
        aria-pressed={expanded}
      >
        ▤
      </button>
      {offlineAudio.supported && (
        <button
          className={cx('icon-btn', pinned && 'active')}
          onClick={togglePin}
          data-tip={pinLabel}
          aria-label={pinLabel}
        >
          ⤓
        </button>
      )}
      <button
        className="icon-btn"
        onClick={() => player.close()}
        data-tip="Fermer le lecteur"
        aria-label="Fermer le lecteur"
      >
        ✕
      </button>
    </div>
  );
}
