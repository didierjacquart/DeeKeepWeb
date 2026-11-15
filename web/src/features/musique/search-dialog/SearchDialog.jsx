import { cx } from '../../../lib/cx';
import { AlbumHeader } from './components/AlbumHeader';
import { BrowseNav } from './components/BrowseNav';
import { EntityTable } from './components/EntityTable';
import { LinkSearch } from './components/LinkSearch';
import { SearchBar } from './components/SearchBar';
import { TrackFilters } from './components/TrackFilters';
import { TrackTable } from './components/TrackTable';
import { useSearchDialog } from './hooks/useSearchDialog';
import { useSearchView } from './hooks/useSearchView';
import './search-dialog.scss';

/**
 * Fenêtre « Rechercher et ajouter des pistes » (spec 01 §2.4) : recherche Deezer (titre,
 * artiste, album, playlist, lien), exploration des entités avec fil d'Ariane, pastilles de
 * présence qui verrouillent ou décochent les cases, écoute d'extrait, dédoublonnage.
 * Les modifications de l'historique des recherches sont conservées même si la fenêtre est annulée.
 *
 * @param {{ initialQuery: string, close: import('../../../shared/dialog/dialog-stack').CloseDialog }} props
 */
export function SearchDialog({ initialQuery, close }) {
  const dialog = useSearchDialog({ initialQuery, close });
  const { online, catalog } = dialog;
  const { state } = catalog;
  const view = useSearchView(state);
  const showTracks = state.mode === 'tracks' && state.tracks.length > 0;
  const pickedCount = state.picked.size;

  return (
    <div className="dk-search-dialog">
      <div className="sd">
        <div className="sd-head">
          <h2 className="sd-title">Rechercher et ajouter des pistes</h2>
          <SearchBar dialog={dialog} />
          <LinkSearch disabled={state.loading || !online} onSearch={dialog.search} />

          {!online && (
            <p className="sd-note">
              Hors ligne : la recherche Deezer n'est pas disponible. Les titres déjà affichés restent consultables.
            </p>
          )}
          {state.loading && (
            <div className="sd-progress">
              <div className="dk-progress indeterminate">
                <div className="fill" />
              </div>
            </div>
          )}

          <BrowseNav canGoBack={state.stack.length > 0} crumb={state.crumb} onBack={catalog.back} />
          {state.header && <AlbumHeader header={state.header} />}

          {showTracks && (
            <TrackFilters
              allChecked={view.allChecked}
              onSelectAll={on => catalog.selectAll(view.visible, on)}
              filter={state.filter}
              onFilter={catalog.setFilter}
              present={view.present}
              possible={view.possible}
              duplicatesOnly={state.duplicatesOnly}
              onDuplicatesOnly={catalog.toggleDuplicatesOnly}
              canDedupe={view.dedupCandidates.length > 0}
              onDedupe={() => void catalog.dedupe(view.dedupCandidates)}
            />
          )}
        </div>

        <div className="sd-body">
          {state.message && (
            <div className={cx('sd-message', state.message.error && 'error')}>
              <strong>{state.message.title}</strong>
              <span>{state.message.text}</span>
            </div>
          )}

          {state.mode === 'entities' && (
            <EntityTable
              entities={view.shownEntities}
              firstKind={state.entities[0]?.kind}
              entitySort={state.entitySort}
              onSort={catalog.sortEntities}
              onOpen={catalog.openEntity}
            />
          )}

          {showTracks && (
            <TrackTable
              tracks={view.visible}
              picked={state.picked}
              sortColumn={state.sortColumn}
              sortAsc={state.sortAsc}
              onSort={catalog.sortBy}
              onToggle={catalog.toggle}
              onArtist={catalog.openArtist}
              onAlbum={catalog.openAlbum}
            />
          )}
        </div>

        <div className="sd-foot">
          <button className="control-btn" type="button" onClick={dialog.cancel}>
            Annuler
          </button>
          <button className="primary-btn" type="button" disabled={pickedCount === 0} onClick={dialog.add}>
            Ajouter la sélection{pickedCount > 0 ? ' (' + pickedCount + ')' : ''}
          </button>
        </div>
      </div>
    </div>
  );
}
