import { cx } from '../../lib/cx';

/** Titre d'une boîte de dialogue. */
export function DialogTitle({ children }) {
  return <h2 className="dk-dialog-title">{children}</h2>;
}

/** Contenu défilant d'une boîte de dialogue. */
export function DialogContent({ children, className }) {
  return <div className={cx('dk-dialog-content', className)}>{children}</div>;
}

/** Boutons d'une boîte de dialogue, alignés à droite. */
export function DialogActions({ children }) {
  return <div className="dk-dialog-actions">{children}</div>;
}
