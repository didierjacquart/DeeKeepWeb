import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ScrollingModule } from '@angular/cdk/scrolling';
import { MatMenuModule, MatMenuTrigger } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { debounceTime, merge } from 'rxjs';
import { GqlService } from '../../core/graphql/gql.service';
import { ConnectivityService } from '../../core/offline/connectivity.service';
import { NotifyService } from '../../core/ui/notify.service';
import { PlayerService, PlayerTrack } from '../../core/player/player.service';
import { OfflineAudioService } from '../../core/player/offline-audio.service';
import { archiveMediaUrl, ConfirmService, coverUrl, mediaUrl } from '../../shared/ui';
import { formatBytes, formatDuration } from '../../shared/pipes';
import { ArchivePanel } from './archive-panel/archive-panel';
import { ArchiveActions } from './archive-panel/archive-actions.service';
import {
  ARCHIVE_CHANGED, buildValueIndex, FILTER_DELAY_MS, LibraryRow, MIN_SUGGEST_CHARS, canonical, selectable, SortColumn, sortRows, Suggestion,
  suggestionsFor, suggestionText, toRow, Track, TRACKS_CHANGED, TRACKS_QUERY, TRACKS_REMOVED, TrackPage,
} from './library-model';

interface Column { key: SortColumn | null; label: string; cls?: string }

/**
 * Bibliothèque : tous les titres connus de la base, état disque et archive, recherche multi-tags
 * (tous les mots, ordre libre), cinq propositions au clavier, tri par en-tête, écoute (la liste
 * affichée devient la file). La liste vient du cache Apollo : elle s'affiche hors ligne.
 */
