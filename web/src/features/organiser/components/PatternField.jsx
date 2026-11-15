import { useLayoutEffect, useRef } from 'react';
import { cx } from '../../../lib/cx';
import { MenuButton } from '../../../shared/menu/MenuButton';
import { MenuItem } from '../../../shared/menu/MenuItem';
import { SubMenu } from '../../../shared/menu/SubMenu';

/** Motif de nommage : champ (avec les motifs proposés) et menu « Insérer une balise » à la position du curseur. */
export function PatternField({ value, enabled, nameTags, onChange }) {
  const input = useRef(null);
  const pendingCaret = useRef(null);

  const insertToken = token => {
    const el = input.current;
    if (!el) {
      onChange(value + token);
      return;
    }
    const start = el.selectionStart ?? value.length;
    const end = el.selectionEnd ?? start;
    // Le curseur est replacé après la balise une fois la nouvelle valeur affichée (voir l'effet ci-dessous).
    pendingCaret.current = start + token.length;
    onChange(value.slice(0, start) + token + value.slice(end));
  };

  useLayoutEffect(() => {
    const pos = pendingCaret.current;
    const el = input.current;
    if (pos == null || !el) return;
    pendingCaret.current = null;
    el.focus();
    el.setSelectionRange(pos, pos);
  }, [value]);

  return (
    <div className={cx('pattern', !enabled && 'off')}>
      <input
        ref={input}
        className="dk-input"
        list="dk-presets"
        value={value}
        disabled={!enabled}
        onChange={e => onChange(e.target.value)}
        aria-label="Motif de nommage"
        data-tip="Motif de nommage. Les balises %…% sont remplacées par les tags du fichier ; une balise vide disparaît du nom. Les balises « _00 » forcent deux chiffres : %piste_00% donne 07."
      />
      <datalist id="dk-presets">
        {(nameTags?.presets ?? []).map(p => (
          <option key={p} value={p} />
        ))}
      </datalist>
      <MenuButton className="control-btn" disabled={!enabled} label="Insérer une balise ▾">
        {(nameTags?.categories ?? []).map(c => (
          <SubMenu key={c.name} label={c.name}>
            {c.items.map(t => (
              <MenuItem key={t.token} onClick={() => insertToken(t.token)}>
                {t.label}
                <span className="dk-organize-tok">{t.token}</span>
              </MenuItem>
            ))}
          </SubMenu>
        ))}
      </MenuButton>
    </div>
  );
}
