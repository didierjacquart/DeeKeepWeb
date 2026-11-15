import { cx } from '../../../lib/cx';

/** Une piste Deezer et, dessous, les fichiers du disque qui lui répondent ; toute la ligne coche la case. */
export function TrackDedupGroup({ group: g, checked, onToggle }) {
  return (
    <>
      <div className="dk-row head-row track">
        <span className="t-title" data-tip={g.title || undefined}>
          {g.title}
        </span>
        <span className="t-sub">{g.subtitle}</span>
        <span className="t-reason dk-faint" data-tip={g.reason || undefined}>
          {g.reason}
        </span>
      </div>
      {g.files.map(f => (
        <label key={f.path} className={cx('dk-row head-row file', f.keep && 'keep')}>
          <span className="f-name">
            <input
              type="checkbox"
              className="dk-check"
              checked={checked.has(f.path)}
              onChange={() => onToggle(f.path)}
            />
            <span className="name" data-tip={f.path || undefined}>
              {f.name}
            </span>
          </span>
          <span className="f-quality" data-tip={f.qualityTip || undefined}>
            {f.quality}
            {f.keep && <span className="star"> ★</span>}
          </span>
          <span className="f-folder dk-faint" data-tip={f.path + (f.reason ? '\n' + f.reason : '') || undefined}>
            {f.folder}
          </span>
        </label>
      ))}
    </>
  );
}
