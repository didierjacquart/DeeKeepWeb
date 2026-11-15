import { Thumb } from './Thumb';
import './history-panel.scss';

/**
 * Panneau « Dernières recherches » sous le champ. Un clic dans le panneau ne fait
 * pas perdre le focus au champ (sinon le panneau se refermerait avant le clic).
 * @param {{ recent: { query: string }[], covers: Map<string, string>, onPick: (query: string) => void,
 *   onRemove: (entry: object, event: Event) => void, onClear: (event: Event) => void }} props
 */
export function HistoryPanel({ recent, covers, onPick, onRemove, onClear }) {
  return (
    <div className="sd-history" role="presentation" onMouseDown={e => e.preventDefault()}>
      <div className="sd-history-head">
        <strong>Dernières recherches</strong>
        <button
          className="icon-btn"
          type="button"
          data-tip="Effacer les dernières recherches"
          aria-label="Effacer les dernières recherches"
          onClick={onClear}
        >
          🗑
        </button>
      </div>
      {recent.map(entry => (
        <div key={entry.query} className="sd-history-row">
          <button className="sd-history-pick" type="button" onClick={() => onPick(entry.query)}>
            <Thumb url={covers.get(entry.query)} fallback="🎵" className="sd-thumb-sm" />
            <span className="sd-history-text">
              <span className="sd-history-q">{entry.query}</span>
              <span className="sd-history-sub">Recherche</span>
            </span>
          </button>
          <button
            className="icon-btn"
            type="button"
            data-tip="Retirer cette recherche"
            aria-label="Retirer cette recherche"
            onClick={e => onRemove(entry, e)}
          >
            ✕
          </button>
        </div>
      ))}
    </div>
  );
}
