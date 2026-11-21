import { usePermissions } from '../../../core/auth/usePermissions';

const NO_DOWNLOAD = "Vous n'avez pas le droit de télécharger";

/**
 * Barre d'actions de la file : manipulation de la liste à gauche, « Rechercher »
 * (accent) puis « Extraire » (principal) en dernier. Sans le droit de télécharger, les
 * actions sur la file sont désactivées (le serveur les refuserait).
 */
export function QueueActions({
  hasSelection,
  canRetry,
  canExtract,
  onSelectAll,
  onRemoveSelection,
  onPurge,
  onRetry,
  onSearch,
  onExtract,
}) {
  const { canDownload } = usePermissions();
  return (
    <div className="dk-actions">
      <button className="control-btn" onClick={() => onSelectAll(true)}>
        Tout cocher
      </button>
      <button className="control-btn" onClick={() => onSelectAll(false)}>
        Tout décocher
      </button>
      <button
        className="control-btn"
        disabled={!hasSelection || !canDownload}
        onClick={onRemoveSelection}
        data-tip={
          canDownload
            ? 'Retirer de la liste les lignes cochées (les fichiers déjà extraits sont conservés sur le disque)'
            : NO_DOWNLOAD
        }
      >
        Retirer la sélection
      </button>
      <span className="dk-spacer" />
      <button
        className="control-btn"
        onClick={onPurge}
        disabled={!canDownload}
        data-tip={canDownload ? 'Retirer de la liste toutes les lignes téléchargées avec succès' : NO_DOWNLOAD}
      >
        Purger les téléchargés
      </button>
      <button
        className="control-btn"
        disabled={!canRetry || !canDownload}
        onClick={onRetry}
        data-tip={canDownload ? 'Relancer les téléchargements en échec' : NO_DOWNLOAD}
      >
        Réessayer
      </button>
      <button className="accent-btn" onClick={onSearch} data-tip="Chercher des titres à ajouter à la liste">
        Rechercher
      </button>
      <button
        className="primary-btn"
        disabled={!canExtract || !canDownload}
        onClick={onExtract}
        data-tip={
          !canDownload
            ? NO_DOWNLOAD
            : hasSelection
              ? 'Télécharger les titres cochés'
              : 'Télécharger tous les titres de la liste'
        }
      >
        Extraire
      </button>
    </div>
  );
}
