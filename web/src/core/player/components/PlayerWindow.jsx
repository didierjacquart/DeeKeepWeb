import { useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useStore } from '../../../lib/store';
import { useEscape } from '../../../lib/hooks/useEscape';
import { useMediaQuery } from '../../../lib/hooks/useMediaQuery';
import { dialogs } from '../../../shared/dialog/dialog-stack';
import { coverSources } from '../cover-sources';
import { player } from '../player';
import { clampWindowPosition, loadWindowPosition, saveWindowPosition } from '../window-position';
import { Cover } from './Cover';
import { LedEqualizer } from './LedEqualizer';
import { PlayerQueue } from './PlayerQueue';
import { PlayerSeek } from './PlayerSeek';
import './player-window.scss';

const viewport = () => ({ width: window.innerWidth, height: window.innerHeight });

/**
 * Déplacement de la fenêtre par sa barre de titre (souris, stylet ou doigt). Sans position
 * retenue, la feuille de style la pose en bas à droite ; une position retenue devenue hors
 * écran (fenêtre du navigateur rétrécie) est recalée à l'affichage.
 */
function useDrag(box, enabled) {
  const [pos, setPos] = useState(loadWindowPosition);
  const shown = pos && clampWindowPosition(pos, { width: Math.min(560, window.innerWidth - 32) }, viewport());

  const onPointerDown = e => {
    const el = box.current;
    if (!enabled || !el || e.button !== 0 || e.target.closest('button')) return;
    e.preventDefault();
    const start = { x: e.clientX, y: e.clientY, left: el.offsetLeft, top: el.offsetTop };
    const size = { width: el.offsetWidth, height: el.offsetHeight };
    let last = null;
    const move = ev => {
      last = clampWindowPosition(
        { x: start.left + ev.clientX - start.x, y: start.top + ev.clientY - start.y },
        size,
        viewport(),
      );
      setPos(last);
    };
    const up = () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      if (last) saveWindowPosition(last);
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  };

  return { style: enabled && shown ? { left: shown.x, top: shown.y } : undefined, onPointerDown };
}

/** Boutons de transport façon platine : précédent, lecture / pause, arrêt, suivant. */
function Transport({ playing, hasPrevious, hasNext }) {
  const { position, buffering } = useStore(player.clock);
  const toggleLabel = playing ? 'Pause (Espace)' : 'Lecture (Espace)';
  return (
    <div className="transport">
      <button
        className="hifi-btn"
        onClick={() => player.previous()}
        disabled={!hasPrevious && position < 3}
        data-tip="Précédent (P)"
        aria-label="Précédent"
      >
        ⏮
      </button>
      <button className="hifi-btn main" onClick={() => player.toggle()} data-tip={toggleLabel} aria-label={toggleLabel}>
        {buffering ? '⏳' : playing ? '⏸' : '▶'}
      </button>
      <button className="hifi-btn" onClick={() => player.stop()} data-tip="Arrêter" aria-label="Arrêter">
        ■
      </button>
      <button
        className="hifi-btn"
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

/**
 * Fenêtre « Lecteur DeeKeep », comme celle de l'application desktop : jaquette du fichier,
 * égaliseur à diodes, position, transport, volume et file d'attente. Non modale et
 * déplaçable : on continue à parcourir la bibliothèque pendant l'écoute.
 */
export function PlayerWindow({ state, track }) {
  const box = useRef(null);
  const floating = !useMediaQuery('(max-width: 599px)');
  const drag = useDrag(box, floating);
  const [queueOpen, setQueueOpen] = useState(false);
  const covers = coverSources(track);
  // Échap ferme la fenêtre, sauf si une boîte de dialogue ouverte par-dessus le prend.
  useEscape(() => dialogs.state.stack.length === 0 && player.setExpanded(false));

  return createPortal(
    <section ref={box} className="dk-player-window" style={drag.style} aria-label="Lecteur">
      <header className="pw-titlebar" onPointerDown={drag.onPointerDown}>
        <span className="pw-name">Lecteur DeeKeep</span>
        <span className="dk-faint">
          Titre {state.index + 1} / {state.queue.length}
        </span>
        <span className="dk-spacer" />
        <button
          className="icon-btn"
          onClick={() => player.setExpanded(false)}
          data-tip="Fermer la fenêtre (la lecture continue)"
          aria-label="Replier"
        >
          ✕
        </button>
      </header>

      <div className="pw-now">
        <Cover key={covers.join('|')} sources={covers} className="pw-cover" />
        <div className="pw-meta">
          <div className="pw-title" title={track.title}>
            {track.title}
          </div>
          <div className="dk-muted">{[track.artist, track.album].filter(Boolean).join(' — ')}</div>
          {track.context && <div className="pw-context">{track.context}</div>}
        </div>
      </div>

      <LedEqualizer active={state.playing} />

      <PlayerSeek />

      <div className="pw-controls">
        <Transport
          playing={state.playing}
          hasPrevious={state.index > 0}
          hasNext={state.index < state.queue.length - 1}
        />
        <label className="pw-volume">
          <span aria-hidden="true">🔊</span>
          <input
            type="range"
            min="0"
            max="100"
            value={state.volume * 100}
            onChange={e => player.setVolume(Number(e.target.value) / 100)}
            aria-label="Volume"
          />
        </label>
        <span className="dk-spacer" />
        <button
          className="control-btn"
          onClick={() => setQueueOpen(o => !o)}
          aria-expanded={queueOpen}
          disabled={state.queue.length < 2}
        >
          {queueOpen ? '▾' : '▸'} File d&apos;attente
        </button>
      </div>

      {queueOpen && <PlayerQueue queue={state.queue} index={state.index} />}

      {state.pausedByPreview && <div className="dk-chip info">En pause : un extrait est en cours d&apos;écoute</div>}
      {state.error && (
        <div className="dk-error-text" role="alert">
          {state.error}
        </div>
      )}
    </section>,
    document.body,
  );
}
