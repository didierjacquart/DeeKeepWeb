import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatTooltipModule } from '@angular/material/tooltip';
import { gql } from '@apollo/client';
import { GqlService } from '../../core/graphql/gql.service';
import { ConnectivityService } from '../../core/offline/connectivity.service';
import { OfflineQueueService } from '../../core/offline/offline-queue.service';
import { OfflineAudioService } from '../../core/player/offline-audio.service';
import { JOB_FIELDS, Job, JobsService, isActive } from '../../core/jobs/jobs.service';
import { NotifyService } from '../../core/ui/notify.service';
import { formatBytes, formatWhen } from '../../shared/pipes';

interface Tool { name: string; available: boolean; version: string | null; path: string | null; detail: string | null }
interface Link { producer: string; connected: boolean; since: number | null; lastError: string | null }
interface Info {
  service: string; version: string; startedAt: number; workingSetBytes: number; managedHeapBytes: number;
  activeJobs: number; outboxPending: number; links: Link[]; tools: Tool[];
}
interface Health { service: string; reachable: boolean; error: string | null; eventsConnected: boolean; info: Info | null }

const SERVICES_Q = gql`
  query Services {
    services {
      service reachable error eventsConnected
      info { service version startedAt workingSetBytes managedHeapBytes activeJobs outboxPending
             links { producer connected since lastError } tools { name available version path detail } }
    }
    settings
  }
`;
const UPDATE_YTDLP = gql`mutation UpdateYtDlp { updateYtDlp { ${JOB_FIELDS} } }`;

const LABELS: Record<string, string> = {
  catalog: 'Catalogue (Deezer, MusicBrainz, AcoustID)', library: 'Bibliothèque (titres, jaquettes, réglages)', downloader: 'Téléchargement (yt-dlp, ffmpeg)',
  quality: 'Qualité (analyse ffmpeg)', storage: 'Archive',
};

/**
 * Diagnostic : état de chaque microservice (joignable, mémoire, tâches, boîte d'envoi, liens
 * d'événements), outils externes avec leurs versions, dossier d'enregistrement, et état de la
 * liaison du client (connexion, file d'actions hors ligne, titres épinglés).
 */
@Component({
  selector: 'dk-diagnostic-page',
  imports: [MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './diagnostic.page.html',
  styleUrl: './diagnostic.page.scss',
})
export class DiagnosticsPage {
  private readonly gql = inject(GqlService);
  private readonly jobs = inject(JobsService);
  private readonly notify = inject(NotifyService);
  private readonly destroyRef = inject(DestroyRef);
  readonly connectivity = inject(ConnectivityService);
  readonly queue = inject(OfflineQueueService);
  readonly offlineAudio = inject(OfflineAudioService);

  readonly bytes = formatBytes;
  readonly when = formatWhen;
  readonly services = signal<Health[]>([]);
  readonly downloadDir = signal<string>('');
  readonly loading = signal(true);
  readonly stale = signal(false);
  readonly error = signal<string | null>(null);
  readonly storage = signal<{ used: number; quota: number } | null>(null);

  private readonly toolJobs = this.jobs.byType('tools.update');
  readonly updateJob = computed<Job | undefined>(() => this.toolJobs()[0]);
  readonly updating = computed(() => !!this.updateJob() && isActive(this.updateJob()!));

  readonly tools = computed(() => this.services().flatMap(s => (s.info?.tools ?? []).map(t => ({ ...t, service: s.service }))));
  readonly reachable = computed(() => this.services().filter(s => s.reachable).length);

  constructor() {
    // Rafraîchi toutes les 5 s tant que la page est affichée.
    this.gql.watch<{ services: Health[]; settings: Record<string, unknown> }>(SERVICES_Q, undefined, 5000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(s => {
      if (s.data?.services) this.services.set(s.data.services);
      const dir = s.data?.settings?.['download_dir'];
      if (typeof dir === 'string') this.downloadDir.set(dir);
      this.loading.set(s.loading && !s.data);
      this.stale.set(s.stale);
      this.error.set(s.error && !s.data ? s.error : null);
    });
    void this.offlineAudio.usage().then(u => this.storage.set(u));
  }

  label(service: string): string {
    return LABELS[service] ?? service;
  }

  uptime(startedAt: number): string {
    const s = Math.max(0, Math.floor(Date.now() / 1000 - startedAt));
    const d = Math.floor(s / 86400);
    const h = Math.floor((s % 86400) / 3600);
    const m = Math.floor((s % 3600) / 60);
    return d ? `${d} j ${h} h` : h ? `${h} h ${m} min` : `${m} min`;
  }

  async updateYtDlp(): Promise<void> {
    const r = await this.gql.mutate<{ updateYtDlp: Job }>(UPDATE_YTDLP, {}, { label: 'Mettre à jour yt-dlp', queueable: false });
    if (r.error) this.notify.error(r.error);
    else if (r.data) this.jobs.upsert(r.data.updateYtDlp);
  }

  refresh(): void {
    this.gql.refetchActive();
  }
}
