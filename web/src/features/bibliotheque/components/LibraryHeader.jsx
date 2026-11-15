import { cx } from '../../../lib/cx';
import { SortHeader } from '../../../shared/components/SortHeader';
import { sortMark } from '../library-model';

const COLUMNS = [
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

const ariaSort = (sort, key) => (sort.column === key ? (sort.desc ? 'descending' : 'ascending') : 'none');

/** En-têtes de colonnes ; un clic sur une colonne triable trie (deuxième clic : sens inverse). */
export function LibraryHeader({ sort, onSort }) {
  return (
    <div className="dk-thead lib-grid" role="row">
      {COLUMNS.map((c, i) =>
        c.key ? (
          <span
            key={i}
            className={cx(c.cls, sort.column === c.key && 'sorted')}
            role="columnheader"
            aria-sort={ariaSort(sort, c.key)}
          >
            <SortHeader
              className="fill"
              label={c.label}
              sorted={sort.column === c.key}
              mark={sortMark(sort, c.key)}
              onSort={() => onSort(c.key)}
            />
          </span>
        ) : (
          <span key={i} className={c.cls} role="columnheader">
            {c.label}
          </span>
        ),
      )}
    </div>
  );
}
