import { DIVERS_RANGE, readBool, readNum } from '../settings-values';

/** Rangement des fichiers exportés : sous-dossiers par artiste et album, regroupement « Divers ». */
export function FolderOptions({ settings, save }) {
  const divers = readBool(settings, 'org_divers');
  return (
    <>
      <label className="dk-check">
        <input
          type="checkbox"
          className="dk-check"
          checked={readBool(settings, 'org_artist', true)}
          onChange={e => void save({ org_artist: e.target.checked })}
        />{' '}
        Créer un sous-dossier par Artiste
      </label>
      <label className="dk-check">
        <input
          type="checkbox"
          className="dk-check"
          checked={readBool(settings, 'org_album', true)}
          onChange={e => void save({ org_album: e.target.checked })}
        />{' '}
        Créer un sous-dossier par Album
      </label>

      <div
        className="row"
        data-tip="À l'organisation de la bibliothèque, les artistes comptant trop peu de titres ne reçoivent pas leur propre dossier : leurs fichiers sont rassemblés dans « Divers ». À l'inverse, un titre déjà dans « Divers » rejoint le dossier de son artiste dès que celui-ci atteint le seuil."
      >
        <label className="dk-check">
          <input
            type="checkbox"
            className="dk-check"
            checked={divers}
            onChange={e => void save({ org_divers: e.target.checked })}
          />{' '}
          Regrouper dans un dossier « Divers » les artistes de moins de
        </label>
        <span className="inline">
          <select
            className="dk-select narrow"
            disabled={!divers}
            value={String(readNum(settings, 'org_divers_min', 3))}
            onChange={e => void save({ org_divers_min: +e.target.value }, 'Changer le seuil Divers')}
            aria-label="Seuil de titres"
          >
            {DIVERS_RANGE.map(n => (
              <option key={n} value={String(n)}>
                {n}
              </option>
            ))}
          </select>
          titres
        </span>
      </div>
    </>
  );
}
