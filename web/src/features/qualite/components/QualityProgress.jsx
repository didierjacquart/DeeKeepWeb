import { ProgressBar } from '../../../shared/components/ProgressBar';

/** Avancement de l'analyse, puis barre globale des retéléchargements en cours. */
export function QualityProgress({ scanning, progress, redownload }) {
  return (
    <>
      {scanning && <ProgressBar value={progress} label={null} />}

      {redownload && (
        <>
          <ProgressBar value={redownload.value} label={null} />
          <p className="status-line">
            {redownload.label} ({(redownload.value * 100).toFixed(0)} %)
          </p>
        </>
      )}
    </>
  );
}
