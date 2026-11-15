import { get, set, del } from 'idb-keyval';

const KEY = 'dk-apollo-cache-v1';
const MAX_BYTES = 25 * 1024 * 1024;

/**
 * Persistance du cache GraphQL dans IndexedDB : à l'ouverture hors ligne,
 * la bibliothèque, l'historique et les réglages s'affichent tels qu'ils
 * étaient lors de la dernière visite.
 */
export class CachePersistence {
  dirty = false;
  timer = null;
  cache;

  constructor(cache) {
    this.cache = cache;
  }

  async restore() {
    try {
      const data = await get(KEY);
      if (data) this.cache.restore(data);
    } catch {
      // Cache illisible (version, quota) : on repart à vide.
      await del(KEY).catch(() => undefined);
    }
  }

  markDirty() {
    this.dirty = true;
  }

  start() {
    if (this.timer) return;
    this.timer = setInterval(() => void this.flush(), 5000);
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') void this.flush();
      });
    }
  }

  async flush() {
    if (!this.dirty) return;
    this.dirty = false;
    try {
      const snapshot = this.cache.extract();
      // Garde-fou : un cache démesuré (jaquettes en base64, etc.) n'est pas persisté.
      if (JSON.stringify(snapshot).length > MAX_BYTES) return;
      await set(KEY, snapshot);
    } catch {
      this.dirty = true;
    }
  }

  async clear() {
    await del(KEY);
  }
}
