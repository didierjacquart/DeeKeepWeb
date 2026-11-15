import { gql } from '@apollo/client';

export const HISTORY_Q = gql`
  query History {
    history {
      path
      title
      artist
      album
      coverSha1
      position
      onDisk
      downloadedAt
    }
    searchHistory {
      query
      position
    }
  }
`;

export const SEARCH_COVER = gql`
  query SearchCover($query: String!) {
    searchCover(query: $query) {
      query
      url
    }
  }
`;

export const PURGE = gql`
  mutation PurgeMissingHistory {
    purgeMissingHistory {
      removed
      message
    }
  }
`;

export const CLEAR = gql`
  mutation ClearHistory {
    clearHistory {
      removed
      message
    }
  }
`;

export const DELETE_SEARCH = gql`
  mutation DeleteSearch($query: String!) {
    deleteSearch(query: $query) {
      query
      position
    }
  }
`;

export const CLEAR_SEARCHES = gql`
  mutation ClearSearches {
    clearSearches {
      query
      position
    }
  }
`;

export const ADD_PENDING = gql`
  mutation AddPending($tracks: [DownloadTrackInput!]!) {
    addPendingDownloads(tracks: $tracks) {
      id
    }
  }
`;

export const HISTORY_CHANGED = gql`
  subscription HistoryChanged {
    historyChanged {
      paths
    }
  }
`;
