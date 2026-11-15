import { useState } from 'react';

/**
 * Lignes cochées, par identifiant. Quand la file change, les lignes disparues
 * sont oubliées (motif « ajuster l'état quand une prop change »).
 */
export function useRowSelection(rows) {
  const [selected, setSelected] = useState(() => new Set());
  const idsKey = rows.map(r => r.id).join(',');
  const [knownIds, setKnownIds] = useState(idsKey);

  if (idsKey !== knownIds) {
    setKnownIds(idsKey);
    const ids = new Set(rows.map(r => r.id));
    setSelected(s => new Set([...s].filter(id => ids.has(id))));
  }

  const toggle = id =>
    setSelected(s => {
      const n = new Set(s);
      if (!n.delete(id)) n.add(id);
      return n;
    });

  const selectAll = on => setSelected(on ? new Set(rows.map(r => r.id)) : new Set());

  const clear = () => setSelected(new Set());

  return { selected, toggle, selectAll, clear };
}
