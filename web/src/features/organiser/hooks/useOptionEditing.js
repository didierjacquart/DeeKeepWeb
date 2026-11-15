import { useEffect, useRef } from 'react';
import { fetchPreview, saveOptions } from '../organiser-api';
import { optionsInput } from '../organize-options';

/**
 * Modification d'une option du volet Filtres : l'aperçu du nom suit tout de suite, le plan est recalculé
 * (sans toucher aux cases) 350 ms après la dernière modification, et l'option est enregistrée.
 * @returns {(key: string, value: unknown) => void}
 */
export function useOptionEditing({ options, setOptions, setPreview, setMessage, live, loadPlan }) {
  const timers = useRef({ replan: null, preview: null });

  useEffect(() => {
    const t = timers.current;
    return () => {
      clearTimeout(t.replan);
      clearTimeout(t.preview);
    };
  }, []);

  const schedulePreview = () => {
    clearTimeout(timers.current.preview);
    timers.current.preview = setTimeout(async () => {
      try {
        const { options: o, session } = live.current;
        setPreview(await fetchPreview(session?.id || null, o.pattern, o.rename));
      } catch {
        /* l'aperçu viendra avec le plan */
      }
    }, 120);
  };

  const commitOptions = async () => {
    await saveOptions(optionsInput(live.current.options));
    try {
      const { session, entries, options: o } = live.current;
      await loadPlan(session, entries, o);
    } catch {
      setMessage('Plan non recalculé (connexion perdue).');
    }
  };

  return (key, value) => {
    const next = { ...options, [key]: value };
    setOptions(next);
    // L'aperçu suit tout de suite, le plan 350 ms après la dernière modification.
    if (key === 'pattern' || key === 'rename') schedulePreview();
    if (key === 'filtersOpen') {
      void saveOptions(optionsInput(next));
      return;
    }
    clearTimeout(timers.current.replan);
    timers.current.replan = setTimeout(() => void commitOptions(), 350);
  };
}
