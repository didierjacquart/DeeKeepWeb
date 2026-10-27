import { ScrollingModule } from '@angular/cdk/scrolling';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule, MatMenuTrigger } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subscription } from 'rxjs';
import { GqlService } from '../../core/graphql/gql.service';
import { Job, JobsService } from '../../core/jobs/jobs.service';
import { ConnectivityService } from '../../core/offline/connectivity.service';
import { PlayerService } from '../../core/player/player.service';
import { NotifyService } from '../../core/ui/notify.service';
import { openFsBrowser } from '../../shared/fs-browser/fs-browser-dialog';
import { ConfirmService, ProgressBar, mediaUrl } from '../../shared/ui';
import { openFileDedupDialog } from './dedup/file-dedup-dialog';
import {
  APPLY, CHECKS, DUPLICATES, DuplicatesResult, ENTRIES, LOOKUP, LookupFile, NAME_TAGS, NameTags, OrganizeEntry, OrganizeOptions,
  OrganizePlan, OrganizeSession, OrganizeState, PLAN, PREVIEW, PlanRow, RESET_CHECKS, SAVE_OPTIONS, SET_CHECKS, START, STATE,
} from './organiser.queries';
import { openTagReviewDialog } from './tags/tag-review-dialog';

/** Ligne affichée de l'arbre : un dossier ou un fichier. */
interface DirNode { kind: 'dir'; key: string; name: string; depth: number; files: number[]; note: string | null }
interface FileNode { kind: 'file'; entry: OrganizeEntry; depth: number; row: PlanRow | null }
type Node = DirNode | FileNode;

const DEFAULT_OPTIONS: OrganizeOptions = {
  artist: true, album: true, rename: false, pattern: '%titre% - %artiste%', divers: false, diversMin: 3,
  keepAlbums: true, dedup: true, removeEmpty: true, reportUntagged: true, filtersOpen: false,
};
const LOOKUP_KEY = 'dk-organize-lookup';
const TAGS_LOOKUP_WARN = 50;
const SECONDS_PER_LOOKUP = 1.2;

/**
 * Page « Organiser la bibliothèque » : analyse d'un dossier (tâche serveur, arbre rempli au fil de l'eau),
 * plan de rangement recalculé à chaque changement d'option sans toucher aux cases, application, recherche des
 * doublons, compléter les tags. L'analyse, les cases et la revue des tags vivent côté serveur : rien ne se perd
 * à un rechargement ou une coupure.
 */
