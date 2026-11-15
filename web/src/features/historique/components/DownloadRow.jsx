import { cx } from '../../../lib/cx';
import { coverUrl } from '../../../shared/urls';

/** Un téléchargement réussi : tags, chemin local, jaquette agrandissable et écoute (si le fichier existe). */
export function DownloadRow({ entry: e, onPlay, onZoom }) {
  const src = coverUrl(e.coverSha1);
  return (
    <div className={cx('dk-row', 'g-dl', !e.onDisk && 'dim')} onDoubleClick={onPlay}>
      <span data-tip={e.title || undefined}>
        {e.title}
        <span className="dk-cell-sub dk-hide-desktop">{e.artist}</span>
      </span>
      <span className="dk-hide-mobile">{e.artist}</span>
      <span className="dk-hide-mobile">{e.album}</span>
      <span className="dk-hide-mobile path" data-tip={e.path || undefined}>
        {e.path}
      </span>
      <span>
        {src ? (
          <button
            type="button"
            className="zoom"
            data-tip="Cliquer pour agrandir"
            aria-label="Agrandir la jaquette"
            onClick={onZoom}
          >
            <img className="dk-cover thumb zoomable" src={src} alt="Jaquette" loading="lazy" />
          </button>
        ) : (
          <span className="dk-cover thumb">♪</span>
        )}
      </span>
      <span>
        <button
          className="play"
          disabled={!e.onDisk}
          data-tip="Écouter ce titre dans le lecteur"
          onClick={onPlay}
          aria-label="Écouter"
        >
          ▶
        </button>
      </span>
    </div>
  );
}
