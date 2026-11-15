import { openDialog } from '../../../shared/dialog/dialog-stack';
import { TagReviewDialog } from './TagReviewDialog';

/**
 * Ouvre la revue des tags ; rend le nombre de fichiers écrits, ou undefined si annulée.
 * La boîte ne se ferme ni par Échap ni par un clic dehors : la recherche tourne, seuls « Annuler » et
 * « Écrire » en sortent.
 * @param {{ sessionId: string, job: import('../../../core/jobs/jobs').Job, files: any[], acoustIdAvailable?: boolean }} data
 * @returns {Promise<number | undefined>}
 */
export function openTagReviewDialog(data) {
  return openDialog(close => <TagReviewDialog data={data} close={close} />, {
    width: 'min(1300px, 97vw)',
    maxWidth: '97vw',
    autoFocus: false,
    disableClose: true,
  });
}
