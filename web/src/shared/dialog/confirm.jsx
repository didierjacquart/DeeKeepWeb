import { openDialog } from './dialog-stack';
import { ConfirmDialog } from './ConfirmDialog';

/**
 * Boîte de question (les messages de l'original sont repris dans les pages).
 * @param {import('./ConfirmDialog').ConfirmData} data
 * @returns {Promise<boolean>} vrai seulement si l'utilisateur a confirmé.
 */
export async function confirm(data) {
  const answer = await openDialog(close => <ConfirmDialog data={data} close={close} />, { width: 'min(480px, 94vw)' });
  return answer === true;
}