@Component({
  selector: 'dk-bibliotheque-page',
  imports: [ScrollingModule, MatMenuModule, MatTooltipModule, ArchivePanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './bibliotheque.page.html',
  styleUrl: './bibliotheque.page.scss',
})
export class LibraryPage {
  private readonly gql = inject(GqlService);
  private readonly player = inject(PlayerService);
  private readonly offlineAudio = inject(OfflineAudioService);
  private readonly archive = inject(ArchiveActions);
  private readonly confirm = inject(ConfirmService);
  private readonly notify = inject(NotifyService);
  private readonly connectivity = inject(ConnectivityService);
  private readonly destroyRef = inject(DestroyRef);

  readonly bytes = formatBytes;
  readonly duration = formatDuration;
  readonly cover = coverUrl;

  readonly columns: Column[] = [
    { key: null, label: '' },
    { key: null, label: 'Écoute' },
    { key: 'cloud', label: 'Nuage' },
    { key: 'title', label: 'Titre' },
    { key: 'artist', label: 'Artiste', cls: 'dk-hide-mobile' },
    { key: 'album', label: 'Album', cls: 'dk-hide-mobile' },
    { key: 'genre', label: 'Genre', cls: 'dk-hide-mobile' },
    { key: 'year', label: 'Année', cls: 'dk-hide-mobile' },
    { key: 'duration', label: 'Durée', cls: 'dk-hide-mobile' },
    { key: 'format', label: 'Format', cls: 'dk-hide-mobile' },
    { key: 'path', label: 'Chemin', cls: 'dk-hide-mobile' },
  ];

  // ---------------------------------------------------------------- données
  private readonly page = signal<TrackPage | null>(null);
  readonly loading = signal(true);
  readonly stale = signal(false);
  readonly error = signal<string | null>(null);
  readonly rows = computed<LibraryRow[]>(() => (this.page()?.items ?? []).map(toRow));

  // ---------------------------------------------------------------- filtre, tri, sélection
  readonly input = signal('');
  /** Texte réellement appliqué (après le différé de frappe de 220 ms). */
  readonly query = signal('');
  readonly includeMissing = signal(false);
  readonly sort = signal<{ column: SortColumn; desc: boolean }>({ column: 'artist', desc: false });
  /** Chemins cochés : survivent au filtrage et au tri, purgés des titres disparus de la base. */
  readonly checked = signal<ReadonlySet<string>>(new Set());
  readonly archiveOpen = signal(false);

  readonly displayed = computed<LibraryRow[]>(() => {
    const tokens = canonical(this.query().trim()).split(' ').filter(Boolean);
    const missing = this.includeMissing();
    const rows = this.rows().filter(r => selectable(r.track, missing) && tokens.every(t => r.hay.includes(t)));
    const { column, desc } = this.sort();
    return sortRows(rows, column, desc);
  });
  readonly visiblePaths = computed(() => this.displayed().map(r => r.track.path));
  readonly selectedPaths = computed(() => this.displayed().filter(r => this.checked().has(r.track.path)).map(r => r.track.path));

  // ---------------------------------------------------------------- propositions
  private readonly valueIndex = computed(() => buildValueIndex(this.rows(), this.includeMissing()));
  readonly suggestions = computed<Suggestion[]>(() =>
    this.input().trim().length < MIN_SUGGEST_CHARS || !this.suggestOpen() ? [] : suggestionsFor(this.valueIndex(), this.input()));
  readonly suggestOpen = signal(false);
  readonly suggestIndex = signal(-1);
  readonly suggestionText = suggestionText;
  private filterTimer: ReturnType<typeof setTimeout> | null = null;

  // ---------------------------------------------------------------- statut
  readonly summary = computed(() => {
    const all = this.rows();
    const shown = this.displayed();
    const onDisk = all.filter(r => r.track.onDisk);
    const parts = [`${shown.length} titres affichés sur ${all.length}`, `${onDisk.length} sur le disque (${formatBytes(onDisk.reduce((s, r) => s + (r.track.fileSize ?? 0), 0))})`];
    const fresh = all.filter(r => r.track.archiveState === 'ok');
    if (fresh.length) {
      const stored = fresh.reduce((s, r) => s + (r.track.archiveBytes ?? 0), 0);
      const source = fresh.reduce((s, r) => s + (r.track.archiveSourceBytes ?? 0), 0);
      const pct = source > 0 ? Math.round((100 * stored) / source) : 0;
      parts.push(`${fresh.length} en miroir (${formatBytes(stored)}, soit ${pct} % du poids d'origine)`);
    } else parts.push('aucun titre en miroir');
    const stale = all.filter(r => r.track.archiveState === 'stale').length;
    if (stale) parts.push(`${stale} à renvoyer`);
    return parts.join(' · ');
  });

  // ---------------------------------------------------------------- menu contextuel
  readonly menuTrigger = viewChild(MatMenuTrigger);
  readonly menuAt = signal({ x: 0, y: 0 });
  readonly menuRow = signal<Track | null>(null);
  readonly menuPinned = computed(() => {
    const t = this.menuRow();
    return !!t && this.offlineAudio.isPinned(mediaUrl(t.path));
  });

  constructor() {
    this.gql.watch<{ tracks: TrackPage }>(TRACKS_QUERY).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(s => {
      if (s.data?.tracks) {
        this.page.set(s.data.tracks);
        // Les coches de titres disparus de la base sont oubliées.
        const known = new Set(s.data.tracks.items.map(t => t.path));
        this.checked.update(c => (c.size && [...c].some(p => !known.has(p)) ? new Set([...c].filter(p => known.has(p))) : c));
      }
      this.loading.set(s.loading && !s.data);
      this.stale.set(s.stale);
      this.error.set(s.error && !s.data ? s.error : null);
    });
    // Changements côté serveur (enregistrement, relecture, archivage) : rechargement groupé.
    merge(
      this.gql.subscribe(TRACKS_CHANGED), this.gql.subscribe(TRACKS_REMOVED), this.gql.subscribe(ARCHIVE_CHANGED),
    ).pipe(debounceTime(1200), takeUntilDestroyed(this.destroyRef)).subscribe({ next: () => this.gql.refetchActive(), error: () => undefined });
  }

  // ---------------------------------------------------------------- recherche
  onInput(value: string): void {
    this.input.set(value);
    this.suggestIndex.set(-1);
    this.suggestOpen.set(value.trim().length >= MIN_SUGGEST_CHARS);
    if (this.filterTimer) clearTimeout(this.filterTimer);
    this.filterTimer = setTimeout(() => this.query.set(value), FILTER_DELAY_MS);
  }

  applyNow(value = this.input()): void {
    if (this.filterTimer) clearTimeout(this.filterTimer);
    this.query.set(value);
  }

  clearSearch(): void {
    this.input.set('');
    this.suggestOpen.set(false);
    this.applyNow('');
  }

  onKey(event: KeyboardEvent): void {
    const list = this.suggestions();
    if (event.key === 'ArrowDown' && list.length) {
      event.preventDefault();
      this.suggestIndex.update(i => Math.min(list.length - 1, i + 1));
    } else if (event.key === 'ArrowUp' && list.length) {
      event.preventDefault();
      this.suggestIndex.update(i => Math.max(0, i - 1));
    } else if (event.key === 'Escape' && list.length) {
      this.suggestOpen.set(false);
    } else if (event.key === 'Enter') {
      const chosen = list[this.suggestIndex()];
      if (chosen) this.pick(chosen);
      else {
        this.suggestOpen.set(false);
        this.applyNow();
      }
    }
  }

  /** Clic sur une proposition : le champ reçoit la valeur, sans nouvelles propositions. */
  pick(s: Suggestion): void {
    this.suggestOpen.set(false);
    this.input.set(s.value);
    this.applyNow(s.value);
  }

  // ---------------------------------------------------------------- tri et cases
  sortBy(column: SortColumn | null): void {
    if (!column) return;
    this.sort.update(s => (s.column === column ? { column, desc: !s.desc } : { column, desc: false }));
  }

  sortMark(column: SortColumn | null): string {
    const s = this.sort();
    return column && s.column === column ? (s.desc ? ' ▾' : ' ▴') : '';
  }

  toggle(path: string, on: boolean): void {
    this.checked.update(c => {
      const n = new Set(c);
      if (on) n.add(path);
      else n.delete(path);
      return n;
    });
  }

  checkAll(): void {
    this.checked.update(c => new Set([...c, ...this.visiblePaths()]));
  }

  uncheckAll(): void {
    const shown = new Set(this.visiblePaths());
    this.checked.update(c => new Set([...c].filter(p => !shown.has(p))));
  }

  refresh(): void {
    this.gql.refetchActive();
  }

  // ---------------------------------------------------------------- écoute
  playable(t: Track): boolean {
    return t.onDisk || t.archiveState === 'ok';
  }

  playTip(t: Track): string {
    if (t.onDisk) return 'Écouter ce titre';
    return t.archiveState === 'ok' ? "Écouter la copie de l'archive (fichier absent du disque)" : "Fichier absent du disque, et pas de copie d'archive";
  }

  cloudTip(t: Track): string {
    if (t.archiveState === 'stale') return "Une copie existe dans l'archive, mais le fichier local a changé depuis.";
    return t.archiveState === 'none' ? "Ce titre n'est pas dans l'archive." : "Copie conservée dans l'archive.";
  }

  private asPlayerTrack(t: Track): PlayerTrack {
    return {
      id: t.path,
      title: t.title || t.path.split(/[\\/]/).pop() || t.path,
      artist: t.artist,
      album: t.album,
      src: t.onDisk ? mediaUrl(t.path) : archiveMediaUrl(t.path),
      cover: coverUrl(t.coverSha1),
      duration: t.duration,
      context: [t.format, t.year, t.genre].filter(Boolean).join(' · ') || null,
    };
  }

  /** La liste affichée devient la file ; les lignes sans source sont sautées. */
  play(t: Track): void {
    if (!this.playable(t)) {
      this.notify.info("Ce titre n'est ni sur le disque ni dans l'archive : il n'y a rien à écouter.");
      return;
    }
    this.player.play(this.asPlayerTrack(t), this.displayed().map(r => r.track).filter(x => this.playable(x)).map(x => this.asPlayerTrack(x)));
  }

  async pin(t: Track): Promise<void> {
    const src = mediaUrl(t.path);
    if (this.offlineAudio.isPinned(src)) {
      await this.offlineAudio.unpin(src);
      this.notify.info('Titre retiré de l’écoute hors ligne.');
    } else {
      const ok = await this.offlineAudio.pin(src);
      if (ok) this.notify.success('Titre épinglé : il restera écoutable sans connexion.');
      else this.notify.error('Épinglage impossible (titre absent du disque ou stockage indisponible).');
    }
  }

  // ---------------------------------------------------------------- menu contextuel
  openMenu(event: MouseEvent, t: Track): void {
    event.preventDefault();
    this.menuRow.set(t);
    this.menuAt.set({ x: event.clientX, y: event.clientY });
    queueMicrotask(() => this.menuTrigger()?.openMenu());
  }

  async copyPath(t: Track): Promise<void> {
    try {
      await navigator.clipboard.writeText(t.path);
      this.notify.info('Chemin copié.');
    } catch {
      this.notify.error('Copie impossible dans ce navigateur.');
    }
  }

  /** « Retirer de l'archive » demande confirmation (l'original ne le faisait pas : défaut D5). */
  async removeFromArchive(t: Track): Promise<void> {
    const ok = await this.confirm.ask({
      title: "Retirer de l'archive",
      message: `Retirer « ${t.title || t.path} » de l'archive ?\nLe fichier du disque n'est pas touché.`,
      confirm: 'Retirer',
      danger: true,
    });
    if (ok) await this.archive.remove([t.path]);
  }

  archiveOne(t: Track): Promise<void> {
    return this.archive.archive([t.path]);
  }

  restoreOne(t: Track): Promise<void> {
    return this.archive.restore([t.path]);
  }

  readonly online = this.connectivity.isOnline;
  trackBy = (_: number, r: LibraryRow) => r.track.path;
}
