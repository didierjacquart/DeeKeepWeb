import { cx } from '../../../lib/cx';
import { coverOf, formatText, matchOf, statusOf } from '../reencode-labels';

/** Une ligne : case à cocher, écoute, jaquette, tags, fichier, format, correspondance et statut. */
export function ReencodeRow({ item, job, onToggle, onListen }) {
  const src = coverOf(item);
  const m = matchOf(item);
  const st = statusOf(item, job);
  return (
    <div className={cx('dk-row', 'grid', item.selected && 'selected')} onDoubleClick={onListen}>
      <span>
        <input
          type="checkbox"
          className="dk-check"
          checked={item.selected}
          onChange={onToggle}
          aria-label={'Cocher ' + (item.title || item.path)}
        />
      </span>
      <span>
        <button
          className="icon-btn"
          onClick={onListen}
          data-tip="Écouter ce fichier dans le lecteur"
          aria-label="Écouter"
        >
          ▶
        </button>
      </span>
      <span className="dk-hide-mobile">
        {src ? <img className="dk-cover" src={src} alt="" loading="lazy" /> : <span className="dk-cover">🎵</span>}
      </span>
      <span className="main">
        <span className="t">{item.title || '—'}</span>
        <span className="dk-cell-sub dk-hide-desktop">
          {item.artist} · {formatText(item)}
        </span>
        <span className="dk-cell-sub dk-hide-desktop">{st.text}</span>
      </span>
      <span className="dk-hide-mobile">{item.artist}</span>
      <span className="dk-hide-mobile path" data-tip={item.path || undefined}>
        {item.path}
      </span>
      <span className="dk-hide-mobile">{formatText(item)}</span>
      <span className={cx('dk-hide-mobile', m.cls)} data-tip={m.tip || undefined}>
        {m.text}
      </span>
      <span className={cx('dk-hide-mobile', st.cls)} data-tip={st.tip || undefined}>
        {st.text}
      </span>
    </div>
  );
}
