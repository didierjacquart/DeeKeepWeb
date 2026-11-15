import { gql } from '@apollo/client';
import { api } from '../../../core/graphql/api';
import { DELETE_FILES } from '../organiser.queries';

/** Suppression hors session d'analyse (« Dédoublonner… » de la recherche). */
const DELETE = gql`
  mutation DeleteOrganizeFiles($paths: [String!]!) {
    deleteOrganizeFiles(paths: $paths) {
      deleted
      failed
      errors
    }
  }
`;

/** Supprime (corbeille de la bibliothèque) des fichiers d'une session d'analyse. */
export const deleteSessionFiles = (paths, sessionId) =>
  api.mutate(DELETE_FILES, { paths, sessionId }, { label: 'Supprimer des doublons', queueable: false });

/** Supprime (corbeille de la bibliothèque) des fichiers de la bibliothèque. */
export const deleteLibraryFiles = paths =>
  api.mutate(DELETE, { paths }, { label: 'Supprimer des doublons', queueable: false });
