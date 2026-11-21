import { usePermissions } from '../../../core/auth/usePermissions';

/**
 * Barre d'actions : sélection et vidage à gauche ; Arrêter, Analyser (accent) puis Retélécharger (principal).
 * @param {{
 *   scanning: boolean, busy: boolean, downloading: boolean, selectedCount: number,
 *   onCheckAll: () => void, onUncheckAll: () => void, onClear: () => void,
 *   onStop: () => void, onScan: () => void, onRedownload: () => void,
 * }} props
 */
export function QualityActions(props) {
  const { scanning, busy, downloading, selectedCount } = props;
  const { canDownload } = usePermissions();
  return (
    <div className="dk-actions">
      <button className="control-btn" onClick={props.onCheckAll}>
        Tout cocher
      </button>
      <button className="control-btn" onClick={props.onUncheckAll}>
        Tout décocher
      </button>
      <button
        className="control-btn"
        onClick={props.onClear}
        disabled={busy}
        data-tip="Retirer tous les résultats affichés (les fichiers du disque sont conservés)"
      >
        Vider la liste
      </button>
      <span className="spacer"></span>
      <button className="control-btn" onClick={props.onStop} disabled={!scanning}>
        Arrêter
      </button>
      <button
        className="accent-btn"
        onClick={props.onScan}
        disabled={busy}
        data-tip="Mesurer la qualité sonore de tous les fichiers du dossier"
      >
        Analyser
      </button>
      <button
        className="primary-btn"
        onClick={props.onRedownload}
        disabled={downloading || !canDownload}
        data-tip={
          canDownload
            ? "Réextraire les titres cochés et remplacer le fichier abîmé dans son dossier d'origine"
            : "Vous n'avez pas le droit de télécharger"
        }
      >
        Retélécharger la sélection{selectedCount > 0 && ` (${selectedCount})`}
      </button>
    </div>
  );
}
