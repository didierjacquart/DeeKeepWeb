import { isEntitiesView, noResultMessage, searchError } from './search-model';
import { pickVisible, togglePick, uncheckChanged, uncheckDuplicates } from './track-list';

/**
 * État de la fenêtre de recherche : vue courante (pistes, entités), navigation
 * (pile d'états pour « ← Retour »), filtre, tri et pistes cochées. Les effets
 * (réseau, extrait, minuteries) restent dans `useCatalogSearch`.
 */
export const initialSearchState = {
  loading: false,
  message: null,
  mode: 'idle',
  tracks: [],
  entities: [],
  crumb: '',
  header: null,
  /** États empilés par l'exploration. */
  stack: [],
  /** Requête qui a produit la vue (rejouée pour recalculer les pastilles). */
  request: null,
  filter: '',
  duplicatesOnly: false,
  sortColumn: null,
  sortAsc: true,
  entitySort: null,
  /** Pistes cochées, par identifiant (conservées d'une vue à l'autre). */
  picked: new Map(),
};

const clearedFilter = { filter: '', duplicatesOnly: false };

/** Affiche le résultat d'une requête (l'état précédent est déjà empilé). */
function show(state, data, req) {
  if (data.error) return { ...state, message: searchError(data.error), mode: 'idle', tracks: [], entities: [] };
  if (isEntitiesView(data, req)) {
    return { ...state, mode: 'entities', entities: data.entities, tracks: [], header: null, entitySort: null };
  }
  const next = {
    ...state,
    ...clearedFilter,
    mode: 'tracks',
    tracks: data.tracks,
    entities: [],
    header: req.type === 'browse' && req.kind !== 'ARTIST' ? data.header : null,
  };
  if (data.tracks.length === 0) return { ...next, message: noResultMessage(req) };
  return { ...next, picked: uncheckDuplicates(state.picked, data.tracks) };
}

export function searchReducer(state, action) {
  switch (action.type) {
    // Nouvelle recherche : la navigation repart de zéro.
    case 'search':
      return { ...state, ...clearedFilter, stack: [], crumb: '', header: null, sortColumn: null };
    // Exploration d'une entité : la vue courante est empilée.
    case 'navigate': {
      const frame = {
        mode: state.mode === 'entities' ? 'entities' : 'tracks',
        tracks: state.tracks,
        entities: state.entities,
        crumb: state.crumb,
        header: state.header,
        request: state.request,
      };
      return { ...state, ...clearedFilter, stack: [...state.stack, frame], sortColumn: null };
    }
    case 'started':
      return { ...state, loading: true, message: null };
    case 'loaded':
      return {
        ...show({ ...state, request: action.request, crumb: action.crumb }, action.data, action.request),
        loading: false,
      };
    case 'failed':
      return { ...state, loading: false, message: searchError(action.error) };
    case 'stopped':
      return { ...state, loading: false };
    // « ← Retour » : restaure l'état empilé sans requête réseau (en-tête d'album compris).
    case 'back': {
      const frame = state.stack[state.stack.length - 1];
      if (!frame) return state;
      return { ...state, ...frame, ...clearedFilter, loading: false, message: null, stack: state.stack.slice(0, -1) };
    }
    case 'presence':
      return { ...state, tracks: action.tracks, picked: uncheckChanged(state.picked, state.tracks, action.tracks) };
    case 'toggle':
      return { ...state, picked: togglePick(state.picked, action.track, action.on) };
    case 'select-all':
      return { ...state, picked: pickVisible(state.picked, action.visible, action.on) };
    case 'filter':
      return { ...state, filter: action.filter };
    case 'duplicates-only':
      return { ...state, duplicatesOnly: !state.duplicatesOnly };
    case 'sort':
      return state.sortColumn === action.column
        ? { ...state, sortAsc: !state.sortAsc }
        : // Popularité : les plus populaires en tête dès le premier clic.
          { ...state, sortColumn: action.column, sortAsc: action.column !== 'rank' };
    case 'sort-entities':
      return {
        ...state,
        entitySort: {
          column: action.column,
          asc: state.entitySort?.column === action.column ? !state.entitySort.asc : true,
        },
      };
    default:
      return state;
  }
}
