import { BehaviorSubject, Subject } from 'rxjs';
import { vi } from 'vitest';
import { api } from '../core/api/api';

/** Nom d'une opération déclarée avec `endpoint`, `combine` ou `serverEvent`. */
export const operationName = op => op?.name ?? '?';

/**
 * Remplace le serveur pour un test : chaque opération est servie par son
 * nom. Une réponse est une valeur, une fonction `(variables) => valeur` (éventuellement
 * asynchrone) ou une `Error` (requête en échec, mutation en erreur).
 *
 * @param {Record<string, unknown>} [responses]
 */
export function mockApi(responses = {}) {
  const streams = new Map();
  const watches = new Map();
  const stream = name => {
    if (!streams.has(name)) streams.set(name, new Subject());
    return streams.get(name);
  };

  const respond = async (doc, variables) => {
    const name = operationName(doc);
    if (!(name in responses)) throw new Error(`mockApi : aucune réponse prévue pour « ${name} »`);
    const r = responses[name];
    if (r instanceof Error) throw r;
    return typeof r === 'function' ? r(variables) : r;
  };

  const query = vi.spyOn(api, 'query').mockImplementation(respond);

  const mutate = vi.spyOn(api, 'mutate').mockImplementation(async (doc, variables) => {
    const name = operationName(doc);
    const r = responses[name];
    if (r instanceof Error) return { data: null, queued: false, error: r.message };
    const data = r === undefined ? {} : typeof r === 'function' ? await r(variables) : r;
    return { data, queued: false };
  });

  const watch = vi.spyOn(api, 'watch').mockImplementation((doc, variables) => {
    const name = operationName(doc);
    const subject = new BehaviorSubject({ data: null, loading: true, error: null, stale: false });
    watches.set(name, subject);
    respond(doc, variables).then(
      data => subject.next({ data, loading: false, error: null, stale: false }),
      e => subject.next({ data: null, loading: false, error: e.message, stale: false }),
    );
    return subject.asObservable();
  });

  const subscribe = vi.spyOn(api, 'subscribe').mockImplementation(doc => stream(operationName(doc)).asObservable());
  const refetchActive = vi.spyOn(api, 'refetchActive').mockImplementation(() => undefined);

  return {
    query,
    mutate,
    watch,
    subscribe,
    refetchActive,
    /** Change la réponse d'une opération pour la suite du test. */
    respond: (name, value) => {
      responses[name] = value;
    },
    /** Pousse un événement sur l'abonnement nommé. */
    emit: (name, data) => stream(name).next(data),
    /** Pousse un nouvel état (`{ data, loading, error, stale }`) sur la requête observée nommée. */
    pushWatch: (name, state) =>
      watches.get(name)?.next({ data: null, loading: false, error: null, stale: false, ...state }),
    /** Variables de chaque appel d'une mutation, dans l'ordre. */
    mutations: name => mutate.mock.calls.filter(c => operationName(c[0]) === name).map(c => c[1]),
    /** Variables de chaque appel d'une requête ponctuelle, dans l'ordre. */
    queries: name => query.mock.calls.filter(c => operationName(c[0]) === name).map(c => c[1]),
  };
}
