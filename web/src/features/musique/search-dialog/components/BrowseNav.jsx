/** « ← Retour » et fil d'Ariane de l'exploration (artiste, album, playlist). */
export function BrowseNav({ canGoBack, crumb, onBack }) {
  if (!canGoBack && !crumb) return null;
  return (
    <div className="sd-nav">
      {canGoBack && (
        <button className="control-btn small" type="button" onClick={onBack}>
          ← Retour
        </button>
      )}
      <span className="sd-crumb">{crumb}</span>
    </div>
  );
}
