import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { gql } from '@apollo/client';
import { firstValueFrom } from 'rxjs';
import { GqlService } from '../../../core/graphql/gql.service';
import { ConnectivityService } from '../../../core/offline/connectivity.service';
import { PlayerService } from '../../../core/player/player.service';
import { NotifyService } from '../../../core/ui/notify.service';
import { ConfirmService, Presence, PresenceBadge, imageUrl, previewUrl } from '../../../shared/ui';
import { DurationPipe } from '../../../shared/pipes';
import { openTrackDedupDialog } from '../../organiser/dedup/track-dedup-dialog';

/** Champs de la piste Deezer utiles à l'extraction (ce que la page Musique reçoit). */
export interface DeezerTrackLite {
  id: number;
  title: string;
  artist: string;
  artistId: number | null;
  album: string | null;
  albumId: number | null;
  duration: number;
  rank: number | null;
  previewUrl: string | null;
  coverSmall: string | null;
  coverBig: string | null;
  link: string | null;
  explicit: boolean;
  trackPosition: number | null;
  diskNumber: number | null;
}

export interface SearchDialogResult {
  tracks: DeezerTrackLite[];
}

interface PresenceFile { path: string; reason: string; bitrate: number | null; fileSize: number | null; ext: string | null }
interface PresenceInfo { key: string; state: Presence; reason: string | null; files: PresenceFile[] }
interface Track extends DeezerTrackLite { presence: PresenceInfo }
interface Entity {
  kind: 'ARTIST' | 'ALBUM' | 'PLAYLIST' | 'TRACK';
  id: number;
  name: string;
  subtitle: string | null;
  picture: string | null;
  trackCount: number | null;
  fanCount: number | null;
}
interface SearchData { query: string; kind: string; error: string | null; header: Entity | null; entities: Entity[]; tracks: Track[] }
type SearchKind = 'ALL' | 'ARTIST' | 'ALBUM' | 'PLAYLIST';
type Request = { type: 'search'; query: string; kind: SearchKind } | { type: 'browse'; kind: 'ARTIST' | 'ALBUM' | 'PLAYLIST'; id: number };

interface Frame {
  mode: 'tracks' | 'entities';
  tracks: Track[];
  entities: Entity[];
  crumb: string;
  header: Entity | null;
  request: Request | null;
}

interface HistoryEntry { query: string; position: number }

const TRACK_FIELDS = `
  id title artist artistId album albumId duration rank previewUrl coverSmall coverBig link explicit trackPosition diskNumber
  presence { key state reason files { path reason bitrate fileSize ext } }
`;
const RESULT_FIELDS = `
  query kind error
  header { kind id name subtitle picture trackCount fanCount }
  entities { kind id name subtitle picture trackCount fanCount }
  tracks { ${TRACK_FIELDS} }
`;
const SEARCH = gql`query CatalogSearch($query: String!, $kind: SearchKind!) { search(query: $query, kind: $kind) { ${RESULT_FIELDS} } }`;
const BROWSE = gql`query CatalogBrowse($kind: EntityKind!, $id: Long!) { browse(kind: $kind, id: $id) { ${RESULT_FIELDS} } }`;
const HISTORY = gql`query SearchHistory { searchHistory { query position } }`;
const COVER = gql`query SearchCover($query: String!) { searchCover(query: $query) { query url } }`;
const REMEMBER = gql`mutation RememberSearch($query: String!) { rememberSearch(query: $query) { query position } }`;
const DELETE = gql`mutation DeleteSearch($query: String!) { deleteSearch(query: $query) { query position } }`;
const CLEAR = gql`mutation ClearSearches { clearSearches { query position } }`;

/** Vignettes des recherches : mémoire partagée entre les ouvertures de la fenêtre (comme _SEARCH_COVER_CACHE). */
const searchCovers = new Map<string, string | null>();

const KIND_OPTIONS: { value: SearchKind; label: string }[] = [
  { value: 'ALL', label: 'Tout / Titre' },
  { value: 'ARTIST', label: 'Artiste' },
  { value: 'ALBUM', label: 'Album' },
  { value: 'PLAYLIST', label: 'Playlist' },
];

type SortColumn = 'title' | 'artist' | 'album' | 'duration' | 'rank';

