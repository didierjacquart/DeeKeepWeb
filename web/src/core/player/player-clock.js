import { Store } from '../../lib/store';

/**
 * Position de lecture, à part du lecteur : elle change plusieurs fois par seconde
 * et seuls les composants qui l'affichent doivent se redessiner.
 *
 * @extends {Store<{ position: number, duration: number, buffering: boolean }>}
 */
export class PlayerClock extends Store {
  constructor() {
    super({ position: 0, duration: 0, buffering: false });
  }

  update(patch) {
    this.setState(patch);
  }
}
