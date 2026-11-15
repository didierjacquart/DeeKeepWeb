import { VirtualList } from '../../../../shared/virtual-list/VirtualList';
import { SortHeader } from '../../../../shared/components/SortHeader';
import { entityLabel } from '../search-model';
import { Thumb } from './Thumb';
import './search-tables.scss';

/**
 * Artistes, albums ou playlists trouvés ; « Voir le top 50 » / « Ouvrir l'album » les explore.
 * @param {{ entities: object[], firstKind?: string, entitySort: { column: string, asc: boolean } | null,
 *   onSort: (column: string) => void, onOpen: (entity: object) => void }} props
 */
export function EntityTable({ entities, firstKind, entitySort, onSort, onOpen }) {
  return (
    <div className="dk-table sd-table">
      <div className="dk-thead sd-ent-grid">
        <span />
        <SortHeader
          label={firstKind === 'ALBUM' ? 'ALBUM' : 'ARTISTE'}
          sorted={entitySort?.column === 'name'}
          onSort={() => onSort('name')}
        />
        <SortHeader
          label="DÉTAILS"
          className="dk-hide-mobile"
          sorted={entitySort?.column === 'subtitle'}
          onSort={() => onSort('subtitle')}
        />
        <span />
      </div>
      <VirtualList
        className="sd-viewport"
        items={entities}
        itemHeight={66}
        itemKey={e => e.id}
        renderItem={e => (
          <div className="dk-row sd-ent-grid sd-row-h">
            <Thumb url={e.picture} fallback="⏳" />
            <span className="sd-title-cell">
              <span className="sd-strong">{e.name}</span>
              <span className="dk-cell-sub dk-hide-desktop">{e.subtitle}</span>
            </span>
            <span className="dk-hide-mobile dk-muted">{e.subtitle}</span>
            <button className="primary-btn small" type="button" onClick={() => onOpen(e)}>
              {entityLabel(e)}
            </button>
          </div>
        )}
      />
    </div>
  );
}
