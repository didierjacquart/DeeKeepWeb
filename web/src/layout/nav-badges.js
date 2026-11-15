import { Store } from '../lib/store';

/** Compteurs affichés à côté des entrées de navigation (ex. Historique). Les pages les renseignent. */
export class NavBadges extends Store {
  constructor() {
    super({ history: null });
  }

  setHistory(count) {
    this.setState({ history: count });
  }
}

export const navBadges = new NavBadges();
