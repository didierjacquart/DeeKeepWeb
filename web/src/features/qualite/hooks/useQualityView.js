import { useMemo, useState } from 'react';
import { ALL_SHOWN } from '../quality-model';
import { filterAndSort, nextSort, visibleDefects } from '../quality-sort';

/** Filtres « Afficher », « Titres sans défaut » et tri : la liste affichée, calculée sur les données reçues. */
export function useQualityView(rows, deep) {
  const [shown, setShown] = useState(ALL_SHOWN);
  const [includeClean, setIncludeClean] = useState(false);
  const [sort, setSort] = useState({ key: 'score', asc: true });

  const visible = useMemo(() => visibleDefects(shown, deep), [shown, deep]);
  const list = useMemo(
    () => filterAndSort([...rows.values()], visible, includeClean, sort),
    [rows, visible, includeClean, sort],
  );

  const toggleShown = (id, checked) => setShown(s => ({ ...s, [id]: checked }));
  const sortBy = key => setSort(cur => nextSort(cur, key));

  return { list, shown, toggleShown, includeClean, setIncludeClean, sort, sortBy };
}
