import { cx } from '../../../../lib/cx';
import { PresenceBadge } from '../../../../shared/components/PresenceBadge';
import { formatDuration } from '../../../../shared/format';
import { checkTip, popularityBars, presenceTip } from '../search-model';
import { ExploreLink } from './ExploreLink';
import { Thumb } from './Thumb';

/**
 * Une piste trouvée : pastille et case (verrouillée si déjà sur le disque), titre,
 * liens d'exploration artiste / album, durée, popularité, écoute de l'extrait.
 * @param {{ track: object, picked: boolean, playing: boolean, onToggle: (on: boolean) => void,
 *   onListen: () => void, onArtist: () => void, onAlbum: () => void }} props
 */
export function TrackRow({ track: t, picked, playing, onToggle, onListen, onArtist, onAlbum }) {
  const artist = tip => <ExploreLink label={t.artist} enabled={!!t.artistId} tip={tip} onOpen={onArtist} />;
  const album = tip => <ExploreLink label={t.album} enabled={!!t.albumId} tip={tip} onOpen={onAlbum} />;
  return (
    <div className={cx('dk-row sd-grid sd-row-h', picked && 'selected')}>
      <span className="sd-check">
        <PresenceBadge state={t.presence.state} tooltip={presenceTip(t)} />
        <input
          type="checkbox"
          className="dk-check"
          checked={picked}
          disabled={t.presence.state === 'PRESENT'}
          aria-label={'Sélectionner ' + t.title}
          data-tip={checkTip(t) || undefined}
          onChange={e => onToggle(e.target.checked)}
        />
      </span>
      <Thumb url={t.coverSmall} fallback="♪" className="dk-hide-mobile" />
      <span className="sd-title-cell">
        <span className="sd-strong">
          {t.title}
          {t.explicit && (
            <span className="dk-chip sd-explicit" data-tip="Paroles explicites">
              E
            </span>
          )}
        </span>
        <span className="dk-cell-sub dk-hide-desktop">
          {artist()}
          {t.album && <> · {album()}</>}
          {' · '}
          {formatDuration(t.duration)}
        </span>
      </span>
      <span className="dk-hide-mobile">{artist('Cliquer pour explorer')}</span>
      <span className="dk-hide-mobile">{album('Cliquer pour explorer')}</span>
      <span className="dk-hide-mobile sd-center">{formatDuration(t.duration)}</span>
      <span className="dk-hide-mobile sd-gauge" data-tip={'Popularité Deezer : ' + (t.rank ?? 0)}>
        {popularityBars(t).map((lit, i) => (
          <i key={i} className={cx(lit && 'on')} />
        ))}
      </span>
      <span className="sd-center">
        <button
          className={cx('sd-play', playing && 'active')}
          type="button"
          disabled={!t.previewUrl}
          aria-label={playing ? "Arrêter l'extrait" : "Écouter l'extrait"}
          data-tip={t.previewUrl ? 'Écouter un extrait de 30 secondes' : 'Aucun extrait disponible pour ce titre'}
          onClick={onListen}
        >
          {playing ? '⏸' : '▶'}
        </button>
      </span>
    </div>
  );
}
