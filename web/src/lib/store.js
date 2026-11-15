import { useSyncExternalStore } from 'react';

/**
 * État partagé hors des composants (services de l'application). L'état est
 * immuable : chaque modification produit un nouvel objet, ce qui permet aux
 * composants de se réabonner via {@link useStore} sans comparaison profonde.
 *
 * @template {object} S
 */
export class Store {
  #state;
  #listeners = new Set();

  /** @param {S} initial */
  constructor(initial) {
    this.#state = initial;
  }

  /** @returns {S} */
  get state() {
    return this.#state;
  }

  /**
   * Fusionne un état partiel ; ne notifie que si une valeur a réellement changé.
   * Réservé aux sous-classes (les composants passent par les méthodes du service).
   * @param {Partial<S> | ((state: S) => Partial<S>)} patch
   */
  setState(patch) {
    const next = typeof patch === 'function' ? patch(this.#state) : patch;
    const changed = Object.keys(next).some(key => !Object.is(this.#state[key], next[key]));
    if (!changed) return;
    this.#state = { ...this.#state, ...next };
    for (const listener of [...this.#listeners]) listener();
  }

  /** @param {() => void} listener */
  subscribe = listener => {
    this.#listeners.add(listener);
    return () => this.#listeners.delete(listener);
  };
}

/**
 * Lit un {@link Store} depuis un composant, qui se redessine à chaque changement.
 * Le sélecteur doit rendre une partie de l'état (ou une valeur simple), jamais un
 * objet construit à la volée : les dérivations se font avec useMemo.
 *
 * @template {object} S
 * @template T
 * @param {Store<S>} store
 * @param {(state: S) => T} [select]
 * @returns {S | T}
 */
export function useStore(store, select) {
  const getSnapshot = () => (select ? select(store.state) : store.state);
  return useSyncExternalStore(store.subscribe, getSnapshot, getSnapshot);
}
