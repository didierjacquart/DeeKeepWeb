import { Observable, filter, map } from 'rxjs';
import { connectivity } from '../offline/connectivity';
import { CachePersistence } from '../offline/cache-persistence';
import { offlineQueue, newIdempotencyKey } from '../offline/offline-queue';
import { buildRequest } from './endpoints';
import { events } from './events';
import { isTransient, send } from './http';

/**
 * @typedef {object} MutateOptions
 * @property {string} [label] Libellé lisible, affiché si l'action part dans la file hors ligne.
 * @property {boolean} [queueable] Mettre l'action en file si le serveur est injoignable (défaut : vrai).
 * @property {string[]} [refetch] Noms des requêtes affichées à recharger après succès.
 */

/**
 * @typedef {object} MutateResult
 * @property {any} data `{ champ: résultat }`, null en cas d'échec ou de mise en file.
 * @property {boolean} queued Action mise en file hors ligne.
 * @property {string} [error]
 */

/**
 * @typedef {object} QueryState
 * @property {any} data
 * @property {boolean} loading
 * @property {string | null} error
 * @property {boolean} stale Données servies par le cache local faute de réseau.
 */

/**
 * Dernières réponses reçues, par opération et variables : à l'ouverture hors ligne,
 * les pages s'affichent avec les données de la dernière visite.
 */
export class ResponseCache {
  /** @type {Map<string, any>} */
  entries = new Map();

  get(key) {
    return this.entries.get(key);
  }

  set(key, value) {
    this.entries.set(key, value);
  }

  extract() {
    return Object.fromEntries(this.entries);
  }

  restore(snapshot) {
    this.entries = new Map(Object.entries(snapshot ?? {}));
  }
}

export const responseCache = new ResponseCache();
export const cachePersistence = new CachePersistence(responseCache);

/** Restaure le cache persisté avant le premier rendu, puis le sauvegarde périodiquement. */
export async function startResponseCache() {
  await cachePersistence.restore();
  cachePersistence.start();
}

const keyOf = (op, variables) => `${op.name}:${JSON.stringify(variables ?? {})}`;
const message = e => (e instanceof Error ? e.message : String(e));

/**
 * Accès serveur des pages : lectures « cache puis réseau » (affichage immédiat, y
 * compris hors ligne), actions résilientes (clé d'idempotence, mise en file hors
 * ligne, message d'erreur lisible) et événements poussés par le serveur.
 */
export class Api {
  /** @type {Set<{ name: string, reload: () => void }>} */
  watchers = new Set();

  constructor() {
    // À chaque reconnexion, les données affichées sont rechargées : les événements
    // manqués pendant la coupure n'ont pas besoin d'être rejoués.
    connectivity.onReconnect(() => this.refetchActive());
  }

  /** Exécute les appels d'une opération et mémorise la réponse. */
  async fetch(op, variables) {
    const results = await Promise.all(op.parts.map(part => send(buildRequest(part, variables))));
    const data = {};
    op.parts.forEach((part, i) => {
      data[part.field] = part.options.result ? part.options.result(results[i]) : results[i];
    });
    responseCache.set(keyOf(op, variables), data);
    cachePersistence.markDirty();
    return data;
  }

  /**
   * Lecture ponctuelle : réseau d'abord, cache en secours si le serveur est injoignable.
   * `network = false` : le cache suffit s'il a une réponse.
   */
  async query(op, variables, network = true) {
    const cached = responseCache.get(keyOf(op, variables));
    if (!network && cached) return cached;
    try {
      return await this.fetch(op, variables);
    } catch (e) {
      if (cached && isTransient(e)) return cached;
      throw e;
    }
  }

  /**
   * Lecture observée : rend d'abord le cache, puis la réponse du serveur ; rechargée
   * à la reconnexion, après une action qui la nomme dans `refetch`, et à chaque `pollInterval`.
   * @returns {Observable<QueryState>}
   */
  watch(op, variables, pollInterval) {
    return new Observable(subscriber => {
      const key = keyOf(op, variables);
      let closed = false;
      const load = async () => {
        const cached = responseCache.get(key);
        subscriber.next({ data: cached ?? null, loading: true, error: null, stale: false });
        try {
          const data = await this.fetch(op, variables);
          if (!closed) subscriber.next({ data, loading: false, error: null, stale: false });
        } catch (e) {
          const fallback = responseCache.get(key);
          if (!closed)
            subscriber.next({ data: fallback ?? null, loading: false, error: message(e), stale: !!fallback });
        }
      };
      const watcher = { name: op.name, reload: () => void load() };
      this.watchers.add(watcher);
      void load();
      const timer = pollInterval ? setInterval(watcher.reload, pollInterval) : null;
      return () => {
        closed = true;
        this.watchers.delete(watcher);
        if (timer) clearInterval(timer);
      };
    });
  }

  /**
   * Action (création, modification, lancement de tâche).
   * @param {MutateOptions} [options]
   * @returns {Promise<MutateResult>}
   */
  async mutate(op, variables, options = {}) {
    const queueable = options.queueable ?? true;
    const label = options.label ?? 'Action';
    const key = newIdempotencyKey();
    const [part] = op.parts;
    const request = buildRequest(part, variables);
    const enqueue = async () => {
      await offlineQueue.enqueue(label, request, key);
      return { data: null, queued: true };
    };
    if (queueable && !connectivity.browserOnline) return enqueue();
    try {
      const result = await send(request, { 'X-Idempotency-Key': key });
      const data = { [part.field]: part.options.result ? part.options.result(result) : result };
      if (options.refetch?.length) this.refetch(options.refetch);
      return { data, queued: false };
    } catch (e) {
      if (queueable && isTransient(e)) return enqueue();
      return { data: null, queued: false, error: message(e) };
    }
  }

  /**
   * Événements poussés par le serveur, rendus sous `{ champ: charge utile }`.
   * @param {import('./endpoints').EventOperation} op
   */
  subscribe(op, variables) {
    return events.on(op.type).pipe(
      filter(data => !op.filter || op.filter(data, variables ?? {})),
      map(data => ({ [op.field]: data })),
    );
  }

  /** Recharge les lectures affichées portant ces noms. */
  refetch(names) {
    for (const w of this.watchers) if (names.includes(w.name)) w.reload();
  }

  /** Recharge toutes les lectures affichées (après reconnexion). */
  refetchActive() {
    for (const w of this.watchers) w.reload();
  }
}

export const api = new Api();
