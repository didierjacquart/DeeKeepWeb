import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { gql } from '@apollo/client';
import { GqlService } from '../../core/graphql/gql.service';
import { Job, JobsService } from '../../core/jobs/jobs.service';
import { ConnectivityService } from '../../core/offline/connectivity.service';
import { PlayerService, PlayerTrack } from '../../core/player/player.service';
import { NotifyService } from '../../core/ui/notify.service';
import { ConfirmService, PresenceBadge, ProgressBar, Presence, imageUrl, mediaUrl, previewUrl } from '../../shared/ui';
import { formatDuration } from '../../shared/pipes';
import { openSearchDialog } from './search-dialog/search-dialog';

/** Piste Deezer d'une ligne (charge utile de la tâche « download » ou ligne en attente). */
interface RowTrack {
  id: number;
  title: string;
  artist: string;
  album: string | null;
  duration: number;
  coverSmall: string | null;
  coverBig: string | null;
  previewUrl: string | null;
  artistId?: number | null;
  albumId?: number | null;
  link?: string | null;
  trackPosition?: number | null;
  diskNumber?: number | null;
}

interface JobWithPayload extends Omit<Job, 'result'> {
  payload: { track?: RowTrack } | null;
  result: { localPath?: string; dbPath?: string; attempts?: number; videoTitle?: string } | null;
}

interface PendingRow {
  id: string;
  track: RowTrack;
  addedAt: number;
}

interface Row {
  id: string;
  kind: 'pending' | 'job';
  job: JobWithPayload | null;
  track: RowTrack;
  status: string;
  color: 'muted' | 'info' | 'ok' | 'err';
  tooltip: string | null;
  progress: number;
  busy: boolean;
  canCancel: boolean;
  failed: boolean;
  succeeded: boolean;
  dbPath: string | null;
  seq: number;
}

type SortKey = 'id' | 'title' | 'artist' | 'album' | 'duration' | 'status';

const TRACK_FIELDS = 'id title artist artistId album albumId duration previewUrl coverSmall coverBig link trackPosition diskNumber';
const JOB_FIELDS = `id service type title status progress done failed total message error attempts groupKey payload result createdAt updatedAt startedAt finishedAt`;

const JOBS_Q = gql`query DownloadJobs { downloadJobs(type: "download") { ${JOB_FIELDS} } }`;
const PENDING_Q = gql`query PendingDownloads { pendingDownloads { id addedAt track { ${TRACK_FIELDS} } } }`;
const PRESENCE_Q = gql`query Presence($items: [PresenceQueryInput!]!) { presence(items: $items) { key state reason files { path reason } } }`;
const ADD_PENDING = gql`mutation AddPending($tracks: [DownloadTrackInput!]!) { addPendingDownloads(tracks: $tracks) { id } }`;
const REMOVE_PENDING = gql`mutation RemovePending($ids: [UUID!]!) { removePendingDownloads(ids: $ids) }`;
const ENQUEUE = gql`mutation Enqueue($tracks: [DownloadTrackInput!]!, $batchId: String, $pendingIds: [UUID!]) {
  enqueueDownloads(tracks: $tracks, batchId: $batchId, pendingIds: $pendingIds) { id } }`;
const RETRY = gql`mutation RetryDownloads($ids: [UUID!]!) { retryDownloadJobs(ids: $ids) { id } }`;
const REMOVE_JOBS = gql`mutation RemoveJobs($ids: [UUID!]!) { removeDownloadJobs(ids: $ids) }`;
const PURGE = gql`mutation Purge { purgeDownloads(type: "download", succeededOnly: true) }`;
const QUEUE_CHANGED = gql`subscription DownloadQueueChanged { downloadQueueChanged { removedJobIds pendingChanged } }`;

const STATUS_RANK: Record<string, number> = { 'En attente': 0, "En file d'attente": 1 };

/**
 * Page Musique : la file d'extraction. Chaque ligne « En attente » est conservée
 * sur le serveur ; « Extraire » en fait une tâche « download » (durable, reprise
 * après redémarrage, suivie ici par abonnement). Fermer la page ou le navigateur
 * n'arrête rien.
 */
