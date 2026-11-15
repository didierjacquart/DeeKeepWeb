import { cx } from '../../../lib/cx';
import { currentTags, fillsText, foundText, foundTip, matchText, sourceText } from './tag-review-model';

/** Un fichier soumis : case « Écrire » (dès qu'une correspondance est trouvée), tags actuels et trouvés. */
export function TagReviewRow({ row: r, checked, correct, acoustIdAvailable, onToggle }) {
  return (
    <div className={cx('dk-row row', r.state !== 'found' && 'dim')}>
      <span className="c-check">
        {r.state === 'found' && (
          <input
            type="checkbox"
            className="dk-check"
            checked={checked}
            onChange={onToggle}
            aria-label={'Écrire ' + r.file.relative}
          />
        )}
      </span>
      <span className="c-file" data-tip={r.file.path || undefined}>
        {r.file.relative}
      </span>
      <span className="c-cur">{currentTags(r.file)}</span>
      <span
        className={cx('c-found', r.sure && 'sure', r.state === 'found' && !r.sure && 'prob')}
        data-tip={foundTip(r) || undefined}
      >
        {foundText(r)}
      </span>
      <span className="c-fills">{fillsText(r, correct)}</span>
      <span className="c-src">{sourceText(r, acoustIdAvailable)}</span>
      <span className="c-match">{matchText(r)}</span>
    </div>
  );
}
