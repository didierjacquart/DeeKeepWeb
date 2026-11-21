import { api } from '../../core/api/api';
import { endpoint } from '../../core/api/endpoints';

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

export const BROWSE_FOLDER = endpoint('BrowseFolder', 'browseFolder', 'GET /api/library/browse');

/**
 * Contenu d'un dossier du serveur (racines autorisées uniquement) ; `null` = première racine.
 * @returns {Promise<FsListing>}
 */
export async function browseFolder(path, audioOnly) {
  const d = await api.query(BROWSE_FOLDER, { path: path ?? '', audioOnly: !!audioOnly });
  return d.browseFolder;
}
