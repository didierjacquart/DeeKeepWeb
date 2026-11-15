import { useEffect, useState } from 'react';
import { offlineAudio } from '../../../core/player/offline-audio';

/** Place occupée sur l'appareil (estimation du navigateur), null tant qu'elle est inconnue. */
export function useDeviceStorage() {
  const [storage, setStorage] = useState(null);
  useEffect(() => {
    let alive = true;
    void offlineAudio.usage().then(u => {
      if (alive) setStorage(u);
    });
    return () => {
      alive = false;
    };
  }, []);
  return storage;
}
