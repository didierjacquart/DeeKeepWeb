import { useId, useMemo } from 'react';
import { cx } from '../../../../lib/cx';
import { openFsBrowser } from '../../../../shared/fs-browser/open-fs-browser';
import { archiveGauge } from '../archive-model';
import { ArchiveGauge } from './ArchiveGauge';

const INTRO =
  'La copie de la bibliothèque vit dans une base SQLite, sur un disque à soi. Les titres y entrent tels quels, octet pour octet : ' +
  "rien n'est réencodé, rien n'est perdu, et une restauration rend l'original — son empreinte est revérifiée à la sortie. " +
  'La seule limite est la place du disque, que la ligne ci-dessous rappelle. Un seul fichier : le déplacer vers un disque plus grand ' +
  "se résume à le copier, puis à corriger le chemin ci-dessous. Les fichiers d'origine ne sont pas touchés.";

/**
 * Volet déplié : emplacement du fichier d'archive, jauge de place, Tester /
 * Vérifier / Mesurer / Enregistrer.
 *
 * @param {{
 *   settings: ReturnType<typeof import('../hooks/useArchiveSettings').useArchiveSettings>,
 *   tasks: ReturnType<typeof import('../hooks/usePanelTasks').usePanelTasks>,
 *   archiveFile: string | null,
 * }} props
 */
export function ArchiveSettings({ settings, tasks, archiveFile }) {
  const { root, usage } = settings;
  const inputId = useId();
  const gauge = useMemo(() => archiveGauge(usage, root), [usage, root]);

  const browse = async () => {
    // Le fichier .db se choisit directement ; un dossier vaut « deekeep-archive.db » dans ce dossier.
    const current = root.trim();
    const picked = await openFsBrowser({
      mode: 'file',
      extensions: ['.db'],
      allowFolder: true,
      start: /\.db$/i.test(current) ? current.replace(/[\\/][^\\/]*$/, '') : current || undefined,
      title: "Fichier d'archive",
    });
    const path = picked?.[0];
    if (path) settings.inputRoot(path);
  };

  return (
    <div className="dk-card panel">
      <p className="intro">{INTRO}</p>
      <div className="field">
        <label className="caption" htmlFor={inputId}>
          Fichier d'archive
        </label>
        <span className="input-row">
          <input
            id={inputId}
            className="dk-input"
            value={root}
            onChange={e => settings.inputRoot(e.target.value)}
            placeholder="/srv/archive/deekeep-archive.db"
            data-tip="Fichier de l'archive. Un seul fichier : le déplacer vers un autre disque se résume à le copier, puis à corriger ce chemin."
          />
          <button className="control-btn" onClick={() => void browse()}>
            Parcourir…
          </button>
        </span>
      </div>
      {archiveFile && <span className="dk-faint small">Fichier : {archiveFile}</span>}

      <ArchiveGauge gauge={gauge} />

      <div className="buttons">
        <span className="dk-spacer" />
        <button
          className="control-btn"
          disabled={tasks.testing}
          data-tip="Vérifier que l'archive s'ouvre et accepte une écriture"
          onClick={() => void tasks.test()}
        >
          Tester
        </button>
        <button
          className="control-btn"
          disabled={tasks.checking}
          data-tip="Vérifier que chaque titre archivé retrouve sa copie dans l'archive"
          onClick={() => void tasks.check()}
        >
          Vérifier
        </button>
        <button
          className="control-btn"
          disabled={tasks.measuring}
          data-tip="Compter les objets réellement présents dans l'archive"
          onClick={() => void tasks.measure()}
        >
          Mesurer l'occupation
        </button>
        <button className="accent-btn" onClick={() => void tasks.save()}>
          Enregistrer
        </button>
      </div>
      {tasks.panelLine && (
        <p className={cx('state', tasks.panelLine.error && 'dk-error-text')}>{tasks.panelLine.text}</p>
      )}
    </div>
  );
}
