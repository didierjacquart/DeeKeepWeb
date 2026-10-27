import { ChangeDetectionStrategy, Component, Injectable, computed, effect, inject, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { gql } from '@apollo/client';
import { GqlService } from '../../core/graphql/gql.service';
import { Job, JobsService, isActive } from '../../core/jobs/jobs.service';
import { ConnectivityService } from '../../core/offline/connectivity.service';
import { PlayerService, PlayerTrack } from '../../core/player/player.service';
import { NotifyService } from '../../core/ui/notify.service';
import { openFsBrowser } from '../../shared/fs-browser/fs-browser-dialog';
import { ConfirmService, ProgressBar, imageUrl, mediaUrl } from '../../shared/ui';

/** Limite de la correspondance Deezer automatique (REENCODE_AUTOMATCH_LIMIT). */
export const AUTOMATCH_LIMIT = 150;

interface Match { id: number; title: string; artist: string; album: string | null; coverSmall: string | null; coverBig: string | null }

type MatchState = 'pending' | 'found' | 'none' | 'skipped';

export interface ReItem {
  path: string;
  dbPath: string;
  title: string;
  artist: string;
  album: string;
  bitrate: number;
  /** Extension en majuscules (« MP3 »). */
  format: string;
  matchState: MatchState;
  match: Match | null;
  selected: boolean;
  jobId: string | null;
  converted: boolean;
}

interface LocalFileInfo {
  localPath: string; dbPath: string; title: string | null; artist: string | null; album: string | null; bitrate: number | null; ext: string;
}

interface ExportChoices { current: { format: string; quality: string } }

const SCAN = gql`mutation ScanLocalFiles($paths: [String!]!) { scanLocalFiles(paths: $paths) { id } }`;
const MATCH = gql`query BestMatch($artist: String!, $title: String!) { bestMatch(artist: $artist, title: $title) { id title artist album coverSmall coverBig } }`;
const EXPORT = gql`query ExportOptions { exportOptions { current { format quality } } }`;
const CONVERT = gql`mutation EnqueueConversions($items: [ConvertItemInput!]!, $batchId: String) { enqueueConversions(items: $items, batchId: $batchId) { id } }`;

const STORAGE_KEY = 'dk-reencode-v1';

/** Règle « déjà au format cible » (spec 01 §3.9) : le format suffit pour le sans perte, sinon ±5 kbps. */
export function isAtTarget(item: Pick<ReItem, 'format' | 'bitrate'>, format: string, quality: string): boolean {
  const target = format.toUpperCase();
  if (item.format.toUpperCase() !== target) return false;
  if (target === 'FLAC' || target === 'WAV') return true;
  const kbps = quality.includes('128') ? 128 : quality.includes('320') ? 320 : 0;
  if (kbps === 0) return true;
  return Math.abs((item.bitrate || 0) - kbps) <= 5;
}

/**
 * État de la page Réencoder, conservé hors du composant : quitter la page ne perd
 * ni la liste ni le suivi des conversions (qui tournent sur le serveur).
 */
@Injectable({ providedIn: 'root' })
export class ReencodeStore {
  private readonly gql = inject(GqlService);
  private readonly jobs = inject(JobsService);
  private readonly confirm = inject(ConfirmService);

  readonly items = signal<ReItem[]>(this.restore());
  readonly scanJobIds = signal<string[]>([]);
  readonly batchIds = signal<string[]>([]);
  readonly matching = signal(0);
  private readonly buffer = new Map<string, LocalFileInfo[]>();

  /** Texte d'avancement de l'analyse locale (« Analyse en cours… n / total fichier(s) analysé(s) »). */
  readonly scanText = computed(() => {
    const job = this.jobs.all().find(j => this.scanJobIds().includes(j.id) && isActive(j));
    return job ? job.message || 'Analyse en cours…' : null;
  });

  constructor() {
    this.jobs.items({ type: 'files.scan' }).subscribe({
      next: d => {
        const e = d?.jobItem;
        if (!e || e.status !== 'ok') return;
        const list = this.buffer.get(e.jobId) ?? [];
        list.push(e.data as LocalFileInfo);
        this.buffer.set(e.jobId, list);
      },
      error: () => undefined,
    });
    // Ingestion en une fois, à la fin de chaque analyse.
    effect(() => {
      const all = this.jobs.all();
      for (const id of this.scanJobIds()) {
        const job = all.find(j => j.id === id);
        if (job && !isActive(job)) this.finishScan(job);
      }
    });
    // Résultat des conversions : fichier produit (chemin, format, débit) et tags retenus.
    effect(() => {
      const all = new Map(this.jobs.all().map(j => [j.id, j]));
      const done = this.items().filter(i => i.jobId && !i.converted && all.get(i.jobId)?.status === 'SUCCEEDED');
      if (!done.length) return;
      this.items.update(list => list.map(i => {
        const job = i.jobId ? all.get(i.jobId) : null;
        if (i.converted || job?.status !== 'SUCCEEDED') return i;
        const r = (job.result ?? {}) as { localPath?: string; dbPath?: string; format?: string; bitrate?: number; title?: string; artist?: string; album?: string };
        return {
          ...i, converted: true, selected: false, path: r.localPath ?? i.path, dbPath: r.dbPath ?? i.dbPath, format: (r.format ?? i.format).toUpperCase(),
          bitrate: r.bitrate ?? i.bitrate, title: r.title || i.title, artist: r.artist || i.artist, album: r.album ?? i.album, matchState: 'found',
        };
      }));
    });
    effect(() => this.persist(this.items()));
    // Filet : si l'événement de fin d'une analyse a été manqué, on relit les tâches.
    setInterval(() => { if (this.scanJobIds().length) void this.jobs.load(); }, 4000);
  }

  jobOf(item: ReItem): Job | null {
    return item.jobId ? this.jobs.all().find(j => j.id === item.jobId) ?? null : null;
  }

  async scan(paths: string[]): Promise<void> {
    const r = await this.gql.mutate<{ scanLocalFiles: { id: string } }>(SCAN, { paths }, { label: 'Lire les fichiers à réencoder', queueable: false });
    const id = r.data?.scanLocalFiles?.id;
    if (!id) throw new Error(r.error ?? 'Analyse impossible');
    this.scanJobIds.update(l => [...l, id]);
    void this.jobs.load();
  }

  private finishScan(job: Job): void {
    this.scanJobIds.update(l => l.filter(x => x !== job.id));
    const found = this.buffer.get(job.id) ?? [];
    this.buffer.delete(job.id);
    if (!found.length) {
      if (job.status === 'SUCCEEDED') void this.info('Aucun ajout', 'Aucun fichier audio trouvé.');
      return;
    }
    const known = new Set(this.items().map(i => i.path.toLowerCase()));
    const fresh: ReItem[] = [];
    for (const f of found) {
      if (known.has(f.localPath.toLowerCase())) continue;
      known.add(f.localPath.toLowerCase());
      fresh.push({
        path: f.localPath, dbPath: f.dbPath, title: f.title ?? '', artist: f.artist ?? '', album: f.album ?? '', bitrate: f.bitrate ?? 0,
        format: (f.ext || '').replace('.', '').toUpperCase(), matchState: 'pending', match: null, selected: false, jobId: null, converted: false,
      });
    }
    if (!fresh.length) { void this.info('Aucun ajout', 'Tous les fichiers trouvés sont déjà présents dans la liste.'); return; }
    const skip = fresh.length > AUTOMATCH_LIMIT;
    this.items.update(l => [...l, ...fresh.map(i => skip ? { ...i, matchState: 'skipped' as const, selected: true } : i)]);
    if (skip) {
      void this.info('Bibliothèque volumineuse',
        `${fresh.length} fichiers ajoutés. La recherche automatique de correspondances Deezer (jaquettes) a été désactivée pour préserver la fluidité ; ` +
        'les fichiers sont présélectionnés et seront réencodés à partir de leurs métadonnées locales.\n\n' +
        'Astuce : utilisez « Masquer ceux déjà au format cible » pour ne convertir que le nécessaire.');
    } else {
      void this.matchAll(fresh.map(i => i.path));
    }
  }

  /** Correspondance Deezer séquentielle (le serveur limite déjà le débit vers Deezer). */
  private async matchAll(paths: string[]): Promise<void> {
    this.matching.update(n => n + paths.length);
    for (const path of paths) {
      const item = this.items().find(i => i.path === path);
      let match: Match | null = null;
      if (item && (item.artist || item.title)) {
        try {
          const data = await this.gql.query<{ bestMatch: Match | null }>(MATCH, { artist: item.artist, title: item.title }, true);
          match = data.bestMatch ?? null;
        } catch {
          match = null;
        }
      }
      this.items.update(l => l.map(i => i.path === path && i.matchState === 'pending'
        ? { ...i, match, matchState: match ? 'found' : 'none', selected: match ? true : i.selected } : i));
      this.matching.update(n => n - 1);
    }
  }

  info(title: string, message: string): Promise<boolean> {
    return this.confirm.ask({ title, message, confirm: 'OK', cancel: 'Fermer' });
  }

  async convert(items: ReItem[]): Promise<void> {
    const batchId = crypto.randomUUID().replaceAll('-', '');
    const r = await this.gql.mutate<{ enqueueConversions: { id: string }[] }>(CONVERT, {
      items: items.map(i => ({ localPath: i.path, deezerId: i.match?.id ?? null })), batchId,
    }, { label: `Convertir ${items.length} fichier(s)`, queueable: false });
    const created = r.data?.enqueueConversions;
    if (!created) throw new Error(r.error ?? 'Conversion impossible');
    const byPath = new Map(items.map((i, n) => [i.path, created[n]?.id ?? null]));
    this.batchIds.set(created.map(j => j.id));
    this.items.update(l => l.map(i => byPath.has(i.path) ? { ...i, jobId: byPath.get(i.path) ?? null, converted: false } : i));
    void this.jobs.load();
  }

  private persist(items: ReItem[]): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(items.slice(0, 20000)));
    } catch {
      // Stockage plein ou refusé : la liste reste en mémoire.
    }
  }

  private restore(): ReItem[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? (JSON.parse(raw) as ReItem[]).map(i => ({ ...i, matchState: i.matchState === 'pending' ? 'none' : i.matchState })) : [];
    } catch {
      return [];
    }
  }
}

