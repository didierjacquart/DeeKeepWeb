import { useSyncExternalStore } from 'react';

/** Largeur « mobile » de l'application (tiroir de navigation, mises en page compactes). */
export const MOBILE_QUERY = '(max-width: 899px)';

/** Vrai tant que la requête média correspond. */
export function useMediaQuery(query) {
  return useSyncExternalStore(
    listener => {
      const list = window.matchMedia(query);
      list.addEventListener('change', listener);
      return () => list.removeEventListener('change', listener);
    },
    () => window.matchMedia(query).matches,
    () => false,
  );
}

/** Vrai sous 900 px de large. */
export function useMobile() {
  return useMediaQuery(MOBILE_QUERY);
}
