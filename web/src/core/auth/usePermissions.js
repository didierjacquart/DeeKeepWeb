import { useStore } from '../../lib/store';
import { auth } from './auth';
import { can, canSeePage, isAdmin } from './permissions';

/**
 * Droits du compte connecté, pour masquer ou désactiver ce que le serveur refuserait.
 * @returns {{ admin: boolean, canDownload: boolean, canDelete: boolean, canSee: (page: string) => boolean }}
 */
export function usePermissions() {
  const session = useStore(auth, s => s.session);
  return {
    admin: isAdmin(session),
    canDownload: can(session, 'download'),
    canDelete: can(session, 'delete'),
    canSee: page => canSeePage(session, page),
  };
}
