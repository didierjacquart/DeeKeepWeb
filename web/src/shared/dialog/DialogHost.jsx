import { useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { useStore } from '../../lib/store';
import { cx } from '../../lib/cx';
import { useEscape } from '../../lib/hooks/useEscape';
import { dialogs } from './dialog-stack';

const FOCUSABLE =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/** Une boîte ouverte : fond, panneau, focus initial et fermeture. */
function DialogPanel({ entry, top }) {
  const panel = useRef(null);
  const { options, close } = entry;

  // Focus initial, puis retour du focus là où il était à la fermeture.
  useEffect(() => {
    const previous = document.activeElement;
    const target =
      options.autoFocus !== false &&
      (panel.current?.querySelector('[data-autofocus]') ?? panel.current?.querySelector(FOCUSABLE));
    (target || panel.current)?.focus();
    return () => previous?.focus?.();
  }, [options.autoFocus]);

  useEscape(e => {
    e.stopPropagation();
    close();
  }, top && !options.disableClose);

  const style = {
    width: options.width,
    height: options.height,
    maxWidth: options.maxWidth ?? '80vw',
    maxHeight: options.maxHeight ?? '100dvh',
  };

  return (
    <div
      className="dk-dialog-backdrop"
      role="presentation"
      onPointerDown={e => {
        if (e.target === e.currentTarget && !options.disableClose) close();
      }}
    >
      <div
        ref={panel}
        className={cx('dk-dialog', options.panelClass)}
        style={style}
        role="dialog"
        aria-modal="true"
        tabIndex={-1}
      >
        {entry.render(close)}
      </div>
    </div>
  );
}

/** Hôte des boîtes de dialogue ouvertes, à placer une fois à la racine de l'application. */
export function DialogHost() {
  const stack = useStore(dialogs, s => s.stack);
  if (!stack.length) return null;
  return createPortal(
    <>
      {stack.map((entry, i) => (
        <DialogPanel key={entry.id} entry={entry} top={i === stack.length - 1} />
      ))}
    </>,
    document.body,
  );
}
