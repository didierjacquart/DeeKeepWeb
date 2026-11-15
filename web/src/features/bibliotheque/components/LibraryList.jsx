import { useMobile } from '../../../lib/hooks/useMediaQuery';
import { VirtualList } from '../../../shared/virtual-list/VirtualList';
import { LibraryRow } from './LibraryRow';

/**
 * Corps du tableau : chargement, liste vide ou liste virtuelle des titres retenus.
 * La hauteur de ligne suit la mise en page (56 px sous 900 px, voir la feuille de style).
 */
export function LibraryList({ loading, total, displayed, checked, onToggle, onPlay, onPlayGroup, onMenu }) {
  const mobile = useMobile();
  if (loading) return <div className="dk-empty">Chargement de la bibliothèque…</div>;
  if (!displayed.length) {
    return (
      <div className="dk-empty">
        {total
          ? 'Aucun titre ne correspond à la recherche.'
          : 'Aucun titre : lancez « Relire la bibliothèque » dans les Paramètres.'}
      </div>
    );
  }
  return (
    <VirtualList
      items={displayed}
      itemHeight={mobile ? 56 : 44}
      itemKey={r => r.track.path}
      renderItem={r => (
        <LibraryRow
          track={r.track}
          checked={checked.has(r.track.path)}
          onToggle={onToggle}
          onPlay={onPlay}
          onPlayGroup={onPlayGroup}
          onMenu={onMenu}
        />
      )}
      className="viewport"
    />
  );
}
