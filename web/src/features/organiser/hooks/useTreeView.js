import { useMemo, useState } from 'react';
import { buildTree, collapsibleKeys, toggled, visibleNodes } from '../organize-tree';

/**
 * Arbre affiché : dossiers repliés, « Tout replier / déplier » et filtre « Masquer ce qui est déjà en place ».
 */
export function useTreeView({ entries, plan, rootName }) {
  const [collapsed, setCollapsed] = useState(() => new Set());
  const [hideInPlace, setHideInPlace] = useState(false);
  const [allCollapsed, setAllCollapsed] = useState(false);

  const rowByIndex = useMemo(() => new Map((plan?.rows ?? []).map(r => [r.index, r])), [plan]);
  const albumFolders = plan?.albumFolders;
  const tree = useMemo(
    () => buildTree(entries, rowByIndex, albumFolders, rootName),
    [entries, rowByIndex, albumFolders, rootName],
  );
  const visible = useMemo(
    () => visibleNodes(tree, collapsed, hideInPlace && !!plan),
    [tree, collapsed, hideInPlace, plan],
  );

  const toggleOpen = key => setCollapsed(s => toggled(s, key));

  const toggleExpandAll = () => {
    if (allCollapsed) {
      setCollapsed(new Set());
      setAllCollapsed(false);
    } else {
      // Replier laisse la racine dépliée.
      setCollapsed(new Set(collapsibleKeys(tree)));
      setAllCollapsed(true);
    }
  };

  const resetFolds = () => {
    setCollapsed(new Set());
    setAllCollapsed(false);
  };

  return {
    rowByIndex,
    visible,
    collapsed,
    allCollapsed,
    hideInPlace,
    toggleHideInPlace: () => setHideInPlace(!hideInPlace),
    toggleOpen,
    toggleExpandAll,
    resetFolds,
  };
}
