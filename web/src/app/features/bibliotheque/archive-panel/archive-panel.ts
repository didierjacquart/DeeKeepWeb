import { ChangeDetectionStrategy, Component, DestroyRef, WritableSignal, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subscription } from 'rxjs';
import { GqlService } from '../../../core/graphql/gql.service';
import { Job, JobsService, isActive } from '../../../core/jobs/jobs.service';
import { ConnectivityService } from '../../../core/offline/connectivity.service';
import { formatBytes } from '../../../shared/pipes';
import { ConfirmService, ProgressBar } from '../../../shared/ui';
import { openFsBrowser } from '../../../shared/fs-browser/fs-browser-dialog';
import {
  ARCHIVE_UPDATED, ARCHIVE_USAGE, ARCHIVE_WORK_TYPES, ArchiveActions, ArchiveItemResult, ArchiveUsage, UNCONFIGURED_TEXT,
} from './archive-actions.service';

interface StateLine {
  text: string;
  error: boolean;
}

/** Résultat de fin d'une tâche de copie (compteurs de library_mirror). */
interface WorkResult {
  sent?: number;
  restored?: number;
  skipped?: number;
  failed?: number;
  bytes?: number;
}

const INTRO =
  'La copie de la bibliothèque vit dans une base SQLite, sur un disque à soi. Les titres y entrent tels quels, octet pour octet : ' +
  'rien n\'est réencodé, rien n\'est perdu, et une restauration rend l\'original — son empreinte est revérifiée à la sortie. ' +
  'La seule limite est la place du disque, que la ligne ci-dessous rappelle. Un seul fichier : le déplacer vers un disque plus grand ' +
  'se résume à le copier, puis à corriger le chemin ci-dessous. Les fichiers d\'origine ne sont pas touchés.';

const human = (n: number | null | undefined) => formatBytes(n ?? 0);
const baseName = (p: string) => p.split(/[\\/]/).pop() ?? p;

/**
 * Volet « Archive » de la Bibliothèque : emplacement du fichier d'archive,
 * jauge de place, Tester / Mesurer / Enregistrer, et les actions Archiver,
 * Restaurer, Arrêter sur les titres cochés (sinon affichés).
 */
