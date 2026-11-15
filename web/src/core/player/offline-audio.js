import { Store } from '../../lib/store';

const CACHE = 'dk-offline-audio-v1';

/**
 * Titres « épinglés » pour l'écoute sans réseau (métro, avion, serveur
 * éteint). Les fichiers sont gardés dans le Cache Storage de l'appareil et
 * lus par URL d'objet : pas de requêtes Range à servir hors ligne.
 */
export class OfflineAudioService extends Store {
  constructor() {
    super({ pinned: new Set(), downloading: new Set() });
    void this.refresh();
  }

  get supported() {
    return typeof caches !== 'undefined';
  }

  isPinned(src) {
    return this.state.pinned.has(src);
  }

  async pin(src) {
    if (!this.supported) return false;
    this.setState(s => ({ downloading: new Set(s.downloading).add(src) }));
    try {
      const response = await fetch(src, { credentials: 'same-origin' });
      if (!response.ok) return false;
      const cache = await caches.open(CACHE);
      await cache.put(src, response);
      await this.refresh();
      return true;
    } catch {
      return false;
    } finally {
      this.setState(s => {
        const n = new Set(s.downloading);
        n.delete(src);
        return { downloading: n };
      });
    }
  }

  async unpin(src) {
    if (!this.supported) return;
    const cache = await caches.open(CACHE);
    await cache.delete(src);
    await this.refresh();
  }

  /** URL d'objet du titre épinglé, ou null s'il ne l'est pas. */
  async objectUrl(src) {
    if (!this.supported || !this.state.pinned.has(src)) return null;
    try {
      const cache = await caches.open(CACHE);
      const response = await cache.match(src);
      return response ? URL.createObjectURL(await response.blob()) : null;
    } catch {
      return null;
    }
  }

  /** Place occupée sur l'appareil (estimation du navigateur). */
  async usage() {
    if (!navigator.storage?.estimate) return null;
    const e = await navigator.storage.estimate();
    return { used: e.usage ?? 0, quota: e.quota ?? 0 };
  }

  async refresh() {
    if (!this.supported) return;
    try {
      const cache = await caches.open(CACHE);
      const keys = await cache.keys();
      this.setState({ pinned: new Set(keys.map(k => new URL(k.url).pathname + new URL(k.url).search)) });
    } catch {
      // Cache Storage indisponible (navigation privée) : pas d'épinglage.
    }
  }
}

export const offlineAudio = new OfflineAudioService();
