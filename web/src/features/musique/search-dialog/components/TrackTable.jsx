import { useStore } from '../../../../lib/store';
import { player } from '../../../../core/player/player';
import { VirtualList } from '../../../../shared/virtual-list/VirtualList';
import { SortHeader } from '../../../../shared/components/SortHeader';
import { deezerPreview, previewId } from '../../deezer-preview';
import { sortArrow } from '../../sort-rows';
import { TrackRow } from './TrackRow';
import './search-tables.scss';

/** Colonnes triables : clé, libellé, classes. */
const COLUMNS = [
  ['title', 'TITRE', undefined],
  ['artist', 'ARTISTE', 'dk-hide-mobile'],
  ['album', 'ALBUM', 'dk-hide-mobile'],
  ['duration', 'DURÉE', 'dk-hide-mobile sd-center'],
  ['rank', 'POP.', 'dk-hide-mobile'],
];

/**
 * Pistes trouvées (liste virtuelle), triables par colonne.
 * @param {{ tracks: object[], picked: Map<number, object>, sortColumn: string | null, sortAsc: boolean,
 *   onSort: (column: string) => void, onToggle: (track: object, on: boolean) => void,
 *   onArtist: (track: object) => void, onAlbum: (track: object) => void }} props
 */
export function TrackTable({ tracks, picked, sortColumn, sortAsc, onSort, onToggle, onArtist, onAlbum }) {
  const preview = useStore(player, s => s.preview);
  const previewPlaying = useStore(player, s => s.previewPlaying);

  const listen = t => {
    if (t.previewUrl) player.togglePreview(deezerPreview(t));
  };

  return (
    <div className="dk-table sd-table">
      <div className="dk-thead sd-grid">
        <span />
        <span className="dk-hide-mobile" />
        {COLUMNS.map(([column, label, className]) => (
          <SortHeader
            key={column}
            label={label}
            className={className}
            sorted={sortColumn === column}
            mark={sortArrow(column, sortColumn, sortAsc)}
            onSort={() => onSort(column)}
          />
        ))}
        <span className="sd-center">ÉCOUTE</span>
      </div>
      <VirtualList
        className="sd-viewport"
        items={tracks}
        itemHeight={64}
        itemKey={t => t.id}
        renderItem={t => (
          <TrackRow
            track={t}
            picked={picked.has(t.id)}
            playing={preview?.id === previewId(t) && previewPlaying}
            onToggle={on => onToggle(t, on)}
            onListen={() => listen(t)}
            onArtist={() => onArtist(t)}
            onAlbum={() => onAlbum(t)}
          />
        )}
      />
    </div>
  );
}
