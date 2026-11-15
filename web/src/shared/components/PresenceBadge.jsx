import { cx } from '../../lib/cx';

/** @typedef {'PRESENT' | 'POSSIBLE' | 'ABSENT' | 'PENDING'} Presence */

const GLYPHS = { PRESENT: '✓', POSSIBLE: '≈', ABSENT: '○', PENDING: '⏳' };
const LABELS = {
  PRESENT: 'Déjà présent',
  POSSIBLE: 'Peut-être présent',
  ABSENT: 'Absent',
  PENDING: 'Vérification en cours',
};

/**
 * Pastille ✓ ≈ ○ ⏳ : ce que le dossier de dépôt contient déjà.
 * @param {{ state?: Presence | null, tooltip?: string | null }} props
 */
export function PresenceBadge({ state, tooltip }) {
  const s = state ?? 'PENDING';
  return (
    <span className={cx('dk-badge', s.toLowerCase())} data-tip={tooltip || undefined} role="img" aria-label={LABELS[s]}>
      {GLYPHS[s]}
    </span>
  );
}
