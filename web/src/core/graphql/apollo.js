import { ApolloClient, ApolloLink, HttpLink, InMemoryCache, split } from '@apollo/client';
import { GraphQLWsLink } from '@apollo/client/link/subscriptions';
import { ErrorLink } from '@apollo/client/link/error';
import { getMainDefinition } from '@apollo/client/utilities';
import { createClient } from 'graphql-ws';
import { tap } from 'rxjs';
import { connectivity } from '../offline/connectivity';
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

let onUnauthorized = () => undefined;

/** Réaction à une session expirée (renvoi vers l'écran de connexion), fixée au démarrage. */
export function setUnauthorizedHandler(handler) {
  onUnauthorized = handler;
}

function wsUrl() {
  const { protocol, host } = window.location;
  return `${protocol === 'https:' ? 'wss' : 'ws'}://${host}/graphql`;
}

/**
 * WebSocket des abonnements : reconnexion infinie à intervalle croissant
 * (1 s → 30 s, avec gigue) ; elle sert aussi de sonde de joignabilité.
 */
function createWsLink() {
  const wsClient = createClient({
    url: wsUrl(),
    lazy: false,
    keepAlive: 15000,
    retryAttempts: Number.POSITIVE_INFINITY,
    shouldRetry: () => true,
    retryWait: async retries => {
      connectivity.retrying();
      const base = Math.min(30000, 1000 * 2 ** Math.min(retries, 5));
      await new Promise(r => setTimeout(r, base / 2 + (Math.random() * base) / 2));
    },
    on: {
      connected: () => connectivity.connected(),
      closed: e => connectivity.disconnected(e?.reason || undefined),
      error: () => connectivity.disconnected('Erreur de connexion'),
    },
  });
  return new GraphQLWsLink(wsClient);
}

function createLink() {
  const errorLink = new ErrorLink(({ error }) => {
    if (error?.statusCode === 401) onUnauthorized();
  });
  // Toute réponse reçue rend le cache « à sauvegarder ».
  const persistLink = new ApolloLink((operation, forward) =>
    forward(operation).pipe(tap(() => cachePersistence.markDirty())),
  );
  const http = new HttpLink({ uri: '/graphql', credentials: 'include' });
  return split(
    ({ query }) => {
      const def = getMainDefinition(query);
      return def.kind === 'OperationDefinition' && def.operation === 'subscription';
    },
    createWsLink(),
    ApolloLink.from([errorLink, persistLink, http]),
  );
}

let client = null;

/**
 * Client GraphQL de l'application, créé à la première utilisation : importer ce
 * module n'ouvre aucune connexion (les tests remplacent `api` sans réseau).
 * @returns {ApolloClient}
 */
export function getApollo() {
  client ??= new ApolloClient({
    link: createLink(),
    cache: apolloCache,
    defaultOptions: {
      watchQuery: { fetchPolicy: 'cache-and-network' },
      query: { fetchPolicy: 'network-only' },
    },
  });
  return client;
}

/** Restaure le cache persisté avant le premier rendu, puis le sauvegarde périodiquement. */
export async function startApolloCache() {
  await cachePersistence.restore();
  cachePersistence.start();
}
