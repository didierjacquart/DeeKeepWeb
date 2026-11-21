import { endpoint } from '../../../core/api/endpoints';

/** Suppression depuis la bibliothèque : corbeille (durée réglable), propagée au disque USB. */
export const DELETE_TRACKS = endpoint('DeleteTracks', 'deleteFiles', 'POST /api/library/delete-files', {
  body: v => ({ paths: v.paths, reason: 'user' }),
});

/** Fichiers de la corbeille (`TrashEntry` : id, dbPath, name, folder, deletedAt, expiresAt, size). */
export const TRASH = endpoint('Trash', 'trash', 'GET /api/trash');
export const TRASH_RESTORE = endpoint('TrashRestore', 'restore', 'POST /api/trash/restore');
/** Effacement définitif ; `ids` vide = toute la corbeille. */
export const TRASH_PURGE = endpoint('TrashPurge', 'purge', 'POST /api/trash/purge');

/** Durée de conservation de la corbeille (`retentionDays`, 0 = jamais d'effacement automatique). */
export const TRASH_SETTINGS = endpoint('TrashSettings', 'trashSettings', 'GET /api/trash/settings');
export const SAVE_TRASH_SETTINGS = endpoint('SaveTrashSettings', 'trashSettings', 'PUT /api/trash/settings');
