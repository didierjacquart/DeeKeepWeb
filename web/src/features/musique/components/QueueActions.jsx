/**
 * Barre d'actions de la file : manipulation de la liste à gauche, « Rechercher »
 * (accent) puis « Extraire » (principal) en dernier.
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
        disabled={!hasSelection}
        onClick={onRemoveSelection}
        data-tip="Retirer de la liste les lignes cochées (les fichiers déjà extraits sont conservés sur le disque)"
      >
        Retirer la sélection
      </button>
      <span className="dk-spacer" />
      <button
        className="control-btn"
        onClick={onPurge}
        data-tip="Retirer de la liste toutes les lignes téléchargées avec succès"
      >
        Purger les téléchargés
      </button>
      <button
        className="control-btn"
        disabled={!canRetry}
        onClick={onRetry}
        data-tip="Relancer les téléchargements en échec"
      >
        Réessayer
      </button>
      <button className="accent-btn" onClick={onSearch} data-tip="Chercher des titres à ajouter à la liste">
        Rechercher
      </button>
      <button
        className="primary-btn"
        disabled={!canExtract}
        onClick={onExtract}
        data-tip={hasSelection ? 'Télécharger les titres cochés' : 'Télécharger tous les titres de la liste'}
      >
        Extraire
      </button>
    </div>
  );
}
