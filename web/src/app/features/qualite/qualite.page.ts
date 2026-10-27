import { ScrollingModule } from '@angular/cdk/scrolling';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule, MatMenuTrigger } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { GqlService } from '../../core/graphql/gql.service';
import { ConnectivityService } from '../../core/offline/connectivity.service';
import { Job, JobsService, isActive } from '../../core/jobs/jobs.service';
import { PlayerService, PlayerTrack } from '../../core/player/player.service';
import { NotifyService } from '../../core/ui/notify.service';
import { openFsBrowser } from '../../shared/fs-browser/fs-browser-dialog';
import { ConfirmService, ProgressBar, mediaUrl } from '../../shared/ui';
import {
  CLEAR, DEEP_DEFECTS, DEEP_TIP, DEFECTS, DefectId, QUALITY_ENTRIES, QUALITY_UPDATED, QualityPageData, QualityRow, QualityThresholds,
  QualityUpdate, REANALYZE, REDOWNLOAD, SET_FOLDER, START_SCAN, SortKey, THRESHOLD_FIELDS_UI, ThresholdField, UPDATE_THRESHOLDS,
  compareKeys, keepRow, scoreClass, sortKey,
} from './quality-model';

interface Column { key: SortKey | null; label: string; cls: string; tip?: string; hideMobile?: boolean }

/**
 * Écran « Qualité » : mesure du signal de chaque fichier (tâche serveur « quality.scan »), verdict recalculé
 * par le serveur avec les seuils courants, retéléchargement des titres abîmés et réanalyse automatique.
 * La liste vit sur le serveur : quitter la page ou perdre la connexion n'interrompt rien, et les lignes
 * arrivent au fil de l'eau par l'abonnement « qualityUpdated ».
 */
