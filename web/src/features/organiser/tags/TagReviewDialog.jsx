import { useMemo, useState } from 'react';
import { jobs } from '../../../core/jobs/jobs';
import { useJob } from '../../../core/jobs/useJobs';
import { cx } from '../../../lib/cx';
import { ProgressBar } from '../../../shared/components/ProgressBar';
import { DialogActions, DialogContent, DialogTitle } from '../../../shared/dialog/DialogParts';
import { isRunning } from '../organize-labels';
import { buildRows, reviewStateText } from './tag-review-model';
import { TagReviewRow } from './TagReviewRow';
import { useTagProposals } from './useTagProposals';
import { useWriteTags } from './useWriteTags';
import './tag-review-dialog.scss';

/**
 * Écran de revue « Tags manquants » : une ligne par fichier soumis, remplie au fil de la recherche (la tâche
 * « tags.find » du Catalogue tourne côté serveur : fermer la page ou perdre la connexion ne l'interrompt pas, les
 * propositions déjà reçues sont relues à la réouverture). Seuls les champs VIDES sont écrits, sauf si
 * « Corriger aussi… » est coché. « Écrire » est actif dès la première proposition.
 */
export function TagReviewDialog({ data, close }) {
  const [{ results, checked }, dispatch] = useTagProposals(data.job.id);
  const [correct, setCorrect] = useState(false);
  const [stopping, setStopping] = useState(false);
  const liveJob = useJob(data.job.id) ?? data.job;

  const running = isRunning(liveJob);
  const examined = results.size;
  const total = data.files.length;
  const progress = total ? examined / total : 0;
  const rows = useMemo(() => buildRows(data.files, results), [data.files, results]);
  const stateText = reviewStateText({ rows, running, stopping, examined, total });
  const { busy, write, cancel } = useWriteTags({ data, rows, checked, correct, running, examined, close });

  const setAll = on =>
    dispatch({ type: 'check', keys: on ? rows.filter(r => r.state === 'found').map(r => r.file.dbPath) : [] });

  const stop = async () => {
    setStopping(true);
    await jobs.cancel(data.job);
  };

  return (
    <div className="dk-tag-review-dialog">
      <DialogTitle>Tags manquants</DialogTitle>
      <DialogContent className="content">
        <p className="sub">
          {total} fichier(s) ne portent pas tous leurs tags : le catalogue (Deezer, puis MusicBrainz et l'empreinte
          acoustique pour un titre, un artiste ou un album manquant) est interrogé fichier par fichier. Les
          correspondances sûres sont cochées d'office ; les probables sont à vérifier. Par défaut, seuls les champs
          vides sont renseignés. Rien n'est écrit sans validation.
        </p>
        <p className={cx('state', stopping && 'warn')}>{stateText}</p>
        {running && <ProgressBar value={progress} label={examined + ' / ' + total} />}
        <label
          className="dk-check correct"
          data-tip="Par défaut, seuls les champs vides sont renseignés. Cochez pour remplacer aussi les tags qui contredisent la correspondance retenue — un artiste erroné, par exemple. Les champs concernés sont signalés « ⟳ » dans le tableau."
        >
          <input type="checkbox" className="dk-check" checked={correct} onChange={() => setCorrect(!correct)} />{' '}
          Corriger aussi les tags que la correspondance contredit
        </label>
        <div className="dk-table list">
          <div className="dk-thead row dk-hide-mobile">
            <span>Écrire</span>
            <span>Fichier</span>
            <span>Tags actuels</span>
            <span>Tags trouvés</span>
            <span>Champs comblés</span>
            <span>Source</span>
            <span>Correspondance</span>
          </div>
          {rows.map(r => (
            <TagReviewRow
              key={r.file.dbPath}
              row={r}
              checked={checked.has(r.file.dbPath)}
              correct={correct}
              acoustIdAvailable={data.acoustIdAvailable}
              onToggle={() => dispatch({ type: 'toggle', key: r.file.dbPath })}
            />
          ))}
        </div>
      </DialogContent>
      <DialogActions>
        <button className="control-btn" onClick={() => setAll(true)}>
          Tout cocher
        </button>
        <button className="control-btn" onClick={() => setAll(false)}>
          Tout décocher
        </button>
        <span className="dk-spacer" />
        {running && (
          <button className="control-btn" onClick={() => void stop()} disabled={stopping}>
            Arrêter la recherche
          </button>
        )}
        <button className="control-btn" onClick={() => void cancel()} disabled={busy}>
          Annuler
        </button>
        <button className="primary-btn" onClick={() => void write()} disabled={busy || checked.size === 0}>
          Écrire les tags cochés
        </button>
      </DialogActions>
    </div>
  );
}
