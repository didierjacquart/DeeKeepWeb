import { useCallback, useMemo, useState } from 'react';
import { api } from '../../core/graphql/api';
import { ArchivePanel } from './archive-panel/ArchivePanel';
import { LibraryActions } from './components/LibraryActions';
import { LibraryHeader } from './components/LibraryHeader';
import { LibraryList } from './components/LibraryList';
import { LibrarySearch } from './components/LibrarySearch';
import { TrackMenu } from './components/TrackMenu';
import { useCheckedPaths } from './hooks/useCheckedPaths';
import { useLibrarySearch } from './hooks/useLibrarySearch';
import { useLibraryTracks } from './hooks/useLibraryTracks';
import { albumTracks, artistTracks, countLabel, groupLabel, groupOfSuggestion } from './listen-groups';
import { filterRows, nextSort, sortRows } from './library-model';
import { statusLine } from './library-status';
import { playGroup, playTrack } from './track-actions';
import './library-page.scss';

/** Bouton d'écoute de l'artiste ou de l'album choisi dans les propositions de la recherche. */
function GroupPlay({ group }) {
  if (!group) return null;
  const what = groupLabel(group.kind, group.name);
  return (
    <div className="dk-actions lib-group">
      <button className="accent-btn" onClick={() => playGroup(group.tracks, what)}>
        ▶ Écouter {what}
      </button>
      <span className="dk-faint">{countLabel(group.tracks.length)}</span>
    </div>
  );
}

/**
 * Bibliothèque : tous les titres connus de la base, état disque et archive, recherche multi-tags
 * (tous les mots, ordre libre), cinq propositions au clavier, tri par en-tête, écoute (la liste
 * affichée devient la file ; un album ou un artiste entier depuis le menu d'un titre ou une
 * proposition de la recherche). La fenêtre du lecteur s'ouvre à chaque écoute lancée d'ici.
 * La liste vient du cache Apollo : elle s'affiche hors ligne.
 */
export function LibraryPage() {
  const { page, rows, loading, stale, error } = useLibraryTracks();
  const [includeMissing, setIncludeMissing] = useState(false);
  const [sort, setSort] = useState({ column: 'artist', desc: false });
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [menu, setMenu] = useState(null);
  const search = useLibrarySearch(rows, includeMissing);
  const tracks = useMemo(() => rows.map(r => r.track), [rows]);
  const group = useMemo(() => groupOfSuggestion(tracks, search.picked), [tracks, search.picked]);

  const displayed = useMemo(
    () => sortRows(filterRows(rows, search.query, includeMissing), sort.column, sort.desc),
    [rows, search.query, includeMissing, sort],
  );
  const visiblePaths = useMemo(() => displayed.map(r => r.track.path), [displayed]);
  const { checked, toggle, checkAll, uncheckAll } = useCheckedPaths(page, visiblePaths);
  const selectedPaths = useMemo(() => visiblePaths.filter(p => checked.has(p)), [visiblePaths, checked]);
  const summary = useMemo(() => statusLine(rows, displayed.length), [rows, displayed]);

  const play = t => playTrack(t, displayed);
  const playGroupOf = (kind, t) =>
    kind === 'album'
      ? playGroup(albumTracks(tracks, t), groupLabel('album', t.album))
      : playGroup(artistTracks(tracks, t.artist), groupLabel('artist', t.artist));
  const closeMenu = useCallback(() => setMenu(null), []);
  const openMenu = (event, t) => {
    event.preventDefault();
    setMenu({ x: event.clientX, y: event.clientY, track: t });
  };

  return (
    <section className="dk-page dk-library-page lib">
      <h1 className="dk-page-title">Bibliothèque</h1>

      <LibraryActions
        onCheckAll={checkAll}
        onUncheckAll={uncheckAll}
        includeMissing={includeMissing}
        onIncludeMissing={setIncludeMissing}
        archiveOpen={archiveOpen}
        onToggleArchive={() => setArchiveOpen(!archiveOpen)}
        onRefresh={() => api.refetchActive()}
      />

      <LibrarySearch search={search} />
      <GroupPlay group={group} />

      {archiveOpen && <ArchivePanel selected={selectedPaths} visible={visiblePaths} />}

      {stale && (
        <p className="dk-help dk-error-text">Hors ligne : liste servie par le cache local, peut être périmée.</p>
      )}
      {error && <p className="dk-error-text">Base illisible : {error}</p>}

      <div className="dk-table lib-table">
        <LibraryHeader sort={sort} onSort={column => setSort(s => nextSort(s, column))} />
        <LibraryList
          loading={loading}
          total={rows.length}
          displayed={displayed}
          checked={checked}
          onToggle={toggle}
          onPlay={play}
          onPlayGroup={playGroupOf}
          onMenu={openMenu}
        />
      </div>

      <p className="dk-help status" aria-live="polite">
        {summary}
      </p>

      <TrackMenu menu={menu} tracks={tracks} onClose={closeMenu} onPlay={play} />
    </section>
  );
}
