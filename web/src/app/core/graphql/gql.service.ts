import { Injectable, inject } from '@angular/core';
import { Apollo } from 'apollo-angular';
import type { DocumentNode, OperationVariables } from '@apollo/client';
import { print } from 'graphql';
import { Observable, firstValueFrom, map } from 'rxjs';
import { ConnectivityService } from '../offline/connectivity.service';
import { OfflineQueueService, newIdempotencyKey } from '../offline/offline-queue.service';

export interface MutateOptions {
  /** Libellé lisible, affiché si l'action part dans la file hors ligne. */
  label?: string;
  /** Mettre l'action en file si le serveur est injoignable (défaut : vrai). */
  queueable?: boolean;
  /** Requêtes à rafraîchir après succès. */
  refetch?: string[];
}

export interface MutateResult<T> {
  data: T | null;
  queued: boolean;
  error?: string;
}

export interface QueryState<T> {
  data: T | null;
  loading: boolean;
  error: string | null;
  /** Données servies par le cache local faute de réseau. */
  stale: boolean;
}

/**
 * Accès GraphQL des pages : requêtes « cache puis réseau » (affichage immédiat,
 * y compris hors ligne) et mutations résilientes (clé d'idempotence, mise en
 * file hors ligne, message d'erreur lisible).
 */
@Injectable({ providedIn: 'root' })
export class GqlService {
  private readonly apollo = inject(Apollo);
  private readonly connectivity = inject(ConnectivityService);
  private readonly queue = inject(OfflineQueueService);

  constructor() {
    // À chaque reconnexion, les requêtes affichées sont rafraîchies : les
    // événements manqués pendant la coupure n'ont pas besoin d'être rejoués.
    this.connectivity.onReconnect(() => this.refetchActive());
  }

  /** Requête observée : se met à jour au fil du cache (abonnements, mutations, reconnexion). */
  watch<T, V extends OperationVariables = OperationVariables>(query: DocumentNode, variables?: V, pollInterval?: number): Observable<QueryState<T>> {
    return this.apollo
      .watchQuery<T, V>({
        query,
        variables: variables as V,
        fetchPolicy: 'cache-and-network',
        nextFetchPolicy: 'cache-first',
        errorPolicy: 'all',
        notifyOnNetworkStatusChange: true,
        pollInterval,
      })
      .valueChanges.pipe(
        map(r => ({
          data: (r.data as T) ?? null,
          loading: r.loading,
          error: r.error ? r.error.message : null,
          stale: !!r.error && !!r.data,
        })),
      );
  }

  /** Requête ponctuelle (réseau d'abord, cache en secours). */
  async query<T, V extends OperationVariables = OperationVariables>(query: DocumentNode, variables?: V, network = true): Promise<T> {
    const result = await firstValueFrom(
      this.apollo.query<T, V>({ query, variables: variables as V, fetchPolicy: network ? 'network-only' : 'cache-first', errorPolicy: 'all' }),
    ).catch(async e => {
      if (!network) throw e;
      return firstValueFrom(this.apollo.query<T, V>({ query, variables: variables as V, fetchPolicy: 'cache-only' }));
    });
    if (result.error && !result.data) throw new Error(result.error.message);
    return result.data as T;
  }

  async mutate<T, V extends OperationVariables = OperationVariables>(mutation: DocumentNode, variables?: V, options: MutateOptions = {}): Promise<MutateResult<T>> {
    const queueable = options.queueable ?? true;
    const label = options.label ?? 'Action';
    const key = newIdempotencyKey();
    if (queueable && !this.connectivity.browserOnline()) {
      await this.queue.enqueue(label, print(mutation), (variables ?? {}) as Record<string, unknown>, key);
      return { data: null, queued: true };
    }
    try {
      const result = await firstValueFrom(this.apollo.mutate<T, V>({
        mutation,
        variables: variables as V,
        errorPolicy: 'all',
        context: { headers: { 'X-Idempotency-Key': key } },
        refetchQueries: options.refetch,
      }));
      const message = result.error?.message;
      if (message && /SERVICE_UNAVAILABLE/.test(JSON.stringify(result.error)) && queueable) {
        await this.queue.enqueue(label, print(mutation), (variables ?? {}) as Record<string, unknown>, key);
        return { data: null, queued: true };
      }
      return { data: (result.data as T) ?? null, queued: false, error: message };
    } catch (e) {
      const message = e instanceof Error ? e.message : String(e);
      if (queueable && /fetch|network|Failed|Load failed/i.test(message)) {
        await this.queue.enqueue(label, print(mutation), (variables ?? {}) as Record<string, unknown>, key);
        return { data: null, queued: true };
      }
      return { data: null, queued: false, error: message };
    }
  }

  /** Abonnement GraphQL (graphql-ws, reconnexion automatique). */
  subscribe<T, V extends OperationVariables = OperationVariables>(query: DocumentNode, variables?: V): Observable<T> {
    return this.apollo.subscribe<T, V>({ query, variables: variables as V }).pipe(map(r => r.data as T));
  }

  /** Vide et recharge les requêtes actives (après reconnexion). */
  refetchActive(): void {
    void this.apollo.client.refetchQueries({ include: 'active' });
  }
}
