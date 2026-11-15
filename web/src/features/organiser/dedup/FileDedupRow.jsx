import { cx } from '../../../lib/cx';
import { formatLabel } from './file-dedup';

/** Un fichier d'un groupe de doublons ; toute la ligne coche la case. */
export function FileDedupRow({ file: f, group, checked, onToggle }) {
  return (
    <label className={cx('dk-row row', f.keep && 'keep')}>
      <span>
        <input type="checkbox" className="dk-check" checked={checked} onChange={onToggle} />
      </span>
      <span className="grp" data-tip={f.keep ? 'Exemplaire conservé par défaut (meilleure qualité)' : undefined}>
        {group}
        {f.keep ? ' ★' : ''}
      </span>
      <span className="ttl">{f.title}</span>
      <span className="art">{f.artist}</span>
      <span className="fmt">{formatLabel(f)}</span>
      <span className="path dk-faint" data-tip={f.path || undefined}>
        {f.path}
      </span>
    </label>
  );
}
