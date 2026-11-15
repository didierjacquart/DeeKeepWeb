import { DialogActions, DialogContent, DialogTitle } from '../../../shared/dialog/DialogParts';
import './cover-popup.scss';

/** Agrandissement de la jaquette : titre, artiste, image 320 px. */
export function CoverPopup({ title, artist, src, close }) {
  return (
    <div className="dk-cover-popup">
      <DialogTitle>
        {title} — {artist}
      </DialogTitle>
      <DialogContent className="popup">
        {src ? (
          <img src={src} width="320" height="320" alt="Jaquette" />
        ) : (
          <p className="dk-faint">Aucune jaquette disponible</p>
        )}
        <p>
          <strong>{title}</strong>
          <br />
          {artist}
        </p>
      </DialogContent>
      <DialogActions>
        <button className="control-btn" onClick={() => close()}>
          Fermer
        </button>
      </DialogActions>
    </div>
  );
}
