import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatMenuModule } from '@angular/material/menu';
import { gql } from '@apollo/client';
import { debounceTime } from 'rxjs';
import { GqlService } from '../../core/graphql/gql.service';
import { NotifyService } from '../../core/ui/notify.service';
import { PlayerService, PlayerTrack } from '../../core/player/player.service';
import { NavBadges } from '../../layout/shell';
import { ConfirmService, coverUrl, imageUrl, mediaUrl } from '../../shared/ui';
import { openSearchDialog } from '../musique/search-dialog/search-dialog';

interface HistoryEntry { path: string; title: string | null; artist: string | null; album: string | null; coverSha1: string | null; position: number; onDisk: boolean; downloadedAt: number | null }
interface SearchEntry { query: string; position: number }

const HISTORY_Q = gql`query History { history { path title artist album coverSha1 position onDisk downloadedAt } searchHistory { query position } }`;
const SEARCH_COVER = gql`query SearchCover($query: String!) { searchCover(query: $query) { query url } }`;
const PURGE = gql`mutation PurgeMissingHistory { purgeMissingHistory { removed message } }`;
const CLEAR = gql`mutation ClearHistory { clearHistory { removed message } }`;
const DELETE_SEARCH = gql`mutation DeleteSearch($query: String!) { deleteSearch(query: $query) { query position } }`;
const CLEAR_SEARCHES = gql`mutation ClearSearches { clearSearches { query position } }`;
const ADD_PENDING = gql`mutation AddPending($tracks: [DownloadTrackInput!]!) { addPendingDownloads(tracks: $tracks) { id } }`;
const HISTORY_CHANGED = gql`subscription HistoryChanged { historyChanged { paths } }`;

type SortKey = 'title' | 'artist' | 'album' | 'path';

