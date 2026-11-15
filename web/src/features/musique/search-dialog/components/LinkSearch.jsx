import { useState } from 'react';

/**
 * Lien Deezer collé (titre, album, playlist) : « Coller et rechercher » lit le
 * presse-papiers quand le champ est vide.
 * @param {{ disabled: boolean, onSearch: (text: string) => Promise<void> }} props
 */
export function LinkSearch({ disabled, onSearch }) {
  const [link, setLink] = useState('');

  const pasteAndSearch = async () => {
    let text = link.trim();
    if (!text) {
      try {
        text = (await navigator.clipboard.readText()).trim();
      } catch {
        /* presse-papiers refusé : on s'en tient au champ */
      }
    }
    if (!text) return;
    setLink(text);
    await onSearch(text);
  };

  return (
    <div className="sd-row">
      <input
        className="dk-input sd-link"
        type="url"
        autoComplete="off"
        aria-label="Lien Deezer"
        placeholder="Collez un lien Deezer (https://www.deezer.com/track/… ou /playlist/…)"
        value={link}
        onChange={e => setLink(e.target.value)}
        onKeyDown={e => {
          if (e.key === 'Enter') void pasteAndSearch();
        }}
      />
      <button className="control-btn" type="button" disabled={disabled} onClick={() => void pasteAndSearch()}>
        Coller et rechercher
      </button>
    </div>
  );
}
