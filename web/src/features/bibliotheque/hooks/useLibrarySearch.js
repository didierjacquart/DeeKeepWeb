import { useEffect, useMemo, useRef, useState } from 'react';
import { FILTER_DELAY_MS, MIN_SUGGEST_CHARS, buildValueIndex, completionFor, suggestionsFor } from '../library-model';

/**
 * Recherche de la Bibliothèque (titre, artiste, album) : texte saisi, texte appliqué au filtre
 * (après un différé de frappe de 220 ms, ou tout de suite sur Entrée, un choix ou un effacement),
 * propositions tirées de la bibliothèque parcourues au clavier, et saisie prédictive : la suite de
 * la meilleure proposition s'affiche dans le champ, Tab (ou → en fin de champ) l'accepte.
 */
export function useLibrarySearch(rows, includeMissing) {
  const [input, setInput] = useState('');
  /** Texte réellement appliqué (après le différé de frappe de 220 ms). */
  const [query, setQuery] = useState('');
  // Minuterie du différé : un choix ou Entrée l'annule et applique tout de suite.
  const filterTimer = useRef(null);
  useEffect(() => {
    return () => clearTimeout(filterTimer.current);
  }, []);

  const [suggestOpen, setSuggestOpen] = useState(false);
  /** Proposition choisie (artiste, album…) tant que le champ n'est pas retouché. */
  const [picked, setPicked] = useState(null);
  const [activeIndex, setActiveIndex] = useState(-1);
  const valueIndex = useMemo(() => buildValueIndex(rows, includeMissing), [rows, includeMissing]);
  const suggestions = useMemo(
    () => (input.trim().length < MIN_SUGGEST_CHARS || !suggestOpen ? [] : suggestionsFor(valueIndex, input)),
    [input, suggestOpen, valueIndex],
  );

  const predicted = useMemo(() => completionFor(suggestions, input, activeIndex), [suggestions, input, activeIndex]);

  const applyNow = value => {
    clearTimeout(filterTimer.current);
    setQuery(value);
  };

  const onInput = value => {
    setInput(value);
    setPicked(null);
    setActiveIndex(-1);
    setSuggestOpen(value.trim().length >= MIN_SUGGEST_CHARS);
    clearTimeout(filterTimer.current);
    filterTimer.current = setTimeout(() => setQuery(value), FILTER_DELAY_MS);
  };

  const clear = () => {
    setInput('');
    setPicked(null);
    setSuggestOpen(false);
    applyNow('');
  };

  /** Clic sur une proposition : le champ reçoit la valeur, sans nouvelles propositions. */
  const pick = s => {
    setSuggestOpen(false);
    setInput(s.value);
    setPicked(s);
    applyNow(s.value);
  };

  const onKeyDown = event => {
    const list = suggestions;
    if (event.key === 'ArrowDown' && list.length) {
      event.preventDefault();
      setActiveIndex(i => Math.min(list.length - 1, i + 1));
    } else if (event.key === 'ArrowUp' && list.length) {
      event.preventDefault();
      setActiveIndex(i => Math.max(0, i - 1));
    } else if (predicted && !event.shiftKey && (event.key === 'Tab' || (event.key === 'ArrowRight' && atEnd(event)))) {
      event.preventDefault();
      pick(predicted.suggestion);
    } else if (event.key === 'Escape' && list.length) {
      setSuggestOpen(false);
    } else if (event.key === 'Enter') {
      const chosen = list[activeIndex];
      if (chosen) pick(chosen);
      else {
        setSuggestOpen(false);
        applyNow(input);
      }
    }
  };

  return {
    input,
    query,
    picked,
    suggestions,
    activeIndex,
    predicted,
    onInput,
    onKeyDown,
    onBlur: () => setSuggestOpen(false),
    clear,
    pick,
  };
}

/** Curseur en fin de champ, sans sélection. */
function atEnd(event) {
  const el = event.currentTarget;
  return el.selectionStart === el.value.length && el.selectionEnd === el.value.length;
}
