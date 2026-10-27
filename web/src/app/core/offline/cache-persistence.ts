import { InMemoryCache } from '@apollo/client';
import { get, set, del } from 'idb-keyval';

const KEY = 'dk-apollo-cache-v1';
const MAX_BYTES = 25 * 1024 * 1024;

/**
 * Persistance du cache GraphQL dans IndexedDB : à l'ouverture hors ligne,
 * la bibliothèque, l'historique et les réglages s'affichent tels qu'ils
 * étaient lors de la dernière visite.
 */
export class CachePersistence {
  private dirty = false;
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor(private readonly cache: InMemoryCache) {}

  async restore(): Promise<void> {
    try {
      const data = await get(KEY);
      if (data) this.cache.restore(data);
    } catch {
      // Cache illisible (version, quota) : on repart à vide.
      await del(KEY).catch(() => undefined);
    }
  }

  markDirty(): void {
    this.dirty = true;
  }

  start(): void {
    if (this.timer) return;
    this.timer = setInterval(() => void this.flush(), 5000);
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') void this.flush();
      });
    }
  }

  async flush(): Promise<void> {
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

  async clear(): Promise<void> {
    await del(KEY);
  }
}
