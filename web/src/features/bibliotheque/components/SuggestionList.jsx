import { cx } from '../../../lib/cx';
import { countLabel } from '../listen-groups';
import { highlightParts, suggestionText } from '../library-model';

/** Valeur proposée, la partie qui correspond à la frappe surlignée. */
function Highlighted({ value, typed }) {
  const [before, match, after] = highlightParts(value, typed);
  return (
    <span className="value">
      {before}
      {match && <mark>{match}</mark>}
      {after}
    </span>
  );
}

/**
 * Propositions sous le champ de recherche : nature (titre, artiste, album), valeur et nombre de
 * titres. Le choix se fait dès l'appui de la souris (avant que le champ ne perde le focus et ne
 * ferme la liste).
 */
export function SuggestionList({ id, typed, suggestions, activeIndex, onPick }) {
  return (
    <ul className="suggest" role="listbox" id={id} aria-label="Propositions">
      {suggestions.map((s, i) => (
        <li
          key={s.key}
          id={`${id}-${i}`}
          role="option"
          tabIndex={-1}
          className={cx(i === activeIndex && 'active')}
          aria-selected={i === activeIndex}
          aria-label={suggestionText(s)}
          onMouseDown={e => {
            onPick(s);
            e.preventDefault();
          }}
        >
          <span className="kind">{s.label}</span>
          <Highlighted value={s.value} typed={typed} />
          <span className="count">{countLabel(s.count)}</span>
        </li>
      ))}
    </ul>
  );
}
