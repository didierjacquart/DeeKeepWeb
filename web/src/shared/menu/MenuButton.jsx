import { useCallback, useRef, useState } from 'react';
import { Menu } from './Menu';

/** Bouton ouvrant un menu déroulant sous lui ; un second clic le referme. */
export function MenuButton({ className, disabled, label, tip, children }) {
  const [at, setAt] = useState(null);
  const trigger = useRef(null);
  const close = useCallback(() => setAt(null), []);
  return (
    <>
      <button
        ref={trigger}
        className={className}
        disabled={disabled}
        data-tip={tip}
        aria-haspopup="menu"
        aria-expanded={!!at}
        onClick={e => {
          const r = e.currentTarget.getBoundingClientRect();
          setAt(open => (open ? null : { x: r.left, y: r.bottom + 4 }));
        }}
      >
        {label}
      </button>
      <Menu at={at} onClose={close} ignore={trigger}>
        {children}
      </Menu>
    </>
  );
}
