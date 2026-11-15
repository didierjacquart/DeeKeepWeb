import { cx } from '../../../lib/cx';
import { OPTION_CHECKS } from '../organize-options';
import { DiversField } from './DiversField';
import { PatternField } from './PatternField';

/** Volet Filtres : options de rangement, motif de nommage, seuil « Divers » et « Cocher ce qui est à faire ». */
export function OptionsPanel({ options, nameTags, preview, onOption, canCheckPending, onCheckPending }) {
  return (
    <div className="dk-card filters">
      <div className="grid">
        {OPTION_CHECKS.map(c => (
          <label key={c.key} className="dk-check" data-tip={c.tip}>
            <input
              type="checkbox"
              className="dk-check"
              checked={options[c.key]}
              onChange={() => onOption(c.key, !options[c.key])}
            />{' '}
            {c.label}
          </label>
        ))}
      </div>

      <PatternField
        value={options.pattern}
        enabled={options.rename}
        nameTags={nameTags}
        onChange={pattern => onOption('pattern', pattern)}
      />
      <p className={cx('preview', preview?.warning && 'warn')}>{preview?.text}</p>

      <DiversField options={options} onOption={onOption} />

      <div className="pending">
        <button className="control-btn" onClick={onCheckPending} disabled={!canCheckPending}>
          Cocher ce qui est à faire
        </button>
        <span className="dk-help">décoche les fichiers déjà en place dont les tags sont complets</span>
      </div>
    </div>
  );
}
