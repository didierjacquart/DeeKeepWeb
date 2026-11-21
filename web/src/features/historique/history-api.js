import { combine, endpoint, serverEvent } from '../../core/api/endpoints';

/** Historique des téléchargements et des recherches. */
export const HISTORY_Q = combine(
  'History',
  endpoint('History', 'history', 'GET /api/library/history'),
  endpoint('History', 'searchHistory', 'GET /api/catalog/searches'),
);

export const SEARCH_COVER = endpoint('SearchCover', 'searchCover', 'GET /api/catalog/search-cover');
export const PURGE = endpoint('PurgeMissingHistory', 'purgeMissingHistory', 'POST /api/library/history/purge-missing');
export const CLEAR = endpoint('ClearHistory', 'clearHistory', 'POST /api/library/history/clear');
export const DELETE_SEARCH = endpoint('DeleteSearch', 'deleteSearch', 'POST /api/catalog/searches/delete');
export const CLEAR_SEARCHES = endpoint('ClearSearches', 'clearSearches', 'POST /api/catalog/searches/clear');
export const ADD_PENDING = endpoint('AddPending', 'addPendingDownloads', 'POST /api/downloads/pending');
export const HISTORY_CHANGED = serverEvent('HistoryChanged', 'historyChanged', 'history.changed');
