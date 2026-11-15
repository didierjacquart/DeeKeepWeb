import { openDialog } from '../../shared/dialog/dialog-stack';
import { coverUrl } from '../../shared/urls';
import { CoverPopup } from './components/CoverPopup';

/** Agrandit la jaquette d'une entrée de l'historique des téléchargements. */
export function openCoverPopup(e) {
  const data = { title: e.title ?? '', artist: e.artist ?? '', src: coverUrl(e.coverSha1) };
  return openDialog(close => <CoverPopup {...data} close={close} />, { width: 'min(420px, 94vw)' });
}
