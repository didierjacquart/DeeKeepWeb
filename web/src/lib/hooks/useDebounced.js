import { useEffect, useState } from 'react';

/** Valeur recopiée après un délai sans changement (saisie au clavier). */
export function useDebounced(value, ms) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(id);
  }, [value, ms]);
  return debounced;
}
