import { Store } from '../../lib/store';

/** État de la liaison avec le serveur, affiché en permanence en pied de barre latérale. */

export function linkState(s) {
  if (!s.browserOnline) return 'offline';
  if (s.socketConnected) return 'online';
  return s.reconnectCount > 2 ? 'offline' : 'reconnecting';
}

/**
 * Connaissance de la connectivité : réseau du navigateur ET joignabilité du
 * serveur (la WebSocket GraphQL sert de sonde). Un téléphone « en ligne » dont
 * le serveur ne répond pas est traité comme hors ligne.
 */
export class ConnectivityService extends Store {
  wasConnected = false;
  listeners = [];

  constructor() {
    super({
      browserOnline: typeof navigator === 'undefined' ? true : navigator.onLine,
      socketConnected: false,
      lastError: null,
      reconnectCount: 0,
      resyncTick: 0,
    });
    if (typeof window !== 'undefined') {
      window.addEventListener('online', () => this.setState({ browserOnline: true }));
      window.addEventListener('offline', () => this.setState({ browserOnline: false, socketConnected: false }));
    }
  }

  get browserOnline() {
    return this.state.browserOnline;
  }

  get linkState() {
    return linkState(this.state);
  }

  get isOnline() {
    return this.linkState === 'online';
  }

  /** Appelé par le client graphql-ws. */
  connected() {
    const resync = this.wasConnected;
    this.wasConnected = true;
    this.setState(s => ({
      socketConnected: true,
      reconnectCount: 0,
      lastError: null,
      resyncTick: resync ? s.resyncTick + 1 : s.resyncTick,
    }));
    if (resync) for (const listener of this.listeners) listener();
  }

  disconnected(reason) {
    this.setState(reason ? { socketConnected: false, lastError: reason } : { socketConnected: false });
  }

  retrying() {
    this.setState(s => ({ reconnectCount: s.reconnectCount + 1 }));
  }

  /** Rappel à chaque reconnexion (rejouer la file hors ligne, rafraîchir les requêtes). */
  onReconnect(listener) {
    this.listeners.push(listener);
    return () => {
      const i = this.listeners.indexOf(listener);
      if (i >= 0) this.listeners.splice(i, 1);
    };
  }
}

export const connectivity = new ConnectivityService();