@Component({
  selector: 'dk-musique-page',
  imports: [MatTooltipModule, ProgressBar, PresenceBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="dk-page">
      <span class="dk-overline">Statut actuel</span>
      <div class="stats">
        <span class="num">{{ doneCount() }}</span> <span class="lbl">fichiers téléchargés</span>
        <span class="sep">|</span>
        <span class="num">{{ failedCount() }}</span> <span class="lbl">fichiers abandonnés</span>
      </div>
      <div class="global">
        <span class="dk-help">Progression globale du traitement :</span>
        <dk-progress [value]="globalProgress()" [large]="true" />
      </div>

      <div class="dk-actions">
        <button class="control-btn" (click)="selectAll(true)">Tout cocher</button>
        <button class="control-btn" (click)="selectAll(false)">Tout décocher</button>
        <button class="control-btn" [disabled]="!selected().size" (click)="removeSelection()"
          matTooltip="Retirer de la liste les lignes cochées (les fichiers déjà extraits sont conservés sur le disque)">Retirer la sélection</button>
        <span class="dk-spacer"></span>
        <button class="control-btn" (click)="purge()" matTooltip="Retirer de la liste toutes les lignes téléchargées avec succès">Purger les téléchargés</button>
        <button class="control-btn" [disabled]="!failedRows().length" (click)="retry()" matTooltip="Relancer les téléchargements en échec">Réessayer</button>
        <button class="accent-btn" (click)="search()" matTooltip="Chercher des titres à ajouter à la liste">Rechercher</button>
        <button class="primary-btn" [disabled]="!eligible().length" (click)="extract()"
          [matTooltip]="selected().size ? 'Télécharger les titres cochés' : 'Télécharger tous les titres de la liste'">Extraire</button>
      </div>

      @if (error()) { <p class="dk-error-text">{{ error() }}</p> }
      @if (stale()) { <p class="dk-help">Hors ligne : dernier état connu de la liste.</p> }

      <div class="dk-table">
        <div class="dk-thead grid">
          <span></span>
          <span class="dk-hide-mobile">Jaquette</span>
          <span class="dk-hide-mobile" [class.sorted]="sortKey() === 'id'" (click)="sort('id')">ID{{ arrow('id') }}</span>
          <span [class.sorted]="sortKey() === 'title'" (click)="sort('title')">Titre{{ arrow('title') }}</span>
          <span class="dk-hide-mobile" [class.sorted]="sortKey() === 'artist'" (click)="sort('artist')">Artiste{{ arrow('artist') }}</span>
          <span class="dk-hide-mobile" [class.sorted]="sortKey() === 'album'" (click)="sort('album')">Album{{ arrow('album') }}</span>
          <span class="dk-hide-mobile" [class.sorted]="sortKey() === 'duration'" (click)="sort('duration')">Durée / progression{{ arrow('duration') }}</span>
          <span class="dk-hide-mobile" [class.sorted]="sortKey() === 'status'" (click)="sort('status')">Statut{{ arrow('status') }}</span>
          <span>Écoute</span>
        </div>
        @for (r of sorted(); track r.id) {
          <div class="dk-row grid" [class.selected]="selected().has(r.id)" (dblclick)="listen(r)">
            <span class="check">
              <dk-presence [state]="presence(r)" [tooltip]="presenceTip(r)" />
              <input type="checkbox" class="dk-check" [checked]="selected().has(r.id)" (change)="toggle(r.id)" [attr.aria-label]="'Cocher ' + r.track.title" />
            </span>
            <span class="dk-hide-mobile">
              @if (cover(r); as src) { <img class="dk-cover" [src]="src" alt="" loading="lazy" /> } @else { <span class="dk-cover">🎵</span> }
            </span>
            <span class="dk-hide-mobile dk-faint">{{ r.track.id }}</span>
            <span class="main">
              <span class="t">{{ r.track.title }}</span>
              <span class="dk-cell-sub">{{ r.track.artist }}@if (r.track.album) { · {{ r.track.album }} }</span>
              <span class="dk-hide-desktop mstatus" [class]="r.color" [matTooltip]="r.tooltip ?? ''">{{ r.status }}</span>
            </span>
            <span class="dk-hide-mobile">{{ r.track.artist }}</span>
            <span class="dk-hide-mobile">{{ r.track.album }}</span>
            <span class="prog">
              <dk-progress [value]="r.progress / 100" [label]="progressLabel(r)" />
              @if (r.canCancel) { <button class="x" (click)="cancel(r)" matTooltip="Annuler ce téléchargement" aria-label="Annuler">X</button> }
            </span>
            <span class="dk-hide-mobile status" [class]="r.color" [matTooltip]="r.tooltip ?? ''">{{ r.status }}</span>
            <span>
              <button class="icon-btn" [disabled]="!canListen(r)" [class.active]="isPlaying(r)" (click)="listen(r, true)" [matTooltip]="listenTip(r)"
                [attr.aria-label]="listenTip(r)">{{ isPlaying(r) ? '■' : '▶' }}</button>
            </span>
          </div>
        } @empty {
          <div class="dk-empty">Aucun titre. « Rechercher » permet d'en ajouter à la liste.</div>
        }
      </div>
    </section>
  `,
  styles: `
    :host { display: block; }
    .stats { display: flex; align-items: baseline; gap: 8px; flex-wrap: wrap; margin: 6px 0 14px; }
    .num { font: 800 26px var(--dk-font-title); color: var(--dk-text); }
    .lbl { font-size: 15px; color: var(--dk-muted); }
    .sep { color: var(--dk-separator); }
    .global { display: grid; gap: 6px; margin-bottom: 18px; }
    .dk-actions { margin-bottom: 18px; }
    .grid { grid-template-columns: 78px 56px 70px minmax(130px, 2fr) minmax(80px, 1fr) minmax(80px, 1fr) minmax(130px, 1.4fr) minmax(120px, 1fr) 68px; }
    .check { display: flex; align-items: center; gap: 8px; }
    .main { display: flex; flex-direction: column; min-width: 0; }
    .t { overflow: hidden; text-overflow: ellipsis; }
    .prog { display: flex; align-items: center; gap: 6px; dk-progress { flex: 1; min-width: 0; } }
    .x { width: 20px; height: 20px; border-radius: 50%; border: 0; background: var(--dk-error-container); color: var(--dk-on-error-container); font: 700 9px var(--dk-font); cursor: pointer; flex: none; }
    .muted { color: var(--dk-muted); } .info { color: var(--dk-primary); } .ok { color: var(--dk-success); } .err { color: var(--dk-error); }
    .mstatus { font-size: 11px; }
    .status { cursor: help; }
    @media (max-width: 899px) {
      .grid { grid-template-columns: 64px 1fr 44px; row-gap: 6px; }
      .dk-thead.grid > :nth-child(3) { display: none; }
      .dk-row.grid { min-height: 64px; padding: 10px; }
      .check { flex-direction: column; gap: 6px; }
      .prog { grid-column: 1 / -1; order: 10; }
      .icon-btn { width: 40px; height: 40px; }
      .x { width: 28px; height: 28px; }
      input.dk-check { width: 22px; height: 22px; }
      .num { font-size: 22px; }
    }
  `,
})
export class MusicPage {
  private readonly gql = inject(GqlService);
  private readonly jobs = inject(JobsService);
  private readonly player = inject(PlayerService);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);
  private readonly connectivity = inject(ConnectivityService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly base = signal<JobWithPayload[]>([]);
  private readonly pending = signal<PendingRow[]>([]);
  private readonly live = this.jobs.byType('download');
  private readonly verdicts = signal(new Map<string, { state: Presence; reason: string | null }>());
  private readonly purged = signal(0);

  readonly error = signal<string | null>(null);
  readonly stale = signal(false);
  readonly selected = signal(new Set<string>());
  readonly sortKey = signal<SortKey | null>(null);
  private readonly sortAsc = signal(true);

  readonly rows = computed<Row[]>(() => {
    const liveMap = new Map(this.live().map(j => [j.id, j]));
    const pending = this.pending().map((p, i) => toRow({ id: p.id, kind: 'pending', job: null, track: p.track, seq: i }));
    const jobs = [...this.base()]
      .sort((a, b) => a.createdAt - b.createdAt)
      .map((b, i) => {
        const l = liveMap.get(b.id);
        const job = (l ? { ...b, ...l, payload: b.payload, result: (l.result as JobWithPayload['result']) ?? b.result } : b) as JobWithPayload;
        return toRow({ id: b.id, kind: 'job', job, track: b.payload?.track ?? emptyTrack(b.title), seq: pending.length + i });
      });
    return [...pending, ...jobs];
  });

  readonly sorted = computed(() => {
    const key = this.sortKey();
    const rows = this.rows();
    if (!key) return rows;
    const dir = this.sortAsc() ? 1 : -1;
    const value = (r: Row): string | number => {
      switch (key) {
        case 'id': return r.track.id || 0;
        case 'duration': return r.track.duration || 0;
        case 'title': return r.track.title.toLowerCase();
        case 'artist': return r.track.artist.toLowerCase();
        case 'album': return (r.track.album ?? '').toLowerCase();
        default: return r.status.toLowerCase();
      }
    };
    return [...rows].sort((a, b) => {
      const x = value(a), y = value(b);
      return (x < y ? -1 : x > y ? 1 : 0) * dir || a.seq - b.seq;
    });
  });

  readonly doneCount = computed(() => this.purged() + this.rows().filter(r => r.succeeded).length);
  readonly failedRows = computed(() => this.rows().filter(r => r.failed));
  readonly failedCount = computed(() => this.failedRows().length);
  readonly globalProgress = computed(() => {
    const rows = this.rows();
    return rows.length ? Math.floor(rows.reduce((s, r) => s + r.progress, 0) / rows.length) / 100 : 0;
  });
  /** Lignes à extraire : en attente, ou en échec / annulées ; seules les cochées si l'on en a coché. */
  readonly eligible = computed(() => {
    const all = this.rows().filter(r => r.kind === 'pending' || r.failed);
    const sel = this.selected();
    const chosen = all.filter(r => sel.has(r.id));
    return chosen.length ? chosen : all;
  });

  private reloading = false;
  private again = false;

  constructor() {
    void this.reload();
    this.gql.subscribe<{ downloadQueueChanged: { removedJobIds: string[]; pendingChanged: boolean } }>(QUEUE_CHANGED)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: d => {
          const removed = d?.downloadQueueChanged?.removedJobIds ?? [];
          if (removed.length) this.jobs.forget(removed);
          void this.reload();
        },
        error: () => undefined,
      });
    // Nouvelle tâche créée ailleurs (autre appareil) : sa charge utile ne vient que de la requête.
    effect(() => {
      const known = new Set(this.base().map(j => j.id));
      if (this.live().some(j => !known.has(j.id))) void this.reload();
    });
    effect(() => {
      this.connectivity.resyncTick();
      void this.reload();
    });
    // Pastilles de présence : recalculées quand l'ensemble des titres ou le nombre de fichiers extraits change.
    effect(() => {
      const keys = this.rows().map(r => `${r.id}|${r.succeeded ? 1 : 0}`).join(',');
      if (keys) void this.refreshPresence();
    });
  }

  async reload(): Promise<void> {
    if (this.reloading) { this.again = true; return; }
    this.reloading = true;
    try {
      do {
        this.again = false;
        const [jobs, pending] = await Promise.all([
          this.gql.query<{ downloadJobs: JobWithPayload[] }>(JOBS_Q),
          this.gql.query<{ pendingDownloads: PendingRow[] }>(PENDING_Q),
        ]);
        this.base.set(jobs.downloadJobs ?? []);
        this.pending.set(pending.pendingDownloads ?? []);
        const ids = new Set([...(jobs.downloadJobs ?? []).map(j => j.id), ...(pending.pendingDownloads ?? []).map(p => p.id)]);
        this.selected.update(s => new Set([...s].filter(id => ids.has(id))));
        this.error.set(null);
        this.stale.set(false);
      } while (this.again);
    } catch (e) {
      this.stale.set(true);
      this.error.set(this.rows().length ? null : e instanceof Error ? e.message : String(e));
    } finally {
      this.reloading = false;
    }
  }

  private async refreshPresence(): Promise<void> {
    const rows = this.rows();
    if (!rows.length) return;
    try {
      const data = await this.gql.query<{ presence: { key: string; state: Presence; reason: string | null }[] }>(PRESENCE_Q, {
        items: rows.map(r => ({ key: r.id, title: r.track.title, artist: r.track.artist, album: r.track.album })),
      });
      this.verdicts.set(new Map((data.presence ?? []).map(v => [v.key, { state: v.state, reason: v.reason }])));
    } catch {
      // Pastilles « ⏳ » : l'information reste facultative.
    }
  }

  presence(r: Row): Presence { return this.verdicts().get(r.id)?.state ?? 'PENDING'; }
  presenceTip(r: Row): string | null { return this.verdicts().get(r.id)?.reason ?? null; }
  cover(r: Row): string | null { return imageUrl(r.track.coverSmall ?? r.track.coverBig); }
  arrow(key: SortKey): string { return this.sortKey() === key ? (this.sortAsc() ? ' ▲' : ' ▼') : ''; }

  progressLabel(r: Row): string {
    const duration = formatDuration(r.track.duration);
    return r.busy || r.succeeded ? `${duration} - ${Math.round(r.progress)}%` : duration;
  }

  sort(key: SortKey): void {
    if (this.sortKey() === key) this.sortAsc.update(a => !a);
    else { this.sortKey.set(key); this.sortAsc.set(true); }
  }

  toggle(id: string): void {
    this.selected.update(s => { const n = new Set(s); if (!n.delete(id)) n.add(id); return n; });
  }

  selectAll(on: boolean): void {
    this.selected.set(on ? new Set(this.rows().map(r => r.id)) : new Set());
  }

  async search(): Promise<void> {
    const result = await openSearchDialog(this.dialog);
    const tracks = (result?.tracks ?? []) as unknown as Record<string, unknown>[];
    if (!tracks.length) return;
    const known = new Set(this.rows().map(r => r.track.id));
    const fresh = tracks.map(toInput).filter(t => t.id && !known.has(t.id));
    const skipped = tracks.length - fresh.length;
    if (fresh.length) {
      const r = await this.gql.mutate(ADD_PENDING, { tracks: fresh }, { label: `Ajouter ${fresh.length} titre(s) à la liste` });
      if (r.error) this.notify.error(r.error);
      else if (r.queued) this.notify.info('Ajout mis en attente : il partira au retour de la connexion.');
      await this.reload();
    }
    if (skipped) this.notify.info(`${skipped} titre(s) déjà dans la liste n'ont pas été ajoutés une seconde fois.`);
  }

  async extract(): Promise<void> {
    const rows = this.eligible();
    const pending = rows.filter(r => r.kind === 'pending');
    const failed = rows.filter(r => r.kind === 'job');
    if (pending.length) {
      const r = await this.gql.mutate(ENQUEUE, {
        tracks: pending.map(p => toInput(p.track as unknown as Record<string, unknown>)),
        batchId: crypto.randomUUID().replaceAll('-', ''),
        pendingIds: pending.map(p => p.id),
      }, { label: `Extraire ${pending.length} titre(s)` });
      if (r.error) this.notify.error(r.error);
    }
    if (failed.length) await this.retryJobs(failed);
    this.selected.set(new Set());
    await this.reload();
  }

  async retry(): Promise<void> {
    await this.retryJobs(this.failedRows());
    await this.reload();
  }

  private async retryJobs(rows: Row[]): Promise<void> {
    if (!rows.length) return;
    const r = await this.gql.mutate(RETRY, { ids: rows.map(x => x.id) }, { label: 'Réessayer les téléchargements en échec' });
    if (r.error) this.notify.error(r.error);
  }

  async cancel(r: Row): Promise<void> {
    if (r.job) await this.jobs.cancel({ service: 'downloader', id: r.id });
  }

  async removeSelection(): Promise<void> {
    const ids = [...this.selected()];
    const rows = this.rows().filter(r => ids.includes(r.id));
    const pend = rows.filter(r => r.kind === 'pending').map(r => r.id);
    const jobIds = rows.filter(r => r.kind === 'job').map(r => r.id);
    if (!rows.length) return;
    if (pend.length) await this.gql.mutate(REMOVE_PENDING, { ids: pend }, { label: 'Retirer des lignes de la liste' });
    if (jobIds.length) await this.gql.mutate(REMOVE_JOBS, { ids: jobIds }, { label: 'Retirer des lignes de la liste' });
    this.jobs.forget(jobIds);
    this.purged.set(0);
    this.selected.set(new Set());
    this.notify.info(`${rows.length} élément(s) retiré(s) de votre liste.`);
    await this.reload();
  }

  async purge(): Promise<void> {
    const done = this.rows().filter(r => r.succeeded).length;
    if (!done) { this.notify.info('Aucun téléchargement réussi à purger.'); return; }
    const r = await this.gql.mutate<{ purgeDownloads: number }>(PURGE, {}, { label: 'Purger les téléchargés' });
    if (r.error) { this.notify.error(r.error); return; }
    this.purged.update(n => n + done);
    this.notify.info(`${r.data?.purgeDownloads ?? done} téléchargement(s) réussi(s) retiré(s) de la liste.`);
    await this.reload();
  }

  // ------------------------------------------------------------ Écoute

  canListen(r: Row): boolean { return !!r.dbPath || !!r.track.previewUrl || !!r.track.id; }

  isPlaying(r: Row): boolean {
    const id = r.dbPath ?? `deezer:${r.track.id}`;
    return r.dbPath ? this.player.current()?.id === id && this.player.playing() : this.player.preview()?.id === id && this.player.previewPlaying();
  }

  listenTip(r: Row): string {
    if (r.dbPath) return 'Écouter le fichier extrait';
    if (r.track.previewUrl || r.track.id) return "Écouter l'extrait de 30 secondes (le titre n'est pas encore extrait)";
    return "Titre pas encore extrait, et Deezer n'en donne aucun extrait";
  }

  /** Fichier extrait → lecteur complet (la liste des fichiers extraits devient la file) ; sinon extrait de 30 s. */
  listen(r: Row, fromButton = false): void {
    if (r.dbPath) {
      const queue = this.sorted().filter(x => x.dbPath).map(x => this.playerTrack(x));
      this.player.play(this.playerTrack(r), queue);
    } else if (r.track.id && (fromButton || r.track.previewUrl)) {
      this.player.togglePreview({
        id: `deezer:${r.track.id}`, title: r.track.title, artist: r.track.artist, album: r.track.album,
        src: previewUrl(r.track.id), cover: imageUrl(r.track.coverSmall), duration: 30,
      });
    }
  }

  private playerTrack(r: Row): PlayerTrack {
    return {
      id: r.dbPath!, title: r.track.title, artist: r.track.artist, album: r.track.album, src: mediaUrl(r.dbPath!),
      cover: imageUrl(r.track.coverSmall), duration: r.track.duration,
    };
  }
}

function emptyTrack(title: string | null): RowTrack {
  return { id: 0, title: title ?? '?', artist: '', album: null, duration: 0, coverSmall: null, coverBig: null, previewUrl: null };
}

/** Statut, couleur et progression d'une ligne (textes et couleurs du spec 01 §2.3.3). */
function toRow(base: Pick<Row, 'id' | 'kind' | 'job' | 'track' | 'seq'>): Row {
  const { job } = base;
  const row: Row = { ...base, status: 'En attente', color: 'muted', tooltip: null, progress: 0, busy: false, canCancel: false, failed: false, succeeded: false, dbPath: null };
  if (!job) return row;
  switch (job.status) {
    case 'QUEUED':
      row.status = job.attempts > 0 && job.error ? `Nouvelle tentative (${job.error})` : "En file d'attente";
      row.canCancel = true;
      break;
    case 'RUNNING':
      row.status = job.message || 'Initialisation...';
      row.color = 'info';
      row.progress = Math.round((job.progress ?? 0) * 100);
      row.busy = true;
      row.canCancel = true;
      break;
    case 'SUCCEEDED': {
      const n = job.result?.attempts ?? 1;
      row.status = n > 1 ? `Téléchargé ✓ (${n} essais)` : 'Téléchargé ✓';
      row.color = 'ok';
      row.progress = 100;
      row.succeeded = true;
      row.dbPath = job.result?.dbPath ?? null;
      row.tooltip = job.result?.videoTitle ? `Vidéo : ${job.result.videoTitle}` : null;
      break;
    }
    case 'FAILED':
      row.status = job.message && job.message !== 'Terminé' ? job.message : 'Erreur';
      row.color = 'err';
      row.tooltip = job.error;
      row.failed = true;
      break;
    default:
      row.status = 'Annulé';
      row.color = 'err';
      row.failed = true;
  }
  return row;
}

/** Piste de la recherche (ou ligne existante) → entrée GraphQL ; tolère les variantes de noms de champs. */
function toInput(t: Record<string, unknown>) {
  const s = (v: unknown) => (v == null || v === '' ? null : String(v));
  const n = (v: unknown) => (v == null || v === '' || Number.isNaN(Number(v)) ? null : Number(v));
  let duration = n(t['duration']) ?? 0;
  if (typeof t['duration'] === 'string' && (t['duration'] as string).includes(':')) {
    duration = (t['duration'] as string).split(':').reverse().reduce((acc, p, i) => acc + Number(p) * 60 ** i, 0);
  }
  return {
    id: n(t['id']) ?? 0,
    title: s(t['title']) ?? '',
    artist: s(t['artist']) ?? '',
    album: s(t['album']),
    duration,
    artistId: n(t['artistId']),
    albumId: n(t['albumId']),
    previewUrl: s(t['previewUrl'] ?? t['preview']),
    coverSmall: s(t['coverSmall'] ?? t['coverUrl'] ?? t['cover']),
    coverBig: s(t['coverBig']),
    link: s(t['link']),
    trackPosition: n(t['trackPosition']),
    diskNumber: n(t['diskNumber']),
  };
}
