import { useLayoutEffect, useRef } from 'react';

/**
 * Référence vers la dernière valeur rendue, pour les gestionnaires asynchrones
 * qui doivent relire l'état après un `await`. Dans un effet, préférer useEffectEvent.
 * @template T
 * @param {T} value
 * @returns {{ readonly current: T }}
 */
export function useLatest(value) {
  const ref = useRef(value);
  useLayoutEffect(() => {
    ref.current = value;
  });
  return ref;
}
