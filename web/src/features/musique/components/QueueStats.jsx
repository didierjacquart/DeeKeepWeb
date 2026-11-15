import { ProgressBar } from '../../../shared/components/ProgressBar';

/** « Statut actuel » : compteurs et progression globale de la file. */
export function QueueStats({ doneCount, failedCount, progress }) {
  return (
    <>
      <span className="dk-overline">Statut actuel</span>
      <div className="stats">
        <span className="num">{doneCount}</span> <span className="lbl">fichiers téléchargés</span>
        <span className="sep">|</span>
        <span className="num">{failedCount}</span> <span className="lbl">fichiers abandonnés</span>
      </div>
      <div className="global">
        <span className="dk-help">Progression globale du traitement :</span>
        <ProgressBar value={progress} large />
      </div>
    </>
  );
}