/** Agrandissement de la jaquette : titre, artiste, image 320 px. */
@Component({
  selector: 'dk-cover-popup',
  imports: [MatDialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.title }} — {{ data.artist }}</h2>
    <mat-dialog-content class="popup">
      @if (data.src) { <img [src]="data.src" width="320" height="320" alt="Jaquette" /> } @else { <p class="dk-faint">Aucune jaquette disponible</p> }
      <p><strong>{{ data.title }}</strong><br />{{ data.artist }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button class="control-btn" mat-dialog-close>Fermer</button></mat-dialog-actions>
  `,
  styles: `.popup { text-align: center; img { max-width: 100%; height: auto; border-radius: 10px; object-fit: cover; } }`,
})
export class CoverPopup {
  readonly data = inject<{ title: string; artist: string; src: string | null }>(MAT_DIALOG_DATA);
}

/**
 * Historique d'utilisation : recherches effectuées (relançables) et téléchargements réussis
 * (écoute, jaquette agrandissable, purge des introuvables, vidage confirmé).
 */
@Component({
  selector: 'dk-historique-page',
  imports: [MatTooltipModule, MatMenuModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './historique.page.html',
  styleUrl: './historique.page.scss',
})
export class HistoryPage {
  private readonly gql = inject(GqlService);
  private readonly player = inject(PlayerService);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(ConfirmService);
  private readonly notify = inject(NotifyService);
  private readonly badges = inject(NavBadges);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly tab = signal<'searches' | 'downloads'>('searches');
  readonly history = signal<HistoryEntry[]>([]);
  readonly searches = signal<SearchEntry[]>([]);
  readonly loading = signal(true);
  readonly stale = signal(false);
  readonly searchSort = signal<'asc' | 'desc' | null>(null);
  readonly sort = signal<{ key: SortKey; desc: boolean } | null>(null);
  readonly cover = coverUrl;
  /** Pochette du premier résultat Deezer par recherche (« ⏳ » puis image, « ♪ » si rien). */
  readonly searchCovers = signal<Record<string, string | null | undefined>>({});
  private readonly asked = new Set<string>();

  readonly shownSearches = computed(() => {
    const list = [...this.searches()].sort((a, b) => a.position - b.position);
    const s = this.searchSort();
    return s ? list.sort((a, b) => (s === 'asc' ? 1 : -1) * a.query.localeCompare(b.query, 'fr', { sensitivity: 'base' })) : list;
  });

  /** Les plus récents en premier : l'historique est rangé du plus ancien au plus récent. */
  readonly shownDownloads = computed(() => {
    const list = [...this.history()].sort((a, b) => b.position - a.position);
    const s = this.sort();
    if (!s) return list;
    const val = (e: HistoryEntry) => (s.key === 'path' ? e.path : (e[s.key] ?? '')).toLowerCase();
    return list.sort((a, b) => (s.desc ? -1 : 1) * val(a).localeCompare(val(b), 'fr', { sensitivity: 'base' }));
  });

  constructor() {
    this.gql.watch<{ history: HistoryEntry[]; searchHistory: SearchEntry[] }>(HISTORY_Q).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(s => {
      if (s.data) {
        this.history.set(s.data.history ?? []);
        this.searches.set(s.data.searchHistory ?? []);
      }
      this.loading.set(s.loading && !s.data);
      this.stale.set(s.stale);
    });
    this.gql.subscribe(HISTORY_CHANGED)
      .pipe(debounceTime(800), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: () => this.gql.refetchActive(), error: () => undefined });
    // Compteur de l'entrée de navigation : recherches + téléchargements.
    effect(() => this.badges.history.set(this.history().length + this.searches().length || null));
    effect(() => {
      for (const s of this.shownSearches().slice(0, 30)) void this.loadSearchCover(s.query);
    });
  }

  private async loadSearchCover(query: string): Promise<void> {
    if (this.asked.has(query)) return;
    this.asked.add(query);
    try {
      const d = await this.gql.query<{ searchCover: { url: string | null } }>(SEARCH_COVER, { query }, true);
      this.searchCovers.update(m => ({ ...m, [query]: d.searchCover?.url ?? null }));
    } catch {
      this.searchCovers.update(m => ({ ...m, [query]: null }));
    }
  }

  coverOfSearch(query: string): string | null | undefined {
    const url = this.searchCovers()[query];
    return url ? imageUrl(url) : url;
  }

  // ---------------------------------------------------------------- recherches
  toggleSearchSort(): void {
    this.searchSort.update(s => (s === 'asc' ? 'desc' : 'asc'));
  }

  /** « Relancer » : ouvre la fenêtre de recherche avec la requête ; les pistes choisies rejoignent la liste de la page Musique. */
  async relaunch(query: string): Promise<void> {
    await this.router.navigateByUrl('/musique');
    const result = await openSearchDialog(this.dialog, query);
    const tracks = (result?.tracks ?? []) as unknown as Record<string, unknown>[];
    if (!tracks.length) return;
    const s = (v: unknown) => (v == null || v === '' ? null : String(v));
    const n = (v: unknown) => (v == null || v === '' || Number.isNaN(Number(v)) ? null : Number(v));
    const input = tracks.map(t => {
      let duration = n(t['duration']) ?? 0;
      if (typeof t['duration'] === 'string' && (t['duration'] as string).includes(':'))
        duration = (t['duration'] as string).split(':').reverse().reduce((acc, p, i) => acc + Number(p) * 60 ** i, 0);
      return {
        id: n(t['id']) ?? 0, title: s(t['title']) ?? '', artist: s(t['artist']) ?? '', album: s(t['album']), duration,
        artistId: n(t['artistId']), albumId: n(t['albumId']), previewUrl: s(t['previewUrl'] ?? t['preview']),
        coverSmall: s(t['coverSmall'] ?? t['coverUrl'] ?? t['cover']), coverBig: s(t['coverBig']), link: s(t['link']),
        trackPosition: n(t['trackPosition']), diskNumber: n(t['diskNumber']),
      };
    }).filter(t => t.id);
    const r = await this.gql.mutate(ADD_PENDING, { tracks: input }, { label: `Ajouter ${input.length} titre(s) à la liste` });
    if (r.error) this.notify.error(r.error);
    else if (r.queued) this.notify.info('Ajout mis en attente : il partira au retour de la connexion.');
  }

  async deleteSearch(query: string): Promise<void> {
    const r = await this.gql.mutate(DELETE_SEARCH, { query }, { label: 'Supprimer une recherche', refetch: ['History'] });
    if (r.error) this.notify.error(r.error);
  }

  async clearSearches(): Promise<void> {
    const ok = await this.confirm.ask({ title: 'Vider les recherches', message: "Retirer toutes les recherches de l'historique ?", confirm: 'Vider', danger: true });
    if (!ok) return;
    const r = await this.gql.mutate(CLEAR_SEARCHES, {}, { label: 'Vider les recherches', refetch: ['History'] });
    if (r.error) this.notify.error(r.error);
  }

  // ---------------------------------------------------------------- téléchargements
  sortBy(key: SortKey): void {
    this.sort.update(s => (s?.key === key ? { key, desc: !s.desc } : { key, desc: false }));
  }

  mark(key: SortKey): string {
    const s = this.sort();
    return s?.key === key ? (s.desc ? ' ▾' : ' ▴') : '';
  }

  private playerTrack(e: HistoryEntry): PlayerTrack {
    return { id: e.path, title: e.title || e.path.split(/[\\/]/).pop() || e.path, artist: e.artist, album: e.album, src: mediaUrl(e.path), cover: coverUrl(e.coverSha1) };
  }

  /** La file est tout l'historique (entrées présentes sur le disque), départ sur la ligne cliquée. */
  play(e: HistoryEntry): void {
    if (!e.onDisk) {
      this.notify.info("Ce fichier n'est plus sur le disque : il n'y a rien à écouter.");
      return;
    }
    this.player.play(this.playerTrack(e), this.shownDownloads().filter(x => x.onDisk).map(x => this.playerTrack(x)));
  }

  zoom(e: HistoryEntry): void {
    this.dialog.open(CoverPopup, { data: { title: e.title ?? '', artist: e.artist ?? '', src: coverUrl(e.coverSha1) }, width: 'min(420px, 94vw)' });
  }

  async copyPath(e: HistoryEntry): Promise<void> {
    try {
      await navigator.clipboard.writeText(e.path);
      this.notify.info('Chemin copié.');
    } catch {
      this.notify.error('Copie impossible dans ce navigateur.');
    }
  }

  async purge(): Promise<void> {
    const r = await this.gql.mutate<{ purgeMissingHistory: { removed: number; message: string } }>(PURGE, {}, { label: 'Purger les fichiers introuvables', refetch: ['History'] });
    if (r.error) this.notify.error(r.error);
    else if (r.data) this.notify.info(r.data.purgeMissingHistory.message);
  }

  /** « Vider l'historique » demande confirmation ; les fichiers du disque sont conservés. */
  async clear(): Promise<void> {
    const ok = await this.confirm.ask({
      title: "Vider l'historique",
      message: "Retirer toutes les entrées de l'historique des téléchargements ?\nLes fichiers du disque sont conservés.",
      confirm: 'Vider',
      danger: true,
    });
    if (!ok) return;
    const r = await this.gql.mutate<{ clearHistory: { message: string } }>(CLEAR, {}, { label: "Vider l'historique", refetch: ['History'] });
    if (r.error) this.notify.error(r.error);
    else if (r.data) this.notify.info(r.data.clearHistory.message);
  }
}
