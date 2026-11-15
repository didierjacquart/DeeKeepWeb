import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { tooltipPosition } from './tooltip-position';

const SHOW_DELAY = 250;
function TooltipBubble({ tip }) {
  const bubble = useRef(null);
  useLayoutEffect(() => {
    const el = bubble.current;
    if (!el) return;
    const { width, height } = el.getBoundingClientRect();
    const { left, top } = tooltipPosition(
      tip.rect,
      { width, height },
      { width: window.innerWidth, height: window.innerHeight },
    );
    el.style.left = `${left}px`;
    el.style.top = `${top}px`;
    el.style.visibility = 'visible';
  }, [tip]);
  return (
    <div ref={bubble} className="dk-tooltip" role="tooltip" style={{ left: 0, top: 0, visibility: 'hidden' }}>
      {tip.text}
    </div>
  );
}

/**
 * Infobulles de toute l'application : tout élément portant `data-tip="…"` en
 * affiche une au survol ou au focus clavier (sous l'élément, au-dessus s'il n'y
 * a pas la place). Les retours à la ligne du texte sont respectés.
 */
export function TooltipHost() {
  const [tip, setTip] = useState(null);

  useEffect(() => {
    let timer = null;
    let current = null;
    const clear = () => {
      clearTimeout(timer);
      timer = null;
    };
    const show = (el, delay) => {
      clear();
      current = el;
      timer = setTimeout(() => {
        const text = el.getAttribute('data-tip');
        if (text && el.isConnected) setTip({ text, rect: el.getBoundingClientRect() });
      }, delay);
    };
    const hide = () => {
      clear();
      current = null;
      setTip(null);
    };
    const onOver = e => {
      const el = e.target?.closest?.('[data-tip]');
      if (el === current) return;
      if (!el) hide();
      else show(el, SHOW_DELAY);
    };
    const onFocus = e => {
      const el = e.target?.closest?.('[data-tip]');
      if (el && e.target.matches(':focus-visible')) show(el, 0);
    };
    document.addEventListener('pointerover', onOver);
    document.addEventListener('focusin', onFocus);
    document.addEventListener('focusout', hide);
    document.addEventListener('pointerdown', hide, true);
    window.addEventListener('scroll', hide, true);
    return () => {
      clear();
      document.removeEventListener('pointerover', onOver);
      document.removeEventListener('focusin', onFocus);
      document.removeEventListener('focusout', hide);
      document.removeEventListener('pointerdown', hide, true);
      window.removeEventListener('scroll', hide, true);
    };
  }, []);

  if (!tip) return null;
  return createPortal(<TooltipBubble tip={tip} />, document.body);
}
