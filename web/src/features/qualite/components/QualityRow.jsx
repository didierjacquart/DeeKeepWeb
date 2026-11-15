import { cx } from '../../../lib/cx';
import { defectsClass, hasDefect, scoreClass } from '../quality-model';

const tip = text => text || undefined;

/**
 * Une ligne du tableau (une carte sur mobile, par la feuille de style) : case, écoute, note, titre, défauts,
 * mesures, format, fichier et statut. Double-clic : écouter ; clic droit ou « ⋮ » : menu de la ligne.
 * @param {{ row: object, selected: boolean, status: { text: string, tip: string | null, cls: string },
 *   onToggle: (checked: boolean) => void, onPlay: () => void, onMenu: (at: { x: number, y: number }) => void }} props
 */
export function QualityRow({ row: r, selected, status, onToggle, onPlay, onMenu }) {
  const bad = (...ids) => ids.some(id => hasDefect(r, id)) && 'bad';

  const openMenuAt = e => {
    e.stopPropagation();
    const rect = e.currentTarget.getBoundingClientRect();
    onMenu({ x: rect.left, y: rect.bottom });
  };

  return (
    <div
      className={cx('dk-row grid-cols', selected && 'selected', !r.onDisk && 'dim')}
      onDoubleClick={onPlay}
      onContextMenu={e => {
        e.preventDefault();
        onMenu({ x: e.clientX, y: e.clientY });
      }}
    >
      <span className="c-chk">
        <input
          type="checkbox"
          className="dk-check"
          checked={selected}
          disabled={!!r.error}
          onChange={e => onToggle(e.target.checked)}
          aria-label={`Cocher ${r.title || r.path}`}
        />
      </span>
      <span className="c-play">
        <button
          className="icon-btn"
          onClick={onPlay}
          data-tip="Écouter ce titre dans le lecteur (égaliseur, déplacement)"
          aria-label="Écouter"
        >
          ▶
        </button>
      </span>
      <span className={cx('c-score score', scoreClass(r))}>{r.cells['score']}</span>
      <span className="c-title" data-tip={tip(r.path)}>
        <b>{r.title}</b>
        <span className="dk-cell-sub dk-hide-desktop">{r.artist}</span>
      </span>
      <span className="c-artist">{r.artist}</span>
      <span className={cx('c-defects', defectsClass(r))} data-tip={tip(r.tooltips['defects'])}>
        {r.cells['defects']}
      </span>
      <span className={cx('c-hiss', bad('hiss'))} data-tip={tip(r.tooltips['hiss'])}>
        {r.cells['hiss']}
      </span>
      <span className={cx('c-crackle', bad('crackle'))} data-tip={tip(r.tooltips['crackle'])}>
        {r.cells['crackle']}
      </span>
      <span className={cx('c-clip', bad('clipping'))}>{r.cells['clipping']}</span>
      <span className={cx('c-gaps', bad('gaps'))} data-tip={tip(r.tooltips['gaps'])}>
        {r.cells['gaps']}
      </span>
      <span className={cx('c-dur', bad('duration'))} data-tip={tip(r.tooltips['duration'])}>
        {r.cells['duration']}
      </span>
      <span className={cx('c-blanks', bad('tail', 'head'))} data-tip={tip(r.tooltips['blanks'])}>
        {r.cells['blanks']}
      </span>
      <span className="c-fmt">{r.cells['format']}</span>
      <span className="c-path" data-tip={tip(r.path)}>
        {r.path}
      </span>
      <span className={cx('c-status', status.cls)} data-tip={tip(status.tip)}>
        {status.text}
      </span>
      <button className="icon-btn c-more" onClick={openMenuAt} aria-label="Plus d'actions">
        ⋮
      </button>
    </div>
  );
}
