import { useMemo, useState } from 'react';
import { nextDownloadSort, nextSearchSort, sortDownloads, sortSearches } from '../history-sort';

/** Ordre d'affichage des deux tableaux ; il est conservé quand on change d'onglet. */
export function useHistoryView(history, searches) {
  const [searchSort, setSearchSort] = useState(null);
  const [sort, setSort] = useState(null);

  const shownSearches = useMemo(() => sortSearches(searches, searchSort), [searches, searchSort]);
  const shownDownloads = useMemo(() => sortDownloads(history, sort), [history, sort]);

  return {
    searchSort,
    toggleSearchSort: () => setSearchSort(nextSearchSort),
    shownSearches,
    sort,
    sortBy: key => setSort(s => nextDownloadSort(s, key)),
    shownDownloads,
  };
}
