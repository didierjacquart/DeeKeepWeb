import { SortHeader } from '../../../shared/components/SortHeader';
import { COLUMNS } from '../quality-columns';
import { sortMark } from '../quality-sort';

/** En-têtes de colonnes : un clic trie, un second inverse le sens. */
export function QualityTableHeader({ sort, onSort }) {
  return (
    <div className="dk-thead grid-cols dk-hide-mobile">
      {COLUMNS.map(c =>
        c.key ? (
          <SortHeader
            key={c.cls}
            label={c.label}
            className={c.cls}
            tip={c.tip}
            sorted={sort.key === c.key}
            mark={sortMark(sort, c.key)}
            onSort={() => onSort(c.key)}
          />
        ) : (
          <span key={c.cls} className={c.cls}>
            {c.label}
          </span>
        ),
      )}
    </div>
  );
}