@Component({
  selector: 'dk-archive-panel',
  imports: [MatTooltipModule, ProgressBar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bar">
      <button class="control-btn toggle" [attr.aria-expanded]="open()" (click)="open.set(!open())">{{ open() ? '▾ Archive' : '▸ Archive' }}</button>
      <span class="dk-spacer"></span>
      <button class="control-btn" [disabled]="busy() || !actions.configured()" [matTooltip]="restoreTip()" (click)="restore()">Restaurer</button>
      <button class="control-btn" [disabled]="!anyActive()" (click)="stop()">Arrêter</button>
      <button class="primary-btn" [disabled]="busy() || !actions.configured()" [matTooltip]="archiveTip()" (click)="archive()">Archiver</button>
    </div>

    @if (work(); as job) {
      @if (busy()) {
        <dk-progress [value]="job.total ? job.progress : null" [label]="job.total ? (job.done + job.failed) + ' / ' + job.total : ''" />
      }
    }
    @if (state(); as s) {
      <p class="state" [class.dk-error-text]="s.error">{{ s.text }}</p>
    }
    @if (lastFailure(); as f) { <p class="state dk-error-text">{{ f }}</p> }

    @if (open()) {
      <div class="dk-card panel">
        <p class="intro">{{ intro }}</p>
        <label class="field">
          <span class="caption">Fichier d'archive</span>
          <span class="input-row">
            <input class="dk-input" [value]="root()" (input)="onRootInput($any($event.target).value)" placeholder="/srv/archive/deekeep-archive.db"
                   matTooltip="Fichier de l'archive. Un seul fichier : le déplacer vers un autre disque se résume à le copier, puis à corriger ce chemin." />
            <button class="control-btn" (click)="browse()">Parcourir…</button>
          </span>
        </label>
        @if (archiveFile(); as file) { <span class="dk-faint small">Fichier : {{ file }}</span> }

        @if (gauge(); as g) {
          <p class="gauge" [class.warn]="g.warn" [class.ok]="!g.warn">{{ g.text }}</p>
          @if (g.bar; as b) {
            <div class="meter" aria-hidden="true">
              <span class="used" [style.width.%]="b.used"></span>
              <span class="todo" [style.width.%]="b.todo"></span>
            </div>
            <span class="dk-faint small">{{ g.detail }}</span>
          }
        }

        <div class="buttons">
          <span class="dk-spacer"></span>
          <button class="control-btn" [disabled]="testing()" matTooltip="Vérifier que l'archive s'ouvre et accepte une écriture" (click)="test()">Tester</button>
          <button class="control-btn" [disabled]="checking()" matTooltip="Vérifier que chaque titre archivé retrouve sa copie dans l'archive" (click)="check()">Vérifier</button>
          <button class="control-btn" [disabled]="measuring()" matTooltip="Compter les objets réellement présents dans l'archive" (click)="measure()">Mesurer l'occupation</button>
          <button class="accent-btn" (click)="save()">Enregistrer</button>
        </div>
        @if (panelState(); as s) { <p class="state" [class.dk-error-text]="s.error">{{ s.text }}</p> }
      </div>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 8px; }
    .bar { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
    .state { margin: 0; font-size: 12px; color: var(--dk-faint); }
    .panel { display: flex; flex-direction: column; gap: 12px; padding: 16px 20px; background: var(--dk-charcoal); }
    .intro { margin: 0; font-size: 12px; color: var(--dk-faint); line-height: 1.5; }
    .field { display: flex; flex-direction: column; gap: 6px; }
    .caption { font-size: 12px; color: var(--dk-muted); }
    .input-row { display: flex; gap: 10px; align-items: center; }
    .input-row .dk-input { flex: 1 1 auto; min-width: 0; }
    .small { font-size: 11px; word-break: break-all; }
    .gauge { margin: 0; font-size: 12px; line-height: 1.5; }
    .gauge.warn { color: var(--dk-primary); }
    .gauge.ok { color: var(--dk-success); }
    .meter { display: flex; height: 8px; border-radius: 4px; overflow: hidden; background: var(--dk-gauge-off); }
    .meter .used { background: var(--dk-gradient); }
    .meter .todo { background: repeating-linear-gradient(45deg, var(--dk-warn), var(--dk-warn) 4px, transparent 4px, transparent 8px); }
    .buttons { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; }
    @media (max-width: 899px) {
      .bar > button, .buttons > button { min-height: 40px; }
      .bar .toggle { flex: 1 1 100%; }
      .panel { padding: 14px; }
      .input-row { flex-wrap: wrap; }
      .input-row .dk-input { flex-basis: 100%; min-height: 40px; }
      .buttons > button { flex: 1 1 calc(50% - 10px); }
      .buttons .dk-spacer { display: none; }
    }
  `,
})
export class ArchivePanel {
  /** Chemins en base des titres cochés. */
  readonly selected = input<string[]>([]);
  /** Chemins en base des titres affichés (dans l'ordre d'affichage). */
  readonly visible = input<string[]>([]);

  readonly actions = inject(ArchiveActions);
  private readonly jobs = inject(JobsService);
  private readonly gql = inject(GqlService);
  private readonly confirm = inject(ConfirmService);
  private readonly dialog = inject(MatDialog);
  private readonly connectivity = inject(ConnectivityService);

  readonly intro = INTRO;
  readonly open = signal(false);
  readonly root = signal('');
  readonly usage = signal<ArchiveUsage | null>(null);
  readonly state = signal<StateLine | null>(null);
  readonly panelState = signal<StateLine | null>(null);
  readonly lastFailure = signal<string | null>(null);
  readonly testing = signal(false);
  private readonly measureJobId = signal<string | null>(null);
  private readonly checkJobId = signal<string | null>(null);
  private readonly measureJob = computed(() => this.byId(this.measureJobId()));
  private readonly checkJob = computed(() => this.byId(this.checkJobId()));
  readonly measuring = computed(() => !!this.measureJob() && isActive(this.measureJob()!));
  readonly checking = computed(() => !!this.checkJob() && isActive(this.checkJob()!));

  readonly busy = this.actions.busy;
  readonly work = this.actions.currentWork;
  readonly anyActive = computed(() => this.busy() || this.measuring() || this.checking());
  readonly archiveFile = computed(() => this.usage()?.archiveFile ?? this.actions.config()?.archiveFile ?? null);

  /** Cochés ET affichés, dans l'ordre d'affichage (_selected_entries). */
  private readonly chosen = computed(() => {
    const checked = new Set(this.selected());
    return this.visible().filter(p => checked.has(p));
  });

  readonly archiveTip = computed(() => this.actions.configured()
    ? 'Copier les titres cochés dans l\'archive, tels quels — aucun réencodage, rien n\'est perdu'
    : 'Aucune archive configurée : ouvrez le volet « Archive » et indiquez le fichier où ranger les titres.');
  readonly restoreTip = computed(() => this.actions.configured()
    ? 'Ressortir de l\'archive les titres qui ne sont plus sur le disque. Rien n\'est écrasé : seuls les fichiers absents sont écrits.'
    : 'Aucune archive configurée : ouvrez le volet « Archive » et indiquez le fichier où ranger les titres.');

  readonly gauge = computed(() => {
    const u = this.usage();
    if (!u) return null;
    const root = this.root().trim();
    if (!root) {
      return {
        text: `Indiquez le fichier d'archive : les ${u.tracksOnDisk} titres présents sur le disque y entreraient tels quels, soit ${human(u.bytesOnDisk)}.`,
        warn: true, bar: null, detail: '',
      };
    }
    const free = u.diskFreeBytes ?? 0;
    // Ce qui reste à copier, et non tout ce qui est présent : une archive déjà
    // constituée ne demande plus que la place des titres neufs ou changés.
    const fits = free > u.bytesToArchive * 1.1;
    const parts = [
      `Les ${u.tracksOnDisk} titres présents sur le disque entreraient tels quels dans l'archive, soit ${human(u.bytesOnDisk)} — pour ${human(free)} de libre sur son volume.`,
      fits ? 'Tous reviendraient octet pour octet.' : 'La place ne suffit pas.',
    ];
    if (u.sameVolume) {
      parts.push('Attention : l\'archive est sur le même volume que la bibliothèque — elle ne survivrait pas à une panne de ce disque.');
    }
    if (u.error) parts.push(u.error);
    const total = u.archiveFileBytes + free;
    const bar = total > 0 ? {
      used: (100 * u.archiveFileBytes) / total,
      todo: Math.min(100 - (100 * u.archiveFileBytes) / total, (100 * u.bytesToArchive) / total),
    } : null;
    const detail = `Archive : ${human(u.archiveFileBytes)} (${u.objectCount} objets) · ${u.tracksArchived} titres archivés · ` +
      `${u.tracksStale} à renvoyer · ${u.tracksNotArchived} non archivés — reste ${human(u.bytesToArchive)} à archiver.`;
    return { text: parts.join(' '), warn: !fits || u.sameVolume, bar, detail };
  });

  private rootTimer: ReturnType<typeof setTimeout> | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  private lastWorkStatus = new Map<string, string>();

  constructor() {
    const destroyRef = inject(DestroyRef);
    const subs = new Subscription();
    destroyRef.onDestroy(() => {
      subs.unsubscribe();
      if (this.rootTimer) clearTimeout(this.rootTimer);
      if (this.refreshTimer) clearTimeout(this.refreshTimer);
    });

    void this.actions.loadConfig().then(c => {
      this.root.set(c?.root ?? '');
      this.refreshUsage();
    });

    // La colonne NUAGE change : la jauge suit (regroupé, une archive de 3 000 titres émet souvent).
    subs.add(this.gql.subscribe(ARCHIVE_UPDATED).subscribe({ next: () => this.scheduleRefresh(), error: () => undefined }));
    for (const type of ARCHIVE_WORK_TYPES) {
      subs.add(this.jobs.items({ type }).subscribe({
        next: d => {
          const item = d?.jobItem;
          if (item?.status === 'failed') {
            const data = item.data as ArchiveItemResult | null;
            this.lastFailure.set(`${baseName(data?.path ?? item.itemKey)} — ${item.message ?? ''}`);
          }
        },
        error: () => undefined,
      }));
    }

    // Une archive demandée sans archive configurée ouvre le volet.
    let lastRequest = this.actions.openPanelRequest();
    effect(() => {
      const n = this.actions.openPanelRequest();
      if (n !== lastRequest) {
        lastRequest = n;
        this.open.set(true);
      }
    });

    // Reconnexion : configuration et jauge relues.
    effect(() => {
      if (this.connectivity.resyncTick() === 0) return;
      untracked(() => void this.actions.loadConfig().then(() => this.refreshUsage()));
    });

    // Ligne d'état : message de la tâche de copie en cours, puis son bilan.
    effect(() => {
      const job = this.work();
      if (!job) return;
      untracked(() => this.onWorkUpdate(job));
    });

    effect(() => {
      const job = this.measureJob();
      if (job && !isActive(job)) untracked(() => this.finishPanelJob(job, this.measureJobId));
    });
    effect(() => {
      const job = this.checkJob();
      if (job && !isActive(job)) untracked(() => this.finishPanelJob(job, this.checkJobId));
    });
  }

  onRootInput(value: string): void {
    this.root.set(value);
    if (this.rootTimer) clearTimeout(this.rootTimer);
    this.rootTimer = setTimeout(() => this.refreshUsage(), 400);
  }

  async browse(): Promise<void> {
    const picked: unknown = await openFsBrowser(this.dialog, { mode: 'folder' });
    const path = typeof picked === 'string' ? picked : (picked as { localPath?: string; path?: string } | null)?.localPath ??
      (picked as { path?: string } | null)?.path;
    if (path) this.onRootInput(path);
  }

  async archive(): Promise<void> {
    if (this.busy()) return;
    let paths = this.chosen();
    if (!paths.length) {
      const visible = this.visible();
      if (!visible.length) return;
      const ok = await this.confirm.ask({
        title: 'Archiver', message: `Aucun titre coché. Envoyer les ${visible.length} titres affichés ?`, confirm: 'Oui', cancel: 'Non',
      });
      if (!ok) return;
      paths = visible;
    }
    this.lastFailure.set(null);
    await this.actions.archive(paths);
  }

  async restore(): Promise<void> {
    if (this.busy()) return;
    const chosen = this.chosen();
    const paths = chosen.length ? chosen : this.visible();
    if (!paths.length) return;
    if (!this.actions.configured()) {
      this.state.set({ text: UNCONFIGURED_TEXT, error: false });
      this.open.set(true);
      return;
    }
    const plan = await this.actions.plan(paths);
    if (!plan) return;
    if (plan.restorable && !chosen.length) {
      const ok = await this.confirm.ask({
        title: 'Restaurer',
        message: `Aucun titre coché. Restaurer les ${plan.restorable} titres manquants parmi les ${paths.length} affichés ?`,
        confirm: 'Oui', cancel: 'Non',
      });
      if (!ok) return;
    }
    this.lastFailure.set(null);
    await this.actions.restore(paths, plan);
  }

  async stop(): Promise<void> {
    if (this.busy()) this.state.set({ text: 'Arrêt demandé : le titre en cours est abandonné.', error: false });
    await this.actions.stop();
  }

  async save(): Promise<void> {
    const saved = await this.actions.saveConfig(this.root().trim());
    if (!saved) return;
    this.root.set(saved.root ?? '');
    this.panelState.set({ text: saved.configured ? 'Archive enregistrée.' : 'Indiquez le fichier d\'archive.', error: false });
    this.refreshUsage();
  }

  async test(): Promise<void> {
    if (this.testing()) return;
    this.testing.set(true);
    this.panelState.set({ text: 'Vérification de l\'archive…', error: false });
    try {
      const r = await this.actions.test(this.root().trim() || null);
      this.panelState.set({ text: r.message, error: !r.ok });
    } finally {
      this.testing.set(false);
    }
  }

  async measure(): Promise<void> {
    if (this.measuring()) return;
    this.panelState.set({ text: 'Mesure de l\'archive…', error: false });
    const job = await this.actions.measure(this.root().trim() || null);
    this.measureJobId.set(job?.id ?? null);
  }

  async check(): Promise<void> {
    if (this.checking()) return;
    this.panelState.set({ text: 'Vérification des titres archivés…', error: false });
    const job = await this.actions.check();
    this.checkJobId.set(job?.id ?? null);
  }

  private byId(id: string | null): Job | null {
    return id ? this.jobs.all().find(j => j.id === id) ?? null : null;
  }

  private finishPanelJob(job: Job, idSignal: WritableSignal<string | null>): void {
    idSignal.set(null);
    if (job.status === 'SUCCEEDED') this.panelState.set({ text: job.message ?? 'Terminé.', error: false });
    else if (job.status === 'CANCELLED') this.panelState.set({ text: 'Arrêtée.', error: false });
    else this.panelState.set({ text: job.error ?? job.message ?? 'Échec.', error: true });
    this.refreshUsage();
  }

  private onWorkUpdate(job: Job): void {
    const previous = this.lastWorkStatus.get(job.id);
    this.lastWorkStatus.set(job.id, job.status);
    if (isActive(job)) {
      if (job.message) this.state.set({ text: job.message, error: false });
      return;
    }
    // Bilan affiché une fois, à la transition (ou au retour sur la page).
    if (previous === job.status) return;
    const r = (job.result ?? {}) as WorkResult;
    if (job.status === 'SUCCEEDED') {
      const done = (r.sent ?? 0) + (r.restored ?? 0);
      this.state.set({ text: job.message ?? 'Terminé.', error: (r.failed ?? 0) > 0 && done === 0 });
    } else if (job.status === 'CANCELLED') {
      this.state.set({ text: job.message && job.message !== 'Arrêtée' ? `Arrêtée — ${job.message}` : 'Arrêtée.', error: false });
    } else {
      this.state.set({ text: job.error ?? 'Échec.', error: true });
    }
    if (previous) this.scheduleRefresh();
  }

  private scheduleRefresh(): void {
    if (this.refreshTimer) return;
    this.refreshTimer = setTimeout(() => {
      this.refreshTimer = null;
      this.refreshUsage();
    }, 1500);
  }

  private refreshUsage(): void {
    const root = this.root().trim() || null;
    this.gql.query<{ archiveUsage: ArchiveUsage }>(ARCHIVE_USAGE, { root })
      .then(d => this.usage.set(d.archiveUsage))
      .catch(() => undefined);
  }
}
