import { NavLink } from 'react-router';
import { cx } from '../../lib/cx';

/** Entrée de navigation, avec un compteur facultatif (ex. Historique), en alerte s'il y a à faire (comptes à valider). */
export function NavEntry({ item, sub = false, badge, alert = false, badgeLabel }) {
  return (
    <NavLink to={item.path} className={({ isActive }) => cx('nav-btn', sub && 'sub', isActive && 'active')}>
      <span className="ico" aria-hidden="true">
        {item.icon}
      </span>
      {item.label}
      {badge ? (
        <span className={cx('badge', alert && 'alert')} aria-label={badgeLabel} data-tip={badgeLabel}>
          {badge}
        </span>
      ) : null}
    </NavLink>
  );
}
