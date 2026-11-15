import { DEEP_TIP, THRESHOLD_FIELDS_UI } from '../quality-thresholds';
import { ThresholdField } from './ThresholdField';
import './thresholds-panel.scss';

/** Volet « Seuils » : analyse approfondie (craquements) et seuils de détection de chaque défaut. */
export function ThresholdsPanel({ thresholds, scanning, onDeep, onThreshold }) {
  return (
    <div className="dk-card dk-quality-thresholds">
      {thresholds ? (
        <>
          <label className="dk-check deep" data-tip={DEEP_TIP}>
            <input
              type="checkbox"
              checked={thresholds.deep}
              disabled={scanning}
              onChange={e => onDeep(e.target.checked)}
            />
            Détecter les craquements (analyse ~4× plus lente)
          </label>
          <div className="grid">
            {THRESHOLD_FIELDS_UI.map(f => (
              <ThresholdField
                key={f.key}
                field={f}
                value={thresholds[f.key] ?? 0}
                onCommit={raw => onThreshold(f, raw)}
              />
            ))}
          </div>
          <p className="note">Modifier un seuil réévalue aussitôt l'analyse déjà faite : aucun fichier n'est relu.</p>
        </>
      ) : (
        <p className="note">Seuils indisponibles : le service Qualité ne répond pas.</p>
      )}
    </div>
  );
}
