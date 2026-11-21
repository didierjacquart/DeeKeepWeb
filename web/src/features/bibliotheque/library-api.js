import { endpoint, serverEvent } from '../../core/api/endpoints';

/**
 * Toute la bibliothèque visible (limite 0 = sans limite) : filtre et tri se font sur le client, depuis le cache.
 * Un utilisateur reçoit ses titres ; l'administrateur tous (ou ceux de `userId`), avec leurs utilisateurs.
 */
export const TRACKS_QUERY = endpoint('LibraryTracks', 'tracks', 'POST /api/library/tracks/query', {
  body: v => ({ includeMissing: true, limit: 0, userId: v?.userId ?? null }),
});

/** Administrateur : nombre de titres de chaque utilisateur (filtre « Titres de »). */
export const USER_COUNTS = endpoint('UserCounts', 'userCounts', 'GET /api/library/user-counts');
/** Administrateur : utilisateurs d'un titre (variables : `path`). */
export const TRACK_USERS = endpoint('TrackUsers', 'trackUsers', 'GET /api/library/track-users');
/** Administrateur : retire des titres de la bibliothèque d'un utilisateur (fichiers intacts). */
export const UNLINK_USER = endpoint('UnlinkUser', 'unlink', 'POST /api/library/user-tracks/unlink');

export const TRACKS_CHANGED = serverEvent('LibraryTracksChanged', 'tracksChanged', 'tracks.changed');
export const TRACKS_REMOVED = serverEvent('LibraryTracksRemoved', 'tracksRemoved', 'tracks.removed');
export const ARCHIVE_CHANGED = serverEvent('LibraryArchiveChanged', 'archiveUpdated', 'archive.updated');
