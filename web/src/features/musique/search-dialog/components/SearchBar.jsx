import { KIND_OPTIONS } from '../search-model';
import { HistoryPanel } from './HistoryPanel';

/**
 * Champ de recherche (avec l'historique en dessous), type de recherche et bouton « Rechercher ».
 * @param {{ dialog: ReturnType<typeof import('../hooks/useSearchDialog').useSearchDialog> }} props
 */
export function SearchBar({ dialog }) {
  const { online, query, kind, setKind, inputRef, history, search, onQueryInput, onQueryKeyDown } = dialog;
  const loading = dialog.catalog.state.loading;
  return (
    <>
      <label className="sd-label" htmlFor="sd-q">
        Rechercher par morceau, artiste, album ou playlist :
      </label>
      <div className="sd-row">
        <div className="sd-field">
          <input
            id="sd-q"
            ref={inputRef}
            className="dk-input"
            type="search"
            autoComplete="off"
            enterKeyHint="search"
            placeholder="Saisissez un titre, un artiste, ou collez un lien de playlist..."
            value={query}
            onChange={e => onQueryInput(e.target.value)}
            onFocus={history.openPanel}
            onClick={history.openPanel}
            onBlur={history.closeSoon}
            onKeyDown={onQueryKeyDown}
          />
          {history.open && (
            <HistoryPanel
              recent={history.recent}
              covers={history.covers}
              onPick={q => void search(q)}
              onRemove={(entry, e) => void history.remove(entry, e)}
              onClear={e => void history.clear(e)}
            />
          )}
        </div>
        <select
          className="dk-select sd-kind"
          aria-label="Type de recherche"
          value={kind}
          onChange={e => setKind(e.target.value)}
        >
          {KIND_OPTIONS.map(k => (
            <option key={k.value} value={k.value}>
              {k.label}
            </option>
          ))}
        </select>
        <button
          className="accent-btn"
          type="button"
          disabled={loading || !online || !query.trim()}
          onClick={() => void search()}
        >
          {loading ? 'Recherche...' : 'Rechercher'}
        </button>
      </div>
    </>
  );
}
