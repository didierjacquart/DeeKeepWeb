import { gql } from '@apollo/client';

/** Titre de la bibliothèque tel que le serveur le décrit (état disque et archive compris). */

export const TRACK_FIELDS = `
  path title artist album albumArtist genre year duration bitrate codec ext fileSize coverSha1 inHistory onDisk
  archiveState archiveBytes archiveSourceBytes format
`;

/** Toute la bibliothèque (limite 0 = sans limite) : filtre et tri se font sur le client, depuis le cache. */
export const TRACKS_QUERY = gql`
  query LibraryTracks {
    tracks(query: { includeMissing: true, limit: 0 }) { total totalOnDisk totalMissing items { ${TRACK_FIELDS} } }
  }
`;

export const TRACKS_CHANGED = gql`
  subscription LibraryTracksChanged {
    tracksChanged {
      paths
      reason
    }
  }
`;
export const TRACKS_REMOVED = gql`
  subscription LibraryTracksRemoved {
    tracksRemoved {
      paths
      reason
    }
  }
`;
export const ARCHIVE_CHANGED = gql`
  subscription LibraryArchiveChanged {
    archiveUpdated {
      paths
      reason
    }
  }
`;
