import { cx } from '../../lib/cx';

/**
 * Barre de progression au dégradé navy → cyan (indéterminée si `value` est nul).
 * @param {{ value?: number | null, label?: string | null, large?: boolean, className?: string }} props
 *   `value` 0..1 ; `label` undefined = pourcentage, null = aucun texte.
 */
export function ProgressBar({ value = null, label, large = false, className }) {
  const percent = Math.round((value ?? 0) * 100);
  return (
    <div
      className={cx('dk-progress', large && 'large', value == null && 'indeterminate', className)}
      role="progressbar"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={value == null ? undefined : percent}
      aria-valuetext={label || undefined}
    >
      <div className="fill" style={{ width: `${percent}%` }} />
      {label !== null && <span className="label">{label ?? `${percent} %`}</span>}
    </div>
  );
}
