import { useMemo, useState } from 'react';
import { debounceTime, merge } from 'rxjs';
import { api } from '../../../core/api/api';
import { useWatch } from '../../../core/api/useWatch';
import { useSubscription } from '../../../lib/hooks/useSubscription';
import { ARCHIVE_CHANGED, TRACKS_CHANGED, TRACKS_QUERY, TRACKS_REMOVED } from '../library-api';
import { toRow } from '../library-model';

/**
 * Titres de la bibliothèque, lus dans le cache local (affichés hors ligne), et
 * rechargés après chaque changement côté serveur.
 * @returns {{ page: object | null, rows: object[], loading: boolean, stale: boolean, error: string | null }}
 */
export function useLibraryTracks(userId = null) {
  const watched = useWatch(TRACKS_QUERY, userId ? { userId } : undefined);
  // Dernière page reçue : gardée si une réponse ultérieure arrive sans données.
  const [kept, setKept] = useState(null);
  const received = watched.data?.tracks;
  if (received && received !== kept) setKept(received);
  const page = received ?? kept;

  const rows = useMemo(() => (page?.items ?? []).map(toRow), [page]);

  // Changements côté serveur (enregistrement, relecture, archivage) : rechargement groupé.
  const changes = useMemo(
    () =>
      merge(api.subscribe(TRACKS_CHANGED), api.subscribe(TRACKS_REMOVED), api.subscribe(ARCHIVE_CHANGED)).pipe(
        debounceTime(1200),
      ),
    [],
  );
  useSubscription(changes, () => api.refetchActive());

  return {
    page,
    rows,
    loading: watched.loading && !watched.data,
    stale: watched.stale,
    error: watched.error && !watched.data ? watched.error : null,
  };
}
