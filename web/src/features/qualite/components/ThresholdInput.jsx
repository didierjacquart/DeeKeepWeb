import { useEffect, useEffectEvent, useRef } from 'react';

/**
 * Champ de seuil : comme l'original, la valeur n'est prise qu'à l'événement natif « change » (validation,
 * perte du focus, flèches) et non à chaque frappe ; le champ est réécrit quand la valeur retenue change.
 */
export function ThresholdInput({ id, field, value, onCommit }) {
  const input = useRef(null);
  const commit = useEffectEvent(raw => onCommit(raw));

  useEffect(() => {
    if (input.current) input.current.value = value;
  }, [value]);

  useEffect(() => {
    const el = input.current;
    if (!el) return undefined;
    const onChange = () => commit(el.value);
    el.addEventListener('change', onChange);
    return () => el.removeEventListener('change', onChange);
  }, []);

  return (
    <input
      ref={input}
      id={id}
      className="dk-input"
      type="number"
      min={field.min}
      max={field.max}
      step={field.step}
      defaultValue={value}
    />
  );
}
