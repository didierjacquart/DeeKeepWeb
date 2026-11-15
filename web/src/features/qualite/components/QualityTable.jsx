import { VirtualList } from '../../../shared/virtual-list/VirtualList';
import { rowJob, rowStatus } from '../quality-model';
import { QualityRow } from './QualityRow';
import { QualityTableHeader } from './QualityTableHeader';
import './quality-table.scss';

/**
 * Tableau des résultats (cartes sous 900 px, sans en-têtes) ; seules les lignes visibles sont dessinées.
 * @param {{
 *   list: object[], compact: boolean, empty: string, sort: { key: string, asc: boolean }, onSort: (key: string) => void,
 *   selected: Set<string>, redownloadById: Map<string, object>, onToggle: (row: object, checked: boolean) => void,
 *   onPlay: (row: object) => void, onMenu: (row: object, at: { x: number, y: number }) => void,
 * }} props
 */
export function QualityTable({
  list,
  compact,
  empty,
  sort,
  onSort,
  selected,
  redownloadById,
  onToggle,
  onPlay,
  onMenu,
}) {
  return (
    <div className="dk-table dk-quality-table">
      <div className="scroller">
        <div className="inner">
          {!compact && <QualityTableHeader sort={sort} onSort={onSort} />}
          {list.length === 0 ? (
            <div className="dk-empty">{empty}</div>
          ) : (
            <VirtualList
              items={list}
              itemHeight={compact ? 118 : 49}
              itemKey={r => r.path}
              renderItem={r => (
                <QualityRow
                  row={r}
                  selected={selected.has(r.path)}
                  status={rowStatus(r, rowJob(r, redownloadById))}
                  onToggle={checked => onToggle(r, checked)}
                  onPlay={() => onPlay(r)}
                  onMenu={at => onMenu(r, at)}
                />
              )}
              className="vp"
            />
          )}
        </div>
      </div>
    </div>
  );
}
