import { cx } from '../../../lib/cx';

/** Onglets « Recherches effectuées » / « Téléchargements réussis », avec leur nombre d'entrées. */
export function HistoryTabs({ tab, onChange, searchCount, downloadCount }) {
  return (
    <div className="tabs" role="tablist">
      <button
        role="tab"
        aria-selected={tab === 'searches'}
        className={cx(tab === 'searches' && 'active')}
        onClick={() => onChange('searches')}
      >
        Recherches<span className="dk-hide-mobile"> effectuées</span> ({searchCount})
      </button>
      <button
        role="tab"
        aria-selected={tab === 'downloads'}
        className={cx(tab === 'downloads' && 'active')}
        onClick={() => onChange('downloads')}
      >
        Téléchargements<span className="dk-hide-mobile"> réussis</span> ({downloadCount})
      </button>
    </div>
  );
}
