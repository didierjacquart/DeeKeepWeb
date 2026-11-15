import { useId } from 'react';
import { SuggestionList } from './SuggestionList';

/**
 * Champ de recherche (titre, artiste, album) et ses propositions (liste déroulante parcourue
 * au clavier : le focus reste dans le champ, la proposition active est annoncée). La saisie
 * prédictive s'affiche en gris derrière la frappe ; Tab l'accepte.
 *
 * @param {{ search: ReturnType<typeof import('../hooks/useLibrarySearch').useLibrarySearch> }} props
 */
export function LibrarySearch({ search }) {
  const listId = useId();
  const { input, suggestions, activeIndex, predicted } = search;
  const expanded = suggestions.length > 0;

  return (
    <div className="search">
      <input
        className="dk-input"
        type="search"
        role="combobox"
        autoComplete="off"
        spellCheck={false}
        aria-label="Rechercher"
        aria-autocomplete="list"
        aria-expanded={expanded}
        aria-controls={expanded ? listId : undefined}
        aria-activedescendant={expanded && activeIndex >= 0 ? `${listId}-${activeIndex}` : undefined}
        placeholder="Rechercher un titre, un artiste ou un album…"
        value={input}
        onChange={e => search.onInput(e.target.value)}
        onKeyDown={search.onKeyDown}
        onBlur={search.onBlur}
      />
      {predicted && (
        <div className="predict" aria-hidden="true">
          <span className="typed">{input}</span>
          <span className="rest">{predicted.rest}</span>
          <kbd>Tab</kbd>
        </div>
      )}
      {input && (
        <button className="icon-btn clear" onClick={search.clear} aria-label="Effacer la recherche">
          ✕
        </button>
      )}
      {expanded && (
        <SuggestionList
          id={listId}
          typed={input}
          suggestions={suggestions}
          activeIndex={activeIndex}
          onPick={search.pick}
        />
      )}
    </div>
  );
}
