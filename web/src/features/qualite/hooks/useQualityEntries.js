import { useEffect, useMemo, useState } from 'react';
import { useStore } from '../../../lib/store';
import { useSubscription } from '../../../lib/hooks/useSubscription';
import { api } from '../../../core/graphql/api';
import { connectivity } from '../../../core/offline/connectivity';
import { qualityUpdates, watchQualityEntries } from '../quality-api';
import { mergeEntries } from '../quality-model';

/**
 * Liste tenue par le serveur : lue « cache puis réseau », puis tenue à jour au fil de l'eau par l'abonnement
 * « qualityUpdated ». `onThresholds` reçoit les seuils relus ; `onCleared` est appelé quand la liste est vidée.
 */
export function useQualityEntries({ onThresholds, onCleared }) {
  const [rows, setRows] = useState(() => new Map());
  const [folder, setFolder] = useState('');
  const [loaded, setLoaded] = useState(false);
  const [stale, setStale] = useState(false);

  // Lecture « cache puis réseau » : la liste s'affiche hors ligne.
  const entries = useMemo(() => watchQualityEntries(), []);
  useSubscription(entries, state => {
    const page = state.data?.qualityEntries;
    setStale(state.stale);
    if (!page) return;
    setLoaded(true);
    setFolder(page.folder ?? '');
    onThresholds(page.thresholds);
    setRows(new Map(page.items.map(r => [r.path, r])));
  });

  const apply = update => {
    if (update.reason === 'cleared' || update.reason === 'folder') {
      setRows(new Map());
      onCleared();
      if (update.reason === 'folder') api.refetchActive();
      return;
    }
    if (update.reason === 'thresholds') {
      // Le verdict est recalculé par le serveur : on relit la liste (aucun fichier n'est relu).
      api.refetchActive();
      return;
    }
    if (update.entries.length === 0) return;
    setRows(m => mergeEntries(m, update));
  };

  const updates = useMemo(() => qualityUpdates(), []);
  useSubscription(updates, d => {
    if (d?.qualityUpdated) apply(d.qualityUpdated);
  });

  // La page tient un état local : à chaque reconnexion, elle se resynchronise.
  const resyncTick = useStore(connectivity, s => s.resyncTick);
  useEffect(() => {
    api.refetchActive();
  }, [resyncTick]);

  const reset = () => setRows(new Map());

  return { rows, folder, setFolder, loaded, stale, reset };
}
