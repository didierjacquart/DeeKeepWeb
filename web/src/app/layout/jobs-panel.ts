import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Job, JobsService, isActive } from '../core/jobs/jobs.service';
import { ConnectivityService } from '../core/offline/connectivity.service';
import { OfflineQueueService } from '../core/offline/offline-queue.service';
import { ProgressBar } from '../shared/ui';
import { WhenPipe } from '../shared/pipes';

const TYPE_LABELS: Record<string, string> = {
  'download': 'Extraction',
  'convert': 'Conversion',
  'redownload': 'Retéléchargement',
  'quality.scan': 'Analyse qualité',
  'quality.reanalyze': 'Réanalyse',
  'quality.references': 'Durées de référence',
  'organize.analyze': 'Analyse de la bibliothèque',
  'organize.apply': 'Rangement',
  'tags.find': 'Recherche de tags',
  'tags.write': 'Écriture des tags',
  'library.scan': 'Lecture de la bibliothèque',
  'library.import': 'Import de l\'application desktop',
  'files.scan': 'Lecture de fichiers',
  'archive.upload': 'Archivage',
  'archive.restore': 'Restauration',
  'archive.check': 'Vérification de l\'archive',
  'archive.usage': 'Mesure de l\'archive',
  'tools.update': 'Mise à jour de yt-dlp',
};

/**
 * Panneau des tâches de fond : tout ce qui tourne sur le serveur, quelle que
 * soit la page. Fermer le navigateur n'arrête rien ; revenir montre où en est
 * chaque tâche.
 */
@Component({
  selector: 'dk-jobs-panel',
  imports: [MatTooltipModule, ProgressBar, WhenPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <aside class="panel" role="dialog" aria-label="Tâches de fond">
      <header>
        <span class="dk-overline">Tâches de fond</span>
        <span class="dk-spacer"></span>
        <button class="control-btn small" [class.checked]="showDone()" (click)="showDone.set(!showDone())">Terminées</button>
        <button class="icon-btn" (click)="closed.emit()" aria-label="Fermer">✕</button>
      </header>
      <p class="dk-help">
        {{ connectivity.state() === 'online' ? 'Connecté au serveur.' : connectivity.state() === 'reconnecting' ? 'Reconnexion au serveur…' : 'Hors ligne : affichage du dernier état connu.' }}
        @if (queue.pending()) { {{ queue.pending() }} action(s) attendent le retour de la connexion. }
      </p>
      @for (q of queue.items(); track q.id) {
        <div class="job queued">
          <div class="line"><span class="name">{{ q.label }}</span><span class="dk-chip warn">en attente</span></div>
          @if (q.lastError) { <span class="dk-faint small">{{ q.lastError }}</span> }
          <button class="control-btn small" (click)="queue.remove(q.id)">Abandonner</button>
        </div>
      }
      @for (job of visible(); track job.id) {
        <div class="job">
          <div class="line">
            <span class="name" [matTooltip]="job.title ?? ''">{{ label(job) }}</span>
            <span class="dk-chip" [class]="chip(job)">{{ status(job) }}</span>
          </div>
          @if (job.title) { <span class="dk-faint small ellipsis">{{ job.title }}</span> }
          @if (job.status === 'RUNNING') {
            <dk-progress [value]="job.total > 0 ? job.progress : null" [label]="job.total > 0 ? (job.done + job.failed) + ' / ' + job.total : ''" />
          }
          @if (job.message) { <span class="dk-muted small">{{ job.message }}</span> }
          @if (job.error) { <span class="dk-error-text small">{{ job.error }}</span> }
          <div class="line">
            <span class="dk-faint small">{{ job.updatedAt | when }}</span>
            <span class="dk-spacer"></span>
            @if (active(job)) {
              <button class="control-btn small" (click)="jobs.cancel(job)">Arrêter</button>
            } @else if (job.status === 'FAILED' || job.status === 'CANCELLED') {
              <button class="control-btn small" (click)="jobs.retry(job)">Relancer</button>
            }
          </div>
        </div>
      } @empty {
        <p class="dk-empty">Aucune tâche {{ showDone() ? '' : 'en cours' }}.</p>
      }
    </aside>
  `,
  styles: `
    .panel {
      position: fixed; z-index: 60; top: 0; right: 0; bottom: 0; width: min(420px, 100vw);
      background: var(--dk-surface); border-left: 1px solid var(--dk-glass);
      padding: 16px 16px calc(16px + var(--dk-player-height) + var(--dk-safe-bottom)); overflow: auto;
      display: flex; flex-direction: column; gap: 10px; box-shadow: -12px 0 40px rgba(0,0,0,.5);
    }
    header { display: flex; align-items: center; gap: 8px; }
    .job { display: flex; flex-direction: column; gap: 6px; padding: 10px 12px; border: 1px solid var(--dk-glass); border-radius: 10px; background: rgba(28,30,38,.6); }
    .line { display: flex; align-items: center; gap: 8px; }
    .name { font-weight: 600; font-size: 13px; }
    .small { font-size: 11px; }
    .ellipsis { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  `,
})
export class JobsPanel {
  readonly jobs = inject(JobsService);
  readonly connectivity = inject(ConnectivityService);
  readonly queue = inject(OfflineQueueService);
  readonly closed = output<void>();
  readonly showDone = signal(false);
  readonly visible = computed(() => (this.showDone() ? this.jobs.all().slice(0, 100) : this.jobs.active()));

  active(job: Job): boolean {
    return isActive(job);
  }

  label(job: Job): string {
    return TYPE_LABELS[job.type] ?? job.type;
  }

  status(job: Job): string {
    return { QUEUED: 'en file', RUNNING: 'en cours', SUCCEEDED: 'terminée', FAILED: 'échec', CANCELLED: 'arrêtée' }[job.status];
  }

  chip(job: Job): string {
    return 'dk-chip ' + ({ QUEUED: '', RUNNING: 'info', SUCCEEDED: 'ok', FAILED: 'err', CANCELLED: 'warn' }[job.status]);
  }
}
