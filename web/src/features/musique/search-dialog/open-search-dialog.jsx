import { openDialog } from '../../../shared/dialog/dialog-stack';
import { SearchDialog } from './SearchDialog';

/**
 * Piste Deezer rendue par la recherche (sans sa pastille de présence).
 * @typedef {object} DeezerTrackLite
 * @property {number} id
 * @property {string} title
 * @property {string} artist
 * @property {string | null} album
 * @property {number} duration
 */

/**
 * Ouvre la fenêtre « Rechercher et ajouter des pistes ». Renvoie les pistes cochées,
 * ou undefined si la fenêtre est annulée. Plein écran sur mobile.
 * @param {string} [initialQuery] Recherche lancée dès l'ouverture.
 * @returns {Promise<{ tracks: DeezerTrackLite[] } | undefined>}
 */
export function openSearchDialog(initialQuery) {
  const mobile = typeof window !== 'undefined' && window.innerWidth < 900;
  return openDialog(close => <SearchDialog initialQuery={initialQuery ?? ''} close={close} />, {
    width: mobile ? '100vw' : 'min(1400px, 94vw)',
    maxWidth: '100vw',
    height: mobile ? '100dvh' : 'min(90vh, 900px)',
    maxHeight: '100dvh',
    panelClass: mobile ? 'dk-search-panel mobile' : 'dk-search-panel',
    autoFocus: !mobile,
  });
}
