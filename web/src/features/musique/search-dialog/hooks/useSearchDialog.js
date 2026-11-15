import { useEffect, useRef, useState } from 'react';
import { useStore } from '../../../../lib/store';
import { connectivity, linkState } from '../../../../core/offline/connectivity';
import { pickedTracks } from '../track-list';
import { useCatalogSearch } from './useCatalogSearch';
import { useSearchHistory } from './useSearchHistory';

/**
 * Pilotage de la fenêtre « Rechercher et ajouter des pistes » : champ de recherche,
 * type, historique, catalogue, validation. Une requête initiale est lancée dès l'ouverture.
 * @param {{ initialQuery: string, close: (result?: { tracks: object[] }) => void }} p
 */
export function useSearchDialog({ initialQuery, close }) {
  const online = useStore(connectivity, linkState) === 'online';
  const [opening] = useState(() => initialQuery.trim());
  const [query, setQuery] = useState(opening);
  const [kind, setKind] = useState('ALL');
  // La requête initiale est lancée (et mémorisée) par chacun dès l'ouverture.
  const history = useSearchHistory(opening);
  const catalog = useCatalogSearch(opening);
  const inputRef = useRef(null);

  const search = async queryOverride => {
    const q = (queryOverride ?? query).trim();
    if (!q) return;
    setQuery(q);
    history.setOpen(false);
    history.remember(q);
    await catalog.search(q, kind);
  };

  // Sans requête initiale, le champ prend le focus (même sur mobile).
  useEffect(() => {
    if (!opening) queueMicrotask(() => inputRef.current?.focus());
  }, [opening]);

  const onQueryInput = value => {
    setQuery(value);
    history.setOpen(false);
  };

  const onQueryKeyDown = event => {
    if (event.key === 'Enter') void search();
    // Échap ferme d'abord le panneau d'historique, sans fermer la fenêtre.
    if (event.key === 'Escape' && history.open) {
      history.setOpen(false);
      event.stopPropagation();
    }
  };

  /** « Annuler » : interrompt une recherche en cours (la fenêtre reste ouverte), sinon ferme. */
  const cancel = () => {
    if (catalog.state.loading) catalog.stop();
    else close(undefined);
  };

  const add = () => close({ tracks: pickedTracks(catalog.state.picked) });

  return {
    online,
    query,
    kind,
    setKind,
    inputRef,
    history,
    catalog,
    search,
    onQueryInput,
    onQueryKeyDown,
    cancel,
    add,
  };
}
