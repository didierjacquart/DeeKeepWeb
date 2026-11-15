import { useEffect, useEffectEvent } from 'react';

/** Touche Échap au niveau du document, tant que `active` est vrai. */
export function useEscape(onEscape, active = true) {
  const handle = useEffectEvent(onEscape);
  useEffect(() => {
    if (!active) return undefined;
    const onKey = e => {
      if (e.key === 'Escape') handle(e);
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [active]);
}
