import { useState } from 'react';

/** Cases cochées (par chemin), conservées quand les filtres masquent une ligne. */
export function useRowSelection() {
  const [selected, setSelected] = useState(() => new Set());

  const toggle = (row, checked) =>
    setSelected(s => {
      const next = new Set(s);
      if (checked) next.add(row.path);
      else next.delete(row.path);
      return next;
    });

  /** « Tout cocher » : lignes affichées, jamais les illisibles. */
  const checkAll = list =>
    setSelected(s => {
      const next = new Set(s);
      for (const r of list) if (!r.error) next.add(r.path);
      return next;
    });

  const uncheckAll = list =>
    setSelected(s => {
      const next = new Set(s);
      for (const r of list) next.delete(r.path);
      return next;
    });

  const clear = () => setSelected(new Set());

  /** Décoche les chemins donnés (titres partis au retéléchargement). */
  const remove = paths => setSelected(s => new Set([...s].filter(p => !paths.includes(p))));

  return { selected, toggle, checkAll, uncheckAll, clear, remove };
}
