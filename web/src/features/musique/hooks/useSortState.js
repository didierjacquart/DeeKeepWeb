import { useState } from 'react';

/** Colonne de tri : un clic trie par la colonne, un second clic inverse l'ordre. */
export function useSortState() {
  const [sortKey, setSortKey] = useState(null);
  const [sortAsc, setSortAsc] = useState(true);

  const sort = key => {
    if (sortKey === key) setSortAsc(a => !a);
    else {
      setSortKey(key);
      setSortAsc(true);
    }
  };

  return { sortKey, sortAsc, sort };
}
