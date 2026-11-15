import { useCallback, useState } from 'react';
import { VirtualList } from '../../../shared/virtual-list/VirtualList';
import { emptyText } from '../organize-labels';
import { dirState, nodeKey } from '../organize-tree';
import { DirRow } from './DirRow';
import { FileMenu } from './FileMenu';
import { FileRow } from './FileRow';

/**
 * Arbre du dossier analysé (liste virtuelle : une bibliothèque compte des milliers de fichiers), avec son menu
 * contextuel.
 */
export function OrganizeTree({ visible, entryCount, loaded, scanning, hasPlan, view, checks, onPlay }) {
  const [ctx, setCtx] = useState(null);
  const closeMenu = useCallback(() => setCtx(null), []);
  const { checkedSet } = checks;

  const openMenu = (event, entry) => {
    event.preventDefault();
    setCtx({ x: event.clientX, y: event.clientY, entry });
  };

  const renderNode = n =>
    n.kind === 'dir' ? (
      <DirRow
        node={n}
        state={dirState(n, checkedSet)}
        collapsed={view.collapsed.has(n.key)}
        onToggleOpen={() => view.toggleOpen(n.key)}
        onToggleCheck={() => checks.toggleDir(n)}
      />
    ) : (
      <FileRow
        node={n}
        checked={checkedSet.has(n.entry.index)}
        hasPlan={hasPlan}
        onToggle={() => checks.toggleFile(n.entry)}
        onPlay={() => onPlay(n.entry)}
        onMenu={e => openMenu(e, n.entry)}
      />
    );

  return (
    <div className="dk-table tree">
      <div className="dk-thead head dk-hide-mobile">
        <span>Dossier / Fichier</span>
        <span>Artiste</span>
        <span>Album</span>
        <span>Action</span>
      </div>
      {visible.length <= 1 && entryCount === 0 ? (
        <div className="dk-empty">{emptyText(loaded, scanning)}</div>
      ) : (
        <VirtualList items={visible} itemHeight={52} itemKey={nodeKey} renderItem={renderNode} className="viewport" />
      )}
      <FileMenu ctx={ctx} onClose={closeMenu} onPlay={onPlay} />
    </div>
  );
}
