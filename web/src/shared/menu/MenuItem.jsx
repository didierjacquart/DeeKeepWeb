import { useContext } from 'react';
import { MenuCloseContext } from './menu-context';

/** Entrée de menu ; le menu se ferme après le clic. */
export function MenuItem({ onClick, disabled, children }) {
  const close = useContext(MenuCloseContext);
  return (
    <button
      className="dk-menu-item"
      role="menuitem"
      disabled={disabled}
      onClick={() => {
        close();
        onClick?.();
      }}
    >
      {children}
    </button>
  );
}
