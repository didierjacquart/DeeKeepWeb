import { useEffect, useEffectEvent } from 'react';

/** Rappel périodique pendant la vie du composant (`ms` nul = arrêté). */
export function useInterval(callback, ms) {
  const tick = useEffectEvent(callback);
  useEffect(() => {
    if (ms == null) return undefined;
    const id = setInterval(() => tick(), ms);
    return () => clearInterval(id);
  }, [ms]);
}
