import { useEffect, useEffectEvent, useReducer, useRef } from 'react';
import { useLatest } from '../../../../lib/hooks/useLatest';
import { player } from '../../../../core/player/player';
import { notify } from '../../../../core/ui/notify';
import { openTrackDedupDialog } from '../../../organiser/dedup/open-track-dedup-dialog';
import { fetchCatalog } from '../search-api';
import { crumbFor, dedupItems, entityKind, hasPendingPresence, needsPresenceRetry } from '../search-model';
import { initialSearchState, searchReducer } from '../search-reducer';
import { mergePresence } from '../track-list';
import { usePresenceRetry } from './usePresenceRetry';

/**
 * Recherche et exploration du catalogue Deezer : requêtes, fil d'Ariane, pastilles
 * de présence recalculées tant que l'index n'est pas prêt, dédoublonnage. Une
 * réponse arrivée après une autre requête (ou après « Annuler ») est ignorée.
 */
export function useCatalogSearch(initialQuery) {
  // Une recherche initiale est déjà « en cours » au premier rendu.
  const [state, dispatch] = useReducer(searchReducer, initialQuery, q =>
    q ? searchReducer(searchReducer(initialSearchState, { type: 'search' }), { type: 'started' }) : initialSearchState,
  );
  // Valeurs fraîches pour les traitements différés (minuteries, suites d'await).
  const latest = useLatest(state);
  const token = useRef(0);
  const retry = usePresenceRetry(attempt => void refreshPresence(attempt));

  /** Affiche le résultat de la requête numéro `t`, sauf si une autre l'a remplacée entre-temps. */
  const load = (t, request, crumb) =>
    fetchCatalog(request).then(
      data => {
        if (t !== token.current) return;
        player.stopPreview(true);
        dispatch({ type: 'loaded', data, request, crumb });
        if (needsPresenceRetry(data, request)) retry.schedule();
      },
      e => {
        if (t !== token.current) return;
        dispatch({ type: 'failed', error: e instanceof Error ? e.message : String(e) });
      },
    );

  /** Lance une requête (recherche ou exploration) et affiche son résultat ; l'état précédent doit déjà être empilé. */
  const run = (request, crumb) => {
    const t = ++token.current;
    retry.clear();
    dispatch({ type: 'started' });
    return load(t, request, crumb);
  };

  const startInitial = useEffectEvent(() => {
    player.stopPreview(true);
    if (initialQuery) void load(++token.current, { type: 'search', query: initialQuery, kind: 'ALL' }, '');
  });

  // Ouverture : recherche initiale ; fermeture : les réponses en route sont ignorées et l'extrait s'arrête.
  useEffect(() => {
    const requests = token;
    startInitial();
    return () => {
      requests.current++;
      player.stopPreview(true);
    };
  }, []);

  /** Recalcule les pastilles (index prêt, suppression de doublons) sans toucher à la liste. */
  async function refreshPresence(attempt = 0) {
    const { request, mode } = latest.current;
    if (!request || mode !== 'tracks') return;
    const t = token.current;
    try {
      const data = await fetchCatalog(request);
      if (t !== token.current) return;
      const tracks = mergePresence(latest.current.tracks, data.tracks);
      dispatch({ type: 'presence', tracks });
      if (hasPendingPresence(tracks)) retry.schedule(attempt);
    } catch {
      retry.schedule(attempt);
    }
  }

  const search = (query, kind) => {
    dispatch({ type: 'search' });
    player.stopPreview(true);
    return run({ type: 'search', query, kind }, '');
  };

  const go = (kind, id, name) => {
    dispatch({ type: 'navigate' });
    void run({ type: 'browse', kind, id }, crumbFor(kind, name));
  };

  const back = () => {
    const frame = latest.current.stack.at(-1);
    if (!frame) return;
    token.current++;
    retry.clear();
    player.stopPreview(true);
    dispatch({ type: 'back' });
    if (hasPendingPresence(frame.tracks)) retry.schedule();
  };

  /** Interrompt la requête en cours (la fenêtre reste ouverte). */
  const stop = () => {
    token.current++;
    dispatch({ type: 'stopped' });
  };

  const dedupe = async candidates => {
    if (!candidates.length) {
      notify.info("Aucun fichier du dossier de dépôt n'est rapproché des titres affichés.");
      return;
    }
    player.stopPreview(true);
    const deleted = await openTrackDedupDialog(dedupItems(candidates));
    if (deleted?.length) {
      notify.info(`Déduplication : ${deleted.length} fichier(s) supprimé(s).`);
      await refreshPresence();
    }
  };

  return {
    state,
    search,
    back,
    stop,
    dedupe,
    openEntity: entity => go(entityKind(entity), entity.id, entity.name),
    openArtist: track => track.artistId && go('ARTIST', track.artistId, track.artist),
    openAlbum: track => track.albumId && go('ALBUM', track.albumId, track.album ?? ''),
    toggle: (track, on) => dispatch({ type: 'toggle', track, on }),
    selectAll: (visible, on) => dispatch({ type: 'select-all', visible, on }),
    setFilter: filter => dispatch({ type: 'filter', filter }),
    toggleDuplicatesOnly: () => dispatch({ type: 'duplicates-only' }),
    sortBy: column => {
      player.stopPreview(true);
      dispatch({ type: 'sort', column });
    },
    sortEntities: column => dispatch({ type: 'sort-entities', column }),
  };
}
