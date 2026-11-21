import { Observable, Subject } from 'rxjs';
import { connectivity } from '../offline/connectivity';
import { notifyUnauthorized } from './http';

/**
 * Flux unique des événements du serveur (`GET /api/events`, Server-Sent Events).
 * Ouvert au premier abonnement ; il sert aussi de sonde de joignabilité : ouvert =
 * en ligne, coupé = reconnexion (le navigateur réessaie de lui-même toutes les 3 s,
 * puis, si le serveur a fermé, on rouvre à intervalle croissant jusqu'à 30 s).
 */
export class EventStream {
  /** @type {EventSource | null} */
  source = null;
  /** @type {Map<string, Subject<any>>} */
  subjects = new Map();
  retries = 0;
  timer = null;

  /** Démarre la connexion (sans attendre un abonnement). */
  start() {
    if (this.source || this.timer || typeof EventSource === 'undefined') return;
    this.open();
  }

  /**
   * Charges utiles d'un type d'événement.
   * @param {string} type
   * @returns {Observable<any>}
   */
  on(type) {
    return new Observable(subscriber => {
      let subject = this.subjects.get(type);
      if (!subject) {
        subject = new Subject();
        this.subjects.set(type, subject);
        this.source?.addEventListener(type, this.handler(type));
      }
      const sub = subject.subscribe(subscriber);
      this.start();
      return () => sub.unsubscribe();
    });
  }

  handler(type) {
    return e => {
      let data = null;
      try {
        data = e.data ? JSON.parse(e.data) : null;
      } catch {
        return;
      }
      this.subjects.get(type)?.next(data);
    };
  }

  open() {
    this.timer = null;
    const source = new EventSource('/api/events', { withCredentials: true });
    this.source = source;
    for (const type of this.subjects.keys()) source.addEventListener(type, this.handler(type));
    source.onopen = () => {
      this.retries = 0;
      connectivity.connected();
    };
    source.onerror = () => {
      connectivity.disconnected('Connexion au serveur perdue');
      connectivity.retrying();
      if (source.readyState === EventSource.CLOSED) this.reopenLater();
    };
  }

  /** Le serveur a refusé ou fermé le flux : session expirée, ou passerelle arrêtée. */
  reopenLater() {
    this.source?.close();
    this.source = null;
    void fetch('/api/ping', { credentials: 'include' })
      .then(r => {
        if (r.status === 401) notifyUnauthorized();
      })
      .catch(() => undefined);
    const base = Math.min(30000, 1000 * 2 ** Math.min(this.retries++, 5));
    this.timer = setTimeout(() => this.open(), base / 2 + (Math.random() * base) / 2);
  }
}

export const events = new EventStream();
