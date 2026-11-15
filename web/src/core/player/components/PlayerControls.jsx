import { useStore } from '../../../lib/store';
import { player } from '../player';

/** Précédent, lecture / pause, suivant. */
export function PlayerControls({ playing, hasPrevious, hasNext }) {
  const { position, buffering } = useStore(player.clock);
  const toggleLabel = playing ? 'Pause (Espace)' : 'Lecture (Espace)';
  return (
    <div className="controls">
      <button
        className="icon-btn"
        onClick={() => player.previous()}
        disabled={!hasPrevious && position < 3}
        data-tip="Précédent (P)"
        aria-label="Précédent"
      >
        ⏮
      </button>
      <button
        className="icon-btn active play"
        onClick={() => player.toggle()}
        data-tip={toggleLabel}
        aria-label={toggleLabel}
      >
        {buffering ? '⏳' : playing ? '⏸' : '▶'}
      </button>
      <button
        className="icon-btn"
        onClick={() => player.next()}
        disabled={!hasNext}
        data-tip="Suivant (N)"
        aria-label="Suivant"
      >
        ⏭
      </button>
    </div>
  );
}
