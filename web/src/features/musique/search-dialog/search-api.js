import { gql } from '@apollo/client';
import { api } from '../../../core/graphql/api';

/** Champs de la piste Deezer utiles à l'extraction (ce que la page Musique reçoit). */
const TRACK_FIELDS = `
  id title artist artistId album albumId duration rank previewUrl coverSmall coverBig link explicit trackPosition diskNumber
  presence { key state reason files { path reason bitrate fileSize ext } }
`;
const RESULT_FIELDS = `
  query kind error
  header { kind id name subtitle picture trackCount fanCount }
  entities { kind id name subtitle picture trackCount fanCount }
  tracks { ${TRACK_FIELDS} }
`;
const SEARCH = gql`query CatalogSearch($query: String!, $kind: SearchKind!) { search(query: $query, kind: $kind) { ${RESULT_FIELDS} } }`;
const BROWSE = gql`query CatalogBrowse($kind: EntityKind!, $id: Long!) { browse(kind: $kind, id: $id) { ${RESULT_FIELDS} } }`;
const HISTORY = gql`
  query SearchHistory {
    searchHistory {
      query
      position
    }
  }
`;
const COVER = gql`
  query SearchCover($query: String!) {
    searchCover(query: $query) {
      query
      url
    }
  }
`;
const REMEMBER = gql`
  mutation RememberSearch($query: String!) {
    rememberSearch(query: $query) {
      query
      position
    }
  }
`;
const DELETE = gql`
  mutation DeleteSearch($query: String!) {
    deleteSearch(query: $query) {
      query
      position
    }
  }
`;
const CLEAR = gql`
  mutation ClearSearches {
    clearSearches {
      query
      position
    }
  }
`;

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
