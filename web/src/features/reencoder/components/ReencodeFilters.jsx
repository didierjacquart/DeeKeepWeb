import { cx } from '../../../lib/cx';

/** Filtre « déjà au format cible » et compteur des fichiers affichés. */
export function ReencodeFilters({ hideTarget, onToggle, shown, total, target }) {
  return (
    <div className="dk-filters">
      <button
        className={cx('control-btn', hideTarget && 'checked')}
        aria-pressed={hideTarget}
        onClick={onToggle}
        data-tip="Masquer les fichiers déjà dans le format et la qualité définis dans les Paramètres (rien à réencoder)"
      >
        {hideTarget ? 'Afficher tous les fichiers' : 'Masquer ceux déjà au format cible'}
      </button>
      <span className="dk-help">
        {shown} fichier(s) affiché(s) sur {total} · cible : {target.format} · {target.quality}
      </span>
    </div>
  );
}
