import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { cx } from '../../lib/cx';
import { placeElement } from './place-in-viewport';

/** Délai avant de refermer un sous-menu quitté par le pointeur (passage en diagonale). */
const CLOSE_DELAY = 250;

function SubPanel({ rect, children }) {
  const panel = useRef(null);
  useLayoutEffect(() => {
    if (panel.current) placeElement(panel.current, { x: rect.right, y: rect.top - 8 }, rect.left);
  }, [rect]);
  return (
    <div ref={panel} className="dk-menu sub" role="menu" style={{ left: rect.right, top: rect.top - 8 }}>
      {children}
    </div>
  );
}

/** Entrée ouvrant un sous-menu (au survol, au clic ou à la flèche droite), à droite de l'entrée. */
export function SubMenu({ label, children }) {
  const [rect, setRect] = useState(null);
  const item = useRef(null);
  const timer = useRef(null);

  const open = () => {
    clearTimeout(timer.current);
    if (item.current) setRect(item.current.getBoundingClientRect());
  };
  const scheduleClose = () => {
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setRect(null), CLOSE_DELAY);
  };
  useEffect(() => {
    return () => clearTimeout(timer.current);
  }, []);

  return (
    <div className="dk-submenu" onPointerEnter={open} onPointerLeave={scheduleClose}>
      <button
        ref={item}
        className={cx('dk-menu-item', 'has-sub', rect && 'open')}
        role="menuitem"
        aria-haspopup="menu"
        aria-expanded={!!rect}
        onClick={open}
        onKeyDown={e => e.key === 'ArrowRight' && open()}
      >
        {label}
      </button>
      {rect && <SubPanel rect={rect}>{children}</SubPanel>}
    </div>
  );
}
