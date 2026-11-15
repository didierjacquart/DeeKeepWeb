import { gql } from '@apollo/client';
import { api } from '../../core/graphql/api';

/**
 * @typedef {object} FsEntry
 * @property {string} name
 * @property {string} localPath
 * @property {boolean} isDirectory
 * @property {number | null} size
 * @property {number | null} mtime
 * @property {boolean} isAudio
 */

/**
 * @typedef {object} FsListing
 * @property {string} localPath
 * @property {string | null} parent
 * @property {FsEntry[]} entries
 * @property {string[]} roots
 */

export const BROWSE_FOLDER = gql`
  query BrowseFolder($path: String, $audioOnly: Boolean!) {
    browseFolder(path: $path, audioOnly: $audioOnly) {
      localPath
      parent
      roots
      entries {
        name
        localPath
        isDirectory
        size
        mtime
        isAudio
      }
    }
  }
`;

/**
 * Contenu d'un dossier du serveur (racines autorisées uniquement) ; `null` = première racine.
 * @returns {Promise<FsListing>}
 */
export async function browseFolder(path, audioOnly) {
  const d = await api.query(BROWSE_FOLDER, { path: path ?? '', audioOnly: !!audioOnly });
  return d.browseFolder;
}
