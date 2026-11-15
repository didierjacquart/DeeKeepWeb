import { Store } from '../../lib/store';

/** Messages éphémères (bas d'écran), un seul à la fois. Les erreurs restent plus longtemps. */
export class NotifyService extends Store {
  seq = 0;
  timer = null;

  constructor() {
    super({ toast: null });
  }

  info(message) {
    this.show({ message, action: 'OK', kind: 'info' }, 3500);
  }

  success(message) {
    this.show({ message, action: 'OK', kind: 'success' }, 3000);
  }

  error(message) {
    this.show({ message, action: 'Fermer', kind: 'error' }, 8000);
  }

  dismiss(id) {
    if (id != null && this.state.toast?.id !== id) return;
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
    this.setState({ toast: null });
  }

  show(toast, duration) {
    if (this.timer) clearTimeout(this.timer);
    const id = ++this.seq;
    this.setState({ toast: { ...toast, id } });
    this.timer = setTimeout(() => this.dismiss(id), duration);
  }
}

export const notify = new NotifyService();
