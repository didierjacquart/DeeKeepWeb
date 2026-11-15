import { useState } from 'react';
import { clearLookup, readLookup, writeLookup } from '../lookup-storage';

/**
 * Revue des tags à reprendre (bouton « Reprendre la revue des tags »), gardée dans le navigateur.
 * `restore(sessionId)` la relit au chargement de la page.
 */
export function useLookupResume() {
  const [resume, setResume] = useState(null);

  /** Une revue interrompue par un rechargement se rouvre : les propositions sont gardées côté serveur. */
  const restore = sessionId => {
    const r = readLookup(sessionId);
    if (r) setResume(r);
  };

  const remember = r => {
    setResume(r);
    writeLookup(r);
  };

  const forget = () => {
    setResume(null);
    clearLookup();
  };

  return { resume, restore, remember, forget };
}
