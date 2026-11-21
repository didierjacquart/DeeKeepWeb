import { get, set } from 'idb-keyval';
import { Store } from '../../lib/store';
import { isTransient, send } from '../api/http';
import { connectivity } from './connectivity';
import { notify } from '../ui/notify';

const KEY = 'dk-offline-queue-v2';

/**
 * Action mise en attente faute de réseau, rejouée à la reconnexion.
 * @typedef {object} QueuedAction
 * @property {string} id Clé d'idempotence.
 * @property {string} label
 * @property {import('../api/http').HttpRequest} request Requête HTTP à rejouer telle quelle.
 * @property {number} createdAt
 * @property {number} attempts
 * @property {string} [lastError]
 */

export function newIdempotencyKey() {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

/**
 * File des actions faites hors ligne (ajout de pistes à extraire, réglages,
 * lancement d'analyses…). Elle survit à la fermeture de l'application
 * (IndexedDB) et repart dès que le serveur répond. Chaque entrée porte une clé
 * d'idempotence : rejouée deux fois, elle n'agit qu'une fois.
 */
export class OfflineQueueService extends Store {
  loaded;
  started = false;

  constructor() {
    super({ items: [], replaying: false });
    this.loaded = get(KEY)
      .then(v => this.setState({ items: v ?? [] }))
      .catch(() => undefined);
  }

  get pending() {
    return this.state.items.length;
  }

  /** Démarre le rejeu (une fois la session ouverte). */
  start() {
    if (this.started) return;
    this.started = true;
    connectivity.onReconnect(() => void this.replay());
    // Premier passage : ce qui restait d'une session précédente.
    setTimeout(() => void this.replay(), 3000);
  }

  async enqueue(label, request, id = newIdempotencyKey()) {
    await this.loaded;
    const entry = { id, label, request, createdAt: Date.now(), attempts: 0 };
    this.setState(s => ({ items: [...s.items, entry] }));
    await this.save();
    notify.info(`Hors ligne : « ${label} » sera envoyé dès le retour de la connexion.`);
    return entry;
  }

  async remove(id) {
    this.setState(s => ({ items: s.items.filter(i => i.id !== id) }));
    await this.save();
  }

  async replay() {
    await this.loaded;
    if (this.state.replaying || this.state.items.length === 0 || !connectivity.browserOnline) return;
    this.setState({ replaying: true });
    try {
      // Dans l'ordre : une action peut dépendre de la précédente.
      for (const entry of [...this.state.items]) {
        try {
          await send(entry.request, { 'X-Idempotency-Key': entry.id });
          await this.remove(entry.id);
          notify.success(`« ${entry.label} » envoyé.`);
        } catch (e) {
          if (!isTransient(e)) {
            // Refusée par le serveur : la rejouer ne changerait rien.
            await this.remove(entry.id);
            notify.error(`« ${entry.label} » a échoué : ${e instanceof Error ? e.message : String(e)}`);
            continue;
          }
          const failed = {
            ...entry,
            attempts: entry.attempts + 1,
            lastError: e instanceof Error ? e.message : String(e),
          };
          this.setState(s => ({ items: s.items.map(i => (i.id === entry.id ? failed : i)) }));
          await this.save();
          break; // serveur encore indisponible : on réessaiera à la prochaine reconnexion
        }
      }
    } finally {
      this.setState({ replaying: false });
    }
  }

  async save() {
    try {
      await set(KEY, this.state.items);
    } catch {
      // Quota dépassé : la file reste en mémoire pour la session.
    }
  }
}

export const offlineQueue = new OfflineQueueService();
