import { useState } from 'react';
import { DialogActions, DialogContent, DialogTitle } from '../dialog/DialogParts';
import { FsEntryRow } from './FsEntryRow';
import { useFolderListing } from './useFolderListing';
import './fs-browser.scss';

/**
 * @typedef {object} FsBrowserOptions
 * @property {string} [start] Dossier de départ (chemin local du serveur) ; à défaut, la première racine autorisée.
 * @property {'folder' | 'files' | 'file'} mode
 * @property {string[]} [extensions] Mode « file » : extensions montrées (ex. [".db"]) ; les dossiers restent visibles.
 * @property {boolean} [allowFolder] Mode « file » : le dossier affiché peut aussi être choisi.
 * @property {boolean} [audioOnly]
 * @property {string} [title]
 */

/** Chemin du dossier affiché, modifiable : Entrée ouvre le chemin saisi (ex. un partage réseau). */
function PathField({ value, onGo }) {
  const [draft, setDraft] = useState(value);
  const submit = e => {
    e.preventDefault();
    const path = draft.trim();
    if (path && path !== value) onGo(path);
  };
  return (
    <form className="path-form" onSubmit={submit}>
      <input
        className="path"
        value={draft}
        onChange={e => setDraft(e.target.value)}
        placeholder="Lecteurs et dossiers autorisés — ou tapez un chemin"
        aria-label="Chemin du dossier"
        title={value}
        spellCheck={false}
      />
    </form>
  );
}

/**
 * Navigation dans les dossiers du serveur (racines autorisées uniquement).
 * Mode « folder » : on choisit le dossier affiché ; mode « files » : multi-sélection de fichiers
 * (un dossier coché est aussi accepté : son contenu audio sera lu récursivement par le serveur) ;
 * mode « file » : un seul fichier (double-clic ou « Choisir ce fichier »).
 *
 * @param {{ options: FsBrowserOptions, close: import('../dialog/dialog-stack').CloseDialog }} props
 */
export function FsBrowserDialog({ options, close }) {
  const { listing, error, go } = useFolderListing(options.start, options.audioOnly);
  const [picked, setPicked] = useState(() => new Set());
  const filesMode = options.mode === 'files';
  const fileMode = options.mode === 'file';
  const [chosen, setChosen] = useState(null);
  const extensions = options.extensions?.map(x => x.toLowerCase());
  const visible = (listing?.entries ?? []).filter(
    e => e.isDirectory || !extensions || extensions.some(x => e.name.toLowerCase().endsWith(x)),
  );
  // Un fichier choisi puis quitté (changement de dossier) n'est plus proposé.
  const chosenHere = visible.some(e => e.localPath === chosen) ? chosen : null;

  const toggle = entry =>
    setPicked(s => {
      const n = new Set(s);
      if (!n.delete(entry.localPath)) n.add(entry.localPath);
      return n;
    });

  const open = entry => {
    if (entry.isDirectory) go(entry.localPath);
    else if (filesMode) toggle(entry);
    else if (fileMode) setChosen(entry.localPath);
  };

  const enter = entry => {
    if (entry.isDirectory) go(entry.localPath);
    else if (fileMode) close([entry.localPath]);
  };

  const defaultTitle = filesMode ? 'Choisir des fichiers' : fileMode ? 'Choisir un fichier' : 'Choisir un dossier';

  return (
    <div className="dk-fs-browser">
      <DialogTitle>{options.title ?? defaultTitle}</DialogTitle>
      <DialogContent className="content">
        {listing ? (
          <>
            <div className="bar">
              <button
                className="icon-btn"
                disabled={!listing.parent}
                onClick={() => go(listing.parent)}
                aria-label="Dossier parent"
              >
                ↑
              </button>
              <PathField key={listing.localPath} value={listing.localPath} onGo={go} />
            </div>
            {error && <p className="dk-error-text">{error}</p>}
            {listing.roots.length > 1 && (
              <div className="roots">
                {listing.roots.map(r => (
                  <button key={r} className="control-btn small" onClick={() => go(r)}>
                    {r}
                  </button>
                ))}
              </div>
            )}
            <ul className="list">
              {visible.map(e => (
                <FsEntryRow
                  key={e.localPath}
                  entry={e}
                  selectable={filesMode}
                  selected={picked.has(e.localPath) || chosen === e.localPath}
                  onToggle={() => toggle(e)}
                  onOpen={() => open(e)}
                  onEnter={() => enter(e)}
                />
              ))}
              {!visible.length && <li className="empty">Dossier vide</li>}
            </ul>
          </>
        ) : error ? (
          <p className="dk-error-text">{error}</p>
        ) : (
          <p className="dk-faint">Chargement…</p>
        )}
      </DialogContent>
      <DialogActions>
        <button className="control-btn" onClick={() => close(undefined)}>
          Annuler
        </button>
        {filesMode ? (
          <button className="primary-btn" disabled={picked.size === 0} onClick={() => close([...picked])}>
            Ajouter ({picked.size})
          </button>
        ) : fileMode ? (
          <>
            {options.allowFolder && (
              <button className="control-btn" disabled={!listing?.localPath} onClick={() => close([listing.localPath])}>
                Choisir ce dossier
              </button>
            )}
            <button className="primary-btn" disabled={!chosenHere} onClick={() => close([chosenHere])}>
              Choisir ce fichier
            </button>
          </>
        ) : (
          <button className="primary-btn" disabled={!listing?.localPath} onClick={() => close([listing.localPath])}>
            Choisir ce dossier
          </button>
        )}
      </DialogActions>
    </div>
  );
}
