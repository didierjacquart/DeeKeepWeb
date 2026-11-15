import { cx } from '../../lib/cx';
import { formatBytes } from '../format';

/** Une entrée du dossier : case à cocher (mode fichiers), icône, nom et taille. */
export function FsEntryRow({ entry, selectable, selected, onToggle, onOpen, onEnter }) {
  return (
    <li className={cx(selected && 'selected')}>
      {selectable && (
        <input type="checkbox" className="dk-check" checked={selected} onChange={onToggle} aria-label={entry.name} />
      )}
      <button className="entry" onClick={onOpen} onDoubleClick={onEnter}>
        <span className="ico" aria-hidden="true">
          {entry.isDirectory ? '📁' : entry.isAudio ? '♪' : '·'}
        </span>
        <span className="name">{entry.name}</span>
        {!entry.isDirectory && entry.size != null && <span className="size">{formatBytes(entry.size)}</span>}
      </button>
    </li>
  );
}
