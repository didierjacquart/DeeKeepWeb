import { useEffect, useRef, useState } from 'react';
import { notify } from '../../../core/ui/notify';
import { saveQualityThresholds } from '../quality-api';
import { withThreshold } from '../quality-thresholds';

/**
 * Seuils de détection : affichés depuis le serveur, modifiés localement puis enregistrés tous à la fois après
 * une courte pause de saisie. Tant qu'un enregistrement est en attente, la liste relue ne les écrase pas.
 */
export function useQualityThresholds() {
  const [thresholds, setThresholdsState] = useState(null);
  // Seuils : la valeur la plus récente sert aux saisies successives, même avant le prochain rendu.
  const latest = useRef(null);
  const timer = useRef(undefined);

  const put = t => {
    latest.current = t;
    setThresholdsState(t);
  };

  useEffect(() => {
    const pending = timer;
    return () => clearTimeout(pending.current);
  }, []);

  const save = next => {
    put(next);
    clearTimeout(timer.current);
    // Enregistre tous les réglages à la fois, après une courte pause de saisie.
    timer.current = setTimeout(async () => {
      timer.current = undefined;
      const r = await saveQualityThresholds(next);
      if (r.data?.updateQualityThresholds) put(r.data.updateQualityThresholds);
      if (r.error) notify.error(r.error);
    }, 400);
  };

  /** Valeur saisie dans un champ (événement natif « change »). */
  const setThreshold = (field, raw) => {
    const next = withThreshold(latest.current, field, raw);
    if (next) save(next);
  };

  const setDeep = checked => {
    const current = latest.current;
    if (current) save({ ...current, deep: checked });
  };

  /** Seuils relus du serveur : ignorés tant qu'un enregistrement est en attente. */
  const receive = t => {
    if (!timer.current) put(t);
  };

  return { thresholds, setThreshold, setDeep, receive };
}
