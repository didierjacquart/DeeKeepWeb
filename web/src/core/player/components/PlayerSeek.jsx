import { useStore } from '../../../lib/store';
import { formatDuration } from '../../../shared/format';
import { player } from '../player';

/**
 * Position cliquable : un clic dans la rainure amène la lecture au point visé,
 * sans glisser ; au clavier, les flèches avancent ou reculent de 5 s.
 */
export function PlayerSeek() {
  const { position, duration } = useStore(player.clock);
  const percent = duration > 0 ? Math.min(100, (position / duration) * 100) : 0;
  return (
    <div className="seek">
      <span className="time">{formatDuration(position)}</span>
      <div
        className="groove"
        role="slider"
        tabIndex={0}
        aria-label="Position de lecture"
        aria-valuemin={0}
        aria-valuemax={Math.round(duration)}
        aria-valuenow={Math.round(position)}
        aria-valuetext={formatDuration(position)}
        onPointerDown={e => {
          const rect = e.currentTarget.getBoundingClientRect();
          const ratio = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width));
          player.seek(ratio * player.clock.state.duration);
        }}
        onKeyDown={e => {
          if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
            e.preventDefault();
            e.stopPropagation();
            player.seekBy(e.key === 'ArrowLeft' ? -5 : 5);
          }
        }}
      >
        <div className="fill" style={{ width: `${percent}%` }} />
      </div>
      <span className="time">{formatDuration(duration)}</span>
    </div>
  );
}
