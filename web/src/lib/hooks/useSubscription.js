import { useEffect, useEffectEvent } from 'react';

/**
 * Abonnement à un observable rxjs pendant la vie du composant ; rouvert quand
 * l'observable change (le mémoriser avec useMemo). `null` = pas d'abonnement.
 * Les erreurs sont ignorées (ex. flux d'événements coupé : la page se recale à la reconnexion).
 *
 * @template T
 * @param {import('rxjs').Observable<T> | null | undefined} source
 * @param {(value: T) => void} onNext
 */
export function useSubscription(source, onNext) {
  const next = useEffectEvent(onNext);
  useEffect(() => {
    if (!source) return undefined;
    const sub = source.subscribe({ next: v => next(v), error: () => undefined });
    return () => sub.unsubscribe();
  }, [source]);
}
