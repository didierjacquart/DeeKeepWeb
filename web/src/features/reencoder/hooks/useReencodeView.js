import { useMemo, useState } from 'react';
import { visibleItems } from '../reencode-items';

/**
 * Lignes affichées : filtre « déjà au format cible » et tri par colonne
 * (le tri est figé pendant les conversions).
 */
export function useReencodeView(items, target, busy) {
  const [hideTarget, setHideTarget] = useState(false);
  const [sortKey, setSortKey] = useState(null);
  const [sortAsc, setSortAsc] = useState(true);

  const visible = useMemo(
    () => visibleItems(items, { hideTarget, target, key: sortKey, asc: sortAsc }),
    [items, hideTarget, target, sortKey, sortAsc],
  );

  const sort = k => {
    if (busy) return;
    if (sortKey === k) setSortAsc(a => !a);
    else {
      setSortKey(k);
      setSortAsc(true);
    }
  };

  return { visible, hideTarget, toggleHideTarget: () => setHideTarget(!hideTarget), sortKey, sortAsc, sort };
}
