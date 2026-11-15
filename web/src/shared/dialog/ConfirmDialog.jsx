import { DialogActions, DialogContent, DialogTitle } from './DialogParts';

/**
 * @typedef {object} ConfirmData
 * @property {string} title
 * @property {string} message
 * @property {string} [confirm] Libellé du bouton de confirmation (défaut : « Confirmer »).
 * @property {string} [cancel] Libellé du bouton d'annulation (défaut : « Annuler »).
 * @property {boolean} [danger] Bouton rouge : action destructrice.
 */

/** Question oui / non ; le bouton de confirmation prend le focus. */
export function ConfirmDialog({ data, close }) {
  return (
    <>
      <DialogTitle>{data.title}</DialogTitle>
      <DialogContent>
        <p className="dk-confirm-msg">{data.message}</p>
      </DialogContent>
      <DialogActions>
        <button className="control-btn" onClick={() => close(false)}>
          {data.cancel ?? 'Annuler'}
        </button>
        <button className={data.danger ? 'danger-btn' : 'primary-btn'} onClick={() => close(true)} data-autofocus>
          {data.confirm ?? 'Confirmer'}
        </button>
      </DialogActions>
    </>
  );
}