@Component({
  selector: 'dk-qualite-page',
  imports: [ScrollingModule, MatMenuModule, MatTooltipModule, ProgressBar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './qualite.page.html',
  styleUrl: './qualite.page.scss',
})
export class QualityPage {
  private readonly gql = inject(GqlService);
  private readonly jobs = inject(JobsService);
  private readonly player = inject(PlayerService);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);
  private readonly dialog = inject(MatDialog);
  private readonly connectivity = inject(ConnectivityService);
  private readonly destroyRef = inject(DestroyRef);

  readonly defects = DEFECTS;
  readonly thresholdFields = THRESHOLD_FIELDS_UI;
  readonly deepTip = DEEP_TIP;

  readonly columns: Column[] = [
    { key: null, label: '', cls: 'c-chk' },
    { key: 'score', label: 'Écouter', cls: 'c-play', tip: 'Écouter ce titre dans le lecteur (égaliseur, déplacement)' },
    { key: 'score', label: 'Note', cls: 'c-score' },
    { key: 'title', label: 'Titre', cls: 'c-title' },
    { key: 'artist', label: 'Artiste', cls: 'c-artist', hideMobile: true },
    { key: 'defects', label: 'Défauts', cls: 'c-defects' },
    { key: 'hiss', label: 'Souffle', cls: 'c-hiss', hideMobile: true },
    { key: 'crackle', label: 'Craquements', cls: 'c-crackle', hideMobile: true },
    { key: 'clipping', label: 'Saturation', cls: 'c-clip', hideMobile: true },
    { key: 'gaps', label: 'Coupures', cls: 'c-gaps', hideMobile: true },
    { key: 'duration', label: 'Durée', cls: 'c-dur', hideMobile: true },
    { key: 'blanks', label: 'Blancs', cls: 'c-blanks', hideMobile: true },
    { key: 'format', label: 'Format / débit', cls: 'c-fmt' },
    { key: 'path', label: 'Fichier', cls: 'c-path', hideMobile: true },
    { key: 'status', label: 'Statut', cls: 'c-status' },
  ];

  // ---------------------------------------------------------------- État
  private readonly rows = signal<ReadonlyMap<string, QualityRow>>(new Map());
  readonly folder = signal('');
  readonly loaded = signal(false);
  readonly stale = signal(false);
  readonly thresholds = signal<QualityThresholds | null>(null);
  readonly panelOpen = signal(false);

  readonly shown = signal<Record<DefectId, boolean>>({ hiss: true, crackle: true, clipping: true, gaps: true, duration: true, tail: true, head: true });
  readonly includeClean = signal(false);
  readonly sort = signal<{ key: SortKey; asc: boolean }>({ key: 'score', asc: true });
  readonly selected = signal<ReadonlySet<string>>(new Set());
  readonly menuRow = signal<QualityRow | null>(null);
  readonly menuPos = signal({ x: 0, y: 0 });
  readonly compact = signal(typeof window !== 'undefined' && window.matchMedia('(max-width: 899px)').matches);

  // ---------------------------------------------------------------- Tâches
  private readonly scanJobs = this.jobs.byType('quality.scan');
  private readonly refJobs = this.jobs.byType('quality.references');
  private readonly reanalyzeJobs = this.jobs.byType('quality.reanalyze');
  private readonly redownloadJobs = this.jobs.byType('redownload');

  readonly scanJob = computed<Job | null>(() => this.scanJobs().find(isActive) ?? this.scanJobs()[0] ?? null);
  readonly scanning = computed(() => !!this.scanJob() && isActive(this.scanJob()!));
  private readonly redownloadById = computed(() => new Map(this.redownloadJobs().map(j => [j.id, j])));

  readonly downloading = computed(() =>
    [...this.rows().values()].some(r => this.rowBusy(r)) || this.reanalyzeJobs().some(isActive));
  readonly busy = computed(() => this.scanning() || this.downloading());

  // ---------------------------------------------------------------- Dérivés
  readonly visibleDefects = computed(() => {
    const deep = this.thresholds()?.deep ?? true;
    const shown = this.shown();
    return new Set<DefectId>(DEFECTS.filter(d => shown[d.id] && (deep || !DEEP_DEFECTS.includes(d.id))).map(d => d.id));
  });

  readonly list = computed(() => {
    const visible = this.visibleDefects();
    const clean = this.includeClean();
    const { key, asc } = this.sort();
    const keyed = [...this.rows().values()].filter(r => keepRow(r, visible, clean)).map(r => ({ r, k: sortKey(r, key) }));
    // Pas de retri pendant l'analyse : les lignes arrivent en fin de liste, le tri définitif a lieu à la fin.
    keyed.sort((a, b) => (asc ? 1 : -1) * compareKeys(a.k, b.k) || a.r.uid - b.r.uid);
    return keyed.map(x => x.r);
  });

  readonly stats = computed(() => {
    const all = [...this.rows().values()];
    return {
      count: all.length,
      flagged: all.filter(r => r.defects.length > 0).length,
      errors: all.filter(r => r.error).length,
      waiting: all.filter(r => r.referenceState === 'pending' || r.referenceState === 'network').length,
    };
  });

  readonly selectedCount = computed(() => this.list().filter(r => this.selected().has(r.path)).length);

  readonly progress = computed(() => {
    const job = this.scanJob();
    if (!job || !isActive(job)) return null;
    return job.total > 0 ? Math.min(1, (job.done + job.failed) / job.total) : null;
  });

  /** Retéléchargements en cours : une barre globale, la colonne Statut pouvant être hors écran. */
  readonly redownloadSummary = computed(() => {
    const active = this.redownloadJobs().filter(isActive);
    if (active.length === 0) return null;
    const all = this.redownloadJobs().filter(j => active.some(a => a.groupKey && a.groupKey === j.groupKey) || isActive(j));
    const finished = all.length - active.length;
    const running = active.find(j => j.status !== 'QUEUED');
    const value = (finished + active.reduce((s, j) => s + (j.status === 'QUEUED' ? 0 : j.progress), 0)) / all.length;
    const detail = running ? `${running.title} — ${running.message ?? 'Initialisation…'}` : 'En attente…';
    return { value: Math.min(1, value), label: `Retéléchargement ${finished + 1} / ${all.length} : ${detail}` };
  });

  readonly statusLine = computed(() => {
    const job = this.scanJob();
    if (job && isActive(job)) return job.status === 'QUEUED' ? 'Analyse en attente…' : job.message ?? 'Analyse en cours…';
    const s = this.stats();
    if (s.count === 0) return this.loaded() ? 'Lancez l\'analyse pour repérer les titres à la qualité médiocre.' : 'Chargement…';
    const parts = [`${job?.status === 'CANCELLED' ? 'Analyse interrompue' : 'Analyse terminée'} : ${s.count} fichier(s) lus, ${s.flagged} titre(s) à la qualité douteuse`];
    if (s.errors) parts.push(`${s.errors} illisible(s)`);
    if (s.waiting) parts.push(`durée du morceau en cours de recherche pour ${s.waiting} titre(s)`);
    if (this.thresholds() && !this.thresholds()!.deep) parts.push('craquements non mesurés (analyse rapide)');
    return parts.join(' — ') + '.';
  });

  readonly refBusy = computed(() => this.refJobs().some(isActive));

  private thresholdTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    // Lecture « cache puis réseau » : la liste s'affiche hors ligne.
    this.gql.watch<{ qualityEntries: QualityPageData }>(QUALITY_ENTRIES).pipe(takeUntilDestroyed()).subscribe(state => {
      const page = state.data?.qualityEntries;
      this.stale.set(state.stale);
      if (!page) return;
      this.loaded.set(true);
      this.folder.set(page.folder ?? '');
      untracked(() => {
        if (!this.thresholdTimer) this.thresholds.set(page.thresholds);
        this.rows.set(new Map(page.items.map(r => [r.path, r])));
      });
    });

    this.gql.subscribe<{ qualityUpdated: QualityUpdate }>(QUALITY_UPDATED).pipe(takeUntilDestroyed()).subscribe({
      next: d => d?.qualityUpdated && this.apply(d.qualityUpdated),
      error: () => undefined,
    });

    // La page tient un état local : à chaque reconnexion, elle se resynchronise.
    effect(() => {
      this.connectivity.resyncTick();
      untracked(() => this.gql.refetchActive());
    });

    const mq = typeof window !== 'undefined' ? window.matchMedia('(max-width: 899px)') : null;
    const onChange = (e: MediaQueryListEvent) => this.compact.set(e.matches);
    mq?.addEventListener('change', onChange);
    this.destroyRef.onDestroy(() => {
      mq?.removeEventListener('change', onChange);
      clearTimeout(this.thresholdTimer);
    });
  }

  private apply(update: QualityUpdate): void {
    if (update.reason === 'cleared' || update.reason === 'folder') {
      this.rows.set(new Map());
      this.selected.set(new Set());
      if (update.reason === 'folder') this.gql.refetchActive();
      return;
    }
    if (update.reason === 'thresholds') {
      // Le verdict est recalculé par le serveur : on relit la liste (aucun fichier n'est relu).
      this.gql.refetchActive();
      return;
    }
    if (update.entries.length === 0) return;
    this.rows.update(m => {
      const next = new Map(m);
      for (const e of update.entries) next.set(e.path, e);
      for (const p of update.removed ?? []) next.delete(p);
      return next;
    });
  }

  // ---------------------------------------------------------------- Dossier
  async changeFolder(): Promise<void> {
    if (this.busy()) return;
    const picked = await openFsBrowser(this.dialog, { mode: 'folder', start: this.folder() || undefined, audioOnly: true, title: 'Choisir le dossier à analyser' });
    const folder = picked?.[0];
    if (!folder || folder === this.folder()) return;
    const r = await this.gql.mutate(SET_FOLDER, { folder }, { label: 'Modifier le dossier analysé', queueable: false });
    if (r.error) this.notify.error(r.error);
    else {
      this.folder.set(folder);
      this.notify.info('Dossier modifié : relancez l\'analyse.');
    }
  }

  // ---------------------------------------------------------------- Seuils
  setThreshold(field: ThresholdField, event: Event): void {
    const value = parseFloat((event.target as HTMLInputElement).value);
    const current = this.thresholds();
    if (!current || !Number.isFinite(value)) return;
    this.setThresholds({ ...current, [field.key]: Math.min(field.max, Math.max(field.min, value)) });
  }

  setDeep(event: Event): void {
    const current = this.thresholds();
    if (current) this.setThresholds({ ...current, deep: (event.target as HTMLInputElement).checked });
  }

  private setThresholds(next: QualityThresholds): void {
    this.thresholds.set(next);
    clearTimeout(this.thresholdTimer);
    // Enregistre tous les réglages à la fois, après une courte pause de saisie.
    this.thresholdTimer = setTimeout(async () => {
      this.thresholdTimer = undefined;
      const r = await this.gql.mutate<{ updateQualityThresholds: QualityThresholds }>(UPDATE_THRESHOLDS, { thresholds: next }, { label: 'Modifier les seuils de qualité', refetch: ['QualityEntries'] });
      if (r.data?.updateQualityThresholds) this.thresholds.set(r.data.updateQualityThresholds);
      if (r.error) this.notify.error(r.error);
    }, 400);
  }

  thresholdValue(field: ThresholdField): number {
    return (this.thresholds()?.[field.key] as number | undefined) ?? 0;
  }

  // ---------------------------------------------------------------- Analyse
  async startScan(): Promise<void> {
    if (this.busy()) return;
    if (!this.folder()) {
      this.notify.error('Choisissez d\'abord le dossier à analyser.');
      return;
    }
    const r = await this.gql.mutate(START_SCAN, { folder: this.folder(), deep: this.thresholds()?.deep ?? true }, { label: 'Analyser la qualité', queueable: false });
    if (r.error) this.notify.error(r.error);
    else {
      this.rows.set(new Map());
      this.selected.set(new Set());
    }
  }

  async stopScan(): Promise<void> {
    const job = this.scanJob();
    if (job && isActive(job)) await this.jobs.cancel(job);
  }

  async clearList(): Promise<void> {
    if (this.busy()) {
      this.notify.info('Attendez la fin de l\'analyse et des retéléchargements en cours avant de vider la liste.');
      return;
    }
    const n = this.rows().size;
    if (n === 0) return;
    const ok = await this.confirm.ask({
      title: 'Vider la liste',
      message: `Retirer les ${n} résultat(s) d'analyse ?\nLes fichiers du disque sont conservés ; il faudra relancer l'analyse pour les revoir.`,
      confirm: 'Oui',
      cancel: 'Non',
    });
    if (!ok) return;
    const r = await this.gql.mutate(CLEAR, {}, { label: 'Vider la liste de qualité', queueable: false });
    if (r.error) this.notify.error(r.error);
    else this.notify.info('Écran vidé : relancez l\'analyse.');
  }

  // ---------------------------------------------------------------- Filtres, tri, sélection
  toggleShown(id: DefectId, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.shown.update(s => ({ ...s, [id]: checked }));
  }

  sortBy(col: Column): void {
    if (!col.key) return;
    const cur = this.sort();
    this.sort.set(cur.key === col.key ? { key: col.key, asc: !cur.asc } : { key: col.key, asc: true });
  }

  sortMark(col: Column): string {
    const s = this.sort();
    return col.key && s.key === col.key ? (s.asc ? ' ▲' : ' ▼') : '';
  }

  toggleRow(row: QualityRow, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selected.update(s => {
      const next = new Set(s);
      if (checked) next.add(row.path);
      else next.delete(row.path);
      return next;
    });
  }

  /** « Tout cocher » : lignes affichées, jamais les illisibles. */
  checkAll(): void {
    this.selected.update(s => {
      const next = new Set(s);
      for (const r of this.list()) if (!r.error) next.add(r.path);
      return next;
    });
  }

  uncheckAll(): void {
    this.selected.update(s => {
      const next = new Set(s);
      for (const r of this.list()) next.delete(r.path);
      return next;
    });
  }

  // ---------------------------------------------------------------- Retéléchargement
  async redownload(): Promise<void> {
    if (this.downloading()) {
      this.notify.info('Des retéléchargements sont déjà en cours.');
      return;
    }
    const chosen = this.list().filter(r => this.selected().has(r.path) && !r.error);
    if (chosen.length === 0) {
      this.notify.info('Cochez au moins un titre à retélécharger.');
      return;
    }
    const targets = chosen.filter(r => (r.artist ?? '').trim() && (r.title ?? '').trim());
    const skipped = chosen.length - targets.length;
    if (targets.length === 0) {
      this.notify.error('Les titres cochés n\'ont ni artiste ni titre dans leurs tags : la recherche n\'aurait rien à chercher, et le fichier trouvé remplacerait l\'existant à l\'aveugle. Renseignez d\'abord les tags — l\'écran « Organiser » sait les retrouver à partir du signal.');
      return;
    }
    const ok = await this.confirm.ask({
      title: 'Retélécharger',
      message:
        `${targets.length} titre(s) vont être réextraits et remplaceront la version abîmée.\n\n` +
        'Le fichier produit est rangé selon les règles de l\'écran « Organiser » (sous-dossiers, modèle de nom, « Divers ») : un titre mal placé peut donc changer de dossier au passage.\n\n' +
        'Chaque titre est ensuite réanalysé pour vérifier que le défaut a disparu.' +
        (skipped ? `\n\n${skipped} titre(s) coché(s) sont écartés : leurs tags ne donnent pas l'artiste ou le titre nécessaires à la recherche.` : '') +
        '\n\nContinuer ?',
      confirm: 'Continuer',
      cancel: 'Non',
    });
    if (!ok) return;
    const r = await this.gql.mutate(REDOWNLOAD, { paths: targets.map(t => t.path) }, { label: `Retélécharger ${targets.length} titre(s)`, queueable: false });
    if (r.error) this.notify.error(r.error);
    else this.selected.update(s => new Set([...s].filter(p => !targets.some(t => t.path === p))));
  }

  /** Un retéléchargement ou une réanalyse est-il en cours pour cette ligne ? */
  rowBusy(row: QualityRow): boolean {
    if (row.status === 'reanalyzing') return true;
    const job = row.redownloadJobId ? this.redownloadById().get(row.redownloadJobId) : null;
    return !!job && isActive(job);
  }

  /** Colonne STATUT : avancement de la tâche d'extraction, puis état tenu par le serveur. */
  statusOf(row: QualityRow): { text: string; tip: string | null; cls: string } {
    const job = row.redownloadJobId ? this.redownloadById().get(row.redownloadJobId) : null;
    if (job && job.status === 'FAILED') return { text: 'Échec', tip: job.error ?? row.statusDetail, cls: 'bad' };
    if (job && job.status === 'CANCELLED') return { text: 'Échec', tip: 'Retéléchargement annulé', cls: 'bad' };
    if (job && isActive(job)) {
      if (job.status === 'QUEUED') return { text: 'En attente…', tip: null, cls: '' };
      const pct = Math.round(job.progress * 100);
      return { text: `${job.message ?? 'Initialisation…'} — ${pct}%`, tip: null, cls: '' };
    }
    if (job && job.status === 'SUCCEEDED' && row.status !== 'replaced' && row.status !== 'replaced_error') return { text: 'Réanalyse…', tip: null, cls: '' };
    const text = row.cells['status'] ?? '';
    return { text, tip: row.tooltips['status'] ?? null, cls: text.includes('persistant') ? 'warn' : text.includes('corrigé') ? 'ok' : '' };
  }

  // ---------------------------------------------------------------- Écoute et menu
  play(row: QualityRow): void {
    const queue = this.list().map(r => this.toTrack(r));
    this.player.play(this.toTrack(row), queue);
  }

  private toTrack(r: QualityRow): PlayerTrack {
    return {
      id: r.path,
      title: r.title || r.path,
      artist: r.artist,
      album: r.album,
      src: mediaUrl(r.path),
      duration: r.duration,
      // Format, débit, note et défauts : ce qu'il faut écouter.
      context: r.cells['context'] ?? null,
    };
  }

  openMenu(row: QualityRow, event: MouseEvent, trigger: MatMenuTrigger): void {
    event.preventDefault();
    this.menuRow.set(row);
    this.menuPos.set({ x: event.clientX, y: event.clientY });
    trigger.openMenu();
  }

  openMenuAt(row: QualityRow, event: MouseEvent, trigger: MatMenuTrigger): void {
    event.stopPropagation();
    const r = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.menuRow.set(row);
    this.menuPos.set({ x: r.left, y: r.bottom });
    trigger.openMenu();
  }

  async copyPath(row: QualityRow): Promise<void> {
    try {
      await navigator.clipboard.writeText(row.path);
      this.notify.success('Chemin copié.');
    } catch {
      this.notify.error('Copie impossible : ' + row.path);
    }
  }

  async reanalyze(row: QualityRow): Promise<void> {
    const r = await this.gql.mutate(REANALYZE, { paths: [row.path] }, { label: 'Réanalyser un titre' });
    if (r.error) this.notify.error(r.error);
  }

  has(row: QualityRow, id: DefectId): boolean {
    return row.defects.some(d => d.id === id);
  }

  scoreClass = scoreClass;
  trackBy = (_: number, r: QualityRow) => r.path;
}