@Component({
  selector: 'dk-organiser-page',
  imports: [ScrollingModule, MatMenuModule, MatTooltipModule, ProgressBar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './organiser.page.html',
  styleUrl: './organiser.page.scss',
})
export class OrganizePage {
  private readonly gql = inject(GqlService);
  private readonly jobs = inject(JobsService);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);
  private readonly player = inject(PlayerService);
  private readonly connectivity = inject(ConnectivityService);
  private readonly destroyRef = inject(DestroyRef);

  readonly patternInput = viewChild<ElementRef<HTMLInputElement>>('patternInput');
  readonly ctxTrigger = viewChild<MatMenuTrigger>('ctxTrigger');
  readonly ctxAnchor = viewChild<ElementRef<HTMLElement>>('ctxAnchor');

  // ---- état
  readonly loaded = signal(false);
  readonly defaultRoot = signal('');
  readonly session = signal<OrganizeSession | null>(null);
  readonly entries = signal<OrganizeEntry[]>([]);
  readonly plan = signal<OrganizePlan | null>(null);
  readonly options = signal<OrganizeOptions>({ ...DEFAULT_OPTIONS });
  readonly nameTags = signal<NameTags | null>(null);
  readonly preview = signal<{ text: string; warning: boolean } | null>(null);
  /** Cases posées à la main (index → coché) ; le reste suit la sélection par défaut du plan. */
  readonly touched = signal(new Map<number, boolean>());
  readonly collapsed = signal(new Set<string>());
  readonly hideInPlace = signal(false);
  readonly allCollapsed = signal(false);
  readonly busyApply = signal(false);
  readonly lookupBusy = signal(false);
  readonly message = signal<string>('');
  readonly result = signal<string | null>(null);
  readonly resume = signal<{ job: Job; files: LookupFile[]; sessionId: string } | null>(null);
  readonly ctxEntry = signal<OrganizeEntry | null>(null);

  private readonly applyJobId = signal<string | null>(null);
  private readonly jobSignal = computed(() => {
    const id = this.session()?.jobId;
    return id ? this.jobs.get(id)() : null;
  });
  private readonly applySignal = computed(() => {
    const id = this.applyJobId();
    return id ? this.jobs.get(id)() : null;
  });

  readonly scanning = computed(() => {
    const j = this.jobSignal();
    return this.session()?.state === 'running' && (!j || j.status === 'QUEUED' || j.status === 'RUNNING');
  });
  readonly applying = computed(() => {
    const j = this.applySignal();
    return !!j && (j.status === 'QUEUED' || j.status === 'RUNNING');
  });
  readonly busy = computed(() => this.scanning() || this.applying() || this.busyApply());
  readonly progress = computed(() => {
    const j = this.applying() ? this.applySignal() : this.jobSignal();
    if (!j) return null;
    return j.total > 0 ? j.progress : null;
  });
  readonly statusText = computed(() => {
    if (this.applying()) return this.applySignal()?.message ?? 'Rangement…';
    if (this.scanning()) return this.jobSignal()?.message ?? this.session()?.message ?? 'Analyse en cours…';
    return this.message() || this.plan()?.statusText || '';
  });

  private readonly rowByIndex = computed(() => new Map((this.plan()?.rows ?? []).map(r => [r.index, r])));

  /** Case effective d'un fichier : celle de l'utilisateur, sinon la sélection par défaut du plan. */
  readonly checkedSet = computed(() => {
    const set = new Set<number>();
    const rows = this.rowByIndex();
    const touched = this.touched();
    for (const e of this.entries()) {
      const t = touched.get(e.index);
      if (t ?? rows.get(e.index)?.defaultChecked ?? true) set.add(e.index);
    }
    return set;
  });

  /** Arbre complet (dossiers et fichiers dans l'ordre du parcours), avec les fichiers descendants de chaque dossier. */
  private readonly tree = computed<Node[]>(() => {
    const entries = this.entries();
    const session = this.session();
    const rows = this.rowByIndex();
    const albums = new Map((this.plan()?.albumFolders ?? []).map(a => [a.folder, a.label]));
    const nodes: Node[] = [];
    const dirs = new Map<string, DirNode>();
    const ensure = (key: string): DirNode => {
      let d = dirs.get(key);
      if (d) return d;
      const parts = key ? key.split('/') : [];
      if (key) ensure(parts.slice(0, -1).join('/'));
      d = { kind: 'dir', key, name: key ? parts[parts.length - 1] : (session?.root ?? ''), depth: parts.length, files: [], note: albums.get(key) ?? null };
      dirs.set(key, d);
      nodes.push(d);
      return d;
    };
    ensure('');
    for (const entry of entries) {
      const parts = entry.folder ? entry.folder.split('/') : [];
      ensure(entry.folder);
      for (let i = 0; i <= parts.length; i++) dirs.get(parts.slice(0, i).join('/'))!.files.push(entry.index);
      nodes.push({ kind: 'file', entry, depth: parts.length + 1, row: rows.get(entry.index) ?? null });
    }
    return nodes;
  });

  /** Lignes visibles : dossiers repliés et filtre « Masquer ce qui est déjà en place » appliqués. */
  readonly visible = computed<Node[]>(() => {
    const collapsed = this.collapsed();
    const hide = this.hideInPlace() && !!this.plan();
    const tree = this.tree();
    const shown = (n: FileNode) => !hide || !(n.row?.status === 'inplace' || n.row?.status === 'album');
    const visibleFiles = new Set<number>();
    for (const n of tree) if (n.kind === 'file' && shown(n)) visibleFiles.add(n.entry.index);
    const out: Node[] = [];
    let skipDepth = -1;
    for (const n of tree) {
      if (skipDepth >= 0) {
        if (n.depth > skipDepth) continue;
        skipDepth = -1;
      }
      if (n.kind === 'dir') {
        if (hide && n.key !== '' && !n.files.some(i => visibleFiles.has(i))) {
          skipDepth = n.depth;
          continue;
        }
        out.push(n);
        if (collapsed.has(n.key)) skipDepth = n.depth;
      } else if (shown(n)) out.push(n);
    }
    return out;
  });

  readonly canApply = computed(() => !this.busy() && !!this.plan()?.canApply);
  readonly lookupIndices = computed(() => {
    const checked = this.checkedSet();
    return (this.plan()?.rows ?? []).filter(r => r.needsLookup && checked.has(r.index)).map(r => r.index);
  });
  readonly filtersSummary = computed(() => this.plan()?.filtersSummary ?? '');

  private analysisSub: Subscription | null = null;
  private replanTimer: ReturnType<typeof setTimeout> | null = null;
  private previewTimer: ReturnType<typeof setTimeout> | null = null;
  private lastJobStatus = '';
  private lastApplyStatus = '';

  constructor() {
    void this.init();
    this.connectivity.onReconnect(() => void this.init());

    // L'analyse s'est terminée (ou arrêtée) : la liste finale fait foi, le plan se calcule.
    effect(() => {
      const j = this.jobSignal();
      const status = j?.status ?? '';
      if (status === this.lastJobStatus) return;
      this.lastJobStatus = status;
      if (status === 'SUCCEEDED' || status === 'CANCELLED' || status === 'FAILED') untracked(() => void this.finishAnalysis());
    });
    effect(() => {
      const j = this.applySignal();
      const status = j?.status ?? '';
      if (status === this.lastApplyStatus) return;
      this.lastApplyStatus = status;
      if (j && (status === 'SUCCEEDED' || status === 'FAILED' || status === 'CANCELLED')) untracked(() => this.finishApply(j));
    });
    this.destroyRef.onDestroy(() => {
      this.analysisSub?.unsubscribe();
      if (this.replanTimer) clearTimeout(this.replanTimer);
      if (this.previewTimer) clearTimeout(this.previewTimer);
    });
  }

  // ------------------------------------------------------------------ chargement

  private async init(): Promise<void> {
    try {
      const [state, tags] = await Promise.all([
        this.gql.query<{ organizeState: OrganizeState }>(STATE),
        this.gql.query<{ organizeNameTags: NameTags }>(NAME_TAGS),
      ]);
      this.nameTags.set(tags.organizeNameTags);
      const s = state.organizeState;
      this.defaultRoot.set(s.defaultRoot);
      // Sur téléphone le volet Filtres, ouvert, repousse la liste hors de l'écran : il s'ouvre à la demande
      // (l'affichage seul est replié, le réglage enregistré n'est pas touché).
      const compact = typeof window !== 'undefined' && window.innerWidth < 900;
      this.options.set({ ...s.options, filtersOpen: compact ? false : s.options.filtersOpen });
      this.hideInPlace.update(v => v);
      this.session.set(s.session);
      if (s.session) {
        await this.loadEntries(s.session);
        if (s.session.state !== 'running') await this.loadPlanAndChecks();
        else this.listenBatches(s.session);
      }
      this.restoreLookup();
    } catch (e) {
      this.message.set(this.connectivity.state() === 'online' ? `Chargement impossible : ${e instanceof Error ? e.message : e}` : 'Hors ligne : affichage de la dernière analyse connue.');
    }
    this.loaded.set(true);
  }

  private async loadEntries(session: OrganizeSession): Promise<void> {
    const d = await this.gql.query<{ organizeEntries: { items: OrganizeEntry[] } }>(ENTRIES, { sessionId: session.id });
    this.entries.set(d.organizeEntries.items);
  }

  private async loadPlanAndChecks(): Promise<void> {
    const s = this.session();
    if (!s) return;
    const [, checks] = await Promise.all([this.loadPlan(), this.gql.query<{ organizeChecks: { checked: number[]; unchecked: number[] } }>(CHECKS, { sessionId: s.id })]);
    const map = new Map<number, boolean>();
    for (const i of checks.organizeChecks.checked) map.set(i, true);
    for (const i of checks.organizeChecks.unchecked) map.set(i, false);
    this.touched.set(map);
  }

  /** Recalcule le plan à partir des fichiers déjà lus : ni relecture du disque, ni modification des cases. */
  private async loadPlan(): Promise<void> {
    const s = this.session();
    if (!s || this.entries().length === 0) {
      this.plan.set(null);
      return;
    }
    const o = this.options();
    const d = await this.gql.query<{ organizePlan: OrganizePlan }>(PLAN, { sessionId: s.id, options: this.optionsInput(o) });
    this.plan.set(d.organizePlan);
    this.preview.set(d.organizePlan.preview);
    this.message.set('');
  }

  private optionsInput(o: OrganizeOptions) {
    return { ...o, diversMin: Math.min(10, Math.max(2, Math.round(o.diversMin || 3))) };
  }

  private async finishAnalysis(): Promise<void> {
    const s = this.session();
    if (!s) return;
    try {
      const state = await this.gql.query<{ organizeState: OrganizeState }>(STATE);
      const fresh = state.organizeState.session;
      if (!fresh || fresh.id !== s.id) return;
      this.session.set(fresh);
      await this.loadEntries(fresh);
      if (this.entries().length === 0) {
        this.plan.set(null);
        this.message.set('Aucun fichier audio trouvé dans ce dossier.');
        return;
      }
      await this.loadPlanAndChecks();
    } catch {
      // Hors ligne : la reconnexion relance init().
    }
  }

  private listenBatches(session: OrganizeSession): void {
    this.analysisSub?.unsubscribe();
    this.analysisSub = this.jobs.items({ type: 'organize.analyze' }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: d => {
        const item = d?.jobItem;
        if (!item || item.jobId !== session.jobId || !Array.isArray(item.data)) return;
        const batch = item.data as OrganizeEntry[];
        this.entries.update(list => {
          const byIndex = new Map(list.map(e => [e.index, e]));
          for (const e of batch) byIndex.set(e.index, e);
          return [...byIndex.values()].sort((a, b) => a.index - b.index);
        });
      },
      error: () => undefined,
    });
  }

  // ------------------------------------------------------------------ analyse

  async chooseFolder(): Promise<void> {
    if (this.busy()) return;
    const picked = await openFsBrowser(this.dialog, { mode: 'folder', start: this.session()?.root || this.defaultRoot() || undefined, title: 'Choisir le dossier à organiser' });
    const root = picked?.[0];
    if (!root) return;
    if (root === this.session()?.root) return;
    // Dossier modifié : plan entièrement vidé, nouvelle analyse à lancer.
    this.resetView();
    this.session.set({ id: '', root, rootDb: '', jobId: null, state: 'idle', total: 0, read: 0, message: null });
    this.message.set('Dossier modifié : relancez l\'analyse.');
  }

  async analyze(): Promise<void> {
    if (this.busy()) return;
    const root = this.session()?.root || this.defaultRoot();
    if (!root) {
      this.notify.info('Choisissez d\'abord un dossier à analyser.');
      return;
    }
    this.resetView();
    this.result.set(null);
    this.message.set('Analyse en cours…');
    const r = await this.gql.mutate<{ startOrganizeAnalysis: { session: OrganizeSession; job: Job } }>(START, { root }, { label: 'Analyser un dossier', queueable: false });
    if (!r.data) {
      this.message.set('');
      this.notify.error(r.error ?? 'Analyse impossible.');
      return;
    }
    this.jobs.upsert(r.data.startOrganizeAnalysis.job);
    this.session.set(r.data.startOrganizeAnalysis.session);
    this.listenBatches(r.data.startOrganizeAnalysis.session);
  }

  private resetView(): void {
    this.entries.set([]);
    this.plan.set(null);
    this.touched.set(new Map());
    this.collapsed.set(new Set());
    this.allCollapsed.set(false);
    this.preview.set(null);
    this.analysisSub?.unsubscribe();
  }

  stopScan(): void {
    const j = this.jobSignal();
    if (j) void this.jobs.cancel(j);
  }

  // ------------------------------------------------------------------ options

  setOption<K extends keyof OrganizeOptions>(key: K, value: OrganizeOptions[K]): void {
    this.options.update(o => ({ ...o, [key]: value }));
    // L'aperçu suit tout de suite, le plan 350 ms après la dernière modification.
    if (key === 'pattern' || key === 'rename') this.schedulePreview();
    if (key === 'filtersOpen') {
      void this.gql.mutate(SAVE_OPTIONS, { options: this.optionsInput(this.options()) }, { label: 'Enregistrer les options', queueable: true });
      return;
    }
    if (this.replanTimer) clearTimeout(this.replanTimer);
    this.replanTimer = setTimeout(() => void this.commitOptions(), 350);
  }

  private schedulePreview(): void {
    if (this.previewTimer) clearTimeout(this.previewTimer);
    this.previewTimer = setTimeout(async () => {
      try {
        const o = this.options();
        const d = await this.gql.query<{ organizePreview: { text: string; warning: boolean } }>(PREVIEW, { sessionId: this.session()?.id || null, pattern: o.pattern, rename: o.rename });
        this.preview.set(d.organizePreview);
      } catch { /* l'aperçu viendra avec le plan */ }
    }, 120);
  }

  private async commitOptions(): Promise<void> {
    const o = this.optionsInput(this.options());
    await this.gql.mutate(SAVE_OPTIONS, { options: o }, { label: 'Enregistrer les options', queueable: true });
    try {
      await this.loadPlan();
    } catch {
      this.message.set('Plan non recalculé (connexion perdue).');
    }
  }

  insertToken(token: string): void {
    const input = this.patternInput()?.nativeElement;
    const pattern = this.options().pattern;
    if (!input) {
      this.setOption('pattern', pattern + token);
      return;
    }
    const start = input.selectionStart ?? pattern.length;
    const end = input.selectionEnd ?? start;
    this.setOption('pattern', pattern.slice(0, start) + token + pattern.slice(end));
    queueMicrotask(() => {
      input.focus();
      const pos = start + token.length;
      input.setSelectionRange(pos, pos);
    });
  }

  // ------------------------------------------------------------------ cases

  isChecked(index: number): boolean {
    return this.checkedSet().has(index);
  }

  dirState(d: DirNode): 'all' | 'none' | 'some' | 'empty' {
    if (d.files.length === 0) return 'empty';
    const set = this.checkedSet();
    let n = 0;
    for (const i of d.files) if (set.has(i)) n++;
    return n === 0 ? 'none' : n === d.files.length ? 'all' : 'some';
  }

  toggleFile(entry: OrganizeEntry): void {
    void this.setChecks([entry.index], !this.isChecked(entry.index));
  }

  toggleDir(d: DirNode): void {
    // Un dossier partiel se coche ; l'état partiel ne se propage pas.
    void this.setChecks(d.files, this.dirState(d) !== 'all');
  }

  checkAll(on: boolean): void {
    void this.setChecks(this.entries().map(e => e.index), on);
  }

  private async setChecks(indices: number[], value: boolean): Promise<void> {
    const s = this.session();
    if (!s || indices.length === 0) return;
    this.touched.update(m => {
      const next = new Map(m);
      for (const i of indices) next.set(i, value);
      return next;
    });
    await this.gql.mutate(SET_CHECKS, { sessionId: s.id, indices, value }, { label: 'Cocher des fichiers', queueable: false });
  }

  async checkPending(): Promise<void> {
    const s = this.session();
    if (!s) return;
    this.touched.set(new Map());
    await this.gql.mutate(RESET_CHECKS, { sessionId: s.id }, { label: 'Cocher ce qui est à faire', queueable: false });
  }

  // ------------------------------------------------------------------ affichage

  toggleDirOpen(key: string): void {
    this.collapsed.update(s => {
      const next = new Set(s);
      if (!next.delete(key)) next.add(key);
      return next;
    });
  }

  toggleExpandAll(): void {
    if (this.allCollapsed()) {
      this.collapsed.set(new Set());
      this.allCollapsed.set(false);
    } else {
      // Replier laisse la racine dépliée.
      this.collapsed.set(new Set(this.tree().filter((n): n is DirNode => n.kind === 'dir' && n.key !== '').map(n => n.key)));
      this.allCollapsed.set(true);
    }
  }

  actionClass(n: FileNode): string {
    return n.row?.status ?? (this.plan() ? 'none' : '');
  }

  // ------------------------------------------------------------------ écoute

  play(entry: OrganizeEntry): void {
    // File d'attente : fichiers visibles du même dossier, dans l'ordre de l'arbre, en démarrant sur le fichier choisi.
    const siblings = this.visible().filter((n): n is FileNode => n.kind === 'file' && n.entry.folder === entry.folder).map(n => n.entry);
    const toTrack = (e: OrganizeEntry) => ({ id: e.dbPath, title: e.name, artist: e.artist, album: e.album, src: mediaUrl(e.dbPath), context: e.path });
    this.player.play(toTrack(entry), siblings.map(toTrack));
  }

  openMenu(event: MouseEvent, entry: OrganizeEntry): void {
    event.preventDefault();
    this.ctxEntry.set(entry);
    const trigger = this.ctxTrigger();
    const anchor = this.ctxAnchor()?.nativeElement;
    if (anchor) {
      anchor.style.left = `${event.clientX}px`;
      anchor.style.top = `${event.clientY}px`;
    }
    trigger?.openMenu();
  }

  async copyPath(entry: OrganizeEntry | null): Promise<void> {
    if (!entry) return;
    try {
      await navigator.clipboard.writeText(entry.path);
      this.notify.info('Chemin copié.');
    } catch {
      this.notify.info(entry.path);
    }
  }

  // ------------------------------------------------------------------ appliquer

  async apply(): Promise<void> {
    const s = this.session();
    const plan = this.plan();
    if (!s || !plan || this.busy()) return;
    const checked = this.checkedSet();
    const ops = plan.rows.filter(r => (r.status === 'move' || r.status === 'duplicate') && checked.has(r.index));
    if (ops.length === 0) {
      this.notify.info('Cochez au moins un fichier à ranger.');
      return;
    }
    const moves = ops.filter(r => r.status === 'move').length;
    const dups = ops.length - moves;
    let text = `Déplacer ${moves} fichier(s) dans :\n${s.root}`;
    if (dups) text += `\nSupprimer ${dups} doublon(s) du disque (corbeille de la bibliothèque), dont un autre exemplaire est conservé.`;
    if (this.options().removeEmpty) text += '\nLes dossiers devenus vides seront supprimés.';
    if (!(await this.confirm.ask({ title: 'Confirmer le rangement', message: text, confirm: 'Organiser', cancel: 'Non' }))) return;

    this.busyApply.set(true);
    this.result.set(null);
    const r = await this.gql.mutate<{ applyOrganize: Job }>(APPLY, { sessionId: s.id, indices: ops.map(o => o.index), options: this.optionsInput(this.options()) },
      { label: 'Ranger la bibliothèque', queueable: false });
    this.busyApply.set(false);
    if (!r.data) {
      this.notify.error(r.error ?? 'Rangement impossible.');
      return;
    }
    this.jobs.upsert(r.data.applyOrganize);
    this.applyJobId.set(r.data.applyOrganize.id);
  }

  stopApply(): void {
    const j = this.applySignal();
    if (j) void this.jobs.cancel(j);
  }

  private finishApply(job: Job): void {
    const summary = (job.result as { summary?: string } | null)?.summary;
    this.result.set(job.status === 'FAILED' ? `Le rangement a échoué : ${job.error ?? ''}` : (job.status === 'CANCELLED' ? 'Rangement arrêté entre deux fichiers. ' : '') + (summary ?? ''));
    // Les chemins analysés ont changé : plan vidé, aucune relance automatique.
    this.plan.set(null);
    this.entries.set([]);
    this.touched.set(new Map());
    this.message.set('Rangement terminé : relancez l\'analyse pour vérifier.');
    this.applyJobId.set(null);
  }

  // ------------------------------------------------------------------ doublons

  async findDuplicates(): Promise<void> {
    const s = this.session();
    if (!s) {
      this.notify.info('Lancez d\'abord l\'analyse du dossier.');
      return;
    }
    if (this.busy()) {
      this.notify.info('Attendez la fin de l\'analyse avant de chercher les doublons.');
      return;
    }
    this.message.set('Recherche des doublons…');
    const r = await this.gql.mutate<{ findOrganizeDuplicates: Job }>(DUPLICATES, { sessionId: s.id }, { label: 'Rechercher les doublons', queueable: false });
    if (!r.data) {
      this.message.set('');
      this.notify.error(r.error ?? 'Recherche impossible.');
      return;
    }
    const job = await this.waitFor(r.data.findOrganizeDuplicates);
    this.message.set('');
    if (job.status !== 'SUCCEEDED') {
      this.notify.error(job.error ?? 'Recherche des doublons interrompue.');
      return;
    }
    const result = job.result as DuplicatesResult | null;
    if (!result || result.groups.length === 0) {
      this.notify.info('Aucun fichier ne semble être la copie d\'un autre.');
      return;
    }
    const deleted = await openFileDedupDialog(this.dialog, { result, sessionId: s.id });
    if (deleted && deleted.length > 0) {
      // Les fichiers supprimés ne pèsent plus sur le plan (ils comptaient pour leur artiste, donc pour « Divers »).
      await this.loadEntries(s);
      await this.loadPlanAndChecks();
    }
  }

  // ------------------------------------------------------------------ compléter les tags

  async lookupTags(): Promise<void> {
    const s = this.session();
    const indices = this.lookupIndices();
    if (!s || this.busy()) return;
    if (indices.length === 0) {
      await this.confirm.ask({
        title: 'Rien à compléter',
        message: 'Aucun fichier coché ne manque de tag : ils portent tous leur titre, leur artiste et leur album, ainsi que leur artiste d\'album, leur genre, leur année et leur numéro de piste.',
        confirm: 'OK', cancel: 'Fermer',
      });
      return;
    }
    if (indices.length > TAGS_LOOKUP_WARN) {
      const minutes = Math.max(1, Math.round(indices.length * SECONDS_PER_LOOKUP / 60));
      const ok = await this.confirm.ask({
        title: 'Recherche longue',
        message: `${indices.length} fichiers cochés n'ont pas tous leurs tags.\n\nLe catalogue est interrogé fichier par fichier : comptez de l'ordre de ${minutes} minute(s). La recherche s'interrompt à tout moment — les correspondances déjà trouvées restent écrivables, les fichiers non encore interrogés sont laissés tels quels et un passage suivant les reprendra. Rien n'est écrit sans validation.\n\nLancer la recherche ?`,
        confirm: 'Oui', cancel: 'Non',
      });
      if (!ok) return;
    }
    this.lookupBusy.set(true);
    const r = await this.gql.mutate<{ startOrganizeLookup: { job: Job; files: LookupFile[] } }>(LOOKUP, { sessionId: s.id, indices }, { label: 'Compléter les tags', queueable: false });
    this.lookupBusy.set(false);
    if (!r.data) {
      this.notify.error(r.error ?? 'Recherche impossible.');
      return;
    }
    const { job, files } = r.data.startOrganizeLookup;
    this.jobs.upsert(job);
    this.rememberLookup({ job, files, sessionId: s.id });
    await this.review({ job, files, sessionId: s.id });
  }

  async reopenReview(): Promise<void> {
    const r = this.resume();
    if (r) await this.review(r);
  }

  private async review(r: { job: Job; files: LookupFile[]; sessionId: string }): Promise<void> {
    const written = await openTagReviewDialog(this.dialog, { sessionId: r.sessionId, job: r.job, files: r.files });
    if (written === undefined) {
      // Annulée : la recherche est arrêtée, rien n'est écrit.
      this.forgetLookup();
      return;
    }
    this.forgetLookup();
    // Des tags ont changé sur le disque : nouvelle analyse automatique pour que le plan suive.
    if (written > 0) await this.analyze();
  }

  private rememberLookup(r: { job: Job; files: LookupFile[]; sessionId: string }): void {
    this.resume.set(r);
    try { localStorage.setItem(LOOKUP_KEY, JSON.stringify(r)); } catch { /* stockage indisponible */ }
  }

  private forgetLookup(): void {
    this.resume.set(null);
    try { localStorage.removeItem(LOOKUP_KEY); } catch { /* stockage indisponible */ }
  }

  /** Une revue interrompue par un rechargement se rouvre : les propositions sont gardées côté serveur. */
  private restoreLookup(): void {
    try {
      const raw = localStorage.getItem(LOOKUP_KEY);
      if (!raw) return;
      const r = JSON.parse(raw) as { job: Job; files: LookupFile[]; sessionId: string };
      if (r.sessionId === this.session()?.id) this.resume.set(r);
    } catch { /* ignoré */ }
  }

  private async waitFor(job: Job): Promise<Job> {
    this.jobs.upsert(job);
    const current = this.jobs.get(job.id);
    for (;;) {
      const j = current() ?? job;
      if (j.status === 'SUCCEEDED' || j.status === 'FAILED' || j.status === 'CANCELLED') return j;
      await new Promise(res => setTimeout(res, 300));
    }
  }

  trackNode(_: number, n: Node): string {
    return n.kind === 'dir' ? 'd:' + n.key : 'f:' + n.entry.index;
  }

  dismissResult(): void {
    this.result.set(null);
  }
}
