import { useMemo } from 'react';
import { cx } from '../../../lib/cx';
import { player } from '../player';
import { queueWindow } from '../queue-window';

/** File d'attente ; un clic joue le titre choisi. */
export function PlayerQueue({ queue, index }) {
  const shown = useMemo(() => queueWindow(queue, index), [queue, index]);
  return (
    <ol className="queue" aria-label="File d'attente">
      {shown.map(({ track, at }) => (
        <li key={track.id} className={cx(at === index && 'current')} aria-current={at === index ? 'true' : undefined}>
          <button onClick={() => player.playAt(at)}>
            <span className="q-title">{track.title}</span>
            <span className="dk-faint">{track.artist}</span>
          </button>
        </li>
      ))}
    </ol>
  );
}
