import { useStore } from '../../lib/store';
import { cx } from '../../lib/cx';
import { auth } from '../../core/auth/auth';
import { canSeePage } from '../../core/auth/permissions';
import { AppLogo } from '../AppLogo';
import { navBadges } from '../nav-badges';
import { BOTTOM_NAV, MAIN_NAV, TOOLS_NAV, visibleNav } from './nav-items';
import { NavEntry } from './NavEntry';
import { LinkStatus } from './LinkStatus';

/**
 * Barre latérale : logo, navigation (groupe « Outils » repliable) et état de la liaison.
 * Seules les pages ouvertes au compte y figurent ; sans accès aux Paramètres (où se
 * trouve le compte), un bouton de déconnexion les remplace.
 */
export function Sidebar({ open, toolsOpen, onToggleTools, onOpenJobs, onToggleJobs }) {
  const historyBadge = useStore(navBadges, s => s.history);
  const session = useStore(auth, s => s.session);
  const main = visibleNav(MAIN_NAV, session);
  const tools = visibleNav(TOOLS_NAV, session);
  const bottom = visibleNav(BOTTOM_NAV, session);
  const showLogout = !!session?.authEnabled && !canSeePage(session, 'parametres');

  const logout = async () => {
    await auth.logout();
    location.assign('/login');
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
          <NavEntry key={item.path} item={item} badge={item.path === '/historique' ? historyBadge : null} />
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
