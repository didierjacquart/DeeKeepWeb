import { cx } from '../../../lib/cx';

/** Regroupement dans « Divers » des artistes de moins de N titres (actif seulement avec un dossier par artiste). */
export function DiversField({ options, onOption }) {
  return (
    <div className="divers">
      <label
        className={cx('dk-check', !options.artist && 'off')}
        data-tip="Le décompte porte sur l'artiste principal : « Inna » et « Inna Feat. Flo Rida » comptent — et se rangent — ensemble. La règle joue dans les deux sens : le dossier « Divers » est analysé comme le reste de la bibliothèque, et un titre qui s'y trouve en ressort dès que son artiste atteint le seuil, en comptant les titres que contient déjà son dossier."
      >
        <input
          type="checkbox"
          className="dk-check"
          checked={options.divers}
          disabled={!options.artist}
          onChange={() => onOption('divers', !options.divers)}
        />
        Regrouper dans « Divers » les artistes de moins de
      </label>
      <input
        type="number"
        className="dk-input min"
        min="2"
        max="10"
        value={options.diversMin}
        disabled={!options.artist || !options.divers}
        onChange={e => onOption('diversMin', +e.target.value)}
        aria-label="Seuil Divers"
      />
      <span className="dk-help">titres (option enregistrée dans les Paramètres)</span>
    </div>
  );
}
