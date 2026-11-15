import { gql } from '@apollo/client';
import { get, set } from 'idb-keyval';
import { Store } from '../../lib/store';
import { getApollo } from '../graphql/apollo';
import { connectivity } from './connectivity';
import { notify } from '../ui/notify';

const KEY = 'dk-offline-queue-v1';

/**
 * Mutation mise en attente faute de réseau, rejouée à la reconnexion.
 * @typedef {object} QueuedMutation
 * @property {string} id Clé d'idempotence.
 * @property {string} label
 * @property {string} query Texte GraphQL de la mutation.
 * @property {Record<string, unknown>} variables
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

  async enqueue(label, query, variables, id = newIdempotencyKey()) {
    await this.loaded;
    const entry = { id, label, query, variables, createdAt: Date.now(), attempts: 0 };
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
          const result = await getApollo().mutate({
            mutation: gql(entry.query),
            variables: entry.variables,
            context: { headers: { 'X-Idempotency-Key': entry.id } },
            errorPolicy: 'all',
          });
          const errors = result.error;
          if (errors && /SERVICE_UNAVAILABLE|Failed to fetch|NetworkError/i.test(errors.message ?? '')) {
            throw new Error(errors.message);
          }
          await this.remove(entry.id);
          if (errors) notify.error(`« ${entry.label} » a échoué : ${errors.message}`);
          else notify.success(`« ${entry.label} » envoyé.`);
        } catch (e) {
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
