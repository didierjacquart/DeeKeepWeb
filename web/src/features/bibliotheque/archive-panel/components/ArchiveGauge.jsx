import { cx } from '../../../../lib/cx';

/**
 * Jauge de place : texte (alerte si la place manque ou si l'archive partage le
 * volume de la bibliothèque), barre occupé / à copier et détail des compteurs.
 * @param {{ gauge: ReturnType<typeof import('../archive-model').archiveGauge> }} props
 */
export function ArchiveGauge({ gauge }) {
  if (!gauge) return null;
  return (
    <>
      <p className={cx('gauge', gauge.warn ? 'warn' : 'ok')}>{gauge.text}</p>
      {gauge.bar && (
        <>
          <div className="meter" aria-hidden="true">
            <span className="used" style={{ width: `${gauge.bar.used}%` }} />
            <span className="todo" style={{ width: `${gauge.bar.todo}%` }} />
          </div>
          <span className="dk-faint small">{gauge.detail}</span>
        </>
      )}
    </>
  );
}
