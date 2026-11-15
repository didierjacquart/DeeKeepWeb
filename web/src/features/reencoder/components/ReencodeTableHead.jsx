import { SortHeader } from '../../../shared/components/SortHeader';

/** En-têtes du tableau ; les colonnes de texte, de format et de correspondance sont triables (▲ / ▼). */
export function ReencodeTableHead({ sortKey, sortAsc, onSort }) {
  const sortProps = column => ({
    sorted: sortKey === column,
    mark: sortKey === column ? (sortAsc ? ' ▲' : ' ▼') : '',
    onSort: () => onSort(column),
  });
  return (
    <div className="dk-thead grid">
      <span />
      <span>Écoute</span>
      <span className="dk-hide-mobile">Jaquette</span>
      <SortHeader label="Titre" {...sortProps('title')} />
      <SortHeader label="Artiste" className="dk-hide-mobile" {...sortProps('artist')} />
      <SortHeader label="Fichier" className="dk-hide-mobile" {...sortProps('path')} />
      <SortHeader label="Format / débit" className="dk-hide-mobile" {...sortProps('format')} />
      <SortHeader label="Correspondance" className="dk-hide-mobile" {...sortProps('match')} />
      <span className="dk-hide-mobile">Statut</span>
    </div>
  );
}
