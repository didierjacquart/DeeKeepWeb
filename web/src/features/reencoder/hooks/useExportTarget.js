import { useEffect, useState } from 'react';
import { useStore } from '../../../lib/store';
import { connectivity } from '../../../core/offline/connectivity';
import { loadExportTarget } from '../reencode-api';

const DEFAULT_TARGET = { format: 'MP3', quality: '320 kbps (HQ)' };

/** Format et qualité cibles des Paramètres, relus à chaque reconnexion. */
export function useExportTarget() {
  const resyncTick = useStore(connectivity, s => s.resyncTick);
  const [target, setTarget] = useState(DEFAULT_TARGET);

  useEffect(() => {
    void (async () => {
      try {
        const current = await loadExportTarget();
        if (current) setTarget(current);
      } catch {
        // Hors ligne : valeurs par défaut.
      }
    })();
  }, [resyncTick]);

  return target;
}
