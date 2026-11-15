import { cx } from '../../../../lib/cx';

const SUMMARY_TIP =
  'Les titres déjà présents ne peuvent pas être sélectionnés ; les doublons possibles restent libres mais décochés.\n' +
  '« Tout sélectionner » ne coche ni les uns ni les autres.\n' +
  "« Voir les doublons » n'affiche que ces lignes ; « Dédoublonner… » montre les fichiers du disque en cause.";

/**
 * Au-dessus des pistes : « Tout sélectionner », filtre instantané, résumé des
 * doublons, « Voir les doublons » et « Dédoublonner… ».
 */
export function TrackFilters({
  allChecked,
  onSelectAll,
  filter,
  onFilter,
  present,
  possible,
  duplicatesOnly,
  onDuplicatesOnly,
  canDedupe,
  onDedupe,
}) {
  const duplicateTotal = present + possible;
  return (
    <div className="sd-filters dk-filters">
      <label className="dk-check">
        <input
          type="checkbox"
          className="dk-check"
          checked={allChecked}
          onChange={e => onSelectAll(e.target.checked)}
        />{' '}
        <strong>Tout sélectionner</strong>
      </label>
      <input
        className="dk-input sd-filter"
        type="search"
        placeholder="Filtrer instantanément les résultats..."
        aria-label="Filtrer les résultats"
        value={filter}
        onChange={e => onFilter(e.target.value)}
      />
      {duplicateTotal > 0 && (
        <>
          <span className="sd-summary" data-tip={SUMMARY_TIP}>
            {present > 0 && <span className="ok">✓ {present} déjà sur le disque</span>}
            {present > 0 && possible > 0 && ' · '}
            {possible > 0 && <span className="warn">≈ {possible} doublon(s) possible(s)</span>}
          </span>
          <button
            className={cx('control-btn small', duplicatesOnly && 'checked')}
            type="button"
            aria-pressed={duplicatesOnly}
            data-tip="N'afficher que les titres déjà sur le disque ou soupçonnés d'y être"
            onClick={onDuplicatesOnly}
          >
            Voir les doublons ({duplicateTotal})
          </button>
          {canDedupe && (
            <button className="control-btn small" type="button" onClick={onDedupe}>
              Dédoublonner…
            </button>
          )}
        </>
      )}
    </div>
  );
}
