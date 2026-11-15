import { useNavigate } from 'react-router';
import { clearSearches, deleteSearch, relaunchSearch } from '../history-actions';
import { searchSortMark } from '../history-sort';
import { SearchRow } from './SearchRow';
import { SortHeader } from '../../../shared/components/SortHeader';

/** Onglet des recherches effectuées : relançables, supprimables une à une ou toutes ensemble. */
export function SearchesTab({ view, coverOf, count, loading }) {
  const navigate = useNavigate();
  const { searchSort, shownSearches } = view;
  return (
    <>
      <div className="dk-actions">
        <span className="dk-spacer" />
        <button className="danger-btn" disabled={!count} onClick={() => void clearSearches()}>
          Vider les recherches
        </button>
      </div>
      <div className="dk-table">
        <div className="dk-thead g-search" role="row">
          <SortHeader
            label="Recherche"
            mark={searchSortMark(searchSort)}
            sorted={!!searchSort}
            onSort={view.toggleSearchSort}
          />
          <span className="dk-hide-mobile">Catégorie</span>
          <span>Jaquette</span>
          <span>Action</span>
        </div>
        {shownSearches.map(s => (
          <SearchRow
            key={s.query}
            search={s}
            cover={coverOf(s.query)}
            onRelaunch={() => void relaunchSearch(s.query, navigate)}
            onDelete={() => void deleteSearch(s.query)}
          />
        ))}
        {!shownSearches.length && <div className="dk-empty">{loading ? 'Chargement…' : 'Aucune recherche.'}</div>}
      </div>
    </>
  );
}
