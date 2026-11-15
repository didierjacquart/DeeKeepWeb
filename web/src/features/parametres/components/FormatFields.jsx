import { FORMATS, qualitiesFor, qualityFor, readStr } from '../settings-values';

/** Format de fichier d'export et qualité d'enregistrement (la liste des qualités suit le format). */
export function FormatFields({ settings, save }) {
  const format = readStr(settings, 'file_format', 'MP3');
  const qualities = qualitiesFor(format);
  const quality = qualityFor(format, readStr(settings, 'quality', '320 kbps (HQ)'));

  const setFormat = next => {
    // Le format est appliqué d'abord, ce qui reconstruit la liste des qualités ; la qualité suit.
    void save(
      { file_format: next, quality: qualityFor(next, readStr(settings, 'quality')) },
      "Changer le format d'export",
    );
  };

  return (
    <>
      <label className="row">
        <span>Format de fichier d'export :</span>
        <select className="dk-select" value={format} onChange={e => setFormat(e.target.value)}>
          {FORMATS.map(f => (
            <option key={f} value={f}>
              {f}
            </option>
          ))}
        </select>
      </label>

      <label className="row">
        <span>Qualité d'enregistrement :</span>
        <select
          className="dk-select"
          value={quality}
          onChange={e => void save({ quality: e.target.value }, 'Changer la qualité')}
        >
          {qualities.map(q => (
            <option key={q} value={q}>
              {q}
            </option>
          ))}
        </select>
      </label>
    </>
  );
}
