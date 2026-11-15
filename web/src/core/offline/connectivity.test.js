import { describe, expect, it, vi } from 'vitest';
import { ConnectivityService, linkState } from './connectivity';

describe('linkState', () => {
  const base = { browserOnline: true, socketConnected: false, reconnectCount: 0 };

  it('se dit en ligne quand la WebSocket répond', () => {
    expect(linkState({ ...base, socketConnected: true })).toBe('online');
  });

  it('tente de se reconnecter, puis se déclare hors ligne après trois essais', () => {
    expect(linkState({ ...base, reconnectCount: 2 })).toBe('reconnecting');
    expect(linkState({ ...base, reconnectCount: 3 })).toBe('offline');
  });

  it('est hors ligne quand le navigateur l’est, quoi que dise la WebSocket', () => {
    expect(linkState({ ...base, browserOnline: false, socketConnected: true })).toBe('offline');
  });
});

describe('ConnectivityService', () => {
  it('ne déclenche la resynchronisation qu’à une REconnexion', () => {
    const service = new ConnectivityService();
    const listener = vi.fn();
    service.onReconnect(listener);
    service.connected();
    expect(listener).not.toHaveBeenCalled();
    expect(service.state.resyncTick).toBe(0);
    service.disconnected('coupure');
    service.retrying();
    service.connected();
    expect(listener).toHaveBeenCalledTimes(1);
    expect(service.state).toMatchObject({ resyncTick: 1, reconnectCount: 0, lastError: null, socketConnected: true });
  });

  it('garde la raison de la dernière coupure', () => {
    const service = new ConnectivityService();
    service.disconnected('Erreur de connexion');
    expect(service.state.lastError).toBe('Erreur de connexion');
    expect(service.isOnline).toBe(false);
  });

  it('suit les événements réseau du navigateur', () => {
    const service = new ConnectivityService();
    service.connected();
    window.dispatchEvent(new Event('offline'));
    expect(service.state).toMatchObject({ browserOnline: false, socketConnected: false });
    window.dispatchEvent(new Event('online'));
    expect(service.state.browserOnline).toBe(true);
  });

  it('permet de se désinscrire de la resynchronisation', () => {
    const service = new ConnectivityService();
    const listener = vi.fn();
    const off = service.onReconnect(listener);
    off();
    service.connected();
    service.connected();
    expect(listener).not.toHaveBeenCalled();
  });
});
