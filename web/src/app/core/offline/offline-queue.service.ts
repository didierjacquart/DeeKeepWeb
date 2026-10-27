import { Injectable, computed, inject, signal } from '@angular/core';
import { Apollo } from 'apollo-angular';
import { gql } from '@apollo/client';
import { get, set } from 'idb-keyval';
import { firstValueFrom } from 'rxjs';
import { ConnectivityService } from './connectivity.service';
import { NotifyService } from '../ui/notify.service';

const KEY = 'dk-offline-queue-v1';

/** Mutation mise en attente faute de réseau, rejouée à la reconnexion. */
export interface QueuedMutation {
  id: string;
  label: string;
  query: string;
  variables: Record<string, unknown>;
  createdAt: number;
  attempts: number;
  lastError?: string;
}

export function newIdempotencyKey(): string {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

/**
 * File des actions faites hors ligne (ajout de pistes à extraire, réglages,
 * lancement d'analyses…). Elle survit à la fermeture de l'application
 * (IndexedDB) et repart dès que le serveur répond. Chaque entrée porte une clé
 * d'idempotence : rejouée deux fois, elle n'agit qu'une fois.
 */
@Injectable({ providedIn: 'root' })
export class OfflineQueueService {
  private readonly apollo = inject(Apollo);
  private readonly connectivity = inject(ConnectivityService);
  private readonly notify = inject(NotifyService);

  readonly items = signal<QueuedMutation[]>([]);
  readonly pending = computed(() => this.items().length);
  readonly replaying = signal(false);

  private loaded: Promise<void>;

  constructor() {
    this.loaded = get<QueuedMutation[]>(KEY).then(v => this.items.set(v ?? [])).catch(() => undefined);
    this.connectivity.onReconnect(() => void this.replay());
    // Premier passage : ce qui restait d'une session précédente.
    setTimeout(() => void this.replay(), 3000);
  }

  async enqueue(label: string, query: string, variables: Record<string, unknown>, id = newIdempotencyKey()): Promise<QueuedMutation> {
    await this.loaded;
    const entry: QueuedMutation = { id, label, query, variables, createdAt: Date.now(), attempts: 0 };
    this.items.update(list => [...list, entry]);
    await this.save();
    this.notify.info(`Hors ligne : « ${label} » sera envoyé dès le retour de la connexion.`);
    return entry;
  }

  async remove(id: string): Promise<void> {
    this.items.update(list => list.filter(i => i.id !== id));
    await this.save();
  }

  async replay(): Promise<void> {
    await this.loaded;
    if (this.replaying() || this.items().length === 0 || !this.connectivity.browserOnline()) return;
    this.replaying.set(true);
    try {
      // Dans l'ordre : une action peut dépendre de la précédente.
      for (const entry of [...this.items()]) {
        try {
          const result = await firstValueFrom(this.apollo.mutate({
            mutation: gql(entry.query),
            variables: entry.variables,
            context: { headers: { 'X-Idempotency-Key': entry.id } },
            errorPolicy: 'all',
          }));
          const errors = (result as { error?: { message?: string } }).error;
          if (errors && /SERVICE_UNAVAILABLE|Failed to fetch|NetworkError/i.test(errors.message ?? '')) {
            throw new Error(errors.message);
          }
          await this.remove(entry.id);
          if (errors) this.notify.error(`« ${entry.label} » a échoué : ${errors.message}`);
          else this.notify.success(`« ${entry.label} » envoyé.`);
        } catch (e) {
          entry.attempts++;
          entry.lastError = e instanceof Error ? e.message : String(e);
          await this.save();
          break; // serveur encore indisponible : on réessaiera à la prochaine reconnexion
        }
      }
    } finally {
      this.replaying.set(false);
    }
  }

  private async save(): Promise<void> {
    try {
      await set(KEY, this.items());
    } catch {
      // Quota dépassé : la file reste en mémoire pour la session.
    }
  }
}
