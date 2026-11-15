import { Menu } from '../../../shared/menu/Menu';
import { MenuItem } from '../../../shared/menu/MenuItem';
import { copyPath, reanalyze } from '../quality-row-actions';

/** Menu d'une ligne (clic droit ou « ⋮ ») : écouter, copier le chemin, réanalyser. */
export function QualityRowMenu({ menu, onClose, onPlay }) {
  return (
    <Menu at={menu} onClose={onClose}>
      {menu && (
        <>
          <MenuItem onClick={() => onPlay(menu.row)}>Écouter dans DeeKeep</MenuItem>
          <MenuItem onClick={() => void copyPath(menu.row)}>Copier le chemin</MenuItem>
          <MenuItem onClick={() => void reanalyze(menu.row)}>Réanalyser</MenuItem>
        </>
      )}
    </Menu>
  );
}