type SortKey = 'title' | 'artist' | 'path' | 'format' | 'match';

/** Page Réencoder : convertit des fichiers du serveur au format des Paramètres, en les remplaçant sur place. */
@Component({
  selector: 'dk-reencoder-page',
  imports: [MatTooltipModule, ProgressBar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="dk-page">
      <h1 class="dk-page-title">Réencoder des fichiers</h1>
      <p class="dk-help sub">Ajoutez des fichiers MP3 : une correspondance Deezer est recherchée automatiquement, puis « Convertir » réextrait chaque titre au format des paramètres et remplace le fichier dans son dossier d'origine.</p>

      <div class="dk-actions">
        <button class="control-btn" (click)="selectAll(true)">Tout cocher</button>
        <button class="control-btn" (click)="selectAll(false)">Tout décocher</button>
        <button class="control-btn" (click)="removeSelection()" matTooltip="Retirer de la liste les fichiers cochés (les fichiers du disque sont conservés)">Retirer la sélection</button>
        <span class="dk-spacer"></span>
        <button class="control-btn" (click)="purge()" matTooltip="Retirer de la liste les fichiers réencodés avec succès">Purger les convertis</button>
        <button class="control-btn" [disabled]="scanning()" (click)="add('folder')" matTooltip="Ajouter tout le contenu audio d'un dossier et de ses sous-dossiers">Ajouter un dossier</button>
        <button class="accent-btn" [disabled]="scanning()" (click)="add('files')" matTooltip="Parcourir et ajouter des fichiers audio">Ajouter des fichiers</button>
        <button class="primary-btn" [disabled]="busy()" (click)="convert()" matTooltip="Réextraire et réencoder les fichiers sélectionnés, puis les remplacer dans leur dossier d'origine">Convertir</button>
      </div>

      @if (scanning() || store.matching() > 0) { <dk-progress [value]="null" /> }
      @if (busy() || batchTotal() > 0) {
        <div class="global">
          <span class="dk-help">Progression globale de la conversion :</span>
          <dk-progress [value]="batchProgress()" [large]="true" />
        </div>
      }

      <div class="dk-filters">
        <button class="control-btn" [class.checked]="hideTarget()" (click)="hideTarget.set(!hideTarget())"
          matTooltip="Masquer les fichiers déjà dans le format et la qualité définis dans les Paramètres (rien à réencoder)">
          {{ hideTarget() ? 'Afficher tous les fichiers' : 'Masquer ceux déjà au format cible' }}
        </button>
        <span class="dk-help">{{ visible().length }} fichier(s) affiché(s) sur {{ store.items().length }} · cible : {{ target().format }} · {{ target().quality }}</span>
      </div>

      <div class="dk-table">
        <div class="dk-thead grid">
          <span></span>
          <span>Écoute</span>
          <span class="dk-hide-mobile">Jaquette</span>
          <span [class.sorted]="sortKey() === 'title'" (click)="sort('title')">Titre{{ arrow('title') }}</span>
          <span class="dk-hide-mobile" [class.sorted]="sortKey() === 'artist'" (click)="sort('artist')">Artiste{{ arrow('artist') }}</span>
          <span class="dk-hide-mobile" [class.sorted]="sortKey() === 'path'" (click)="sort('path')">Fichier{{ arrow('path') }}</span>
          <span class="dk-hide-mobile" [class.sorted]="sortKey() === 'format'" (click)="sort('format')">Format / débit{{ arrow('format') }}</span>
          <span class="dk-hide-mobile" [class.sorted]="sortKey() === 'match'" (click)="sort('match')">Correspondance{{ arrow('match') }}</span>
          <span class="dk-hide-mobile">Statut</span>
        </div>
        @for (i of visible(); track i.path) {
          <div class="dk-row grid" [class.selected]="i.selected" (dblclick)="listen(i)">
            <span><input type="checkbox" class="dk-check" [checked]="i.selected" (change)="toggle(i)" [attr.aria-label]="'Cocher ' + (i.title || i.path)" /></span>
            <span><button class="icon-btn" (click)="listen(i)" matTooltip="Écouter ce fichier dans le lecteur" aria-label="Écouter">▶</button></span>
            <span class="dk-hide-mobile">
              @if (cover(i); as src) { <img class="dk-cover" [src]="src" alt="" loading="lazy" /> } @else { <span class="dk-cover">🎵</span> }
            </span>
            <span class="main">
              <span class="t">{{ i.title || '—' }}</span>
              <span class="dk-cell-sub dk-hide-desktop">{{ i.artist }} · {{ formatText(i) }}</span>
              <span class="dk-cell-sub dk-hide-desktop">{{ statusOf(i).text }}</span>
            </span>
            <span class="dk-hide-mobile">{{ i.artist }}</span>
            <span class="dk-hide-mobile path" [matTooltip]="i.path">{{ i.path }}</span>
            <span class="dk-hide-mobile">{{ formatText(i) }}</span>
            <span class="dk-hide-mobile" [class]="matchOf(i).cls" [matTooltip]="matchOf(i).tip">{{ matchOf(i).text }}</span>
            <span class="dk-hide-mobile" [class]="statusOf(i).cls" [matTooltip]="statusOf(i).tip">{{ statusOf(i).text }}</span>
          </div>
        } @empty {
          <div class="dk-empty">
            @if (scanning()) { {{ store.scanText() }} }
            @else if (store.items().length) { Tous les fichiers sont déjà au format cible. }
            @else { Aucun fichier. Ajoutez des fichiers ou un dossier entier (sous-dossiers inclus) pour commencer. }
          </div>
        }
      </div>
      @if (scanning() && visible().length) { <p class="dk-help">{{ store.scanText() }}</p> }
    </section>
  `,
  styles: `
    :host { display: block; }
    .sub { margin: 0 0 18px; }
    .dk-actions, .global, .dk-filters { margin-bottom: 14px; }
    .global { display: grid; gap: 6px; }
    .dk-filters { align-items: center; }
    .control-btn.checked { background: rgba(0, 229, 255, .12); border-color: rgba(0, 229, 255, .45); color: var(--dk-primary); }
    .grid { grid-template-columns: 34px 52px 56px minmax(150px, 1.4fr) minmax(90px, .9fr) minmax(160px, 1.6fr) 130px 120px minmax(120px, 1fr); }
    .main { display: flex; flex-direction: column; min-width: 0; }
    .path { color: var(--dk-faint); }
    .muted { color: var(--dk-faint); } .info { color: var(--dk-primary); } .ok { color: var(--dk-success); } .err { color: var(--dk-error); }
    @media (max-width: 899px) {
      .grid { grid-template-columns: 34px 1fr 44px; }
      .dk-thead.grid > :nth-child(2) { display: none; }
      .dk-row.grid { min-height: 64px; }
      .dk-row.grid > :nth-child(2) { order: 3; }
      .icon-btn { width: 40px; height: 40px; }
      input.dk-check { width: 22px; height: 22px; }
    }
  `,
})
export class ReencodePage {
  readonly store = inject(ReencodeStore);
  private readonly gql = inject(GqlService);
  private readonly jobs = inject(JobsService);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly player = inject(PlayerService);
  private readonly connectivity = inject(ConnectivityService);

  readonly target = signal({ format: 'MP3', quality: '320 kbps (HQ)' });
  readonly hideTarget = signal(false);
  readonly sortKey = signal<SortKey | null>(null);
  private readonly sortAsc = signal(true);

  readonly scanning = computed(() => this.store.scanText() !== null);

  readonly visible = computed(() => {
    const t = this.target();
    let list = this.store.items();
    if (this.hideTarget()) list = list.filter(i => !isAtTarget(i, t.format, t.quality));
    const key = this.sortKey();
    if (!key) return list;
    const dir = this.sortAsc() ? 1 : -1;
    const v = (i: ReItem): string | number => {
      switch (key) {
        case 'title': return i.title.toLowerCase();
        case 'artist': return i.artist.toLowerCase();
        case 'path': return i.path.toLowerCase();
        case 'format': return `${i.format}|${String(i.bitrate).padStart(5, '0')}`;
        default: return i.matchState === 'found' ? 1 : 0;
      }
    };
    return [...list].sort((a, b) => (v(a) < v(b) ? -1 : v(a) > v(b) ? 1 : 0) * dir);
  });

  private readonly batchJobs = computed(() => {
    const ids = new Set(this.store.batchIds());
    return this.jobs.all().filter(j => ids.has(j.id));
  });
  readonly busy = computed(() => this.batchJobs().some(isActive));
  readonly batchTotal = computed(() => this.batchJobs().length);
  readonly batchProgress = computed(() => {
    const jobs = this.batchJobs();
    if (!jobs.length) return 0;
    return Math.floor(jobs.reduce((s, j) => s + (j.status === 'SUCCEEDED' ? 100 : j.status === 'RUNNING' ? j.progress * 100 : 0), 0) / jobs.length) / 100;
  });

  constructor() {
    void this.loadTarget();
    effect(() => {
      this.connectivity.resyncTick();
      void this.loadTarget();
    });
  }

  private async loadTarget(): Promise<void> {
    try {
      const d = await this.gql.query<{ exportOptions: ExportChoices }>(EXPORT);
      if (d.exportOptions?.current) this.target.set(d.exportOptions.current);
    } catch {
      // Hors ligne : valeurs par défaut.
    }
  }

  arrow(k: SortKey): string { return this.sortKey() === k ? (this.sortAsc() ? ' ▲' : ' ▼') : ''; }

  sort(k: SortKey): void {
    if (this.busy()) return;
    if (this.sortKey() === k) this.sortAsc.update(a => !a);
    else { this.sortKey.set(k); this.sortAsc.set(true); }
  }

  formatText(i: ReItem): string {
    if (!i.format) return '?';
    return i.bitrate > 0 ? `${i.format} · ${i.bitrate} kbps` : i.format;
  }

  cover(i: ReItem): string | null { return imageUrl(i.match?.coverSmall ?? i.match?.coverBig); }

  matchOf(i: ReItem): { text: string; cls: string; tip: string } {
    switch (i.matchState) {
      case 'found': return { text: '✓ Trouvé', cls: 'ok', tip: i.match ? `${i.match.artist} - ${i.match.title}` : '' };
      case 'skipped': return { text: '—', cls: 'muted', tip: 'Correspondance non recherchée (trop de fichiers)' };
      case 'none': return { text: '✗ Introuvable', cls: 'err', tip: '' };
      default: return { text: '⏳', cls: 'muted', tip: '' };
    }
  }

  statusOf(i: ReItem): { text: string; cls: string; tip: string } {
    const job = this.store.jobOf(i);
    if (i.converted || job?.status === 'SUCCEEDED') return { text: 'Réencodé ✓', cls: 'ok', tip: '' };
    switch (job?.status) {
      case 'QUEUED': return { text: 'En attente...', cls: 'muted', tip: '' };
      case 'RUNNING': return { text: `${job.message || 'Initialisation...'} — ${Math.round(job.progress * 100)}%`, cls: 'info', tip: '' };
      case 'FAILED': return { text: 'Erreur', cls: 'err', tip: job.error ?? '' };
      case 'CANCELLED': return { text: 'Annulé', cls: 'err', tip: '' };
      default: return { text: 'En attente', cls: 'muted', tip: '' };
    }
  }

  toggle(i: ReItem): void {
    this.store.items.update(l => l.map(x => x.path === i.path ? { ...x, selected: !x.selected } : x));
  }

  /** « Tout cocher / décocher » : seulement les lignes visibles. */
  selectAll(on: boolean): void {
    const shown = new Set(this.visible().map(i => i.path));
    this.store.items.update(l => l.map(x => shown.has(x.path) ? { ...x, selected: on } : x));
  }

  async add(mode: 'files' | 'folder'): Promise<void> {
    if (this.busy()) { await this.store.info('Conversion en cours', 'Attendez la fin des conversions avant d\'ajouter des fichiers.'); return; }
    if (this.scanning()) { await this.store.info('Analyse en cours', 'Une analyse de fichiers est déjà en cours.'); return; }
    const paths = await openFsBrowser(this.dialog, { mode, audioOnly: true });
    if (!paths?.length) return;
    try {
      await this.store.scan(paths);
    } catch (e) {
      this.notify.error(e instanceof Error ? e.message : String(e));
    }
  }

  async removeSelection(): Promise<void> {
    if (this.busy()) { await this.store.info('Conversion en cours', 'Attendez la fin des conversions avant de retirer des fichiers.'); return; }
    this.store.items.update(l => l.filter(i => !i.selected));
  }

  async purge(): Promise<void> {
    if (this.busy()) { await this.store.info('Conversion en cours', 'Attendez la fin des conversions avant de purger.'); return; }
    const n = this.store.items().filter(i => i.converted).length;
    this.store.items.update(l => l.filter(i => !i.converted));
    this.notify.info(n ? `${n} fichier(s) converti(s) retiré(s) de la liste.` : 'Aucun fichier converti à purger.');
  }

  async convert(): Promise<void> {
    if (this.busy()) { await this.store.info('Conversion en cours', 'Des conversions sont déjà en cours.'); return; }
    const chosen = this.visible().filter(i => i.selected);
    if (!chosen.length) { await this.store.info('Aucune sélection', 'Sélectionnez au moins un fichier à réencoder (visible).'); return; }
    try {
      await this.store.convert(chosen);
    } catch (e) {
      this.notify.error(e instanceof Error ? e.message : String(e));
    }
  }

  listen(i: ReItem): void {
    const mk = (x: ReItem): PlayerTrack => ({
      id: x.dbPath, title: x.title || x.path, artist: x.artist, album: x.album, src: mediaUrl(x.dbPath), cover: this.cover(x),
      context: `${this.formatText(x)}  ·  ${x.path}`,
    });
    this.player.play(mk(i), this.visible().map(mk));
  }
}
