import { useId } from 'react';
import { ThresholdInput } from './ThresholdInput';

/** Un seuil du volet : libellé, champ numérique et unité ; l'infobulle explique la mesure. */
export function ThresholdField({ field, value, onCommit }) {
  const id = useId();
  return (
    <label className="field" htmlFor={id} data-tip={field.tip}>
      <span className="lbl">{field.label}</span>
      <span className="inp">
        <ThresholdInput id={id} field={field} value={value.toFixed(field.decimals)} onCommit={onCommit} />
        <span className="unit">{field.suffix}</span>
      </span>
    </label>
  );
}
