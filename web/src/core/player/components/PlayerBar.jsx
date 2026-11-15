import { useEffect } from 'react';
import { useStore } from '../../../lib/store';
import { cx } from '../../../lib/cx';
import { currentTrack, player } from '../player';
import { PlayerWindow } from './PlayerWindow';
import { PlayerControls } from './PlayerControls';
import { PlayerSeek } from './PlayerSeek';
import { PlayerExtras } from './PlayerExtras';
import { Cover } from './Cover';
import { coverSources } from '../cover-sources';
import './player-bar.scss';

/**
 * Lecteur en bas d'écran : barre compacte toujours visible ; un clic sur le titre ouvre la
 * fenêtre « Lecteur DeeKeep » (égaliseur à diodes, transport, volume, file d'attente).
 */
export function PlayerBar() {
  const state = useStore(player);
  const track = currentTrack(state);
  const covers = coverSources(track);

  // La hauteur réservée en bas des pages suit la présence du lecteur.
  useEffect(() => {
    document.documentElement.style.setProperty('--dk-player-height', track ? '72px' : '0px');
  }, [track]);

  if (!track) return <div className="dk-player-bar" />;

  return (
    <div className={cx('dk-player-bar', 'visible', state.expanded && 'expanded')}>
      {state.expanded && <PlayerWindow state={state} track={track} />}
      <div className="bar">
        <button
          className="track"
          onClick={() => player.setExpanded(!state.expanded)}
          aria-expanded={state.expanded}
          aria-label={state.expanded ? 'Fermer la fenêtre du lecteur' : 'Ouvrir la fenêtre du lecteur'}
        >
          <Cover key={covers.join('|')} sources={covers} className="dk-cover" />
          <span className="text">
            <span className="title">{track.title}</span>
            {state.error ? (
              <span className="dk-error-text" role="alert" title={state.error}>
                {state.error}
              </span>
            ) : (
              <span className="dk-faint">{track.artist}</span>
            )}
          </span>
        </button>
        <PlayerControls
          playing={state.playing}
          hasPrevious={state.index > 0}
          hasNext={state.index < state.queue.length - 1}
        />
        <PlayerSeek />
        <PlayerExtras track={track} volume={state.volume} expanded={state.expanded} />
      </div>
    </div>
  );
}
