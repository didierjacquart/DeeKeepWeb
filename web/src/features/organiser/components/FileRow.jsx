import { cx } from '../../../lib/cx';
import { actionClass, isSettled } from '../organize-tree';

/**
 * Ligne d'un fichier : case, écoute (bouton, double clic), nom, tags et action prévue par le plan ;
 * clic droit pour le menu contextuel.
 * @param {{ node: import('../organize-tree').FileNode, checked: boolean, hasPlan: boolean, onToggle: () => void, onPlay: () => void, onMenu: (e: import('react').MouseEvent) => void }} props
 */
export function FileRow({ node, checked, hasPlan, onToggle, onPlay, onMenu }) {
  const { entry, row } = node;
  return (
    <div
      className={cx('dk-row node', isSettled(row) && 'dim')}
      style={{ '--depth': node.depth }}
      onDoubleClick={onPlay}
      onContextMenu={onMenu}
    >
      <span className="c-name">
        <span className="twisty" />
        <input
          type="checkbox"
          className="dk-check"
          checked={checked}
          onChange={onToggle}
          aria-label={'Cocher ' + entry.name}
        />
        <button className="play" onClick={onPlay} aria-label="Écouter">
          ▶
        </button>
        <span className="label" data-tip={entry.path || undefined}>
          {entry.name}
        </span>
      </span>
      <span className="c-artist">{entry.artist}</span>
      <span className="c-album">{entry.album}</span>
      <span className={cx('c-action', actionClass(node, hasPlan))} data-tip={row?.tooltip || undefined}>
        {row?.action}
      </span>
    </div>
  );
}
