import { useEffect, useEffectEvent, useMemo, useState } from 'react';
import { confirm } from '../../../../shared/dialog/confirm';
import { clearSearches, deleteSearch, fetchHistory, fetchSearchCover, rememberSearch } from '../search-api';

/** Vignettes des recherches : mémoire partagée entre les ouvertures de la fenêtre (comme _SEARCH_COVER_CACHE). */
const searchCovers = new Map();

/** Recherches montrées dans le panneau. */
const RECENT = 8;

/**
 * « Dernières recherches » : liste du serveur, vignettes, panneau sous le champ.
 * Les modifications sont conservées même si la fenêtre est annulée.
 */
export function useSearchHistory(initialQuery) {
  // Une recherche initiale figure d'emblée en tête de l'historique.
  const [history, setHistory] = useState(() => (initialQuery ? [{ query: initialQuery, position: 0 }] : []));
  const [open, setOpen] = useState(false);
  const recent = useMemo(() => history.slice(0, RECENT), [history]);
  const [covers, setCovers] = useState(() => new Map(searchCovers));

  const loadCovers = async entries => {
    for (const entry of entries) {
      if (searchCovers.has(entry.query)) continue;
      try {
        searchCovers.set(entry.query, await fetchSearchCover(entry.query));
      } catch {
        return;
      }
      setCovers(new Map(searchCovers));
    }
  };

  const load = () =>
    fetchHistory().then(
      list => {
        setHistory(list);
        void loadCovers(list.slice(0, RECENT));
      },
      () => {
        // Hors ligne : l'historique reste celui déjà connu.
      },
    );

  const memorize = query => void rememberSearch(query).then(() => void load());

  const loadOnOpen = useEffectEvent(() => {
    void load();
    if (initialQuery) memorize(initialQuery);
  });
  useEffect(() => {
    loadOnOpen();
  }, []);

  /** L'historique est mis à jour d'emblée, comme à l'original : requête retirée puis insérée en tête. */
  const remember = query => {
    setHistory(h => [{ query, position: 0 }, ...h.filter(e => e.query.toLowerCase() !== query.toLowerCase())]);
    memorize(query);
  };

  const openPanel = () => {
    if (history.length > 0) setOpen(true);
  };

  const closeSoon = () => {
    // Laisse passer un clic sur le panneau avant de le refermer.
    setTimeout(() => setOpen(false), 150);
  };

  const remove = async (entry, event) => {
    event.stopPropagation();
    setHistory(h => h.filter(e => e.query !== entry.query));
    await deleteSearch(entry.query);
  };

  const clear = async event => {
    event.stopPropagation();
    if (
      !(await confirm({
        title: 'Effacer les dernières recherches',
        message: "Vider tout l'historique des recherches ?",
        confirm: 'Effacer',
        danger: true,
      }))
    )
      return;
    setHistory([]);
    setOpen(false);
    await clearSearches();
  };

  return { recent, covers, open, setOpen, openPanel, closeSoon, remember, remove, clear };
}
