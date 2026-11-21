import { cx } from '../../../lib/cx';
import { formatDuration } from '../../../shared/format';
import { groupLabel } from '../listen-groups';
import { cloudText, cloudTip, playTip, playable, titleTip } from '../track-info';

/**
 * Cellule Artiste ou Album : le nom, et un ▶ (visible au survol de la ligne, toujours sur écran
 * tactile) qui écoute l'artiste ou l'album entier.
 */
function GroupCell({ kind, name, track, onPlayGroup }) {
  const what = name ? groupLabel(kind, name) : '';
  return (
    <span className="dk-hide-mobile group-cell">
      <span className="group-name" data-tip={name || undefined}>
        {name}
      </span>
      {name && (
        <button
          className="group-play"
          data-tip={`Écouter ${what}`}
          aria-label={`Écouter ${what}`}
          onClick={() => onPlayGroup(kind, track)}
          onDoubleClick={e => e.stopPropagation()}
        >
          ▶
        </button>
      )}
    </span>
  );
}

/**
 * Ligne d'un titre : case, bouton d'écoute, état de l'archive puis les tags.
 * Double-clic : écoute ; ▶ dans Artiste / Album : écoute de l'artiste ou de l'album entier ;
 * clic droit (ou touche menu) : menu contextuel.
 */
export function LibraryRow({ track: t, checked, onToggle, onPlay, onPlayGroup, onMenu }) {
  return (
    <div
      className={cx('dk-row', 'lib-grid', !t.onDisk && 'dim', checked && 'selected')}
      onDoubleClick={() => onPlay(t)}
      onContextMenu={e => onMenu(e, t)}
    >
      <input
        type="checkbox"
        className="dk-check"
        checked={checked}
        onChange={e => onToggle(t.path, e.target.checked)}
        aria-label={'Cocher ' + (t.title ?? '')}
      />
      <button
        className={cx('play', !playable(t) && 'off')}
        data-tip={playTip(t)}
        onClick={() => onPlay(t)}
        aria-label="Écouter"
      >
        ▶
      </button>
      <span
        className={cx('cloud', t.archiveState === 'ok' && 'ok', t.archiveState === 'stale' && 'stale')}
        data-tip={cloudTip(t)}
      >
        {cloudText(t)}
      </span>
      <span className="title" data-tip={titleTip(t)}>
        {t.title || '(sans titre)'}
        <span className="dk-cell-sub dk-hide-desktop">
          {t.artist}
          {t.format ? ' · ' + t.format : ''}
        </span>
      </span>
      <GroupCell kind="artist" name={t.artist} track={t} onPlayGroup={onPlayGroup} />
      <GroupCell kind="album" name={t.album} track={t} onPlayGroup={onPlayGroup} />
      <span className="dk-hide-mobile">{t.genre}</span>
      <span className="dk-hide-mobile">{t.year}</span>
      <span className="dk-hide-mobile">{t.duration ? formatDuration(t.duration) : ''}</span>
      <span className="dk-hide-mobile">{t.format}</span>
      <span className="dk-hide-mobile path" data-tip={t.path || undefined}>
        {t.path}
      </span>
    </div>
  );
}
