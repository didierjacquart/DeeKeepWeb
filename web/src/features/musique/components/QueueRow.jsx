import { cx } from '../../../lib/cx';
import { PresenceBadge } from '../../../shared/components/PresenceBadge';
import { ProgressBar } from '../../../shared/components/ProgressBar';
import { imageUrl } from '../../../shared/urls';
import { canListen, listenTip } from '../music-listen';
import { progressLabel } from '../music-rows';

/**
 * Une ligne de la file (carte sur mobile). Double-clic ou ▶ : écoute.
 * @param {{ row: object, selected: boolean, playing: boolean, presence?: { state: string, reason: string | null },
 *   onToggle: () => void, onListen: (fromButton: boolean) => void, onCancel: () => void }} props
 */
export function QueueRow({ row: r, selected, playing, presence, onToggle, onListen, onCancel }) {
  const src = imageUrl(r.track.coverSmall ?? r.track.coverBig);
  const tip = listenTip(r);
  return (
    <div className={cx('dk-row grid', selected && 'selected')} onDoubleClick={() => onListen(false)}>
      <span className="check">
        <PresenceBadge state={presence?.state ?? 'PENDING'} tooltip={presence?.reason ?? null} />
        <input
          type="checkbox"
          className="dk-check"
          checked={selected}
          onChange={onToggle}
          aria-label={'Cocher ' + r.track.title}
        />
      </span>
      <span className="dk-hide-mobile">
        {src ? <img className="dk-cover" src={src} alt="" loading="lazy" /> : <span className="dk-cover">🎵</span>}
      </span>
      <span className="dk-hide-mobile dk-faint">{r.track.id}</span>
      <span className="main">
        <span className="t">{r.track.title}</span>
        <span className="dk-cell-sub">
          {r.track.artist}
          {r.track.album ? ` · ${r.track.album}` : null}
        </span>
        <span className={cx('dk-hide-desktop mstatus', r.color)} data-tip={r.tooltip || undefined}>
          {r.status}
        </span>
      </span>
      <span className="dk-hide-mobile">{r.track.artist}</span>
      <span className="dk-hide-mobile">{r.track.album}</span>
      <span className="prog">
        <ProgressBar value={r.progress / 100} label={progressLabel(r)} />
        {r.canCancel && (
          <button className="x" onClick={onCancel} data-tip="Annuler ce téléchargement" aria-label="Annuler">
            X
          </button>
        )}
      </span>
      <span className={cx('dk-hide-mobile status', r.color)} data-tip={r.tooltip || undefined}>
        {r.status}
      </span>
      <span>
        <button
          className={cx('icon-btn', playing && 'active')}
          disabled={!canListen(r)}
          onClick={() => onListen(true)}
          data-tip={tip}
          aria-label={tip}
        >
          {playing ? '■' : '▶'}
        </button>
      </span>
    </div>
  );
}
