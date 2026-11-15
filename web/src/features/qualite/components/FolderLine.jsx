/** Dossier analysé, bouton du volet « Seuils » et changement de dossier. */
export function FolderLine({ folder, panelOpen, onTogglePanel, onChangeFolder, busy }) {
  return (
    <div className="folder-line">
      <span className="folder" data-tip={folder || undefined}>
        Dossier analysé : <b>{folder || '—'}</b>
      </span>
      <button
        className="control-btn"
        onClick={onTogglePanel}
        aria-expanded={panelOpen}
        data-tip="Afficher ou masquer les seuils de détection"
      >
        {panelOpen ? '▾' : '▸'} Seuils
      </button>
      <button className="control-btn" onClick={onChangeFolder} disabled={busy}>
        Modifier le dossier
      </button>
    </div>
  );
}
