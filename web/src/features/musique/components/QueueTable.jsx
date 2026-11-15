import { useStore } from '../../../lib/store';
import { player } from '../../../core/player/player';
import { isPlaying, listenRow } from '../music-listen';
import { sortArrow } from '../sort-rows';
import { QueueRow } from './QueueRow';
import { SortHeader } from '../../../shared/components/SortHeader';

/** Colonnes triables : clé de tri, libellé, masquée sur mobile. */
const COLUMNS = [
  ['id', 'ID', true],
  ['title', 'Titre', false],
  ['artist', 'Artiste', true],
  ['album', 'Album', true],
  ['duration', 'Durée / progression', true],
  ['status', 'Statut', true],
];

/**
 * Tableau de la file (lignes déjà triées).
 * @param {{ rows: object[], selected: Set<string>, verdicts: Map<string, object>, sortKey: string | null,
 *   sortAsc: boolean, onSort: (key: string) => void, onToggle: (id: string) => void, onCancel: (row: object) => void }} props
 */
export function QueueTable({ rows, selected, verdicts, sortKey, sortAsc, onSort, onToggle, onCancel }) {
  const playerState = useStore(player);
  return (
    <div className="dk-table">
      <div className="dk-thead grid">
        <span />
        <span className="dk-hide-mobile">Jaquette</span>
        {COLUMNS.map(([key, label, mobileHidden]) => (
          <SortHeader
            key={key}
            label={label}
            className={mobileHidden ? 'dk-hide-mobile' : undefined}
            sorted={sortKey === key}
            mark={sortArrow(key, sortKey, sortAsc)}
            onSort={() => onSort(key)}
          />
        ))}
        <span>Écoute</span>
      </div>
      {rows.map(r => (
        <QueueRow
          key={r.id}
          row={r}
          selected={selected.has(r.id)}
          playing={isPlaying(r, playerState)}
          presence={verdicts.get(r.id)}
          onToggle={() => onToggle(r.id)}
          onListen={fromButton => listenRow(r, rows, fromButton)}
          onCancel={() => void onCancel(r)}
        />
      ))}
      {!rows.length && <div className="dk-empty">Aucun titre. « Rechercher » permet d'en ajouter à la liste.</div>}
    </div>
  );
}
