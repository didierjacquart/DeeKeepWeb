import { cx } from '../../lib/cx';

/**
 * En-tête de colonne triable : un bouton qui garde l'apparence du texte d'en-tête
 * (`.dk-thead`), en couleur d'accent quand la colonne est triée.
 *
 * @param {{ label: string, sorted?: boolean, mark?: string, tip?: string, className?: string, onSort: () => void }} props
 *   `mark` : flèche de sens (« ▲ », « ▼ »…) affichée après le libellé.
 */
export function SortHeader({ label, sorted = false, mark = '', tip, className, onSort }) {
  return (
    <button
      type="button"
      className={cx('dk-sort', className, sorted && 'sorted')}
      data-tip={tip || undefined}
      onClick={onSort}
    >
      {label}
      {mark && <span aria-hidden="true">{mark}</span>}
    </button>
  );
}