/**
 * Fenêtre « Rechercher et ajouter des pistes » (spec 01 §2.4) : recherche Deezer (titre,
 * artiste, album, playlist, lien), exploration des entités avec fil d'Ariane, pastilles de
 * présence qui verrouillent ou décochent les cases, écoute d'extrait, dédoublonnage.
 * Les modifications de l'historique des recherches sont conservées même si la fenêtre est annulée.
 */
@Component({
  selector: 'dk-search-dialog',
  imports: [ScrollingModule, MatTooltipModule, PresenceBadge, DurationPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './search-dialog.html',
  styleUrl: './search-dialog.scss',
})
export class SearchDialog implements OnInit {
  private readonly ref = inject<MatDialogRef<SearchDialog, SearchDialogResult | undefined>>(MatDialogRef);
  private readonly dialog = inject(MatDialog);
  private readonly gql = inject(GqlService);
  private readonly notify = inject(NotifyService);
  private readonly confirm = inject(ConfirmService);
  private readonly connectivity = inject(ConnectivityService);
  private readonly destroyRef = inject(DestroyRef);
  readonly player = inject(PlayerService);
  private readonly initialQuery = inject<{ query?: string } | null>(MAT_DIALOG_DATA, { optional: true })?.query ?? '';

  readonly kinds = KIND_OPTIONS;
  readonly online = this.connectivity.isOnline;

  readonly query = signal('');
  readonly link = signal('');
  readonly kind = signal<SearchKind>('ALL');
  readonly loading = signal(false);
  readonly message = signal<{ title: string; text: string; error: boolean } | null>(null);

  // Vue courante et navigation.
  readonly mode = signal<'tracks' | 'entities' | 'idle'>('idle');
  readonly tracks = signal<Track[]>([]);
  readonly entities = signal<Entity[]>([]);
  readonly crumb = signal('');
  readonly header = signal<Entity | null>(null);
  private readonly stack = signal<Frame[]>([]);
  readonly canGoBack = computed(() => this.stack().length > 0);
  private request: Request | null = null;

  // Liste de pistes : filtre, tri, sélection.
  readonly filter = signal('');
  readonly duplicatesOnly = signal(false);
  readonly sortColumn = signal<SortColumn | null>(null);
  readonly sortAsc = signal(true);
  readonly entitySort = signal<{ column: 'name' | 'subtitle'; asc: boolean } | null>(null);
  /** Pistes cochées, par identifiant (conservées d'une vue à l'autre). */
  readonly picked = signal(new Map<number, Track>());

  // Historique.
  readonly history = signal<HistoryEntry[]>([]);
  readonly historyOpen = signal(false);
  readonly recent = computed(() => this.history().slice(0, 8));
  readonly covers = signal(new Map(searchCovers));

  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');
  private token = 0;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;

  readonly visibleTracks = computed(() => {
    const text = this.filter().trim().toLowerCase();
    const onlyDup = this.duplicatesOnly();
    let list = this.tracks().filter(t => (!text || t.title.toLowerCase().includes(text)) && (!onlyDup || isDuplicate(t)));
    const column = this.sortColumn();
    if (column) {
      const dir = this.sortAsc() ? 1 : -1;
      const key = (t: Track) => (column === 'duration' ? t.duration : column === 'rank' ? (t.rank ?? 0) : (t[column] ?? '').toString().toLowerCase());
      list = [...list].sort((a, b) => {
        const x = key(a), y = key(b);
        return (x < y ? -1 : x > y ? 1 : 0) * dir;
      });
    }
    return list;
  });
  readonly visibleEntities = computed(() => {
    const sort = this.entitySort();
    if (!sort) return this.entities();
    const dir = sort.asc ? 1 : -1;
    return [...this.entities()].sort((a, b) => (a[sort.column] ?? '').toLowerCase().localeCompare((b[sort.column] ?? '').toLowerCase()) * dir);
  });

  readonly present = computed(() => this.tracks().filter(t => t.presence.state === 'PRESENT').length);
  readonly possible = computed(() => this.tracks().filter(t => t.presence.state === 'POSSIBLE').length);
  readonly duplicateTotal = computed(() => this.present() + this.possible());
  readonly dedupCandidates = computed(() => this.tracks().filter(t => isDuplicate(t) && t.presence.files.length > 0));
  readonly pickedCount = computed(() => this.picked().size);
  readonly allChecked = computed(() => {
    const eligible = this.visibleTracks().filter(t => !isDuplicate(t));
    return eligible.length > 0 && eligible.every(t => this.picked().has(t.id));
  });
  readonly summaryTip =
    'Les titres déjà présents ne peuvent pas être sélectionnés ; les doublons possibles restent libres mais décochés.\n' +
    '« Tout sélectionner » ne coche ni les uns ni les autres.\n' +
    '« Voir les doublons » n\'affiche que ces lignes ; « Dédoublonner… » montre les fichiers du disque en cause.';

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.token++;
      this.clearRetry();
      this.player.stopPreview(true);
    });
  }

  ngOnInit(): void {
    void this.loadHistory();
    if (this.initialQuery.trim()) {
      this.query.set(this.initialQuery.trim());
      void this.search();
    } else {
      queueMicrotask(() => this.searchInput()?.nativeElement.focus());
    }
  }

  // ------------------------------------------------------------------ Recherche

  setQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
    this.historyOpen.set(false);
  }

  setLink(event: Event): void {
    this.link.set((event.target as HTMLInputElement).value);
  }

  setKind(event: Event): void {
    this.kind.set((event.target as HTMLSelectElement).value as SearchKind);
  }

  async pasteAndSearch(): Promise<void> {
    let text = this.link().trim();
    if (!text) {
      try { text = (await navigator.clipboard.readText()).trim(); } catch { /* presse-papiers refusé : on s'en tient au champ */ }
    }
    if (!text) return;
    this.link.set(text);
    this.query.set(text);
    await this.search();
  }

  async search(queryOverride?: string): Promise<void> {
    const query = (queryOverride ?? this.query()).trim();
    if (!query) return;
    this.query.set(query);
    this.historyOpen.set(false);
    this.resetNavigation();
    this.player.stopPreview(true);
    // L'historique est mis à jour d'emblée, comme à l'original : requête retirée puis insérée en tête.
    this.history.update(h => [{ query, position: 0 }, ...h.filter(e => e.query.toLowerCase() !== query.toLowerCase())]);
    void this.gql.mutate(REMEMBER, { query }, { label: 'Mémoriser la recherche', queueable: false }).then(() => void this.loadHistory());
    this.filter.set('');
    this.duplicatesOnly.set(false);
    this.sortColumn.set(null);
    await this.run({ type: 'search', query, kind: this.kind() }, '');
  }

  private resetNavigation(): void {
    this.stack.set([]);
    this.crumb.set('');
    this.header.set(null);
  }

  /** Lance une requête (recherche ou exploration) et affiche son résultat ; l'état précédent doit déjà être empilé. */
  private async run(request: Request, crumb: string): Promise<void> {
    const token = ++this.token;
    this.clearRetry();
    this.loading.set(true);
    this.message.set(null);
    try {
      const data = await this.fetch(request);
      if (token !== this.token) return;
      this.request = request;
      this.crumb.set(crumb);
      this.show(data, request);
    } catch (e) {
      if (token !== this.token) return;
      this.message.set({ title: 'Erreur de connexion', text: `Erreur lors de la recherche : ${e instanceof Error ? e.message : String(e)}`, error: true });
    } finally {
      if (token === this.token) this.loading.set(false);
    }
  }

  private async fetch(request: Request): Promise<SearchData> {
    if (request.type === 'search') {
      const r = await this.gql.query<{ search: SearchData }>(SEARCH, { query: request.query, kind: request.kind });
      return r.search;
    }
    const r = await this.gql.query<{ browse: SearchData }>(BROWSE, { kind: request.kind, id: request.id });
    return r.browse;
  }

  private show(data: SearchData, request: Request): void {
    this.player.stopPreview(true);
    this.picked.update(p => new Map(p));
    if (data.error) {
      this.message.set({ title: 'Erreur de connexion', text: `Erreur lors de la recherche : ${data.error}`, error: true });
      this.mode.set('idle');
      this.tracks.set([]);
      this.entities.set([]);
      return;
    }
    const entitiesView = request.type === 'search' && data.entities.length > 0 || (data.tracks.length === 0 && data.entities.length > 0);
    if (entitiesView) {
      this.mode.set('entities');
      this.entities.set(data.entities);
      this.tracks.set([]);
      this.header.set(null);
      this.entitySort.set(null);
      return;
    }
    this.mode.set('tracks');
    this.tracks.set(data.tracks);
    this.entities.set([]);
    this.header.set(request.type === 'browse' && request.kind !== 'ARTIST' ? data.header : null);
    this.filter.set('');
    this.duplicatesOnly.set(false);
    if (data.tracks.length === 0) {
      const wasEntity = request.type === 'search' && (request.kind === 'ARTIST' || request.kind === 'ALBUM');
      this.message.set({ title: 'Aucun résultat', text: wasEntity ? 'Aucun résultat trouvé.' : 'Aucun morceau trouvé pour cette recherche.', error: false });
    } else {
      this.afterPresence(data.tracks);
    }
  }

  // ------------------------------------------------------------------ Navigation

  openEntity(entity: Entity): void {
    this.go(entity.kind === 'ARTIST' ? 'ARTIST' : entity.kind === 'PLAYLIST' ? 'PLAYLIST' : 'ALBUM', entity.id, entity.name);
  }

  openArtist(track: Track): void {
    if (track.artistId) this.go('ARTIST', track.artistId, track.artist);
  }

  openAlbum(track: Track): void {
    if (track.albumId) this.go('ALBUM', track.albumId, track.album ?? '');
  }

  private go(kind: 'ARTIST' | 'ALBUM' | 'PLAYLIST', id: number, name: string): void {
    this.stack.update(s => [...s, {
      mode: this.mode() === 'entities' ? 'entities' : 'tracks', tracks: this.tracks(), entities: this.entities(), crumb: this.crumb(),
      header: this.header(), request: this.request,
    }]);
    const crumb = kind === 'ARTIST' ? `Artiste : ${name} — top 50` : kind === 'ALBUM' ? `Album : ${name}` : `Playlist : ${name}`;
    this.filter.set('');
    this.duplicatesOnly.set(false);
    this.sortColumn.set(null);
    void this.run({ type: 'browse', kind, id }, crumb);
  }

  /** « ← Retour » : restaure l'état empilé sans requête réseau (en-tête d'album compris). */
  back(): void {
    const stack = this.stack();
    const frame = stack[stack.length - 1];
    if (!frame) return;
    this.token++;
    this.loading.set(false);
    this.clearRetry();
    this.player.stopPreview(true);
    this.stack.set(stack.slice(0, -1));
    this.message.set(null);
    this.mode.set(frame.mode);
    this.tracks.set(frame.tracks);
    this.entities.set(frame.entities);
    this.crumb.set(frame.crumb);
    this.header.set(frame.header);
    this.request = frame.request;
    this.filter.set('');
    this.duplicatesOnly.set(false);
    if (frame.tracks.some(t => t.presence.state === 'PENDING')) this.schedulePresenceRetry();
  }

  // ------------------------------------------------------------------ Présence

  /** Pastilles reçues : ce qui est déjà sur le disque est décoché, et le reste attend si l'index n'est pas prêt. */
  private afterPresence(tracks: Track[]): void {
    this.picked.update(p => {
      const next = new Map(p);
      for (const t of tracks) if (t.presence.state === 'PRESENT' || t.presence.state === 'POSSIBLE') next.delete(t.id);
      return next;
    });
    if (tracks.some(t => t.presence.state === 'PENDING')) this.schedulePresenceRetry();
  }

  private schedulePresenceRetry(attempt = 0): void {
    this.clearRetry();
    if (attempt >= 8) return;
    this.retryTimer = setTimeout(() => void this.refreshPresence(attempt + 1), 3000 + attempt * 1500);
  }

  private clearRetry(): void {
    if (this.retryTimer) clearTimeout(this.retryTimer);
    this.retryTimer = null;
  }

  /** Recalcule les pastilles (index prêt, suppression de doublons) sans toucher à la liste. */
  private async refreshPresence(attempt = 0): Promise<void> {
    const request = this.request;
    if (!request || this.mode() !== 'tracks') return;
    const token = this.token;
    try {
      const data = await this.fetch(request);
      if (token !== this.token) return;
      const byId = new Map(data.tracks.map(t => [t.id, t.presence]));
      const before = new Map(this.tracks().map(t => [t.id, t.presence.state]));
      const updated = this.tracks().map(t => (byId.has(t.id) ? { ...t, presence: byId.get(t.id)! } : t));
      this.tracks.set(updated);
      // Seules les lignes dont l'état vient de changer sont décochées : le choix de l'utilisateur est respecté.
      this.picked.update(p => {
        const next = new Map(p);
        for (const t of updated) {
          const was = before.get(t.id);
          if (was !== t.presence.state && (t.presence.state === 'PRESENT' || t.presence.state === 'POSSIBLE')) next.delete(t.id);
        }
        return next;
      });
      if (updated.some(t => t.presence.state === 'PENDING')) this.schedulePresenceRetry(attempt);
    } catch {
      this.schedulePresenceRetry(attempt);
    }
  }

  presenceTip(t: Track): string {
    const p = t.presence;
    const lines = [p.reason ?? ''];
    for (const f of p.files.slice(0, 5)) lines.push(`• ${f.path}`);
    if (p.files.length > 5) lines.push(`… et ${p.files.length - 5} autre(s)`);
    return lines.filter(Boolean).join('\n');
  }

  checkTip(t: Track): string {
    return t.presence.state === 'PRESENT' ? this.presenceTip(t) : 'Ajouter ce titre à la file de téléchargement';
  }

  // ------------------------------------------------------------------ Sélection

  toggle(t: Track, on: boolean): void {
    if (t.presence.state === 'PRESENT') return;
    this.picked.update(p => {
      const next = new Map(p);
      if (on) next.set(t.id, t);
      else next.delete(t.id);
      return next;
    });
  }

  /** « Tout sélectionner » : n'agit que sur les lignes visibles ; en cochant, ignore ✓ et ≈. */
  selectAll(on: boolean): void {
    this.picked.update(p => {
      const next = new Map(p);
      for (const t of this.visibleTracks()) {
        if (on) { if (!isDuplicate(t)) next.set(t.id, t); }
        else next.delete(t.id);
      }
      return next;
    });
  }

  // ------------------------------------------------------------------ Tri et filtre

  setFilter(event: Event): void {
    this.filter.set((event.target as HTMLInputElement).value);
  }

  sortBy(column: SortColumn): void {
    this.player.stopPreview(true);
    if (this.sortColumn() === column) this.sortAsc.update(a => !a);
    else {
      this.sortColumn.set(column);
      // Popularité : les plus populaires en tête dès le premier clic.
      this.sortAsc.set(column !== 'rank');
    }
  }

  sortEntities(column: 'name' | 'subtitle'): void {
    const current = this.entitySort();
    this.entitySort.set({ column, asc: current?.column === column ? !current.asc : true });
  }

  arrow(column: SortColumn): string {
    return this.sortColumn() === column ? (this.sortAsc() ? ' ▲' : ' ▼') : '';
  }

  // ------------------------------------------------------------------ Écoute

  playing(t: Track): boolean {
    return this.player.preview()?.id === `deezer:${t.id}` && this.player.previewPlaying();
  }

  listen(t: Track): void {
    if (!t.previewUrl) return;
    this.player.togglePreview({
      id: `deezer:${t.id}`, title: t.title, artist: t.artist, album: t.album, src: previewUrl(t.id), cover: imageUrl(t.coverSmall), duration: 30,
    });
  }

  bars(t: Track): boolean[] {
    const lit = Math.round(Math.min((t.rank ?? 0) / 1_000_000, 1) * 7);
    return Array.from({ length: 7 }, (_, i) => i < lit);
  }

  img(url: string | null): string | null {
    return imageUrl(url);
  }

  // ------------------------------------------------------------------ Doublons

  async dedupe(): Promise<void> {
    const candidates = this.dedupCandidates();
    if (!candidates.length) {
      this.notify.info('Aucun fichier du dossier de dépôt n\'est rapproché des titres affichés.');
      return;
    }
    this.player.stopPreview(true);
    const deleted = await openTrackDedupDialog(this.dialog, candidates.map(t => ({
      key: String(t.id), title: t.title, artist: t.artist, album: t.album,
      files: t.presence.files.map(f => ({ path: f.path, reason: f.reason, bitrate: f.bitrate, fileSize: f.fileSize, ext: f.ext })),
    })));
    if (deleted?.length) {
      this.notify.info(`Déduplication : ${deleted.length} fichier(s) supprimé(s).`);
      await this.refreshPresence();
    }
  }

  // ------------------------------------------------------------------ Historique

  private async loadHistory(): Promise<void> {
    try {
      const data = await this.gql.query<{ searchHistory: HistoryEntry[] }>(HISTORY, {});
      this.history.set(data.searchHistory ?? []);
      void this.loadCovers();
    } catch {
      // Hors ligne : l'historique reste celui déjà connu.
    }
  }

  private async loadCovers(): Promise<void> {
    for (const entry of this.recent()) {
      if (searchCovers.has(entry.query)) continue;
      try {
        const r = await this.gql.query<{ searchCover: { url: string | null } }>(COVER, { query: entry.query });
        searchCovers.set(entry.query, r.searchCover.url);
      } catch {
        return;
      }
      this.covers.set(new Map(searchCovers));
    }
  }

  openHistory(): void {
    if (this.history().length > 0) this.historyOpen.set(true);
  }

  closeHistorySoon(): void {
    // Laisse passer un clic sur le panneau avant de le refermer.
    setTimeout(() => this.historyOpen.set(false), 150);
  }

  async removeSearch(entry: HistoryEntry, event: Event): Promise<void> {
    event.stopPropagation();
    this.history.update(h => h.filter(e => e.query !== entry.query));
    await this.gql.mutate(DELETE, { query: entry.query }, { label: 'Retirer une recherche', queueable: false });
  }

  async clearHistory(event: Event): Promise<void> {
    event.stopPropagation();
    if (!(await this.confirm.ask({ title: 'Effacer les dernières recherches', message: 'Vider tout l\'historique des recherches ?', confirm: 'Effacer', danger: true }))) return;
    this.history.set([]);
    this.historyOpen.set(false);
    await this.gql.mutate(CLEAR, {}, { label: 'Effacer les recherches', queueable: false });
  }

  onEscape(event: Event): void {
    if (this.historyOpen()) {
      this.historyOpen.set(false);
      event.stopPropagation();
    }
  }

  // ------------------------------------------------------------------ Validation

  /** « Annuler » : interrompt une recherche en cours (la fenêtre reste ouverte), sinon ferme. */
  cancel(): void {
    if (this.loading()) {
      this.token++;
      this.loading.set(false);
      return;
    }
    this.ref.close(undefined);
  }

  add(): void {
    const tracks = [...this.picked().values()].filter(t => t.id).map(({ presence: _presence, ...lite }) => lite);
    this.ref.close({ tracks });
  }

  trackById = (_: number, t: Track): number => t.id;
  trackEntity = (_: number, e: Entity): number => e.id;

  entityLabel(e: Entity): string {
    return e.kind === 'ARTIST' ? 'Voir le top 50' : 'Ouvrir l\'album';
  }
}

function isDuplicate(t: Track): boolean {
  return t.presence.state === 'PRESENT' || t.presence.state === 'POSSIBLE';
}

/**
 * Ouvre la fenêtre « Rechercher et ajouter des pistes ». Renvoie les pistes cochées,
 * ou undefined si la fenêtre est annulée. Plein écran sur mobile.
 */
export function openSearchDialog(dialog: MatDialog, initialQuery?: string): Promise<SearchDialogResult | undefined> {
  const mobile = typeof window !== 'undefined' && window.innerWidth < 900;
  const ref = dialog.open<SearchDialog, { query?: string }, SearchDialogResult | undefined>(SearchDialog, {
    data: { query: initialQuery },
    width: mobile ? '100vw' : 'min(1400px, 94vw)',
    maxWidth: '100vw',
    height: mobile ? '100dvh' : 'min(90vh, 900px)',
    maxHeight: '100dvh',
    panelClass: mobile ? ['dk-search-panel', 'mobile'] : 'dk-search-panel',
    autoFocus: mobile ? false : 'first-tabbable',
  });
  return firstValueFrom(ref.afterClosed());
}
