/**
 * Barre d'actions : manipulation de la liste à gauche, ajouts puis « Convertir » à droite.
 * @param {{ busy: boolean, scanning: boolean, actions: ReturnType<typeof import('../hooks/useReencodeActions').useReencodeActions> }} props
 */
export function ReencodeActions({ busy, scanning, actions }) {
  return (
    <div className="dk-actions">
      <button className="control-btn" onClick={() => actions.selectAll(true)}>
        Tout cocher
      </button>
      <button className="control-btn" onClick={() => actions.selectAll(false)}>
        Tout décocher
      </button>
      <button
        className="control-btn"
        onClick={() => void actions.removeSelection()}
        data-tip="Retirer de la liste les fichiers cochés (les fichiers du disque sont conservés)"
      >
        Retirer la sélection
      </button>
      <span className="dk-spacer" />
      <button
        className="control-btn"
        onClick={() => void actions.purge()}
        data-tip="Retirer de la liste les fichiers réencodés avec succès"
      >
        Purger les convertis
      </button>
      <button
        className="control-btn"
        disabled={scanning}
        onClick={() => void actions.add('folder')}
        data-tip="Ajouter tout le contenu audio d'un dossier et de ses sous-dossiers"
      >
        Ajouter un dossier
      </button>
      <button
        className="accent-btn"
        disabled={scanning}
        onClick={() => void actions.add('files')}
        data-tip="Parcourir et ajouter des fichiers audio"
      >
        Ajouter des fichiers
      </button>
      <button
        className="primary-btn"
        disabled={busy}
        onClick={() => void actions.convert()}
        data-tip="Réextraire et réencoder les fichiers sélectionnés, puis les remplacer dans leur dossier d'origine"
      >
        Convertir
      </button>
    </div>
  );
}
