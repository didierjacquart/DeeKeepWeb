import { useState } from 'react';

/** Les coches de titres disparus de la base sont oubliées (même ensemble rendu si rien ne change). */
function forgetMissing(checked, page) {
  if (!page || !checked.size) return checked;
  const known = new Set(page.items.map(t => t.path));
  return [...checked].some(p => !known.has(p)) ? new Set([...checked].filter(p => known.has(p))) : checked;
}

/**
 * Chemins cochés : survivent au filtrage et au tri, purgés des titres disparus de la base.
 * @param {{ items: { path: string }[] } | null} page dernière page reçue du serveur
 * @param {string[]} visiblePaths chemins affichés (cible de « Tout cocher » / « Tout décocher »)
 */
export function useCheckedPaths(page, visiblePaths) {
  const [checked, setChecked] = useState(() => new Set());
  const [seenPage, setSeenPage] = useState(page);
  if (page !== seenPage) {
    setSeenPage(page);
    setChecked(c => forgetMissing(c, page));
  }

  const toggle = (path, on) =>
    setChecked(c => {
      const n = new Set(c);
      if (on) n.add(path);
      else n.delete(path);
      return n;
    });

  const checkAll = () => setChecked(c => new Set([...c, ...visiblePaths]));

  const uncheckAll = () => {
    const shown = new Set(visiblePaths);
    setChecked(c => new Set([...c].filter(p => !shown.has(p))));
  };

  return { checked, toggle, checkAll, uncheckAll };
}
