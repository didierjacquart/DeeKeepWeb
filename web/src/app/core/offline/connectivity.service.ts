import { Injectable, computed, signal } from '@angular/core';

/** État de la liaison avec le serveur, affiché en permanence en pied de barre latérale. */
export type LinkState = 'online' | 'reconnecting' | 'offline';

/**
 * Connaissance de la connectivité : réseau du navigateur ET joignabilité du
 * serveur (la WebSocket GraphQL sert de sonde). Un téléphone « en ligne » dont
 * le serveur ne répond pas est traité comme hors ligne.
 */
@Injectable({ providedIn: 'root' })
export class ConnectivityService {
  readonly browserOnline = signal(typeof navigator === 'undefined' ? true : navigator.onLine);
  readonly socketConnected = signal(false);
  readonly lastError = signal<string | null>(null);
  readonly reconnectCount = signal(0);
  /** Incrémenté à chaque reconnexion effective : les pages s'en servent pour se resynchroniser. */
  readonly resyncTick = signal(0);

  readonly state = computed<LinkState>(() => {
    if (!this.browserOnline()) return 'offline';
    if (this.socketConnected()) return 'online';
    return this.reconnectCount() > 2 ? 'offline' : 'reconnecting';
  });

  readonly isOnline = computed(() => this.state() === 'online');

  private wasConnected = false;
  private readonly listeners: Array<() => void> = [];

  constructor() {
    if (typeof window !== 'undefined') {
      window.addEventListener('online', () => this.browserOnline.set(true));
      window.addEventListener('offline', () => {
        this.browserOnline.set(false);
        this.socketConnected.set(false);
      });
    }
  }

  /** Appelé par le client graphql-ws. */
  connected(): void {
    this.socketConnected.set(true);
    this.reconnectCount.set(0);
    this.lastError.set(null);
    if (this.wasConnected) {
      this.resyncTick.update(n => n + 1);
      for (const listener of this.listeners) listener();
    }
    this.wasConnected = true;
  }

  disconnected(reason?: string): void {
    this.socketConnected.set(false);
    if (reason) this.lastError.set(reason);
  }

  retrying(): void {
    this.reconnectCount.update(n => n + 1);
  }

  /** Rappel à chaque reconnexion (rejouer la file hors ligne, rafraîchir les requêtes). */
  onReconnect(listener: () => void): void {
    this.listeners.push(listener);
  }
}
