import { openFsBrowser } from '../../../shared/fs-browser/open-fs-browser';
import { BROWSERS, readStr } from '../settings-values';

/** Compte YouTube : cookies d'un navigateur du serveur, ou fichier cookies.txt (prioritaire). */
export function YoutubeAccount({ settings, save, startDir }) {
  const youtubeBrowser = readStr(settings, 'youtube_browser', 'Aucun (anonyme)');
  const cookiesFile = readStr(settings, 'cookies_file');

  const chooseCookies = async () => {
    const picked = await openFsBrowser({
      start: startDir ?? undefined,
      mode: 'files',
      title: 'Fichier cookies.txt (format Netscape)',
    });
    if (picked?.length) await save({ cookies_file: picked[0] }, 'Choisir le fichier de cookies');
  };

  return (
    <>
      <label
        className="row"
        data-tip="Réutilise la session YouTube du navigateur du serveur. Pour un serveur sans navigateur, utilisez plutôt un fichier cookies.txt ci-dessous (prioritaire)."
      >
        <span>Compte YouTube (cookies du navigateur) :</span>
        <select
          className="dk-select"
          value={youtubeBrowser}
          onChange={e => void save({ youtube_browser: e.target.value }, 'Changer le compte YouTube')}
        >
          {BROWSERS.map(b => (
            <option key={b} value={b}>
              {b}
            </option>
          ))}
        </select>
      </label>

      <div
        className="row"
        data-tip="Fichier de cookies YouTube au format Netscape, exporté avec l'extension « Get cookies.txt LOCALLY ». Prioritaire sur le navigateur ci-dessus. Le fichier se trouve sur le serveur."
      >
        <span>{cookiesFile ? 'Cookies YouTube : ' + cookiesFile : 'Cookies YouTube : aucun fichier'}</span>
        <span className="inline">
          <button className="control-btn" onClick={() => void chooseCookies()}>
            Choisir cookies.txt
          </button>
          <button
            className="control-btn"
            disabled={!cookiesFile}
            onClick={() => void save({ cookies_file: '' }, 'Retirer le fichier de cookies')}
          >
            Retirer
          </button>
        </span>
      </div>
    </>
  );
}
