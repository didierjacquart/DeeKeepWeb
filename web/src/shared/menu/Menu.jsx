import { useEffect, useEffectEvent, useLayoutEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { MenuCloseContext } from './menu-context';
import { placeElement } from './place-in-viewport';

/** @typedef {{ x: number, y: number }} MenuPoint Point d'ancrage, en coordonnées de la fenêtre. */

const outsideMenu = target => !target?.closest?.('.dk-menu');

/** Panneau ouvert : placement, fermeture par Échap / clic dehors / défilement. */
function MenuPanel({ at, onClose, ignore, children }) {
  const panel = useRef(null);
  const close = useEffectEvent(() => onClose());

  useLayoutEffect(() => {
    if (panel.current) placeElement(panel.current, at);
  }, [at]);

  useEffect(() => {
    const onPointer = e => {
      const target = e.target;
      if (!outsideMenu(target) || ignore?.current?.contains(target)) return;
      close();
    };
    const onKey = e => e.key === 'Escape' && close();
    const onScroll = e => outsideMenu(e.target) && close();
    const onResize = () => close();
    document.addEventListener('pointerdown', onPointer, true);
    document.addEventListener('keydown', onKey);
    window.addEventListener('scroll', onScroll, true);
    window.addEventListener('resize', onResize);
    return () => {
      document.removeEventListener('pointerdown', onPointer, true);
      document.removeEventListener('keydown', onKey);
      window.removeEventListener('scroll', onScroll, true);
      window.removeEventListener('resize', onResize);
    };
  }, [ignore]);

  // Le clavier entre dans le menu : première entrée active.
  useEffect(() => {
    panel.current?.querySelector('.dk-menu-item:not(:disabled)')?.focus();
  }, []);

  return createPortal(
    <MenuCloseContext.Provider value={onClose}>
      <div
        ref={panel}
        className="dk-menu"
        role="menu"
        tabIndex={-1}
        style={{ left: at.x, top: at.y }}
        onContextMenu={e => e.preventDefault()}
      >
        {children}
      </div>
    </MenuCloseContext.Provider>,
    document.body,
  );
}

/**
 * Menu flottant (menu contextuel ou déroulant). Affiché tant que `at` n'est pas
 * nul ; se ferme par Échap, par un clic hors du menu ou après le choix d'une entrée.
 *
 * @param {{ at: MenuPoint | null, onClose: () => void, ignore?: import('react').RefObject<HTMLElement>, children: import('react').ReactNode }} props
 *   `ignore` : élément déclencheur, dont le clic est laissé à son propre gestionnaire.
 */
export function Menu({ at, onClose, ignore, children }) {
  if (!at) return null;
  return (
    <MenuPanel at={at} onClose={onClose} ignore={ignore}>
      {children}
    </MenuPanel>
  );
}
