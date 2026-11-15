import { useEffect, useEffectEvent, useMemo, useRef, useState } from 'react';
import { useStore } from '../../../../lib/store';
import { useLatest } from '../../../../lib/hooks/useLatest';
import { useSubscription } from '../../../../lib/hooks/useSubscription';
import { api } from '../../../../core/graphql/api';
import { connectivity } from '../../../../core/offline/connectivity';
import { archiveActions } from '../archive-actions';
import { ARCHIVE_UPDATED, ARCHIVE_USAGE } from '../archive-api';

/**
 * Fichier d'archive saisi dans le volet et jauge de place associée : configuration
 * lue à l'ouverture et à chaque reconnexion, jauge relue après la frappe (400 ms),
 * après chaque changement de l'archive (regroupé sur 1,5 s) ou à la demande.
 */
export function useArchiveSettings() {
  const [root, setRoot] = useState('');
  const [usage, setUsage] = useState(null);
  /** Valeur courante du champ, lue par les rappels différés (minuteries, fins de tâche). */
  const rootRef = useLatest(root);
  const timers = useRef({ root: null, refresh: null });

  const refresh = (value = rootRef.current) => {
    const r = value.trim() || null;
    api
      .query(ARCHIVE_USAGE, { root: r })
      .then(d => setUsage(d.archiveUsage))
      .catch(() => undefined);
  };

  const scheduleRefresh = () => {
    const t = timers.current;
    if (t.refresh) return;
    t.refresh = setTimeout(() => {
      t.refresh = null;
      refresh();
    }, 1500);
  };

  // Demandes de relecture nées pendant le rendu (fin d'une tâche) : servies après coup.
  const [request, setRequest] = useState(null);
  const serve = useEffectEvent(kind => (kind === 'later' ? scheduleRefresh() : refresh()));
  useEffect(() => {
    if (request) serve(request.kind);
  }, [request]);

  // Première lecture de la configuration ; les minuteries du volet s'arrêtent avec lui.
  const loaded = useEffectEvent(c => {
    setRoot(c?.root ?? '');
    refresh(c?.root ?? '');
  });
  useEffect(() => {
    void archiveActions.loadConfig().then(c => loaded(c));
    const t = timers.current;
    return () => {
      clearTimeout(t.root);
      clearTimeout(t.refresh);
      t.refresh = null;
    };
  }, []);

  // Reconnexion : configuration et jauge relues.
  const resyncTick = useStore(connectivity, s => s.resyncTick);
  const resync = useEffectEvent(() => void archiveActions.loadConfig().then(() => refresh()));
  useEffect(() => {
    if (resyncTick !== 0) resync();
  }, [resyncTick]);

  // La colonne NUAGE change : la jauge suit (regroupé, une archive de 3 000 titres émet souvent).
  const updates = useMemo(() => api.subscribe(ARCHIVE_UPDATED), []);
  useSubscription(updates, () => scheduleRefresh());

  return {
    root,
    usage,
    /** Saisie (ou choix) du chemin : la jauge suit 400 ms après la dernière frappe. */
    inputRoot: value => {
      setRoot(value);
      clearTimeout(timers.current.root);
      timers.current.root = setTimeout(() => refresh(value), 400);
    },
    /** Chemin rendu par le serveur après l'enregistrement. */
    setRoot,
    refresh,
    /** Relecture demandée pendant le rendu : `now` tout de suite, `later` regroupée. */
    requestRefresh: kind => setRequest({ kind }),
  };
}
