import { cx } from '../../../../lib/cx';
import { imageUrl } from '../../../../shared/urls';

/**
 * Vignette carrée (image passée par le cache du serveur), ou un symbole à défaut.
 * @param {{ url?: string | null, fallback: string, className?: string, lazy?: boolean }} props
 */
export function Thumb({ url, fallback, className, lazy = true }) {
  return (
    <span className={cx('sd-thumb', className)}>
      {url ? <img src={imageUrl(url) ?? undefined} alt="" loading={lazy ? 'lazy' : undefined} /> : fallback}
    </span>
  );
}
