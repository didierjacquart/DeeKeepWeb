import { useEffect, useRef, useState } from 'react';
import { api } from '../../../core/graphql/api';
import { imageUrl } from '../../../shared/urls';
import { SEARCH_COVER } from '../history-api';

/**
 * Pochette du premier résultat Deezer de chaque recherche affichée (30 premières),
 * demandée une seule fois par requête. Rend `coverOf(query)` : undefined tant que la
 * réponse n'est pas arrivée (« ⏳ »), null s'il n'y a rien (« ♪ »), sinon l'image.
 */
export function useSearchCovers(searches) {
  const [covers, setCovers] = useState({});
  const asked = useRef(new Set());

  useEffect(() => {
    const load = async query => {
      if (asked.current.has(query)) return;
      asked.current.add(query);
      try {
        const d = await api.query(SEARCH_COVER, { query }, true);
        setCovers(m => ({ ...m, [query]: d.searchCover?.url ?? null }));
      } catch {
        setCovers(m => ({ ...m, [query]: null }));
      }
    };
    for (const s of searches.slice(0, 30)) void load(s.query);
  }, [searches]);

  return query => {
    const url = covers[query];
    return url ? imageUrl(url) : url;
  };
}
