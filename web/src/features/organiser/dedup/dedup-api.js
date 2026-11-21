import { api } from '../../../core/api/api';
import { endpoint } from '../../../core/api/endpoints';
import { DELETE_FILES } from '../organiser.queries';

/** Suppression hors session d'analyse (« Dédoublonner… » de la recherche). */
const DELETE = endpoint('DeleteOrganizeFiles', 'deleteOrganizeFiles', 'POST /api/organize/delete-files');

/** Supprime (corbeille de la bibliothèque) des fichiers d'une session d'analyse. */
export const deleteSessionFiles = (paths, sessionId) =>
  api.mutate(DELETE_FILES, { paths, sessionId }, { label: 'Supprimer des doublons', queueable: false });

/** Supprime (corbeille de la bibliothèque) des fichiers de la bibliothèque. */
export const deleteLibraryFiles = paths =>
  api.mutate(DELETE, { paths }, { label: 'Supprimer des doublons', queueable: false });
