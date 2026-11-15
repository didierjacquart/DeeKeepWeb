import { Fragment, useLayoutEffect, useRef, useState } from 'react';
import { cx } from '../../lib/cx';
import { visibleRange } from './visible-range';

/**
 * Liste à hauteur de ligne fixe dont seules les lignes visibles (plus une marge)
 * sont dessinées : des dizaines de milliers de titres défilent sans à-coups.
 * Le conteneur doit recevoir une hauteur (par sa classe CSS).
 *
 * @template T
 * @param {{
 *   items: readonly T[],
 *   itemHeight: number,
 *   renderItem: (item: T, index: number) => import('react').ReactNode,
 *   itemKey?: (item: T, index: number) => import('react').Key,
 *   className?: string,
 *   buffer?: number,
 * }} props `itemHeight` en pixels (identique pour toutes les lignes) ; `buffer` : marge dessinée au-delà de la zone visible.
 */
export function VirtualList({ items, itemHeight, renderItem, itemKey, className, buffer = 600 }) {
  const viewport = useRef(null);
  const [scrollTop, setScrollTop] = useState(0);
  const [height, setHeight] = useState(0);

  // La hauteur du conteneur suit la mise en page (l'observateur la donne dès l'abonnement).
  useLayoutEffect(() => {
    const el = viewport.current;
    if (!el) return undefined;
    const observer = new ResizeObserver(() => setHeight(el.clientHeight));
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const { start, end } = visibleRange({ count: items.length, itemHeight, scrollTop, height, buffer });

  return (
    <div
      ref={viewport}
      className={cx('dk-viewport', className)}
      onScroll={e => setScrollTop(e.currentTarget.scrollTop)}
    >
      <div style={{ height: items.length * itemHeight, position: 'relative' }}>
        <div
          style={{ position: 'absolute', left: 0, right: 0, top: 0, transform: `translateY(${start * itemHeight}px)` }}
        >
          {items.slice(start, end).map((item, i) => (
            <Fragment key={itemKey ? itemKey(item, start + i) : start + i}>{renderItem(item, start + i)}</Fragment>
          ))}
        </div>
      </div>
    </div>
  );
}
