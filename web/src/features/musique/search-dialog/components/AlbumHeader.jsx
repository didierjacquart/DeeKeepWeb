import { Thumb } from './Thumb';

/** En-tête d'un album ou d'une playlist explorés : pochette, nom, détails. */
export function AlbumHeader({ header }) {
  return (
    <div className="sd-album">
      <Thumb url={header.picture} fallback="⏳" className="sd-thumb-lg" lazy={false} />
      <div className="sd-album-text">
        <div className="sd-album-title">{header.name}</div>
        {header.subtitle && <div className="sd-album-sub">{header.subtitle}</div>}
      </div>
    </div>
  );
}
