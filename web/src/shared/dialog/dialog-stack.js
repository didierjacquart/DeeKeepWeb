import { Store } from '../../lib/store';

/**
 * @typedef {object} DialogOptions
 * @property {string} [width]
 * @property {string} [height]
 * @property {string} [maxWidth] Défaut : 80vw (comme les boîtes de dialogue usuelles).
 * @property {string} [maxHeight]
 * @property {string} [panelClass] Classes ajoutées au panneau de la boîte.
 * @property {boolean} [autoFocus] Faux : aucun champ ne prend le focus à l'ouverture.
 *   Sinon `[data-autofocus]`, à défaut le premier élément focalisable.
 * @property {boolean} [disableClose] Vrai : ni Échap ni un clic hors de la boîte ne la ferment.
 */

/**
 * Fermeture d'une boîte : `close(résultat)`, ou `close()` pour annuler.
 * @typedef {(result?: any) => void} CloseDialog
 */

/** Pile des boîtes ouvertes ; la dernière reçoit le clavier. */
export class DialogStack extends Store {
  #seq = 0;

  constructor() {
    super({ stack: [] });
  }

  /**
   * @param {(close: CloseDialog) => import('react').ReactNode} render
   * @param {DialogOptions} options
   */
  open(render, options) {
    return new Promise(resolve => {
      const id = ++this.#seq;
      const close = result => {
        if (!this.state.stack.some(d => d.id === id)) return;
        this.setState(s => ({ stack: s.stack.filter(d => d.id !== id) }));
        resolve(result);
      };
      this.setState(s => ({ stack: [...s.stack, { id, render, options, close }] }));
    });
  }
}

export const dialogs = new DialogStack();

/**
 * Ouvre une boîte de dialogue modale ; la promesse rend le résultat passé à
 * `close`, ou undefined si la boîte est fermée sans résultat (Échap, clic dehors).
 *
 * @param {(close: CloseDialog) => import('react').ReactNode} render
 * @param {DialogOptions} [options]
 */
export function openDialog(render, options = {}) {
  return dialogs.open(render, options);
}
