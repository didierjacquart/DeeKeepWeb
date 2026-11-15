import { openDialog } from '../dialog/dialog-stack';
import { FsBrowserDialog } from './FsBrowserDialog';

/**
 * Ouvre le navigateur de dossiers du serveur ; rend les chemins choisis, ou undefined si annulé.
 * @param {import('./FsBrowserDialog').FsBrowserOptions} options
 * @returns {Promise<string[] | undefined>}
 */
export function openFsBrowser(options) {
  return openDialog(close => <FsBrowserDialog options={options} close={close} />, { width: 'min(620px, 96vw)' });
}
