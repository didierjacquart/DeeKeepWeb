import { useStore } from '../../lib/store';
import { cx } from '../../lib/cx';
import { AppLogo } from '../AppLogo';
import { navBadges } from '../nav-badges';
import { BOTTOM_NAV, MAIN_NAV, TOOLS_NAV } from './nav-items';
import { NavEntry } from './NavEntry';
import { LinkStatus } from './LinkStatus';

/** Barre latérale : logo, navigation (groupe « Outils » repliable) et état de la liaison. */
export function Sidebar({ open, toolsOpen, onToggleTools, onOpenJobs, onToggleJobs }) {
  const historyBadge = useStore(navBadges, s => s.history);
  return (
    <aside className={cx('sidebar', open && 'open')}>
      <nav className="nav" aria-label="Navigation principale">
        <AppLogo className="logo" />
        {MAIN_NAV.map(item => (
          <NavEntry key={item.path} item={item} />
        ))}
        <button className="nav-group" onClick={onToggleTools} aria-expanded={toolsOpen}>
          {toolsOpen ? '▾' : '▸'} Outils
        </button>
        {toolsOpen && TOOLS_NAV.map(item => <NavEntry key={item.path} item={item} sub />)}
        {BOTTOM_NAV.map(item => (
          <NavEntry key={item.path} item={item} badge={item.path === '/historique' ? historyBadge : null} />
        ))}
        <span className="dk-spacer" />
        <LinkStatus onOpenJobs={onOpenJobs} onToggleJobs={onToggleJobs} />
      </nav>
    </aside>
  );
}
