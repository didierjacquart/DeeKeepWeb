import { NavLink } from 'react-router';
import { cx } from '../../lib/cx';

/** Entrée de navigation, avec un compteur facultatif (ex. Historique). */
export function NavEntry({ item, sub = false, badge }) {
  return (
    <NavLink to={item.path} className={({ isActive }) => cx('nav-btn', sub && 'sub', isActive && 'active')}>
      <span className="ico" aria-hidden="true">
        {item.icon}
      </span>
      {item.label}
      {badge ? <span className="badge">{badge}</span> : null}
    </NavLink>
  );
}
