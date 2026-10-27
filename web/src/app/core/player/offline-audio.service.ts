import { Injectable, signal } from '@angular/core';

const CACHE = 'dk-offline-audio-v1';

/**
 * Titres « épinglés » pour l'écoute sans réseau (métro, avion, serveur
 * éteint). Les fichiers sont gardés dans le Cache Storage de l'appareil et
 * lus par URL d'objet : pas de requêtes Range à servir hors ligne.
 */
@Injectable({ providedIn: 'root' })
export class OfflineAudioService {
  readonly pinned = signal<Set<string>>(new Set());
  readonly downloading = signal<Set<string>>(new Set());

  constructor() {
    void this.refresh();
  }

  get supported(): boolean {
    return typeof caches !== 'undefined';
  }

  isPinned(src: string): boolean {
    return this.pinned().has(src);
  }

  async pin(src: string): Promise<boolean> {
    if (!this.supported) return false;
    this.downloading.update(s => new Set(s).add(src));
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
      this.downloading.update(s => {
        const n = new Set(s);
        n.delete(src);
        return n;
      });
    }
  }

  async unpin(src: string): Promise<void> {
    if (!this.supported) return;
    const cache = await caches.open(CACHE);
    await cache.delete(src);
    await this.refresh();
  }

  /** URL d'objet du titre épinglé, ou null s'il ne l'est pas. */
  async objectUrl(src: string): Promise<string | null> {
    if (!this.supported || !this.pinned().has(src)) return null;
    try {
      const cache = await caches.open(CACHE);
      const response = await cache.match(src);
      return response ? URL.createObjectURL(await response.blob()) : null;
    } catch {
      return null;
    }
  }

  /** Place occupée sur l'appareil (estimation du navigateur). */
  async usage(): Promise<{ used: number; quota: number } | null> {
    if (!navigator.storage?.estimate) return null;
    const e = await navigator.storage.estimate();
    return { used: e.usage ?? 0, quota: e.quota ?? 0 };
  }

  private async refresh(): Promise<void> {
    if (!this.supported) return;
    try {
      const cache = await caches.open(CACHE);
      const keys = await cache.keys();
      this.pinned.set(new Set(keys.map(k => new URL(k.url).pathname + new URL(k.url).search)));
    } catch {
      // Cache Storage indisponible (navigation privée) : pas d'épinglage.
    }
  }
}
