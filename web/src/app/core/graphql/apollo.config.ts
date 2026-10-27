import { inject, provideAppInitializer } from '@angular/core';
import { Router } from '@angular/router';
import { provideApollo } from 'apollo-angular';
import { HttpLink } from 'apollo-angular/http';
import { ApolloLink, InMemoryCache, split } from '@apollo/client';
import { GraphQLWsLink } from '@apollo/client/link/subscriptions';
import { ErrorLink } from '@apollo/client/link/error';
import { getMainDefinition } from '@apollo/client/utilities';
import { createClient } from 'graphql-ws';
import { tap } from 'rxjs';
import { ConnectivityService } from '../offline/connectivity.service';
import { CachePersistence } from '../offline/cache-persistence';

/** Cache partagé, persisté dans IndexedDB (lecture hors ligne). */
export const apolloCache = new InMemoryCache({
  typePolicies: {
    // Les titres sont identifiés par leur chemin, les tâches par leur identifiant.
    TrackDto: { keyFields: ['path'] },
    JobDto: { keyFields: ['id'] },
    HistoryEntry: { keyFields: ['path'] },
    QualityEntry: { keyFields: ['path'] },
    Query: { fields: {} },
  },
});

export const cachePersistence = new CachePersistence(apolloCache);

function wsUrl(): string {
  const { protocol, host } = window.location;
  return `${protocol === 'https:' ? 'wss' : 'ws'}://${host}/graphql`;
}

export function provideDeeKeepApollo() {
  return [
    provideAppInitializer(async () => {
      await cachePersistence.restore();
      cachePersistence.start();
    }),
    provideApollo(() => {
      const httpLink = inject(HttpLink);
      const connectivity = inject(ConnectivityService);
      const router = inject(Router);

      // WebSocket des abonnements : reconnexion infinie à intervalle croissant
      // (1 s → 30 s, avec gigue) ; elle sert aussi de sonde de joignabilité.
      const wsClient = createClient({
        url: wsUrl(),
        lazy: false,
        keepAlive: 15000,
        retryAttempts: Number.POSITIVE_INFINITY,
        shouldRetry: () => true,
        retryWait: async retries => {
          connectivity.retrying();
          const base = Math.min(30000, 1000 * 2 ** Math.min(retries, 5));
          await new Promise(r => setTimeout(r, base / 2 + Math.random() * base / 2));
        },
        on: {
          connected: () => connectivity.connected(),
          closed: e => connectivity.disconnected((e as CloseEvent | undefined)?.reason || undefined),
          error: () => connectivity.disconnected('Erreur de connexion'),
        },
      });

      const errorLink = new ErrorLink(({ error }) => {
        const status = (error as { statusCode?: number }).statusCode;
        if (status === 401) void router.navigate(['/login']);
      });

      // Toute réponse reçue rend le cache « à sauvegarder ».
      const persistLink = new ApolloLink((operation, forward) =>
        forward(operation).pipe(tap(() => cachePersistence.markDirty())),
      );

      const http = httpLink.create({ uri: '/graphql', withCredentials: true });
      const link = split(
        ({ query }) => {
          const def = getMainDefinition(query);
          return def.kind === 'OperationDefinition' && def.operation === 'subscription';
        },
        new GraphQLWsLink(wsClient),
        ApolloLink.from([errorLink, persistLink, http]),
      );

      return {
        link,
        cache: apolloCache,
        defaultOptions: {
          watchQuery: { fetchPolicy: 'cache-and-network' },
          query: { fetchPolicy: 'network-only' },
        },
      };
    }),
  ];
}

