import { DEFECTS } from '../quality-model';

/** Filtres « Afficher » : un défaut par case, plus les titres sans défaut. */
export function DefectFilters({ shown, onToggle, includeClean, onIncludeClean }) {
  return (
    <div className="dk-filters">
      <span>Afficher :</span>
      {DEFECTS.map(d => (
        <label key={d.id} className="dk-check">
          <input type="checkbox" checked={shown[d.id]} onChange={e => onToggle(d.id, e.target.checked)} />
          {d.label}
        </label>
      ))}
      <label className="dk-check" data-tip="Afficher aussi les titres qu'aucune mesure ne met en cause">
        <input type="checkbox" checked={includeClean} onChange={e => onIncludeClean(e.target.checked)} />
        Titres sans défaut
      </label>
    </div>
  );
}
