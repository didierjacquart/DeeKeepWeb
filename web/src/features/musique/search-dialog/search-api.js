import { api } from '../../../core/api/api';
import { endpoint } from '../../../core/api/endpoints';

/** Pistes Deezer avec leur pastille de présence (✓ ≈ ○ ⏳), entités, en-tête. */
const SEARCH = endpoint('CatalogSearch', 'search', 'GET /api/catalog/search');
const BROWSE = endpoint('CatalogBrowse', 'browse', 'GET /api/catalog/browse/{kind}/{id}');
const HISTORY = endpoint('SearchHistory', 'searchHistory', 'GET /api/catalog/searches');
const COVER = endpoint('SearchCover', 'searchCover', 'GET /api/catalog/search-cover');
const REMEMBER = endpoint('RememberSearch', 'rememberSearch', 'POST /api/catalog/searches');
const DELETE = endpoint('DeleteSearch', 'deleteSearch', 'POST /api/catalog/searches/delete');
const CLEAR = endpoint('ClearSearches', 'clearSearches', 'POST /api/catalog/searches/clear');

/**
 * Requête du catalogue : recherche (`{ type: 'search', query, kind }`) ou
 * exploration d'une entité (`{ type: 'browse', kind, id }`).
 * @typedef {{ type: 'search', query: string, kind: string } | { type: 'browse', kind: string, id: number }} CatalogRequest
 */

/** Résultat d'une recherche ou d'une exploration (`error` rempli si Deezer a échoué). */
export async function fetchCatalog(request) {
  if (request.type === 'search') {
    const r = await api.query(SEARCH, { query: request.query, kind: request.kind });
    return r.search;
  }
  const r = await api.query(BROWSE, { kind: request.kind, id: request.id });
  return r.browse;
}

export async function fetchHistory() {
  const data = await api.query(HISTORY, {});
  return data.searchHistory ?? [];
}

/** Vignette d'une recherche passée. */
export async function fetchSearchCover(query) {
  const r = await api.query(COVER, { query });
  return r.searchCover.url;
}

export function rememberSearch(query) {
  return api.mutate(REMEMBER, { query }, { label: 'Mémoriser la recherche', queueable: false });
}

export function deleteSearch(query) {
  return api.mutate(DELETE, { query }, { label: 'Retirer une recherche', queueable: false });
}

export function clearSearches() {
  return api.mutate(CLEAR, {}, { label: 'Effacer les recherches', queueable: false });
}
