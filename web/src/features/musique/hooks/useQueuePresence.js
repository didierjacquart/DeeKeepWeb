import { useEffect, useEffectEvent, useMemo, useState } from 'react';
import { fetchPresence } from '../music-api';
import { presenceKey } from '../music-rows';

/**
 * Pastilles de présence des lignes (ce que le dossier de dépôt contient déjà),
 * recalculées quand l'ensemble des titres ou le nombre de fichiers extraits change.
 * @returns {Map<string, { state: string, reason: string | null }>}
 */
export function useQueuePresence(rows) {
  const [verdicts, setVerdicts] = useState(() => new Map());
  const key = useMemo(() => presenceKey(rows), [rows]);

  // Lignes lues au moment de la requête (les plus récentes).
  const request = useEffectEvent(() => (rows.length ? fetchPresence(rows) : null));

  useEffect(() => {
    if (!key) return;
    request()?.then(setVerdicts, () => {
      // Pastilles « ⏳ » : l'information reste facultative.
    });
  }, [key]);

  return verdicts;
}
