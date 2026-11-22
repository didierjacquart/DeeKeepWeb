import { useStore } from '../../lib/store';
import { cx } from '../../lib/cx';
import { auth } from '../../core/auth/auth';
import { useWatch } from '../../core/api/useWatch';
import { USERS } from '../../features/administration/admin-api';
import { AppLogo } from '../AppLogo';
import { navBadges } from '../nav-badges';
import { BOTTOM_NAV, MAIN_NAV, TOOLS_NAV, visibleNav } from './nav-items';
import { NavEntry } from './NavEntry';
import { LinkStatus } from './LinkStatus';

/** Comptes relus toutes les minutes pour la pastille « à valider » (administrateurs seulement). */
const PENDING_POLL_MS = 60000;

/**
 * Barre latérale : logo, navigation (groupe « Outils » repliable), déconnexion et état de la
 * liaison. Seules les pages ouvertes au compte y figurent. Un administrateur voit sur
 * « Administration » une pastille d'alerte : le nombre de comptes qui attendent sa validation.
 */
export function Sidebar({ open, toolsOpen, onToggleTools, onOpenJobs, onToggleJobs }) {
  const historyBadge = useStore(navBadges, s => s.history);
  const session = useStore(auth, s => s.session);
  const main = visibleNav(MAIN_NAV, session);
  const tools = visibleNav(TOOLS_NAV, session);
  const bottom = visibleNav(BOTTOM_NAV, session);
  const showLogout = !!session?.authEnabled && !!session?.authenticated;
  const admin = !!session?.authEnabled && session?.role === 'admin';
  // Même requête que la page Administration : une validation y met aussitôt la pastille à jour.
  const accounts = useWatch(USERS, admin ? undefined : null, PENDING_POLL_MS);
  const pending = admin ? (accounts.data?.users ?? []).filter(u => u.pending).length : 0;

  const logout = async () => {
    await auth.logout();
    location.assign('/login');
  };

  const badgeOf = item => {
    if (item.path === '/historique') return { badge: historyBadge };
    if (item.path === '/administration' && pending > 0)
      return { badge: pending, alert: true, badgeLabel: `${pending} compte(s) à valider` };
    return {};
  };

  return (
    <aside className={cx('sidebar', open && 'open')}>
      <nav className="nav" aria-label="Navigation principale">
        <AppLogo className="logo" />
        {main.map(item => (
          <NavEntry key={item.path} item={item} />
        ))}
        {tools.length > 0 && (
          <button className="nav-group" onClick={onToggleTools} aria-expanded={toolsOpen}>
            {toolsOpen ? '▾' : '▸'} Outils
          </button>
        )}
        {toolsOpen && tools.map(item => <NavEntry key={item.path} item={item} sub />)}
        {bottom.map(item => (
          <NavEntry key={item.path} item={item} {...badgeOf(item)} />
        ))}
        {showLogout && (
          <button className="nav-btn" onClick={() => void logout()} data-tip={`Connecté : ${session.user}`}>
            <span className="ico" aria-hidden="true">
              ⎋
            </span>
            Se déconnecter
          </button>
        )}
        <span className="dk-spacer" />
        <LinkStatus onOpenJobs={onOpenJobs} onToggleJobs={onToggleJobs} />
      </nav>
    </aside>
  );
}
