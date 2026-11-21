import { useMemo, useState } from 'react';
import { useSubscription } from '../../../lib/hooks/useSubscription';
import { api } from '../../../core/api/api';
import { notify } from '../../../core/ui/notify';
import { SETTINGS_CHANGED, SETTINGS_Q, UPDATE } from '../settings-api';

/**
 * Réglages et informations de la base, suivis en direct (requête observée et événement
 * `settingsChanged`). Chaque changement est enregistré aussitôt (comme l'original) ; hors
 * ligne, il part dans la file d'actions en attente.
 */
export function useSettings() {
  const [settings, setSettings] = useState({});
  const [db, setDb] = useState(null);
  const [stale, setStale] = useState(false);

  const watched = useMemo(() => api.watch(SETTINGS_Q), []);
  useSubscription(watched, s => {
    if (s.data?.settings) setSettings(s.data.settings);
    if (s.data?.databaseInfo) setDb(s.data.databaseInfo);
    setStale(s.stale);
  });

  const changes = useMemo(() => api.subscribe(SETTINGS_CHANGED), []);
  useSubscription(changes, d => {
    if (d?.settingsChanged) setSettings(d.settingsChanged);
  });

  const save = async (values, label = 'Enregistrer les réglages') => {
    // Affichage immédiat ; le serveur confirme (ou la file hors ligne rejoue plus tard).
    setSettings(s => ({ ...s, ...values }));
    const r = await api.mutate(UPDATE, { values }, { label });
    if (r.error) notify.error(r.error);
    else if (r.data?.updateSettings) setSettings(r.data.updateSettings);
    else if (r.queued) notify.info('Réglage mis en attente : il sera enregistré au retour de la connexion.');
  };

  return { settings, db, stale, save };
}
