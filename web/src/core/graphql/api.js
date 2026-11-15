import { print } from 'graphql';
import { Observable, map } from 'rxjs';
import { getApollo } from './apollo';
import { connectivity } from '../offline/connectivity';
import { offlineQueue, newIdempotencyKey } from '../offline/offline-queue';

/**
 * @typedef {object} MutateOptions
 * @property {string} [label] Libellé lisible, affiché si l'action part dans la file hors ligne.
 * @property {boolean} [queueable] Mettre l'action en file si le serveur est injoignable (défaut : vrai).
 * @property {string[]} [refetch] Requêtes à rafraîchir après succès.
 */

/**
 * @typedef {object} MutateResult
 * @property {any} data
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

const NETWORK_FAILURE = /fetch|network|Failed|Load failed/i;

/**
 * Accès GraphQL des pages : requêtes « cache puis réseau » (affichage immédiat,
 * y compris hors ligne) et mutations résilientes (clé d'idempotence, mise en
 * file hors ligne, message d'erreur lisible).
 */
export class GraphQLApi {
  constructor() {
    // À chaque reconnexion, les requêtes affichées sont rafraîchies : les
    // événements manqués pendant la coupure n'ont pas besoin d'être rejoués.
    connectivity.onReconnect(() => this.refetchActive());
  }

  /**
   * Requête observée : se met à jour au fil du cache (abonnements, mutations, reconnexion).
   * @returns {Observable<QueryState>}
   */
  watch(query, variables, pollInterval) {
    return new Observable(subscriber =>
      getApollo()
        .watchQuery({
          query,
          variables,
          fetchPolicy: 'cache-and-network',
          nextFetchPolicy: 'cache-first',
          errorPolicy: 'all',
          notifyOnNetworkStatusChange: true,
          pollInterval,
        })
        .pipe(
          map(r => ({
            data: r.data ?? null,
            loading: r.loading,
            error: r.error ? r.error.message : null,
            stale: !!r.error && !!r.data,
          })),
        )
        .subscribe(subscriber),
    );
  }

  /** Requête ponctuelle (réseau d'abord, cache en secours). */
  async query(query, variables, network = true) {
    const apollo = getApollo();
    const result = await apollo
      .query({ query, variables, fetchPolicy: network ? 'network-only' : 'cache-first', errorPolicy: 'all' })
      .catch(async e => {
        if (!network) throw e;
        return apollo.query({ query, variables, fetchPolicy: 'cache-only' });
      });
    if (result.error && !result.data) throw new Error(result.error.message);
    return result.data;
  }

  /**
   * @param {MutateOptions} [options]
   * @returns {Promise<MutateResult>}
   */
  async mutate(mutation, variables, options = {}) {
    const queueable = options.queueable ?? true;
    const label = options.label ?? 'Action';
    const key = newIdempotencyKey();
    const enqueue = async () => {
      await offlineQueue.enqueue(label, print(mutation), variables ?? {}, key);
      return { data: null, queued: true };
    };
    if (queueable && !connectivity.browserOnline) return enqueue();
    try {
      const result = await getApollo().mutate({
        mutation,
        variables,
        errorPolicy: 'all',
        context: { headers: { 'X-Idempotency-Key': key } },
        refetchQueries: options.refetch,
      });
      const message = result.error?.message;
      if (message && /SERVICE_UNAVAILABLE/.test(JSON.stringify(result.error)) && queueable) return enqueue();
      return { data: result.data ?? null, queued: false, error: message };
    } catch (e) {
      const message = e instanceof Error ? e.message : String(e);
      if (queueable && NETWORK_FAILURE.test(message)) return enqueue();
      return { data: null, queued: false, error: message };
    }
  }

  /** Abonnement GraphQL (graphql-ws, reconnexion automatique). */
  subscribe(query, variables) {
    return new Observable(subscriber =>
      getApollo()
        .subscribe({ query, variables })
        .pipe(map(r => r.data))
        .subscribe(subscriber),
    );
  }

  /** Vide et recharge les requêtes actives (après reconnexion). */
  refetchActive() {
    void getApollo().refetchQueries({ include: 'active' });
  }
}

export const api = new GraphQLApi();
